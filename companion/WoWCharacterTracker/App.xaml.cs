using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace WoWCharacterTracker;

public partial class App : Application
{
    Mutex? singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        singleInstance = new Mutex(true, @"Local\WoWCharacterTracker", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("Character Tracker is already running. Look for it in your taskbar.",
                "Character Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnCrash;
        base.OnStartup(e);

        var window = new MainWindow(new MainViewModel(backfill: e.Args.Contains("--backfill")));
        MainWindow = window;
        window.Show();
    }

    // Keep running if something unexpected goes wrong, and say what happened in the console.
    static void OnCrash(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Something went wrong: " + e.Exception.Message);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        singleInstance?.Dispose();
        base.OnExit(e);
    }
}

sealed class BoolToVisibility : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) != Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
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
