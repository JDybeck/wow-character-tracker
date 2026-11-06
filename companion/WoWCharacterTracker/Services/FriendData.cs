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
        ["char"] = 24, ["realm"] = 40, ["class"] = 24, ["classFile"] = 16, ["race"] = 24, ["raceFile"] = 16,
        ["faction"] = 10, ["spec"] = 32, ["talents"] = 1024, ["talentCalc"] = 256, ["gear"] = 512, ["enchants"] = 2048, ["zone"] = 64,
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
                Number(t.Get("refused")) > 0, [.. chars.Select(c => c.Snapshot)], [.. chars.Select(c => c.Inventory)]));
        }
        return list;
    }

    static List<(Snapshot Snapshot, Inventory Inventory)> ReadCharacters(DynValue chars)
    {
        var list = new List<(Snapshot, Inventory)>();
        if (chars.Type != DataType.Table) return list;
        foreach (var p in chars.Table.Pairs)
        {
            if (p.Value.Type != DataType.Table) continue;
            var t = p.Value.Table;
            string S(string key) => Text(t.Get(key), Max[key], lines: key == "enchants");
            double N(string key) => Number(t.Get(key));
            var guid = t.Get("guid").Type == DataType.String ? t.Get("guid").String : "";
            var name = S("char");
            if (!PlayerGuid().IsMatch(guid) || name.Length == 0) continue;
            var snapshot = new Snapshot(guid, name, S("realm"), (int)N("level"), S("class"), S("classFile"), S("race"), S("raceFile"),
                Sex: "", Spec: S("spec"), Role: "", ItemLevel: N("ilvl"), Played: 0, Zone: S("zone"), Professions: "", Talents: S("talents"),
                LastSeen: (long)N("at"), Hardcore: false, Ruleset: "", Money: (long)N("money"), TalentCalc: S("talentCalc"), Faction: S("faction"));
            var inventory = Inventory.Parse(guid, k => k is "gear" or "enchants" ? S(k) : "", _ => 0);
            list.Add((snapshot, inventory));
        }
        // At most MaxCharacters: the ones seen longest ago go.
        return [.. list.OrderByDescending(c => c.Item1.LastSeen).Take(MaxCharacters)];
    }

    public static ShareSettings? ReadShare(DynValue share)
    {
        if (share.Type != DataType.Table) return null;
        var t = share.Table;
        bool? Flag(Table table, string key) => table.Get(key).Type == DataType.Boolean ? table.Get(key).Boolean : null;
        var d = t.Get("defaults").Type == DataType.Table ? t.Get("defaults").Table : null;
        var general = new GeneralCategories();
        if (d is not null)
            general = new(Flag(d, "gear") ?? general.Gear, Flag(d, "talents") ?? general.Talents,
                Flag(d, "location") ?? general.Location, Flag(d, "gold") ?? general.Gold);
        var chars = new Dictionary<string, bool>();
        foreach (var p in Pairs(t.Get("chars")))
            if (p.Value.Type == DataType.Boolean) chars[p.Key.String] = p.Value.Boolean;
        var contacts = new Dictionary<string, ShareContact>();
        foreach (var p in Pairs(t.Get("contacts")))
            if (p.Value.Type == DataType.Table)
            {
                var c = p.Value.Table;
                contacts[p.Key.String] = new(Flag(c, "share") == true, Flag(c, "receive") == true,
                    Flag(c, "gear"), Flag(c, "talents"), Flag(c, "location"), Flag(c, "gold"));
            }
        var blocked = Pairs(t.Get("blocked")).Where(p => p.Value.CastToBool()).Select(p => p.Key.String).ToHashSet();
        return new(Flag(t, "enabled") == true, Flag(t, "newChars") ?? true, general, chars, contacts, blocked, (long)Number(t.Get("at")));
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
