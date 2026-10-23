using System.Collections.Concurrent;

namespace WoWCharacterTracker.Services.LiveChannel;

// Reads the addon's live events from the game's Screenshots folder. For each event the addon
// draws a black and white grid in a corner (Settings.lua gridCorner) and takes a screenshot (see
// the addon's "Live channel"). This finds those pictures, reads the grid, and deletes them.
// A folder watcher reacts the moment the game writes a picture; a slow poll is the safety net. A
// picture still being written (locked, or too new to read whole) is tried again shortly after.
sealed class ScreenshotChannel(string flavorDir, Func<string> corner) : IDisposable
{
    readonly string dir = Path.Combine(flavorDir, "Screenshots");
    readonly ConcurrentDictionary<string, bool> seen = new(StringComparer.OrdinalIgnoreCase);
    readonly object pollLock = new();
    Timer? timer;
    Timer? retry;
    FileSystemWatcher? watcher;

    // A picture the game may still be writing isn't given up on until it's this old.
    static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);

    // With when the game took the picture, and the character's portrait from a check-in shot (or null).
    public event Action<Entry, DateTime, byte[]?>? EntryReceived;
    public event Action<string>? Unreadable; // looked like a data grid but didn't check out

    public void Start()
    {
        // Anything already there is old.
        if (Directory.Exists(dir))
            foreach (var f in Directory.EnumerateFiles(dir)) seen[f] = true;
        retry = new Timer(_ => Poll());
        timer = new Timer(_ => { Watch(); if (GameRunning()) Poll(); }, null, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1));
        Watch();
    }

    // The steady poll only runs while the game does (only it writes data shots), so a closed game
    // costs nothing; the folder watcher still reacts at any time. Asked every few seconds, not every tick.
    static readonly TimeSpan RunningCheck = TimeSpan.FromSeconds(5);
    bool gameRunning;
    DateTime runningCheckedAt = DateTime.MinValue;

    bool GameRunning()
    {
        if (DateTime.UtcNow - runningCheckedAt < RunningCheck) return gameRunning;
        runningCheckedAt = DateTime.UtcNow;
        return gameRunning = Wow.IsRunning(flavorDir);
    }

    // The game makes the Screenshots folder on its first screenshot, so the watcher starts once it's there.
    void Watch()
    {
        if (watcher is not null || !Directory.Exists(dir)) return;
        try
        {
            watcher = new FileSystemWatcher(dir) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
            watcher.Created += (_, _) => Soon();
            watcher.Changed += (_, _) => Soon();
            watcher.Renamed += (_, _) => Soon();
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { watcher = null; }
    }

    void Soon(int ms = 40) => retry?.Change(ms, Timeout.Infinite);

    void Poll()
    {
        if (!Monitor.TryEnter(pollLock)) return;
        var waiting = false;
        try
        {
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.EnumerateFiles(dir, "*.jp*g").Concat(Directory.EnumerateFiles(dir, "*.png")))
            {
                if (seen.ContainsKey(file)) continue;
                var age = DateTime.Now - SafeWriteTime(file);
                // Still being written: locked, or empty so far.
                if (!TryRead(file, out var bytes)) { waiting = true; continue; }

                var preferred = corner();
                var frame = GridDecoder.Load(bytes);
                string? payload = null;
                double cellSize = 0;
                var where = preferred;
                var result = frame is null ? GridResult.NotAGrid : GridDecoder.Decode(frame, out payload, out cellSize, out where, preferred);
                switch (result)
                {
                    case GridResult.Ok:
                        seen[file] = true;
                        var shotAt = File.GetCreationTime(file);
                        Files.TryDelete(file);
                        if (Payload.Parse(payload!) is { } entry)
                            EntryReceived?.Invoke(entry, shotAt, entry.Kind == "snapshot" ? GridDecoder.CropPortrait(frame!, cellSize, where) : null);
                        break;
                    case GridResult.Damaged when age < Settle:
                        waiting = true; // maybe read half-written: once more in a moment
                        break;
                    case GridResult.Damaged:
                        seen[file] = true;
                        // The addon's own shot (it has the grid's start), just unreadable: the event
                        // comes with the next save anyway, so it doesn't stay in the Screenshots folder.
                        Files.TryDelete(file);
                        Unreadable?.Invoke(file);
                        break;
                    default:
                        // Not ours (a normal screenshot), unless it was cut short while being written.
                        if (age < Settle) waiting = true; else seen[file] = true;
                        break;
                }
            }
        }
        catch (Exception ex) { Log.Warn("Couldn't check the Screenshots folder: " + ex.Message); }
        finally { Monitor.Exit(pollLock); }
        if (waiting) Soon(100);
    }

    static DateTime SafeWriteTime(string file)
    {
        try { return File.GetLastWriteTime(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return DateTime.Now; }
    }

    // False while the game still has the file open for writing (it doesn't share it for reading then).
    static bool TryRead(string file, out byte[] bytes)
    {
        bytes = [];
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            bytes = new byte[fs.Length];
            fs.ReadExactly(bytes);
            return bytes.Length > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public void Dispose()
    {
        timer?.Dispose();
        retry?.Dispose();
        watcher?.Dispose();
    }
}
