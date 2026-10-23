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
        RightScroll.SizeChanged += (_, _) => FitRightColumn();
        DiscordView.SizeChanged += (_, _) => FitRightColumn();
        StreamView.SizeChanged += (_, _) => FitRightColumn();
        SetupRightView.SizeChanged += (_, _) => FitRightColumn();
        vm.PageReset += () => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ScrollPagesToTop);
        Tour.FindTarget = TourTarget;
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

    // The right column scrolls as one: the console takes whatever height is left under the setup
    // checks, Discord and the stream panel, but never less than a readable minimum (then the column
    // scrolls instead).
    const double ConsoleMin = 240;
    void FitRightColumn()
    {
        // The credit row sits under the scrolling part, so it isn't counted here.
        var others = SetupRightView.ActualHeight + DiscordView.ActualHeight + StreamView.ActualHeight + StreamView.Margin.Top + ConsoleView.Margin.Top;
        ConsoleView.Height = Math.Max(ConsoleMin, RightScroll.ActualHeight - others);
    }

    // What each tour step (IntroViewModel.Steps) points at. The setup checks are wherever Settings put them.
    FrameworkElement? TourTarget(string key) => key switch
    {
        "tabs" => HeaderView.GameTabs,
        "settings" => HeaderView.SettingsButton,
        "pages" => PageTabs,
        "setup" => new[] { SetupAtRight, SetupAtTop, SetupAtBottom }.FirstOrDefault(s => s.IsVisible),
        "discord" => DiscordView,
        _ => null,
    };

    // A page started over (its tab clicked again): every list and scroll area on it back to the top.
    // The right column is not part of the page and stays where it is.
    void ScrollPagesToTop()
    {
        void Walk(DependencyObject d)
        {
            if (d == RightScroll || d is UIElement { IsVisible: false }) return;
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
