using System.Windows;
using Forms = System.Windows.Forms;

namespace WoWCharacterTracker;

// The icon in the system tray while the window is hidden. Double-click or "Open" shows the window.
sealed class TrayIcon : IDisposable
{
    readonly Forms.NotifyIcon icon;
    bool hinted;

    public TrayIcon(Action open, Action quit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Character Tracker", null, (_, _) => open());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => quit());

        icon = new Forms.NotifyIcon
        {
            Text = "Character Tracker",
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        icon.DoubleClick += (_, _) => open();
    }

    static System.Drawing.Icon LoadIcon()
    {
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        using var stream = info!.Stream;
        return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    // The first time the window goes to the tray, say where it went.
    public void ShowHint()
    {
        if (hinted) return;
        hinted = true;
        icon.ShowBalloonTip(4000, "Character Tracker is still running",
            "It keeps tracking and opens again when you start World of Warcraft. Right-click the icon to quit.",
            Forms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
    }
}
