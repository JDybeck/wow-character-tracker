namespace WoWCharacterTracker.Services;

// Everything the app and the addon have put on this PC, so it can be removed again from the Uninstall
// page. Each part lists its exact files and folders, and removing a part deletes exactly those.
// (There's no Windows installer: the app is one exe, so it's the one that knows what it created.)
static class Uninstaller
{
    public enum Kind { App, Addon, Data, Settings }

    // Game: which game's folder the part is in (addon and data); the game must be closed to remove it.
    public sealed record Part(Kind Kind, string Title, string Detail, IReadOnlyList<string> Paths, long Bytes, Flavor? Game = null, string? GameDir = null);

    public static List<Part> Find(IEnumerable<(Flavor Flavor, string? Dir)> games)
    {
        var parts = new List<Part>();
        if (CanRemoveApp && Environment.ProcessPath is { } exe)
        {
            // Test runs never make the Start menu shortcut, so they leave the real one alone.
            var shortcut = Config.IsTest ? "" : StartMenu.ShortcutPath;
            var files = new[] { exe, exe + ".old", exe + ".new", shortcut }.Where(File.Exists).ToList();
            // The parts .NET unpacked into %TEMP% (ExtractedFiles), gone with the exe.
            var unpacked = ExtractedFiles.BaseDir();
            parts.Add(new Part(Kind.App, "The app",
                "The program, its Start menu shortcut, the files Windows unpacked for it in your temp folder and its entry in Windows startup (Open with World of Warcraft). It's removed a moment after the app closes.",
                unpacked is null ? files : [.. files, unpacked], files.Sum(f => new FileInfo(f).Length) + (unpacked is null ? 0 : Size(unpacked))));
        }
        foreach (var (flavor, dir) in games)
        {
            if (dir is null) continue;
            var addon = AddonInstaller.TargetDir(dir);
            if (Directory.Exists(addon))
                parts.Add(new Part(Kind.Addon, $"The addon in {flavor.Name}",
                    "The addon's files and the settings the app wrote for it. Your other addons aren't touched.",
                    [addon], Size(addon), flavor, dir));
            var saved = Wow.Accounts(dir)
                .Select(account => Wow.SavedVarsFile(dir, account))
                .SelectMany(file => new[] { file, file + ".bak" })
                .Where(File.Exists).ToList();
            if (saved.Count > 0)
                parts.Add(new Part(Kind.Data, $"Your character data in {flavor.Name}",
                    "What the addon saved for each WoW account: level history, gear, bags and bank, highlights. Keep it and a reinstall shows it all again.",
                    saved, saved.Sum(f => new FileInfo(f).Length), flavor, dir));
        }
        if (Directory.Exists(Config.Dir))
            parts.Add(new Part(Kind.Settings, "App settings and caches",
                "Your webhooks and settings, what has been posted, the Wowhead cache, portraits and tab logos.",
                [Config.Dir], Size(Config.Dir)));
        return parts;
    }

    // Removes the parts (except the app's exe, which can only go once the app has closed: see
    // RemoveAppAfterExit). Returns each part with the reason it couldn't be removed, or null.
    public static List<(Part Part, string? Problem)> Remove(IEnumerable<Part> parts)
    {
        var results = new List<(Part, string?)>();
        foreach (var part in parts)
        {
            try
            {
                if (part.Kind == Kind.App)
                {
                    Autostart.Apply(false);
                    foreach (var leftover in part.Paths.Where(p => p != Environment.ProcessPath && File.Exists(p))) File.Delete(leftover);
                    ExtractedFiles.CleanUp(); // the folder in use goes after exit (RemoveAppAfterExit)
                }
                else
                {
                    foreach (var path in part.Paths)
                    {
                        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                        else if (File.Exists(path)) File.Delete(path);
                    }
                }
                results.Add((part, null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                results.Add((part, ex.Message));
            }
        }
        return results;
    }

    // A development build (dotnet build) runs from a folder of dlls, which the user deletes themselves.
    public static bool CanRemoveApp => Updater.CanInstall;

    // The running exe can't delete itself, so a hidden command waits for the app to close, then does.
    public static void RemoveAppAfterExit()
    {
        if (!CanRemoveApp || Environment.ProcessPath is not { } exe) return;
        var unpacked = ExtractedFiles.BaseDir() is { } dir ? $" & rmdir /s /q \"{dir}\"" : "";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe",
            $"/c ping 127.0.0.1 -n 4 >nul & del /f /q \"{exe}\"{unpacked}") { CreateNoWindow = true, UseShellExecute = false });
    }

    static long Size(string dir)
    {
        try { return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }
}
