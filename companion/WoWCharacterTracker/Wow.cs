namespace WoWCharacterTracker;

// Finds the WoW install and the files we care about inside it.
static class Wow
{
    public const string Flavor = "_retail_";
    const string Product = "wow"; // the retail product name in .build.info

    public static string FlavorDir(string wowPath) => Path.Combine(wowPath, Flavor);
    public static string AddOnsDir(string wowPath) => Path.Combine(FlavorDir(wowPath), "Interface", "AddOns");
    public static string AccountsDir(string wowPath) => Path.Combine(FlavorDir(wowPath), "WTF", "Account");

    public static bool IsValid(string? wowPath) =>
        !string.IsNullOrWhiteSpace(wowPath) && Directory.Exists(FlavorDir(wowPath));

    // Turns whatever folder the user pasted (the game folder, _retail_, or Wow.exe) into the game folder.
    public static string? Normalize(string input)
    {
        var path = input.Trim().Trim('"');
        if (path.Length == 0) return null;
        if (File.Exists(path)) path = Path.GetDirectoryName(path)!;
        if (Path.GetFileName(path.TrimEnd('\\', '/')).Equals(Flavor, StringComparison.OrdinalIgnoreCase))
            path = Path.GetDirectoryName(path.TrimEnd('\\', '/'))!;
        return IsValid(path) ? path : null;
    }

    public static string? Detect()
    {
        var candidates = new List<string>();

        // The Battle.net uninstall entry is the most reliable pointer.
        foreach (var key in new[]
                 {
                     @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\World of Warcraft",
                     @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\World of Warcraft",
                     @"SOFTWARE\WOW6432Node\Blizzard Entertainment\World of Warcraft",
                 })
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key);
            foreach (var name in new[] { "InstallLocation", "InstallPath" })
                if (k?.GetValue(name) is string s && s.Length > 0)
                    candidates.Add(s);
        }

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            var root = drive.RootDirectory.FullName;
            candidates.Add(Path.Combine(root, "Program Files (x86)", "World of Warcraft"));
            candidates.Add(Path.Combine(root, "Program Files", "World of Warcraft"));
            candidates.Add(Path.Combine(root, "World of Warcraft"));
            candidates.Add(Path.Combine(root, "Games", "World of Warcraft"));
            candidates.Add(Path.Combine(root, "Battle.net", "World of Warcraft"));
        }

        // The registry can point at the _retail_ folder or somewhere unrelated, so verify each one.
        return candidates.Select(c => Normalize(c)).FirstOrDefault(c => c is not null);
    }

    // Interface number for the installed retail client, e.g. 12.1.5 -> 120105. Read from .build.info.
    public static int? InterfaceVersion(string wowPath)
    {
        var file = Path.Combine(wowPath, ".build.info");
        if (!File.Exists(file)) return null;
        try
        {
            var lines = File.ReadAllLines(file);
            var header = lines[0].Split('|').Select(h => h.Split('!')[0]).ToList();
            var versionCol = header.IndexOf("Version");
            var productCol = header.IndexOf("Product");
            if (versionCol < 0 || productCol < 0) return null;

            foreach (var line in lines.Skip(1))
            {
                var f = line.Split('|');
                if (f.Length <= Math.Max(versionCol, productCol) || f[productCol] != Product) continue;
                var v = f[versionCol].Split('.');
                if (v.Length < 3) continue;
                return int.Parse(v[0]) * 10000 + int.Parse(v[1]) * 100 + int.Parse(v[2]);
            }
        }
        catch (Exception ex) when (ex is IOException or FormatException or IndexOutOfRangeException) { }
        return null;
    }

    public static IEnumerable<string> Accounts(string wowPath)
    {
        var dir = AccountsDir(wowPath);
        if (!Directory.Exists(dir)) return [];
        return Directory.GetDirectories(dir)
            .Select(Path.GetFileName)
            .Where(n => n is not null && n != "SavedVariables")!;
    }

    public static string SavedVarsFile(string wowPath, string account) =>
        Path.Combine(AccountsDir(wowPath), account, "SavedVariables", "WoWCharacterTracker.lua");

    public static bool IsRunning() =>
        System.Diagnostics.Process.GetProcessesByName("Wow").Length > 0;
}
