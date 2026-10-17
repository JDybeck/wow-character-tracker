namespace WoWCharacterTracker.Infrastructure;

// Opens a web page in the user's browser.
static class Browser
{
    public static void Open(string? url)
    {
        if (string.IsNullOrEmpty(url)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }
}
