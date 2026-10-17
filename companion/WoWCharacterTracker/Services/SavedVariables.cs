using MoonSharp.Interpreter;

namespace WoWCharacterTracker.Services;

static class SavedVariables
{
    public static TrackerData Read(string path)
    {
        string text;
        // The game may still have the file open, so share it.
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var sr = new StreamReader(fs))
        {
            text = sr.ReadToEnd();
        }

        var script = new Script(CoreModules.Preset_SoftSandbox);
        script.DoString(text);

        var db = script.Globals.Get("WoWCharacterTrackerDB");
        if (db.Type != DataType.Table) return TrackerData.Empty;

        return new TrackerData(ReadEvents(db.Table.Get("events")), ReadCharacters(db.Table.Get("chars")))
        {
            Inventories = ReadInventories(db.Table.Get("items")),
            XpTable = ReadXpTable(db.Table.Get("xpTable")),
            ItemClasses = ReadItemClasses(db.Table.Get("itemClasses")),
        };
    }

    // itemClasses[itemId] = the item's class (consumable, weapon, armor, trade goods...).
    static Dictionary<int, int> ReadItemClasses(DynValue table)
    {
        var classes = new Dictionary<int, int>();
        if (table.Type != DataType.Table) return classes;
        foreach (var p in table.Table.Pairs)
            if (p.Key.CastToNumber() is double id && p.Value.CastToNumber() is double c) classes[(int)id] = (int)c;
        return classes;
    }

    static List<Entry> ReadEvents(DynValue events)
    {
        var list = new List<Entry>();
        if (events.Type != DataType.Table) return list;

        foreach (var v in events.Table.Values)
        {
            if (v.Type != DataType.Table) continue;
            var t = v.Table;
            list.Add(Entry.From(k => S(t, k), k => N(t, k)));
        }

        return list.Where(x => x.Id.Length > 0).ToList();
    }

    static List<Snapshot> ReadCharacters(DynValue chars)
    {
        var list = new List<Snapshot>();
        if (chars.Type != DataType.Table) return list;

        foreach (var pair in chars.Table.Pairs)
        {
            if (pair.Value.Type != DataType.Table || pair.Key.Type != DataType.String) continue;
            var t = pair.Value.Table;
            list.Add(new Snapshot(
                pair.Key.String, S(t, "char"), S(t, "realm"), (int)N(t, "level"),
                S(t, "class"), S(t, "classFile"), S(t, "race"), S(t, "raceFile"),
                S(t, "sex") == "female" ? "female" : "male",
                S(t, "spec"), S(t, "role"), N(t, "ilvl"), (long)N(t, "played"),
                S(t, "zone"), S(t, "professions"), S(t, "talents"), (long)N(t, "lastSeen"),
                S(t, "hardcore") == "true", S(t, "ruleset"), (long)N(t, "money"), S(t, "crests"),
                t.Get("guild").IsNil() ? null : S(t, "guild"), S(t, "surname"), S(t, "talentPicks"), S(t, "talentExport"), S(t, "stats"), S(t, "talentCalc"), S(t, "region"),
                (long)N(t, "xp"), (long)N(t, "xpMax"), (int)N(t, "maxLevel")));
        }

        return list;
    }

    // items[guid]: gear, bags and bank (the addon's Inventory.lua).
    static List<Inventory> ReadInventories(DynValue items)
    {
        if (items.Type != DataType.Table) return [];
        return items.Table.Pairs
            .Where(p => p.Key.Type == DataType.String && p.Value.Type == DataType.Table)
            .Select(p => Inventory.Parse(p.Key.String, k => S(p.Value.Table, k), k => N(p.Value.Table, k)))
            .ToList();
    }

    // xpTable[level] = XP that level needs on this client.
    static Dictionary<int, long> ReadXpTable(DynValue table)
    {
        var xp = new Dictionary<int, long>();
        if (table.Type != DataType.Table) return xp;
        foreach (var p in table.Table.Pairs)
            if (p.Key.CastToNumber() is double level && p.Value.CastToNumber() is double need && need > 0) xp[(int)level] = (long)need;
        return xp;
    }

    // CastToString skips booleans, so those become "true"/"false" here (the same as the live channel sends).
    static string S(Table t, string key)
    {
        var v = t.Get(key);
        return v.Type == DataType.Boolean ? (v.Boolean ? "true" : "false") : v.CastToString() ?? "";
    }
    static double N(Table t, string key) => t.Get(key).CastToNumber() ?? 0;
}
