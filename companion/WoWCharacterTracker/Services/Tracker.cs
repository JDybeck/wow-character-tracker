using System.Collections.Concurrent;
using System.Text.Json;

namespace WoWCharacterTracker.Services;

// One per tab. Reads the addon's data for that game version from two places, the SavedVariables
// file (written on /reload and logout) and data screenshots (live), and posts what's new.
sealed class Tracker : IDisposable
{
    readonly Flavor flavor;
    readonly string flavorDir;
    readonly Func<List<WebhookInfo>> webhooks;
    readonly Func<string, IReadOnlyCollection<string>> tagsFor;
    readonly Func<FlavorSettings> settings;
    readonly bool backfill;
    readonly string statePath;
    readonly bool firstRun;
    readonly SemaphoreSlim gate = new(1, 1);
    readonly ConcurrentDictionary<string, Entry> live = new();
    readonly ScreenshotChannel shots;
    HashSet<string>? posted;
    FileSystemWatcher? watcher;
    CancellationTokenSource? debounce;
    Timer? safetyNet;
    bool initialDone;

    // Raised on a background thread after every read.
    public event Action<TrackerData>? DataChanged;
    public event Action<Entry>? Posted;
    public event Action<Entry>? LiveTest;
    public event Action<Entry>? CheckedIn; // a character just logged in (live check-in)

    public Tracker(Flavor flavor, string flavorDir, Func<List<WebhookInfo>> webhooks, Func<FlavorSettings> settings,
        Func<string, IReadOnlyCollection<string>> tagsFor, bool backfill)
    {
        this.flavor = flavor;
        this.flavorDir = flavorDir;
        this.webhooks = webhooks;
        this.tagsFor = tagsFor;
        this.settings = settings;
        this.backfill = backfill;

        statePath = Path.Combine(Config.Dir, $"posted-{flavor.Key}.json");
        var oldState = Path.Combine(Config.Dir, "posted.json"); // from before there were tabs
        if (flavor == Flavors.Retail && !File.Exists(statePath) && File.Exists(oldState)) File.Move(oldState, statePath);
        firstRun = !File.Exists(statePath);

        shots = new ScreenshotChannel(flavorDir);
        shots.EntryReceived += OnLiveEntry;
        shots.Unreadable += file => Log.Warn($"{Tag}A live event screenshot couldn't be read ({Path.GetFileName(file)}). It gets posted on your next /reload or logout instead.");
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

        // A slow re-check in case a change notification gets lost. Only while the game runs (plus once
        // right after it closes, when it writes the saved file); with the game closed nothing changes,
        // and the list keeps showing the last known state.
        var wasRunning = true;
        safetyNet = new Timer(_ =>
        {
            var running = Wow.IsRunning(flavorDir);
            if (running || wasRunning) _ = SyncAsync();
            wasRunning = running;
        }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        _ = SyncAsync().ContinueWith(_ => shots.Start()); // live events only after old ones are handled
    }

    void OnLiveEntry(Entry e)
    {
        // Every live event carries the character's current state, so the list updates right away.
        presence[e.Guid] = e;

        if (e.Kind == "test")
        {
            Log.Success($"{Tag}Instant posting works: a test from {e.Char} arrived right away.");
            LiveTest?.Invoke(e);
        }
        else if (e.Kind == "snapshot")
        {
            Log.Info($"{Tag}{e.Char} logged in.");
            CheckedIn?.Invoke(e);
        }
        else
        {
            live[e.Id] = e;
        }
        _ = SyncAsync();
    }

    int lastWaiting;

    // Latest live state per character (from check-ins, tests and events), newer than the saved file.
    readonly ConcurrentDictionary<string, Entry> presence = new();

    static Snapshot ToSnapshot(Entry e) => new(e.Guid, e.Char, e.Realm, e.Level, e.Class, e.ClassFile,
        e.Race, e.RaceFile, e.Sex, e.Spec, e.Role, e.ItemLevel, e.Played, e.Zone, e.Professions, e.Talents,
        e.At, e.Hardcore, e.Ruleset, e.Money, e.Crests, e.Guild, e.Surname, TalentExport: e.TalentExport, TalentCalc: e.TalentCalc, Region: e.Region);

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

    // report: say in the console what was read and posted (Sync now).
    public async Task SyncAsync(bool report = false)
    {
        await gate.WaitAsync();
        var sent = 0;
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
                foreach (var e in pending.Where(e => !Wanted(e, s)))
                {
                    if (e.Kind == "boss" && s.PostBosses && s.SkipBossBecause(e) is { } who)
                        Log.Info($"{Tag}Not posting {e.Boss}: {who.Name} is in the group (they post it).");
                    posted.Add(e.Id);
                    // Only posted highlights keep their screenshot. (The addon normally doesn't take
                    // one for these, unless the settings changed since the game last loaded them.)
                    if (e.Shot && ScreenshotFileFor(e) is { } unwantedShot) TryDelete(unwantedShot);
                }
                var toPost = pending.Where(e => Wanted(e, s)).ToList();

                var hooks = toPost.Count > 0 ? webhooks() : [];
                if (toPost.Count > 0 && hooks.Count == 0)
                {
                    if (toPost.Count != lastWaiting) // say it once per change, not on every sync
                        Log.Warn($"{Tag}{toPost.Count} post{(toPost.Count == 1 ? " is" : "s are")} waiting. Turn on a Discord webhook for this tab to send {(toPost.Count == 1 ? "it" : "them")}.");
                    lastWaiting = toPost.Count;
                }
                else
                {
                    // Each webhook is tracked on its own ("<id>@<webhook>"), so one that fails gets
                    // the post later without the others getting it twice.
                    foreach (var e in toPost)
                    {
                        // Webhooks can be limited to some rulesets (Forever) and to characters with some tags.
                        var tags = tagsFor(e.Guid);
                        var forThis = hooks
                            .Where(w => flavor != Flavors.Forever || w.AcceptsRuleset(e))
                            .Where(w => w.AcceptsTags(tags))
                            .ToList();
                        var shot = forThis.Count > 0 && e.Shot && s.PostsShot(e.Kind) ? await FindScreenshotAsync(e) : null;
                        var allSent = true;
                        foreach (var w in forThis)
                        {
                            var key = $"{e.Id}@{w.Id}";
                            if (posted.Contains(key)) continue;
                            if (await new DiscordPoster(w.Url, flavor, s.NameStyle).PostAsync(e, screenshot: shot))
                            {
                                posted.Add(key);
                                sent++;
                                Log.Success($"{Tag}Posted to \"{w.Name}\": {Describe(e)}.");
                            }
                            else allSent = false;
                        }
                        if (!allSent) break; // keep order, retry the rest on the next sync
                        posted.Add(e.Id);
                        Posted?.Invoke(e);
                    }
                }
            }

            File.WriteAllText(statePath, JsonSerializer.Serialize(posted));
            if (report)
            {
                var chars = data.Characters.Select(c => c.Guid).Concat(data.Events.Select(e => e.Guid)).Distinct().Count();
                Log.Info($"{Tag}Synced: {chars} character{(chars == 1 ? "" : "s")}, {data.Events.Count} event{(data.Events.Count == 1 ? "" : "s")}. " +
                         (sent > 0 ? $"Posted {sent}." : "Nothing new to post. (The game saves its data on /reload or logout; instant posts arrive by themselves.)"));
            }
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
        var at = DateTimeOffset.FromUnixTimeSeconds(e.At).LocalDateTime;
        var fresh = DateTime.Now - at < TimeSpan.FromSeconds(30);

        for (var i = 0; i < (fresh ? 15 : 1); i++)
        {
            if (ScreenshotFileFor(e) is { } match)
            {
                if (fresh) await Task.Delay(500); // let the game finish writing it
                return match;
            }
            await Task.Delay(1000);
        }

        Log.Warn($"{Tag}No screenshot found for {Describe(e)}, posting without one.");
        return null;
    }

