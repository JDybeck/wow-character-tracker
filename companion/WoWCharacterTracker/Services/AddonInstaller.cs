using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace WoWCharacterTracker.Services;

// The addon files are embedded in the exe (see the .csproj), so we can install and update them.
static partial class AddonInstaller
{
    const string Prefix = "Addon/";

    public static string TargetDir(string flavorDir) => Path.Combine(Wow.AddOnsDir(flavorDir), "WoWCharacterTracker");

    static string TocPath(string flavorDir) => Path.Combine(TargetDir(flavorDir), "WoWCharacterTracker.toc");

    public static string BundledVersion => ReadVersion(ReadBundled("WoWCharacterTracker.toc")) ?? "?";

    public static string? InstalledVersion(string flavorDir)
    {
        var toc = TocPath(flavorDir);
        return File.Exists(toc) ? ReadVersion(File.ReadAllText(toc)) ?? "?" : null;
    }

    // The installed addon against this app's: null when not installed, below 0 when older (or a
    // version that can't be read), 0 when the same, above 0 when newer.
    public static int? Compare(string? installed, string bundled)
    {
        if (installed is null) return null;
        if (installed == bundled) return 0;
        return Version.TryParse(installed, out var i) && Version.TryParse(bundled, out var b) ? i.CompareTo(b) : -1;
    }

    public static string? InstalledInterface(string flavorDir)
    {
        var toc = TocPath(flavorDir);
        if (!File.Exists(toc)) return null;
        var m = InterfaceLine().Match(File.ReadAllText(toc));
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    // Copies the bundled addon into the AddOns folder. The interface number is set to the
    // installed client's so the game doesn't flag the addon as out of date.
    public static void Install(string flavorDir, int? interfaceVersion)
    {
        var target = TargetDir(flavorDir);
        Directory.CreateDirectory(target);

        var asm = Assembly.GetExecutingAssembly();
        var bundled = asm.GetManifestResourceNames().Where(n => n.StartsWith(Prefix)).Select(n => n[Prefix.Length..]).ToList();
        foreach (var fileName in bundled)
        {
            var content = ReadBundled(fileName);
            if (fileName.EndsWith(".toc") && interfaceVersion is int v)
                content = InterfaceLine().Replace(content, $"## Interface: {v}", 1);
            File.WriteAllText(Path.Combine(target, fileName), content);
        }

        // Lua files an older version had but this one doesn't (the game would ignore them anyway,
        // since the .toc doesn't list them, but they'd only confuse).
        foreach (var old in Directory.GetFiles(target, "*.lua"))
            if (!bundled.Contains(Path.GetFileName(old), StringComparer.OrdinalIgnoreCase))
                File.Delete(old);
    }

    // Takes the addon out of the game's AddOns folder (the data it saved stays, in WTF). For a game
    // the user turned off; the Uninstall page removes it the same way.
    public static void Remove(string flavorDir)
    {
        var target = TargetDir(flavorDir);
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }

    // Settings.lua tells the addon which events get a screenshot, and carries the characters' notes
    // (by GUID, with when they were written) for /wct note. The game reads it on /reload or login.
    public static void WriteSettings(string flavorDir, FlavorSettings s, IEnumerable<GameNote>? notes = null)
    {
        var target = TargetDir(flavorDir);
        if (!Directory.Exists(target)) return;
        static string B(bool v) => v ? "true" : "false";
        var shots = string.Join(", ", EventKinds.ShotKinds.Select(k => $"{k} = {B(s.Shoots(k))}"));
        // What gets posted, so the addon only sends those events live and screenshots only them.
        var post = $"level = {{ on = {B(s.PostLevels)}, split = {s.LevelSplit}, before = {s.LevelEveryBefore}, after = {s.LevelEveryAfter} }}, "
                   + $"rare = {B(s.PostRare)}, epic = {B(s.PostEpic)}, mount = {B(s.PostMounts)}, boss = {B(s.PostBosses)}, death = {B(s.PostDeaths)}, "
                   + $"keyrun = {{ on = {B(s.PostKeyRuns)}, min = {s.KeyRunMinLevel}, timed = {B(s.KeyRunTimedOnly)} }}, "
                   + $"maxLevel = {B(s.PostMaxLevel)}, duel = {B(s.PostDuels)}, makgora = {B(s.PostMakgora)}, mplus = {B(s.PostMPlus)}, pvp = {B(s.PostPvp)}, "
                   + $"mplusSteps = {{ {string.Join(", ", s.MPlusSteps)} }}, pvpSteps = {{ {string.Join(", ", s.PvpSteps)} }}";
        var lua = "-- Written by the Character Tracker app from its \"What gets posted\" page. Changes apply on /reload.\n"
                  + "WoWCharacterTrackerSettings = {\n"
                  + $"  screenshot = {{ {shots} }},\n"
                  + $"  post = {{ {post} }},\n"
                  + $"  hideChat = {B(s.HideChatInShots)},\n"
                  + $"  hideUI = {B(s.HideUiInShots)},\n"
                  + $"  flightReload = {B(s.FlightReloadPrompt)},\n"
                  + $"  chatReminder = {B(s.ChatReloadReminder)},\n"
                  + $"  keepAllHistory = {B(s.KeepAllHistory)},\n"
                  + $"  announce = {{ guild = {B(s.AnnounceGuild)}, party = {B(s.AnnounceParty)}, kinds = {{ {string.Join(", ", s.AnnounceKinds.Select(k => $"{k} = true"))} }} }},\n"
                  + $"  gridCorner = \"{FlavorSettings.CornerOrDefault(s.GridCorner)}\",\n"
                  + $"  gridLabel = {B(s.GridLabel)},\n"
                  + (s.AdvancedCombatLog is bool acl ? $"  advancedCombatLog = {B(acl)},\n" : "")
                  + "  notes = {\n"
                  + string.Concat((notes ?? []).OrderBy(n => n.Guid, StringComparer.Ordinal)
                      .Select(n => $"    [{LuaString(n.Guid)}] = {{ text = {LuaString(n.Text)}, at = {n.At} }},\n"))
                  + "  },\n"
                  + "}\n";
        var file = Path.Combine(target, "Settings.lua");
        if (!File.Exists(file) || File.ReadAllText(file) != lua) Files.WriteAtomic(file, lua);
    }

    // A Lua string literal for any text. "|" is doubled: in a game font string it starts a color or
    // link code, and "||" shows a plain "|".
    internal static string LuaString(string text)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append(@"\n"); break;
                case '\r': break;
                case '\t': sb.Append(@"\t"); break;
                case '|': sb.Append("||"); break;
                default: if (!char.IsControl(c)) sb.Append(c); break;
            }
        }
        return sb.Append('"').ToString();
    }

    public static void SetInterface(string flavorDir, int interfaceVersion)
    {
        var toc = TocPath(flavorDir);
        var text = File.ReadAllText(toc);
        File.WriteAllText(toc, InterfaceLine().Replace(text, $"## Interface: {interfaceVersion}", 1));
    }

    static string ReadBundled(string fileName)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(Prefix + fileName)
                      ?? throw new InvalidOperationException($"Bundled addon file {fileName} is missing.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    static string? ReadVersion(string toc)
    {
        var m = VersionLine().Match(toc);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    [GeneratedRegex(@"^##\s*Version:\s*(.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex VersionLine();

    [GeneratedRegex(@"^##\s*Interface:\s*(.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex InterfaceLine();
}
