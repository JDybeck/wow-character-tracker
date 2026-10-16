namespace WoWCharacterTracker;

// A game version the app has a tab for. Each one lives in its own folder inside the WoW install.
sealed record Flavor(string Key, string Name, string[] Folders, string WowheadPath, Func<FlavorSettings> Defaults);

static class Flavors
{
    // Retail has fewer low level moments worth a post: every 10 levels, then every level at the top.
    public static readonly Flavor Retail = new("retail", "Retail", ["_retail_"], "", () => new FlavorSettings
    {
        LevelSplit = 80, LevelEveryBefore = 10, LevelEveryAfter = 1,
        PostRare = false, PostEpic = false, PostMounts = true, PostBosses = false, PostDeaths = false,
    });

    // Forever is what this is really made for. Its final folder name isn't known yet, so the
    // beta folder is the fallback.
    public static readonly Flavor Forever = new("forever", "Forever", ["_forever_", "_forever_beta_", "_classic_beta_"], "classic/", () => new FlavorSettings
    {
        LevelSplit = 30, LevelEveryBefore = 5, LevelEveryAfter = 1,
        PostRare = true, PostEpic = true, PostMounts = true, PostBosses = true, PostDeaths = true,
        Screenshot = new() { ["level"] = true, ["mount"] = true, ["boss"] = true, ["death"] = true },
        PostScreenshot = new() { ["level"] = true, ["mount"] = true, ["boss"] = true, ["death"] = true },
    });

    // The kinds of events that can get a screenshot, as the addon names them.
    public static readonly string[] ShotKinds = ["level", "loot", "mount", "boss", "death", "duel", "makgora"];

    public static readonly IReadOnlyList<Flavor> All = [Retail, Forever];
}

// Finds the WoW install and the files we care about inside a flavor folder (e.g. ...\_retail_).
static class Wow
{
    // Folder name to product name in .build.info, for flavor folders without a .flavor.info.
    static readonly Dictionary<string, string> KnownProducts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["_retail_"] = "wow", ["_ptr_"] = "wowt", ["_xptr_"] = "wowxptr", ["_beta_"] = "wow_beta",
        ["_classic_"] = "wow_classic", ["_classic_era_"] = "wow_classic_era", ["_classic_beta_"] = "wow_classic_beta",
        ["_classic_ptr_"] = "wow_classic_ptr", ["_anniversary_"] = "wow_anniversary",
    };

    public static string AddOnsDir(string flavorDir) => Path.Combine(flavorDir, "Interface", "AddOns");
    public static string AccountsDir(string flavorDir) => Path.Combine(flavorDir, "WTF", "Account");

    public const string SavedVarsName = "WoWCharacterTracker.lua";

    public static string SavedVarsFile(string flavorDir, string account) =>
        Path.Combine(AccountsDir(flavorDir), account, "SavedVariables", SavedVarsName);

    // A flavor folder sits inside the WoW folder, starts with "_" and has the game's files in it.
    public static bool IsFlavorDir(string? dir) =>
        !string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir)
        && Path.GetFileName(dir.TrimEnd('\\', '/')).StartsWith('_')
        && (File.Exists(Path.Combine(dir, ".flavor.info")) || Directory.Exists(Path.Combine(dir, "WTF"))
            || Directory.Exists(Path.Combine(dir, "Interface")));

    // Turns whatever the user picked (the flavor folder, the WoW folder, or an exe) into a flavor folder.
    public static string? Normalize(string input, Flavor flavor)
    {
        var path = input.Trim().Trim('"');
        if (path.Length == 0) return null;
        if (File.Exists(path)) path = Path.GetDirectoryName(path)!;
        path = path.TrimEnd('\\', '/');
        if (IsFlavorDir(path)) return path;
        return flavor.Folders.Select(f => Path.Combine(path, f)).FirstOrDefault(IsFlavorDir);
    }

    public static string? Detect(Flavor flavor) =>
        Roots().SelectMany(r => flavor.Folders.Select(f => Path.Combine(r, f))).FirstOrDefault(IsFlavorDir);

    static IEnumerable<string> Roots()
    {
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
                {
                    var dir = s.TrimEnd('\\', '/');
                    // Some entries point at a flavor folder instead of the WoW folder.
                    yield return Path.GetFileName(dir).StartsWith('_') ? Path.GetDirectoryName(dir)! : dir;
                }
        }

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            var root = drive.RootDirectory.FullName;
            yield return Path.Combine(root, "Program Files (x86)", "World of Warcraft");
            yield return Path.Combine(root, "Program Files", "World of Warcraft");
            yield return Path.Combine(root, "World of Warcraft");
            yield return Path.Combine(root, "Games", "World of Warcraft");
            yield return Path.Combine(root, "Battle.net", "World of Warcraft");
        }
    }

    // The product name in .build.info, e.g. "wow" or "wow_classic_beta".
    public static string? Product(string flavorDir)
    {
        var file = Path.Combine(flavorDir, ".flavor.info");
        if (File.Exists(file))
        {
            var lines = File.ReadAllLines(file);
            if (lines.Length >= 2 && lines[1].Trim().Length > 0) return lines[1].Trim();
        }
        return KnownProducts.GetValueOrDefault(Path.GetFileName(flavorDir.TrimEnd('\\', '/')));
    }

    // Client version for this flavor, e.g. "12.1.5.70391", read from .build.info in the WoW folder.
    public static string? ClientVersion(string flavorDir)
    {
        var product = Product(flavorDir);
        var file = Path.Combine(Path.GetDirectoryName(flavorDir.TrimEnd('\\', '/'))!, ".build.info");
        if (product is null || !File.Exists(file)) return null;
        try
        {
            var lines = File.ReadAllLines(file);
            var header = lines[0].Split('|').Select(h => h.Split('!')[0]).ToList();
            var versionCol = header.IndexOf("Version");
            var productCol = header.IndexOf("Product");
            if (versionCol < 0 || productCol < 0) return null;

            return lines.Skip(1)
                .Select(l => l.Split('|'))
                .Where(f => f.Length > Math.Max(versionCol, productCol) && f[productCol] == product)
                .Select(f => f[versionCol])
                .FirstOrDefault(v => v.Length > 0);
        }
        catch (IOException) { return null; }
    }

    // Interface number for the TOC, e.g. 12.1.5 -> 120105, 1.60.1 -> 16001.
    public static int? InterfaceVersion(string flavorDir)
    {
        var v = ClientVersion(flavorDir)?.Split('.');
        if (v is null || v.Length < 3) return null;
        return int.TryParse(v[0], out var a) && int.TryParse(v[1], out var b) && int.TryParse(v[2], out var c)
            ? a * 10000 + b * 100 + c
            : null;
    }

    public static IEnumerable<string> Accounts(string flavorDir)
    {
        var dir = AccountsDir(flavorDir);
        if (!Directory.Exists(dir)) return [];
        return Directory.GetDirectories(dir)
            .Select(Path.GetFileName)
            .Where(n => n is not null && n != "SavedVariables")!;
    }

    // Is this flavor's game running? Each flavor folder has its own exe (Wow.exe, WowB.exe, WowClassic.exe...).
    public static bool IsRunning(string flavorDir)
    {
        try
        {
            return Directory.GetFiles(flavorDir, "Wow*.exe")
                .Select(Path.GetFileNameWithoutExtension)
                .Any(n => System.Diagnostics.Process.GetProcessesByName(n!).Length > 0);
        }
        catch (IOException) { return false; }
    }
}
