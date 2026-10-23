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
    static HashSet<string> names = [];
    static DateTime takenAt = DateTime.MinValue;

    public static bool AnyClient() => Clients.Any(IsRunning);

    // name: the exe's name without ".exe", any letter case.
    public static bool IsRunning(string name)
    {
        lock (Lock)
        {
            if (DateTime.UtcNow - takenAt > MaxAge)
            {
                names = Running();
                takenAt = DateTime.UtcNow;
            }
            return names.Contains(name);
        }
    }

    static HashSet<string> Running()
    {
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            using (p) running.Add(p.ProcessName);
        }
        return running;
    }
}
