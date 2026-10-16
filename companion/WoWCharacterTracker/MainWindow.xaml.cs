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
        if (ScreenshotMode.Target is { } shot) ScreenshotMode.Attach(this, vm, shot);
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
