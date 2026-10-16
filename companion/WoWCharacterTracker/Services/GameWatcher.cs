namespace WoWCharacterTracker.Services;

// Notices when any WoW client starts or stops (Retail, Classic, betas and PTRs all use these names).
sealed class GameWatcher : IDisposable
{
    static readonly string[] ProcessNames = ["Wow", "WowB", "WowT", "WowClassic", "WowClassicB", "WowClassicT"];
    readonly Timer timer;
    bool running;

    public GameWatcher(Action started, Action stopped)
    {
        running = IsAnyRunning(); // a game that's already open when the app starts doesn't count as a start
        timer = new Timer(_ =>
        {
            var now = IsAnyRunning();
            if (now == running) return;
            running = now;
            if (now) started(); else stopped();
        }, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
    }

    static bool IsAnyRunning() =>
        ProcessNames.Any(n => System.Diagnostics.Process.GetProcessesByName(n).Length > 0);

    public void Dispose() => timer.Dispose();
}
