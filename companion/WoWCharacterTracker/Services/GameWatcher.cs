namespace WoWCharacterTracker.Services;

// Notices when any WoW client starts or stops (GameProcesses.Clients).
sealed class GameWatcher : IDisposable
{
    readonly Timer timer;
    bool running;

    public GameWatcher(Action started, Action stopped)
    {
        running = GameProcesses.AnyClient(); // a game that's already open when the app starts doesn't count as a start
        timer = new Timer(_ =>
        {
            var now = GameProcesses.AnyClient();
            if (now == running) return;
            running = now;
            if (now) started(); else stopped();
        }, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
    }

    public void Dispose() => timer.Dispose();
}
