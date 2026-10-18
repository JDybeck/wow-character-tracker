using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WoWCharacterTracker;

// The window only hosts the views (Views/). Startup, closing to the tray and quitting are in App.
public partial class MainWindow : Window
{
    internal MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        SourceInitialized += (_, _) => UseDarkTitleBar();
        RightScroll.SizeChanged += (_, _) => FitRightColumn();
        DiscordView.SizeChanged += (_, _) => FitRightColumn();
        StreamView.SizeChanged += (_, _) => FitRightColumn();
        if (ScreenshotMode.Target is { } shot) ScreenshotMode.Attach(this, vm, shot);
    }

    // The right column scrolls as one: the console takes whatever height is left under Discord and
    // the stream panel, but never less than a readable minimum (then the column scrolls instead).
    const double ConsoleMin = 240;
    void FitRightColumn()
    {
        // The credit row sits under the scrolling part, so it isn't counted here.
        var others = DiscordView.ActualHeight + StreamView.ActualHeight + StreamView.Margin.Top + ConsoleView.Margin.Top;
        ConsoleView.Height = Math.Max(ConsoleMin, RightScroll.ActualHeight - others);
    }

    // Dark title bar to match the window (Windows 10 20H1 and later).
    void UseDarkTitleBar()
    {
        var on = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
