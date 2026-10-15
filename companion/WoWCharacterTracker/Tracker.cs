using System.Text.Json;

namespace WoWCharacterTracker;

// Watches every account's SavedVariables file, reports the data it finds and posts new level-ups.
sealed class Tracker(string wowPath, Func<Discord?> discord, bool backfill) : IDisposable
{
    static readonly string StatePath = Path.Combine(Config.Dir, "posted.json");

    readonly SemaphoreSlim gate = new(1, 1);
    readonly bool firstRun = !File.Exists(StatePath);
    HashSet<string>? posted;
    FileSystemWatcher? watcher;
    CancellationTokenSource? debounce;
    Timer? safetyNet;
    bool initialDone;

    // Raised on a background thread after every read.
    public event Action<TrackerData>? DataChanged;
    public event Action<Entry>? Posted;

    public void Start()
    {
        // Watch the whole WTF folder so accounts (and the file itself) that appear later are picked up.
        var watchDir = Path.Combine(Wow.FlavorDir(wowPath), "WTF");
        if (!Directory.Exists(watchDir)) watchDir = Wow.FlavorDir(wowPath);
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
                : JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(StatePath)) ?? [];

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
                    Log.Info($"Skipped {pending.Count} older level-up{(pending.Count == 1 ? "" : "s")} so the channel isn't flooded.");
            }
            else if (pending.Count > 0)
            {
                var d = discord();
                if (d is null)
                {
                    Log.Warn($"{pending.Count} level-up{(pending.Count == 1 ? " is" : "s are")} waiting. Add a Discord webhook to post {(pending.Count == 1 ? "it" : "them")}.");
                }
                else
                {
                    foreach (var e in pending)
                    {
                        if (!await d.PostAsync(e)) break; // keep order, retry the rest on the next sync
                        posted.Add(e.Id);
                        Log.Success($"Posted: {e.Char} dinged level {e.Level} ({Format.Duration(e.Seconds)}).");
                        Posted?.Invoke(e);
                    }
                }
            }

            File.WriteAllText(StatePath, JsonSerializer.Serialize(posted));
        }
        catch (Exception ex)
        {
            Log.Error("Sync failed: " + ex.Message);
        }
        finally
        {
            gate.Release();
        }
    }

    // Merges every account's file. A character only lives on one account, so nothing collides.
    TrackerData ReadAll()
    {
        var events = new List<Entry>();
        var chars = new List<Snapshot>();
        foreach (var account in Wow.Accounts(wowPath))
        {
            var file = Wow.SavedVarsFile(wowPath, account);
            if (!File.Exists(file)) continue;
            try
            {
                var d = SavedVariables.Read(file);
                events.AddRange(d.Events);
                chars.AddRange(d.Characters);
            }
            catch (Exception ex)
            {
                Log.Warn($"Couldn't read the data for account {account} ({ex.Message}). Will try again on the next change.");
            }
        }
        return new TrackerData(events, chars);
    }

    public void Dispose()
    {
        watcher?.Dispose();
        safetyNet?.Dispose();
    }
}
