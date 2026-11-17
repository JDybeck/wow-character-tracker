using System.ComponentModel;
using System.Diagnostics;

namespace WoWCharacterTracker.Services;

// Which WoW clients are running, for everything that asks (GameWatcher, Wow.IsRunning for the
// trackers and the Screenshots poll). One list of all processes is taken and shared for a few
// seconds: asking Windows per name lists every process each time, and six names every 3 seconds
// kept the app at ~2% CPU with no game open.
static class GameProcesses
{
    // Every client's exe (Retail, Classic, Forever, betas and PTRs).
    public static readonly string[] Clients = ["Wow", "WowB", "WowT", "WowClassic", "WowClassicB", "WowClassicT"];

    static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(3); // GameWatcher's tick
    static readonly object Lock = new();
    static Dictionary<string, List<int>> running = new(StringComparer.OrdinalIgnoreCase); // name -> process ids
    static readonly Dictionary<int, string?> folders = []; // process id -> its exe's folder (null: Windows won't say)
    static DateTime takenAt = DateTime.MinValue;

    public static bool AnyClient() => Clients.Any(IsRunning);

    // name: the exe's name without ".exe", any letter case.
    public static bool IsRunning(string name)
    {
        lock (Lock) return Current().ContainsKey(name);
    }

    // Whether name runs from folder. Retail and Forever both run Wow.exe, so the name alone can't
    // tell the games apart. A process whose path Windows won't give counts as running there.
    public static bool IsRunningFrom(string name, string folder)
    {
        var want = Path.GetFullPath(folder).TrimEnd('\\', '/');
        lock (Lock)
        {
            if (!Current().TryGetValue(name, out var ids)) return false;
            return ids.Any(id => FolderOf(id) is not { } dir || dir.Equals(want, StringComparison.OrdinalIgnoreCase));
        }
    }

    static Dictionary<string, List<int>> Current()
    {
        if (DateTime.UtcNow - takenAt <= MaxAge) return running;
        running = new(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (!running.TryGetValue(p.ProcessName, out var ids)) running[p.ProcessName] = ids = [];
                ids.Add(p.Id);
            }
        }
        // Folders of processes that ended go with them (ids get reused).
        var alive = running.Values.SelectMany(i => i).ToHashSet();
        foreach (var id in folders.Keys.Where(id => !alive.Contains(id)).ToList()) folders.Remove(id);
        takenAt = DateTime.UtcNow;
        return running;
    }

    // Asked only for the processes someone checks, once per process.
    static string? FolderOf(int id)
    {
        if (folders.TryGetValue(id, out var known)) return known;
        string? dir = null;
        try
        {
            using var p = Process.GetProcessById(id);
            dir = Path.GetDirectoryName(p.MainModule?.FileName)?.TrimEnd('\\', '/');
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException or NotSupportedException) { }
        folders[id] = dir;
        return dir;
    }
}
