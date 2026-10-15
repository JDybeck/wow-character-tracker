using System.Collections.Concurrent;
using System.Text.Json;

namespace WoWCharacterTracker;

// One per tab. Reads the addon's data for that game version from two places, the SavedVariables
// file (written on /reload and logout) and the chat log (live), and posts what's new.
sealed class Tracker : IDisposable
{
    readonly Flavor flavor;
    readonly string flavorDir;
    readonly Func<Discord?> discord;
    readonly Func<FlavorSettings> settings;
    readonly bool backfill;
    readonly string statePath;
    readonly bool firstRun;
    readonly SemaphoreSlim gate = new(1, 1);
    readonly ConcurrentDictionary<string, Entry> live = new();
    readonly ChatLog chatLog;
    HashSet<string>? posted;
    FileSystemWatcher? watcher;
    CancellationTokenSource? debounce;
    Timer? safetyNet;
    bool initialDone;

    // Raised on a background thread after every read.
    public event Action<TrackerData>? DataChanged;
    public event Action<Entry>? Posted;
    public event Action<Entry>? LiveTest;

    public Tracker(Flavor flavor, string flavorDir, Func<Discord?> discord, Func<FlavorSettings> settings, bool backfill)
    {
        this.flavor = flavor;
        this.flavorDir = flavorDir;
        this.discord = discord;
        this.settings = settings;
        this.backfill = backfill;

        statePath = Path.Combine(Config.Dir, $"posted-{flavor.Key}.json");
        var oldState = Path.Combine(Config.Dir, "posted.json"); // from before there were tabs
        if (flavor == Flavors.Retail && !File.Exists(statePath) && File.Exists(oldState)) File.Move(oldState, statePath);
        firstRun = !File.Exists(statePath);

        chatLog = new ChatLog(flavorDir);
        chatLog.EntryReceived += OnLiveEntry;
    }

    string Tag => $"[{flavor.Name}] ";

    public void Start()
    {
        // Watch the whole WTF folder so accounts (and the file itself) that appear later are picked up.
        var watchDir = Path.Combine(flavorDir, "WTF");
        if (!Directory.Exists(watchDir)) watchDir = flavorDir;
        watcher = new FileSystemWatcher(watchDir, Wow.SavedVarsName)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
        };
        watcher.Changed += (_, _) => Schedule();
        watcher.Created += (_, _) => Schedule();
        watcher.Renamed += (_, _) => Schedule();
        watcher.EnableRaisingEvents = true;

