using System.Windows;
using System.Windows.Threading;

namespace WoWCharacterTracker;

public partial class App : Application
{
    // Test runs get their own names, so they never talk to (or give way to) the copy you use.
    static readonly string InstanceName = @"Local\WoWCharacterTracker" + (Config.IsTest ? ".Test" : "");
    static readonly string ShowSignalName = InstanceName + ".Show";

    Mutex? singleInstance;
    EventWaitHandle? showSignal;
    MainViewModel? vm;
    TrayIcon? tray;
    GameWatcher? watcher;
    bool quitting;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Right after an update the old version may still be closing, so wait for it a moment.
        singleInstance = new Mutex(false, InstanceName);
        bool isFirstInstance;
        try { isFirstInstance = singleInstance.WaitOne(e.Args.Contains("--updated") ? TimeSpan.FromSeconds(15) : TimeSpan.Zero); }
        catch (AbandonedMutexException) { isFirstInstance = true; } // the old one exited without letting go
        if (!isFirstInstance)
        {
            // Already running (maybe in the tray): bring that window up instead.
            if (EventWaitHandle.TryOpenExisting(ShowSignalName, out var signal)) signal.Set();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnCrash;
        base.OnStartup(e);
        Updater.CleanUp();

        vm = new MainViewModel(backfill: e.Args.Contains("--backfill"), justUpdated: e.Args.Contains("--updated"));
        vm.Updates.UpdateStarted += QuitForUpdate;
        var window = new MainWindow(vm);
        MainWindow = window;
        window.Closing += (_, args) =>
        {
            // With "Open with World of Warcraft" on, closing hides to the tray and tracking keeps going.
            if (!quitting && vm.OpenWithWow && !Config.IsTest)
            {
                args.Cancel = true;
                window.Hide();
                tray?.ShowHint();
            }
        };
        window.Closed += (_, _) => Quit();

        tray = new TrayIcon(ShowWindow, Quit);

        // Another launch of the exe asks this one to show its window.
        showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        new Thread(() =>
        {
            while (showSignal.WaitOne()) Dispatcher.BeginInvoke(ShowWindow);
        }) { IsBackground = true }.Start();

        // Started at login: only the tray icon until a game starts.
        // ("--minimized" is what older versions put in the startup entry; the app rewrites it on start.)
        if (!e.Args.Contains("--background") && !e.Args.Contains("--minimized")) window.Show();
        _ = vm.StartAsync();

        // Open the window when WoW starts, put it back in the tray when the last WoW closes.
        watcher = new GameWatcher(
            started: () => Dispatcher.BeginInvoke(() => { if (vm.OpenWithWow) ShowWindow(); }),
            stopped: () => Dispatcher.BeginInvoke(() => { if (vm.OpenWithWow && vm.HideWhenGameCloses && window.IsVisible) window.Hide(); }));
    }

    void ShowWindow()
    {
        if (MainWindow is not { } w) return;
        w.Show();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
    }

    void Quit()
    {
        if (quitting) return;
        quitting = true;
        watcher?.Dispose();
        tray?.Dispose();
        vm?.Shutdown();
        MainWindow?.Close();
        Shutdown();
    }

    // The exe this runs from was just renamed to .old, and a single-file app reads parts of itself
    // from that file as it goes, so a normal shutdown can fail. Save, drop the tray icon, and go.
    void QuitForUpdate()
    {
        quitting = true;
        tray?.Dispose();
        vm?.Shutdown();
        Environment.Exit(0);
    }

    // Keep running if something unexpected goes wrong, and say what happened in the console.
    static void OnCrash(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Something went wrong: " + e.Exception.Message);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        tray?.Dispose();
        singleInstance?.Dispose();
        base.OnExit(e);
    }
}
