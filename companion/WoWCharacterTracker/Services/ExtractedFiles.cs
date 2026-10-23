using System.Diagnostics;

namespace WoWCharacterTracker.Services;

// The published exe is one file, but WPF's native parts (about 8 MB) can't run from inside it, so
// .NET unpacks them into %TEMP%\.net\WoWCharacterTracker\<build id> the first time each build runs,
// and never removes them: every update left another folder behind. The one in use stays, the others go.
static class ExtractedFiles
{
    // Every WPF app loads this at startup, so while a copy of the app runs from a folder, it can't be deleted there.
    const string AlwaysLoaded = "wpfgfx_cor3.dll";

    // The folder the running app's native parts came from, or null when they aren't unpacked (a bin build).
    public static string? CurrentDir()
    {
        using var me = Process.GetCurrentProcess();
        foreach (ProcessModule m in me.Modules)
            if (m.ModuleName.Equals(AlwaysLoaded, StringComparison.OrdinalIgnoreCase)
                && Path.GetDirectoryName(m.FileName) is { } dir
                && Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(dir))) == ".net")
                return dir;
        return null;
    }

    // %TEMP%\.net\WoWCharacterTracker, or null for a bin build.
    public static string? BaseDir() => CurrentDir() is { } dir ? Path.GetDirectoryName(dir) : null;

    // Removes the folders older builds unpacked. A folder another running copy uses (a test build) is
    // left alone: its AlwaysLoaded file can't be deleted, and that's tried first. Returns the bytes freed.
    public static long CleanUp()
    {
        if (CurrentDir() is not { } current || Path.GetDirectoryName(current) is not { } root) return 0;
        long freed = 0;
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            if (string.Equals(dir, current, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var size = new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                var marker = Path.Combine(dir, AlwaysLoaded);
                if (File.Exists(marker)) File.Delete(marker); // throws while a running copy has it loaded
                Directory.Delete(dir, recursive: true);
                freed += size;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        if (freed > 0) Log.Info($"Removed {freed / 1_000_000.0:0} MB of files older versions of the app left in {root}.");
        return freed;
    }
}