        // A slow re-check in case a change notification gets lost.
        safetyNet = new Timer(_ => _ = SyncAsync(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        _ = SyncAsync().ContinueWith(_ => chatLog.Start()); // live events only after old ones are handled
    }

    void OnLiveEntry(Entry e)
    {
        if (e.Kind == "test")
        {
            Log.Success($"{Tag}Instant posting works: a test from {e.Char} arrived through the chat log.");
            LiveTest?.Invoke(e);
            return;
        }
        live[e.Id] = e;
        _ = SyncAsync();
    }

    void Schedule()
    {
        debounce?.Cancel();
        var cts = debounce = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1000, cts.Token); // let the game finish writing
                await SyncAsync();
            }
            catch (TaskCanceledException) { }
        });
    }

    public async Task SyncAsync()
    {
        await gate.WaitAsync();
        try
        {
            posted ??= firstRun
                ? []
                : JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(statePath)) ?? [];

            var data = ReadAll();
            DataChanged?.Invoke(data);

            // On the very first run, mark old entries as posted instead of flooding the channel.
            var skipPosting = !initialDone && firstRun && !backfill;
            initialDone = true;

            var pending = data.Events
                .Where(e => !posted.Contains(e.Id))
                .OrderBy(e => e.At).ThenBy(e => e.Level)
                .ToList();

            if (skipPosting)
            {
                foreach (var e in pending) posted.Add(e.Id);
                if (pending.Count > 0)
                    Log.Info($"{Tag}Skipped {pending.Count} older event{(pending.Count == 1 ? "" : "s")} so the channel isn't flooded.");
            }
            else if (pending.Count > 0)
            {
                var s = settings();
                // Things this tab isn't set to post are marked as handled, so turning a toggle on
                // later doesn't dump old events into the channel.
                foreach (var e in pending.Where(e => !Wanted(e, s))) posted.Add(e.Id);
                var toPost = pending.Where(e => Wanted(e, s)).ToList();

                var d = toPost.Count > 0 ? discord() : null;
                if (toPost.Count > 0 && d is null)
                {
                    Log.Warn($"{Tag}{toPost.Count} post{(toPost.Count == 1 ? " is" : "s are")} waiting. Pick a Discord webhook for this tab to send {(toPost.Count == 1 ? "it" : "them")}.");
                }
                else if (d is not null)
                {
                    foreach (var e in toPost)
                    {
                        var shot = e.Shot && s.PostsShot(e.Kind) ? await FindScreenshotAsync(e) : null;
                        if (!await d.PostAsync(e, screenshot: shot)) break; // keep order, retry the rest on the next sync
                        posted.Add(e.Id);
                        Log.Success($"{Tag}Posted: {Describe(e)}.");
                        Posted?.Invoke(e);
                    }
                }
            }

            File.WriteAllText(statePath, JsonSerializer.Serialize(posted));
        }
        catch (Exception ex)
        {
            Log.Error($"{Tag}Sync failed: {ex.Message}");
        }
        finally
        {
            gate.Release();
        }
    }

    // The addon takes the screenshot a second or two after the event. The game names it by time,
    // so the newest picture written just after the event is the one. For a live event the file may
    // not be there yet, so wait a little.
    async Task<string?> FindScreenshotAsync(Entry e)
    {
        var dir = Path.Combine(flavorDir, "Screenshots");
        var at = DateTimeOffset.FromUnixTimeSeconds(e.At).LocalDateTime;
        var fresh = DateTime.Now - at < TimeSpan.FromSeconds(30);

        for (var i = 0; i < (fresh ? 15 : 1); i++)
        {
            if (Directory.Exists(dir))
            {
                var match = new DirectoryInfo(dir).EnumerateFiles()
                    .Where(f => f.Extension is ".jpg" or ".jpeg" or ".png")
                    .Where(f => f.LastWriteTime >= at.AddSeconds(-2) && f.LastWriteTime <= at.AddSeconds(20))
                    .OrderBy(f => f.LastWriteTime)
                    .FirstOrDefault();
                if (match is not null)
                {
                    if (fresh) await Task.Delay(500); // let the game finish writing it
                    return match.FullName;
                }
            }
            await Task.Delay(1000);
        }

        Log.Warn($"{Tag}No screenshot found for {Describe(e)}, posting without one.");
        return null;
    }

    static bool Wanted(Entry e, FlavorSettings s) => e.Kind switch
    {
        "level" => s.LevelWanted(e.Level),
        "loot" => e.Quality >= 4 ? s.PostEpic : e.Quality == 3 && s.PostRare,
        "mount" => s.PostMounts,
        "boss" => s.PostBosses,
        "death" => s.PostDeaths,
        _ => false,
    };

    public static string Describe(Entry e) => e.Kind switch
    {
        "boss" => $"{e.Char} killed {e.Boss}",
        "loot" => $"{e.Char} looted {e.Item}",
        "mount" => $"{e.Char} got a new mount, {e.Mount}",
        "death" => $"{e.Char} died at level {e.Level}",
        _ => $"{e.Char} dinged level {e.Level} ({Format.Duration(e.Seconds)})",
    };

    // Merges every account's file plus anything that came in live. A character only lives on one
    // account, and a live event later shows up in the file with the same id.
    TrackerData ReadAll()
    {
        var events = new Dictionary<string, Entry>();
        var chars = new List<Snapshot>();
        foreach (var account in Wow.Accounts(flavorDir))
        {
            var file = Wow.SavedVarsFile(flavorDir, account);
            if (!File.Exists(file)) continue;
            try
            {
                var d = SavedVariables.Read(file);
                foreach (var e in d.Events) events[e.Id] = e;
                chars.AddRange(d.Characters);
            }
            catch (Exception ex)
            {
                Log.Warn($"{Tag}Couldn't read the data for account {account} ({ex.Message}). Will try again on the next change.");
            }
        }
        foreach (var e in live.Values) events.TryAdd(e.Id, e);
        return new TrackerData(events.Values.ToList(), chars);
    }

    public void Dispose()
    {
        watcher?.Dispose();
        safetyNet?.Dispose();
        chatLog.Dispose();
    }
}
