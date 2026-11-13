using System.Globalization;
using System.Text.RegularExpressions;
using MoonSharp.Interpreter;

namespace WoWCharacterTracker.Services;

// What Battle.net friends shared (WoWCharacterTrackerDB.friends) and the sharing settings
// (WoWCharacterTrackerDB.share). Friends' data comes from someone else's PC, so it's checked again
// with the addon's rules (FriendData.lua): known fields only, strings cut to length, finite numbers.
static partial class FriendData
{
    const int MaxCharacters = 50;

    // Longest length per text field (the addon's FIELDS).
    static readonly Dictionary<string, int> Max = new()
    {
        ["char"] = 24, ["surname"] = 24, ["realm"] = 40, ["ruleset"] = 24, ["class"] = 24, ["classFile"] = 16, ["race"] = 24, ["raceFile"] = 16, ["sex"] = 6,
        ["faction"] = 10, ["spec"] = 32, ["talents"] = 1024, ["talentCalc"] = 256, ["gear"] = 512, ["enchants"] = 2048, ["zone"] = 64, ["professions"] = 256,
        ["highlights"] = 1024, ["levels"] = 1600, ["recipes"] = 3000,
    };
    const int MaxLevels = 80;

    // A highlight line is "kind~at~level" and then these, in the addon's order (FriendData.lua HIGHLIGHT_FIELDS).
    const int MaxHighlights = 10;
    static readonly Dictionary<string, string[]> HighlightFields = new()
    {
        ["boss"] = ["boss", "instance", "groupSize"],
        ["keyrun"] = ["instance", "keyLevel", "upgrades"],
        ["loot"] = ["item", "itemId", "quality"],
        ["mount"] = ["mount", "itemId", "bought"],
        ["duel"] = ["opponent", "lost", "fled"],
        ["makgora"] = ["opponent", "lost"],
        ["bg"] = ["instance", "lost"],
        ["profession"] = ["profession", "step"],
        ["mplus"] = ["step", "rating"],
        ["pvp"] = ["step", "bracket", "rating"],
        ["death"] = ["killer", "played", "gold", "zone"],
    };

    public static List<FriendContact> ReadFriends(DynValue friends)
    {
        var list = new List<FriendContact>();
        if (friends.Type != DataType.Table) return list;
        foreach (var p in friends.Table.Pairs)
        {
            if (p.Key.Type != DataType.String || p.Value.Type != DataType.Table) continue;
            var t = p.Value.Table;
            var version = t.Get("version").Type == DataType.String && VersionText().IsMatch(t.Get("version").String) ? t.Get("version").String : "";
            var chars = ReadCharacters(t.Get("chars"));
            list.Add(new FriendContact(p.Key.String, (long)Number(t.Get("seen")), version, (int)Number(t.Get("protocol")),
                Number(t.Get("refused")) > 0, [.. chars.Select(c => c.Snapshot)], [.. chars.Select(c => c.Inventory).OfType<Inventory>()],
                [.. chars.SelectMany(c => c.Highlights)]));
        }
        return list;
    }

    // Several WoW accounts on one PC, each with its own saved friends: per friend, the newest news wins.
    public static List<FriendContact> Merge(IEnumerable<List<FriendContact>> perAccount) =>
        [.. perAccount.SelectMany(f => f).GroupBy(f => f.Tag).Select(g => g.MaxBy(f => f.Seen)!)];

    static List<(Snapshot Snapshot, Inventory? Inventory, List<Entry> Highlights)> ReadCharacters(DynValue chars)
    {
        var list = new List<(Snapshot, Inventory?, List<Entry>)>();
        if (chars.Type != DataType.Table) return list;
        foreach (var p in chars.Table.Pairs)
        {
            if (p.Value.Type != DataType.Table) continue;
            var t = p.Value.Table;
            string S(string key) => Text(t.Get(key), Max[key], lines: key is "enchants" or "highlights" or "levels" or "recipes");
            double N(string key) => Number(t.Get(key));
            var guid = t.Get("guid").Type == DataType.String ? t.Get("guid").String : "";
            var name = S("char");
            if (!PlayerGuid().IsMatch(guid) || name.Length == 0) continue;
            var snapshot = new Snapshot(guid, name, S("realm"), (int)N("level"), S("class"), S("classFile"), S("race"), S("raceFile"),
                Sex: S("sex") == "female" ? "female" : "male", Spec: S("spec"), Role: "", ItemLevel: N("ilvl"), Played: Whole(N("played")), Zone: S("zone"), Professions: S("professions"), Talents: S("talents"),
                LastSeen: (long)N("at"), Hardcore: false, Ruleset: S("ruleset"), Money: (long)N("money"), TalentCalc: S("talentCalc"), Faction: S("faction"),
                Xp: Whole(N("xp")), XpMax: Whole(N("xpMax")), MaxLevel: (int)Math.Min(Whole(N("maxLevel")), 200), LevelTime: Whole(N("levelTime")), Recipes: S("recipes")) { Surname = S("surname") };
            // No gear shared: no inventory, so the gear tab says it isn't shared.
            var inventory = S("gear").Length > 0 ? Inventory.Parse(guid, k => k is "gear" or "enchants" ? S(k) : "", _ => 0) : null;
            list.Add((snapshot, inventory, [.. ReadHighlights(snapshot, S("highlights")), .. ReadLevels(snapshot, S("levels"))]));
        }
        // At most MaxCharacters: the ones seen longest ago go.
        return [.. list.OrderByDescending(c => c.Item1.LastSeen).Take(MaxCharacters)];
    }

