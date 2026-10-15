using Microsoft.Win32;

namespace WoWCharacterTracker;

// Starts the app when you log in to Windows, through the current user's Run key (no admin needed).
static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "WoWCharacterTracker";

    public static void Apply(bool enabled)
    {
        if (Config.IsTest) return; // tests never touch the real startup list
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled && Environment.ProcessPath is { } exe)
            {
                var command = $"\"{exe}\" --minimized";
                if (key.GetValue(ValueName) as string != command) key.SetValue(ValueName, command);
            }
            else if (!enabled && key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Warn("Couldn't change whether the app starts with Windows: " + ex.Message);
        }
    }
}
