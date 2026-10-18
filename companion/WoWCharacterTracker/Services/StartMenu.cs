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
}