    // The character's highlights, one per line, as events of that character. Unknown kinds and
    // lines without a time are dropped, numbers must be whole and not negative.
    static IEnumerable<Entry> ReadHighlights(Snapshot c, string text)
    {
        foreach (var line in text.Split('\n').Take(MaxHighlights))
        {
            var p = line.Split('~');
            if (p.Length < 3 || !HighlightFields.TryGetValue(p[0], out var keys)
                || !long.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out var at) || at <= 0) continue;
            var values = new Dictionary<string, string>
            {
                ["id"] = $"{c.Guid}:{p[0]}:{at}", ["kind"] = p[0], ["at"] = p[1], ["level"] = p[2], ["guid"] = c.Guid, ["char"] = c.Char, ["realm"] = c.Realm,
            };
            for (var i = 0; i < keys.Length && i + 3 < p.Length; i++) values[keys[i]] = p[i + 3];
            yield return Entry.From(k => values.GetValueOrDefault(k, ""),
                k => long.TryParse(values.GetValueOrDefault(k), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n < 1_000_000_000_000 ? n : 0);
        }
    }

    // The character's level times, "level~seconds~at" per line (FriendData.lua), as level events, so
    // the Leveling tab works as for your own. Bad lines are dropped, one per level.
    static IEnumerable<Entry> ReadLevels(Snapshot c, string text)
    {
        var seen = new HashSet<int>();
        foreach (var line in text.Split('\n').Take(MaxLevels))
        {
            var p = line.Split('~');
            if (p.Length != 3 || !int.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out var level) || level is < 2 or > 200
                || !long.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0 || seconds > 100_000_000
                || !long.TryParse(p[2], NumberStyles.None, CultureInfo.InvariantCulture, out var at) || at >= 1_000_000_000_000 || !seen.Add(level)) continue;
            var values = new Dictionary<string, string>
            {
                ["id"] = $"{c.Guid}:{level}", ["kind"] = "level", ["guid"] = c.Guid, ["char"] = c.Char, ["realm"] = c.Realm,
            };
            var numbers = new Dictionary<string, long> { ["level"] = level, ["seconds"] = seconds, ["at"] = at };
            yield return Entry.From(k => values.GetValueOrDefault(k, ""), k => numbers.GetValueOrDefault(k));
        }
    }

    // A count from a friend: whole, not negative, and not absurdly large.
    static long Whole(double n) => n is >= 0 and < 1_000_000_000_000 ? (long)n : 0;

    public static ShareSettings? ReadShare(DynValue share)
    {
        if (share.Type != DataType.Table) return null;
        var t = share.Table;
        bool? Flag(Table table, string key) => table.Get(key).Type == DataType.Boolean ? table.Get(key).Boolean : null;
        var d = t.Get("defaults").Type == DataType.Table ? t.Get("defaults").Table : null;
        var general = new GeneralCategories();
        if (d is not null)
            general = new(Flag(d, "gear") ?? general.Gear, Flag(d, "talents") ?? general.Talents,
                Flag(d, "location") ?? general.Location, Flag(d, "gold") ?? general.Gold, Flag(d, "professions") ?? general.Professions,
                Flag(d, "map") ?? general.Map, Flag(d, "otherFaction") ?? general.OtherFaction, Flag(d, "highlights") ?? general.Highlights, Flag(d, "leveling") ?? general.Leveling);
        var chars = new Dictionary<string, bool>();
        foreach (var p in Pairs(t.Get("chars")))
            if (p.Value.Type == DataType.Boolean) chars[p.Key.String] = p.Value.Boolean;
        var contacts = new Dictionary<string, ShareContact>();
        foreach (var p in Pairs(t.Get("contacts")))
            if (p.Value.Type == DataType.Table)
            {
                var c = p.Value.Table;
                contacts[p.Key.String] = new(Flag(c, "share") == true, Flag(c, "receive") == true,
                    Flag(c, "gear"), Flag(c, "talents"), Flag(c, "location"), Flag(c, "gold"), Flag(c, "professions"),
                    Flag(c, "map"), Flag(c, "otherFaction"), Flag(c, "highlights"), Flag(c, "leveling"));
            }
        var blocked = Pairs(t.Get("blocked")).Where(p => p.Value.CastToBool()).Select(p => p.Key.String).ToHashSet();
        var rate = (int)Number(t.Get("mapRate"));
        return new(Flag(t, "enabled") ?? true, Flag(t, "newChars") ?? true, general, chars, contacts, blocked, (long)Number(t.Get("at")),
            ShareSettings.MapRates.Contains(rate) ? rate : 5);
    }

    // A table's pairs with string keys (anything else is ignored).
    static IEnumerable<TablePair> Pairs(DynValue v) =>
        v.Type == DataType.Table ? v.Table.Pairs.Where(p => p.Key.Type == DataType.String) : [];

    // No | (WoW's escape character) and no control characters (line breaks only where allowed), cut to max.
    static string Text(DynValue v, int max, bool lines = false)
    {
        if (v.Type != DataType.String) return "";
        var s = new string([.. v.String.Where(c => c != '|' && (!char.IsControl(c) || (lines && c == '\n')))]);
        return s.Length > max ? s[..max] : s;
    }

    static double Number(DynValue v) => v.CastToNumber() is double n && double.IsFinite(n) ? n : 0;

    [GeneratedRegex(@"^Player-\d+-[0-9A-Fa-f]+$")]
    private static partial Regex PlayerGuid();

    [GeneratedRegex(@"^[\w.\-]{1,16}$")]
    private static partial Regex VersionText();
}
