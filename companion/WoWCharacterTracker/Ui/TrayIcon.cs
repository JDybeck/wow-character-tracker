using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WoWCharacterTracker.Ui;

// The icon in the system tray while the window is hidden. Double-click or "Open" shows the window.
// Talks to the shell directly (Shell_NotifyIcon) instead of WinForms' NotifyIcon: WinForms was only
// here for this and added ~23 MB of assemblies to the exe.
sealed class TrayIcon : IDisposable
{
    const int CallbackMessage = 0x8001; // WM_APP + 1
    const int WM_LBUTTONDBLCLK = 0x0203, WM_RBUTTONUP = 0x0205;
    const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10;
    const uint NIIF_INFO = 0x1;
    const uint MF_STRING = 0x0, MF_SEPARATOR = 0x800, TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100;
    const int MenuOpen = 1, MenuQuit = 2;

    // Explorer sends this to top-level windows after it restarts; the icon has to be added again.
    static readonly int TaskbarCreated = RegisterWindowMessage("TaskbarCreated");

    readonly HwndSource window;
    readonly IntPtr iconHandle;
    readonly Action open, quit;
    bool hinted;

    public TrayIcon(Action open, Action quit)
    {
        this.open = open;
        this.quit = quit;
        // A hidden top-level window for the icon's messages (a message-only window misses TaskbarCreated).
        window = new HwndSource(new HwndSourceParameters("CharacterTrackerTray") { Width = 0, Height = 0, WindowStyle = 0 });
        window.AddHook(OnMessage);
        iconHandle = LoadSmallIcon();
        Add();
    }

    // The exe's own icon (Assets\app.ico, set as ApplicationIcon), at the system's small icon size.
    static IntPtr LoadSmallIcon()
    {
        var small = new IntPtr[1];
        return Environment.ProcessPath is { } exe && ExtractIconEx(exe, 0, null, small, 1) > 0 ? small[0] : IntPtr.Zero;
    }

    void Add()
    {
        var data = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = iconHandle;
        data.szTip = "Character Tracker";
        Shell_NotifyIcon(NIM_ADD, ref data);
    }

    NOTIFYICONDATA Data(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = window.Handle,
        uID = 1,
        uFlags = flags,
    };

    IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == TaskbarCreated) Add();
        if (msg != CallbackMessage) return IntPtr.Zero;
        switch ((int)lParam & 0xFFFF)
        {
            case WM_LBUTTONDBLCLK:
                open();
                break;
            case WM_RBUTTONUP:
                ShowMenu(hwnd);
                break;
        }
        handled = true;
        return IntPtr.Zero;
    }

    // Windows' own popup menu: it waits for the pick and closes on a click elsewhere (a WPF menu
    // opened from the tray doesn't, since the app isn't in front).
    void ShowMenu(IntPtr hwnd)
    {
        var popup = CreatePopupMenu();
        AppendMenu(popup, MF_STRING, MenuOpen, "Open Character Tracker");
        AppendMenu(popup, MF_SEPARATOR, 0, null);
        AppendMenu(popup, MF_STRING, MenuQuit, "Quit");
        GetCursorPos(out var at);
        SetForegroundWindow(hwnd); // otherwise the menu stays up after clicking away
        var picked = TrackPopupMenuEx(popup, TPM_RETURNCMD | TPM_RIGHTBUTTON, at.X, at.Y, hwnd, IntPtr.Zero);
        DestroyMenu(popup);
        // After this message is handled: Quit disposes this window, which can't happen inside its own hook.
        if (picked == MenuOpen) window.Dispatcher.BeginInvoke(open);
        else if (picked == MenuQuit) window.Dispatcher.BeginInvoke(quit);
    }

    // The first time the window goes to the tray, say where it went.
    public void ShowHint()
    {
        if (hinted) return;
        hinted = true;
        var data = Data(NIF_INFO);
        data.szInfoTitle = "Character Tracker is still running";
        data.szInfo = "It keeps tracking and opens again when you start World of Warcraft. Right-click the icon to quit.";
        data.dwInfoFlags = NIIF_INFO;
        data.uTimeoutOrVersion = 4000;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        var data = Data(0);
        Shell_NotifyIcon(NIM_DELETE, ref data);
        if (iconHandle != IntPtr.Zero) DestroyIcon(iconHandle);
        window.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int RegisterWindowMessage(string name);

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool AppendMenu(IntPtr menu, uint flags, int id, string? text);

    [DllImport("user32.dll")]
    static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);

    [DllImport("user32.dll")]
    static extern bool DestroyMenu(IntPtr menu);
}
