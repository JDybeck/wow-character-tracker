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
        return Version.TryParse(installed, out var i) && Version.TryParse(bundled, out var b) ? Full(i).CompareTo(Full(b)) : -1;
    }

    // Missing parts count as 0, so "1.2" and "1.2.0" are the same version.
    static Version Full(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));

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
            if (!IsText(fileName))
            {
                Files.WriteAtomic(Path.Combine(target, fileName), ReadBundledBytes(fileName)); // pictures, as they are
                continue;
            }
            var content = ReadBundled(fileName);
            if (fileName.EndsWith(".toc") && interfaceVersion is int v)
                content = InterfaceLine().Replace(content, $"## Interface: {v}", 1);
            Files.WriteAtomic(Path.Combine(target, fileName), content);
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
    // hasWebhook: the tab has somewhere to post, so the addon may offer to post a highlight.
    // appUpdate: a newer app version, which the addon mentions in chat on the next login.
    // preset: which Sharing preset the settings match (or "custom"), for the addon window's choice.
    public static void WriteSettings(string flavorDir, FlavorSettings s, IEnumerable<GameNote>? notes = null, bool hasWebhook = false,
        IReadOnlyDictionary<string, string>? friendNames = null, Version? appUpdate = null, string? preset = null,
        IReadOnlyDictionary<string, long>? friendNamesAt = null)
    {
        var target = TargetDir(flavorDir);
        if (!Directory.Exists(target)) return;
        static string B(bool v) => v ? "true" : "false";
        static string LuaSet(IEnumerable<string> keys) => string.Join(", ", keys.Select(k => $"{k} = true"));
        var shots = string.Join(", ", EventKinds.ShotKinds.Select(k => $"{k} = {B(s.Shoots(k))}"));
        // What gets posted, so the addon only sends those events live and screenshots only them.
        var post = $"level = {{ on = {B(s.PostLevels)}, split = {s.LevelSplit}, before = {s.LevelEveryBefore}, after = {s.LevelEveryAfter} }}, "
                   + $"rare = {B(s.PostRare)}, followed = {B(s.PostFollowed)}, rareBoe = {B(s.RareBoeOnly)}, epic = {B(s.PostEpic)}, mount = {B(s.PostMounts)}, boss = {B(s.PostBosses)}, run = {B(s.PostRuns)}, death = {B(s.PostDeaths)}, "
                   + $"keyrun = {{ on = {B(s.PostKeyRuns)}, min = {s.KeyRunMinLevel}, timed = {B(s.KeyRunTimedOnly)} }}, "
                   + $"maxLevel = {B(s.PostMaxLevel)}, duel = {B(s.PostDuels)}, makgora = {B(s.PostMakgora)}, bg = {B(s.PostBattlegrounds)}, mplus = {B(s.PostMPlus)}, pvp = {B(s.PostPvp)}, "
                   + $"profession = {B(s.PostProfessions)}, professionSteps = {{ {string.Join(", ", s.ProfessionSteps)} }}, "
                   + $"mplusSteps = {{ {string.Join(", ", s.MPlusSteps)} }}, pvpSteps = {{ {string.Join(", ", s.PvpSteps)} }}";
        var lua = "-- Written by the Character Tracker app from its Sharing page. Changes apply on /reload.\n"
                  + "WoWCharacterTrackerSettings = {\n"
                  + $"  screenshot = {{ {shots} }},\n"
                  + $"  post = {{ {post} }},\n"
                  // The followed items, since the addon only records rare and better otherwise.
                  + $"  followed = {{ {string.Concat(s.AllFollowed.Select(i => $"[{i.Id}] = true, "))}}},\n"
                  + $"  hideChat = {B(s.HideChatInShots)},\n"
                  + $"  hideUI = {B(s.HideUiInShots)},\n"
                  + $"  flightReload = {B(s.FlightReloadPrompt)},\n"
                  + $"  chatReminder = {B(s.ChatReloadReminder)},\n"
                  + $"  keepAllHistory = {B(s.KeepAllHistory)},\n"
                  + $"  announce = {{ guild = {B(s.AnnounceGuild)}, party = {B(s.AnnounceParty)}, kinds = {{ {LuaSet(s.AnnounceKinds)} }}, "
                  + $"partyKinds = {{ {LuaSet(s.AnnouncePartyKinds ?? s.AnnounceKinds)} }} }},\n"
                  + $"  inGameAt = {s.InGameAt},\n"
                  + $"  ask = {{ {LuaSet(s.AskKinds ?? [])} }},\n"
                  + $"  askSeconds = {FlavorSettings.AskSecondsOrDefault(s.AskSeconds)},\n"
                  // Characters removed in the app and when: never shared with friends until played again.
                  + $"  removed = {{ {string.Concat(s.Removed.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"[{LuaString(r.Key)}] = {r.Value}, "))}}},\n"
                  // Characters not tracked: nothing recorded, announced or shared for them (an In game setting).
                  + $"  ignored = {{ {string.Concat(s.Ignored.Order(StringComparer.Ordinal).Select(g => $"[{LuaString(g)}] = true, "))}}},\n"
                  + $"  discord = {B(hasWebhook)},\n"
                  + $"  gridCorner = \"{FlavorSettings.CornerOrDefault(s.GridCorner)}\",\n"
                  + $"  gridLabel = {B(s.GridLabel)},\n"
                  + (appUpdate is { } update ? $"  appUpdate = \"{update.ToString(3)}\",\n" : "")
                  + (preset is { Length: > 0 } ? $"  preset = {LuaString(preset)},\n" : "")
                  + (s.AdvancedCombatLog is bool acl ? $"  advancedCombatLog = {B(acl)},\n" : "")
                  + "  notes = {\n"
                  + string.Concat((notes ?? []).OrderBy(n => n.Guid, StringComparer.Ordinal)
                      .Select(n => $"    [{LuaString(n.Guid)}] = {{ text = {LuaString(n.Text)}, at = {n.At} }},\n"))
                  + "  },\n"
                  + ShareLua(s.Share)
                  + "  friendNames = { "
                  + string.Concat((friendNames ?? new Dictionary<string, string>()).Where(n => n.Value.Length > 0).OrderBy(n => n.Key, StringComparer.Ordinal)
                      .Select(n => $"[{LuaString(n.Key)}] = {LuaString(n.Value)}, "))
                  + "},\n"
                  // When each was set (or cleared) in the app: a newer one set in game wins (Friends.lua label).
                  + "  friendNameAt = { "
                  + string.Concat((friendNamesAt ?? new Dictionary<string, long>()).OrderBy(n => n.Key, StringComparer.Ordinal)
                      .Select(n => $"[{LuaString(n.Key)}] = {n.Value}, "))
                  + "},\n"
                  + "}\n";
        var file = Path.Combine(target, "Settings.lua");
        if (!File.Exists(file) || File.ReadAllText(file) != lua) Files.WriteAtomic(file, lua);
    }

    // Sharing with friends (the addon's Friends.lua `share`), only when the app has a copy: otherwise
    // the game's own settings stay. A contact's category left out follows the general choice.
    static string ShareLua(ShareSettings? share)
    {
        if (share is null) return "";
        static string B(bool v) => v ? "true" : "false";
        static string Opt(string key, bool? v) => v is bool b ? $", {key} = {B(b)}" : "";
        var d = share.Defaults;
        var contacts = string.Concat(share.Contacts.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c =>
            $"[{LuaString(c.Key)}] = {{ share = {B(c.Value.Share)}, receive = {B(c.Value.Receive)}"
            + Opt("gear", c.Value.Gear) + Opt("talents", c.Value.Talents) + Opt("location", c.Value.Location) + Opt("gold", c.Value.Gold) + Opt("professions", c.Value.Professions)
            + Opt("map", c.Value.Map) + Opt("otherFaction", c.Value.OtherFaction) + Opt("highlights", c.Value.Highlights) + Opt("leveling", c.Value.Leveling) + " }, "));
        var chars = string.Concat(share.Chars.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => $"[{LuaString(c.Key)}] = {B(c.Value)}, "));
        var blocked = string.Concat(share.Blocked.Order(StringComparer.Ordinal).Select(t => $"[{LuaString(t)}] = true, "));
        return $"  share = {{ enabled = {B(share.Enabled)}, newChars = {B(share.NewChars)}, at = {share.At}, mapRate = {share.MapRate},\n"
               + $"    defaults = {{ gear = {B(d.Gear)}, talents = {B(d.Talents)}, location = {B(d.Location)}, gold = {B(d.Gold)}, professions = {B(d.Professions)}, map = {B(d.Map)}, otherFaction = {B(d.OtherFaction)}, highlights = {B(d.Highlights)}, leveling = {B(d.Leveling)} }},\n"
               + $"    contacts = {{ {contacts}}},\n"
               + $"    chars = {{ {chars}}},\n"
               + $"    blocked = {{ {blocked}}} }},\n";
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
        Files.WriteAtomic(toc, InterfaceLine().Replace(text, $"## Interface: {interfaceVersion}", 1));
    }

    static readonly string[] TextExtensions = [".lua", ".toc", ".xml", ".md", ".txt"];

    static bool IsText(string fileName) => TextExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

    static Stream OpenBundled(string fileName) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(Prefix + fileName)
        ?? throw new InvalidOperationException($"Bundled addon file {fileName} is missing.");

    static string ReadBundled(string fileName)
    {
        using var r = new StreamReader(OpenBundled(fileName));
        return r.ReadToEnd();
    }

    static byte[] ReadBundledBytes(string fileName)
    {
        using var s = OpenBundled(fileName);
        using var bytes = new MemoryStream();
        s.CopyTo(bytes);
        return bytes.ToArray();
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
