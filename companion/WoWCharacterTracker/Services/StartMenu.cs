namespace WoWCharacterTracker.Services;

// A shortcut in the current user's Start menu (no admin needed), so Windows search finds the app.
// There's no installer, so the app adds it itself on start and points it at wherever the exe is now.
static class StartMenu
{
    public static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "WoW Character Tracker.lnk");

    public static void Apply()
    {
        // Tests and development builds (a folder of dlls) never get one.
        if (Config.IsTest || !Updater.CanInstall || Environment.ProcessPath is not { } exe) return;
        try
        {
            // Windows Script Host's shell object writes .lnk files; it ships with every Windows.
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            var shortcut = shell.CreateShortcut(ShortcutPath);
            if (File.Exists(ShortcutPath) && string.Equals((string)shortcut.TargetPath, exe, StringComparison.OrdinalIgnoreCase)) return;
            shortcut.TargetPath = exe;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exe);
            shortcut.IconLocation = exe + ",0";
            shortcut.Description = "Posts your WoW moments to Discord";
            shortcut.Save();
        }
        catch (Exception ex)
        {
            Log.Warn("Couldn't add the app to the Start menu: " + ex.Message);
        }
    }

    // An update keeps the exe's path, so Windows keeps showing the icon it cached for it (taskbar,
    // Start menu) until it's told the icon changed. Called after an update (--updated).
    public static void RefreshIcon()
    {
        if (Config.IsTest || Environment.ProcessPath is not { } exe) return;
        SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW, exe, IntPtr.Zero);
        if (File.Exists(ShortcutPath)) SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW, ShortcutPath, IntPtr.Zero);
        SHChangeNotify(SHCNE_ASSOCCHANGED, 0, null, IntPtr.Zero);
    }

    const int SHCNE_UPDATEITEM = 0x2000, SHCNE_ASSOCCHANGED = 0x08000000;
    const uint SHCNF_PATHW = 0x0005;

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern void SHChangeNotify(int eventId, uint flags, string? item1, IntPtr item2);
}
