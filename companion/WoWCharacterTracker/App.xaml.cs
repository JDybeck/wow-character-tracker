using System.Globalization;
using System.Windows;
using System.Windows.Data;
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
        singleInstance = new Mutex(true, InstanceName, out var isFirstInstance);
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

        vm = new MainViewModel(backfill: e.Args.Contains("--backfill")) { JustUpdated = e.Args.Contains("--updated") };
        vm.QuitRequested += Quit;
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
            stopped: () => Dispatcher.BeginInvoke(() => { if (vm.OpenWithWow && window.IsVisible) window.Hide(); }));
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

sealed class BoolToVisibility : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) != Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// local:FocusWhenVisible.Enabled="True" focuses a TextBox and selects its text when it appears,
// so renaming a webhook can start typing right away.
static class FocusWhenVisible
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(FocusWhenVisible), new PropertyMetadata(false, OnChanged));

    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject d, bool value) => d.SetValue(EnabledProperty, value);

    static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not System.Windows.Controls.TextBox box || e.NewValue is not true) return;
        box.IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is not true) return;
            box.Dispatcher.BeginInvoke(() => { box.Focus(); box.SelectAll(); }, DispatcherPriority.Input);
        };
    }
}

// Visible when the bound value equals the ConverterParameter, e.g. the page name.
sealed class EqualsToVisibility : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        Equals(value?.ToString(), parameter?.ToString()) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

sealed class NullToVisibility : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        (value is null or "") == Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
