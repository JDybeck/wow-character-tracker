using System.Collections.Concurrent;

namespace WoWCharacterTracker.Services.LiveChannel;

// Reads the addon's live events from the game's Screenshots folder. For each event the addon
// draws a black and white grid in the top-left corner and takes a screenshot (see the addon's
// "Live channel"). This finds those pictures, reads the grid, and deletes them.
sealed class ScreenshotChannel(string flavorDir) : IDisposable
{
    readonly string dir = Path.Combine(flavorDir, "Screenshots");
    readonly ConcurrentDictionary<string, bool> seen = new(StringComparer.OrdinalIgnoreCase);
    readonly object pollLock = new();
    Timer? timer;

    public event Action<Entry, DateTime>? EntryReceived; // with when the game took the picture
    public event Action<string>? Unreadable; // looked like a data grid but didn't check out

    public void Start()
    {
        // Anything already there is old.
        if (Directory.Exists(dir))
            foreach (var f in Directory.EnumerateFiles(dir)) seen[f] = true;
        timer = new Timer(_ => Poll(), null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
    }

    void Poll()
    {
        if (!Monitor.TryEnter(pollLock)) return;
        try
        {
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.EnumerateFiles(dir, "*.jp*g").Concat(Directory.EnumerateFiles(dir, "*.png")))
            {
                if (seen.ContainsKey(file)) continue;
                // Wait until the game has finished writing it.
                if (!TryRead(file, out var bytes)) continue;
                seen[file] = true;

                switch (GridDecoder.Decode(bytes, out var payload))
                {
                    case GridResult.Ok:
                        var shotAt = File.GetCreationTime(file);
                        TryDelete(file);
                        if (Payload.Parse(payload!) is { } entry) EntryReceived?.Invoke(entry, shotAt);
                        break;
                    case GridResult.Damaged:
                        Unreadable?.Invoke(file);
                        break;
                }
            }
        }
        catch (Exception ex) { Log.Warn("Couldn't check the Screenshots folder: " + ex.Message); }
        finally { Monitor.Exit(pollLock); }
    }

    static bool TryRead(string file, out byte[] bytes)
    {
        bytes = [];
        try
        {
            var info = new FileInfo(file);
            if (DateTime.Now - info.LastWriteTime < TimeSpan.FromMilliseconds(300)) return false; // still being written
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            bytes = new byte[fs.Length];
            fs.ReadExactly(bytes);
            return bytes.Length > 0;
        }
        catch (IOException) { return false; }
    }

    static void TryDelete(string file)
    {
        try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public void Dispose() => timer?.Dispose();
}
