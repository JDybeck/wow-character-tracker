using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace WoWCharacterTracker;

// The window only hosts the views (Views/). Startup, closing to the tray and quitting are in App.
public partial class MainWindow : Window
{
    internal MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        SourceInitialized += (_, _) => UseDarkTitleBar();
        WatchConsoleHover(vm);
        vm.PageReset += () => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ScrollPagesToTop);
        if (ScreenshotMode.Target is { } shot) ScreenshotMode.Attach(this, vm, shot);
        else if (Motion.Enabled) FadeInWhenReady();
    }

    // The window opens on its background and the content fades in once the first layout is done,
    // so the short stall while the character list is built isn't seen as a stutter.
    void FadeInWhenReady()
    {
        Page.Opacity = 0;
        ContentRendered += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            Page.Opacity = 1; // the base value the fade ends on (it holds 0 to 1 while it runs)
            Page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop,
            });
        });
    }

    // The console slides out when the mouse rests on its rail button (or it's clicked) and goes away
    // shortly after the mouse leaves both the button and the console, unless it's pinned.
    void WatchConsoleHover(MainViewModel vm)
    {
        var open = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        var close = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        open.Tick += (_, _) => { open.Stop(); vm.ConsoleOpen = true; };
        close.Tick += (_, _) =>
        {
            close.Stop();
            if (!RailView.ConsoleButton.IsMouseOver && !ConsoleFlyout.IsMouseOver) vm.ConsoleOpen = false;
        };
        RailView.ConsoleButton.MouseEnter += (_, _) => { close.Stop(); open.Start(); };
        RailView.ConsoleButton.MouseLeave += (_, _) => { open.Stop(); close.Start(); };
        ConsoleFlyout.MouseEnter += (_, _) => close.Stop();
        ConsoleFlyout.MouseLeave += (_, _) => close.Start();
    }

    // A page started over (its tab clicked again): every list and scroll area on it back to the top.
    void ScrollPagesToTop()
    {
        void Walk(DependencyObject d)
        {
            if (d is UIElement { IsVisible: false }) return;
            if (d is ScrollViewer scroll) scroll.ScrollToTop();
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(Page);
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