    // The first picture written from just before the event to 20 seconds after. (Data grid
    // screenshots are deleted as soon as they're read, so they don't get picked here.)
    string? ScreenshotFileFor(Entry e)
    {
        var dir = Path.Combine(flavorDir, "Screenshots");
        if (!Directory.Exists(dir)) return null;
        var at = DateTimeOffset.FromUnixTimeSeconds(e.At).LocalDateTime;
        return new DirectoryInfo(dir).EnumerateFiles()
            .Where(f => f.Extension is ".jpg" or ".jpeg" or ".png")
            .Where(f => f.LastWriteTime >= at.AddSeconds(-2) && f.LastWriteTime <= at.AddSeconds(20))
            .OrderBy(f => f.LastWriteTime)
            .FirstOrDefault()?.FullName;
    }

    static void TryDelete(string file)
    {
        try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // Max level and legendary items are always posted, with extra flair.
    static bool Wanted(Entry e, FlavorSettings s) => e.Kind switch
    {
        "level" => (e.MaxLevel && s.PostMaxLevel) || s.LevelWanted(e.Level),
        "loot" => e.Quality >= 5 || (e.Quality == 4 ? s.PostEpic : e.Quality == 3 && s.PostRare),
        "mount" => s.PostMounts,
        "boss" => s.PostBosses && s.SkipBossBecause(e) is null,
        "death" => s.PostDeaths,
        "duel" => s.PostDuels,
        "makgora" => s.PostMakgora,
        "mplus" => s.PostMPlus,
        "pvp" => s.PostPvp,
        _ => false,
    };

    public static string Describe(Entry e) => e.Kind switch
    {
        "boss" => $"{e.Char} killed {e.Boss}",
        "loot" => $"{e.Char} looted {e.Item}",
        "mount" => $"{e.Char} got a new mount, {e.Mount}",
        "death" => $"{e.Char} died at level {e.Level}",
        "duel" => $"{e.Char} won a duel against {e.Opponent}",
        "makgora" => $"{e.Char} won a Mak'gora against {e.Opponent}",
        "mplus" => $"{e.Char} reached {e.Step} Mythic+ rating ({e.Rating})",
        "pvp" => $"{e.Char} reached {e.Step} in {e.Bracket} ({e.Rating})",
        _ => e.Seconds > 0 ? $"{e.Char} dinged level {e.Level} ({Format.Duration(e.Seconds)})" : $"{e.Char} dinged level {e.Level}",
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
        // A live check-in newer than the saved snapshot wins (CharacterRow picks the latest per character).
        chars.AddRange(presence.Values.Select(ToSnapshot));
        return new TrackerData(events.Values.ToList(), chars);
    }

    public void Dispose()
    {
        watcher?.Dispose();
        safetyNet?.Dispose();
        shots.Dispose();
    }
}
