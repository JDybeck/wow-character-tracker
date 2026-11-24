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
            Notes = ReadNotes(db.Table.Get("notes")),
            Nicknames = ReadNicknames(db.Table.Get("friendNames")),
            FriendOrder = ReadFriendOrder(db.Table.Get("friendOrder")),
            OwnPicks = ReadOwnPicks(db.Table.Get("ownPicks")),
            InGame = ReadInGame(db.Table.Get("gameSettings")),
            Friends = FriendData.ReadFriends(db.Table.Get("friends")),
            Share = FriendData.ReadShare(db.Table.Get("share")),
            Incognito = IncognitoWindow.Read(db.Table.Get("incognito")),
            Today = ReadToday(db.Table.Get("today")),
            Days = ReadDays(db.Table.Get("days")),
            CleanedAt = db.Table.Get("cleanedAt") is { Type: DataType.Number } cleaned ? (long)cleaned.Number : 0,
        };
    }

    // today[guid] = { day, ilvlStart, ilvl, moneyStart, money, quests, herbs, ore, skins, fish } (Today.lua).
    internal static List<DayTotals> ReadToday(DynValue table)
    {
        if (table.Type != DataType.Table) return [];
        return [.. table.Table.Pairs
            .Where(p => p.Key.Type == DataType.String && p.Value.Type == DataType.Table)
            .Select(p => Day(p.Key.String, p.Value.Table))];
    }

    // days[guid][day] = a finished day, the same fields as today's (Today.lua archive).
    internal static List<DayTotals> ReadDays(DynValue table)
    {
        if (table.Type != DataType.Table) return [];
        return [.. table.Table.Pairs
            .Where(p => p.Key.Type == DataType.String && p.Value.Type == DataType.Table)
            .SelectMany(p => p.Value.Table.Pairs
                .Where(d => d.Key.Type == DataType.String && d.Value.Type == DataType.Table)
                .Select(d => Day(p.Key.String, d.Value.Table) with { Day = d.Key.String }))];
    }

    static DayTotals Day(string guid, Table t) =>
        new(guid, S(t, "day"), N(t, "ilvlStart"), N(t, "ilvl"), (long)N(t, "moneyStart"), (long)N(t, "money"),
            (int)N(t, "quests"), (int)N(t, "herbs"), (int)N(t, "ore"), (int)N(t, "skins"), (int)N(t, "fish"));

    // gameSettings: the In game settings as changed in the addon's options page (Shared.lua editSettings).
    internal static InGameSettings? ReadInGame(DynValue value)
    {
        if (value.Type != DataType.Table) return null;
        var t = value.Table;
        var at = (long)N(t, "at");
        if (at <= 0) return null;
        static List<string> On(DynValue kinds) => kinds.Type != DataType.Table ? [] :
            [.. kinds.Table.Pairs.Where(p => p.Key.Type == DataType.String && p.Value.CastToBool()).Select(p => p.Key.String)];
        // A setting the change doesn't have is null: the addon then reads the app's value (Shared.lua ns.setting).
        bool? Flag(string key) => t.Get(key).Type == DataType.Boolean ? t.Get(key).Boolean : null;
        var announce = t.Get("announce");
        var a = announce.Type == DataType.Table ? announce.Table : null; // the addon takes this table whole
        var ask = t.Get("ask");
        return new InGameSettings(at,
            a?.Get("guild").CastToBool(), a?.Get("party").CastToBool(),
            a is null ? null : On(a.Get("kinds")),
            a?.Get("partyKinds").Type == DataType.Table ? On(a.Get("partyKinds")) : null, // an older addon had one list for both
            ask.Type == DataType.Table ? On(ask) : null,
            t.Get("gridCorner").Type == DataType.String ? t.Get("gridCorner").String : null,
            Flag("gridLabel"), Flag("keepAllHistory"),
            t.Get("askSeconds").CastToNumber() is double seconds ? (int)seconds : null,
            t.Get("ignored").Type == DataType.Table ? On(t.Get("ignored")) : null,
            t.Get("preset").Type == DataType.String ? t.Get("preset").String : null,
            ReadFilters(t.Get("filters")),
            Switches(t.Get("posts")), Switches(t.Get("screenshot")),
            t.Get("distanceUnit").Type == DataType.String ? t.Get("distanceUnit").String : null);
    }

    // { key = true/false }: the Sharing table's Discord and screenshot boxes, off ones included.
    static Dictionary<string, bool>? Switches(DynValue value)
    {
        if (value.Type != DataType.Table) return null;
        return value.Table.Pairs.Where(p => p.Key.Type == DataType.String && p.Value.Type == DataType.Boolean)
            .ToDictionary(p => p.Key.String, p => p.Value.Boolean);
    }

    static GameFilters? ReadFilters(DynValue value)
    {
        if (value.Type != DataType.Table) return null;
        var f = value.Table;
        int? Number(string key) => f.Get(key).CastToNumber() is double n ? (int)n : null;
        bool? Flag(string key) => f.Get(key).Type == DataType.Boolean ? f.Get(key).Boolean : null;
        return new GameFilters(Number("split"), Number("before"), Number("after"), Flag("rareBoe"), Flag("firstKill"));
    }

    // notes[guid] = { text, at }: notes edited in game. The game keeps "|" as "||".
    static List<GameNote> ReadNotes(DynValue table)
    {
        var notes = new List<GameNote>();
        if (table.Type != DataType.Table) return notes;
        foreach (var p in table.Table.Pairs)
            if (p.Key.Type == DataType.String && p.Value.Type == DataType.Table)
                notes.Add(new GameNote(p.Key.String, S(p.Value.Table, "text").Replace("||", "|"), (long)N(p.Value.Table, "at")));
        return notes;
    }

    // friendNames[battleTag] = { name, at }: friends' nicknames set in game ("" = cleared).
    internal static List<GameNickname> ReadNicknames(DynValue table)
    {
        if (table.Type != DataType.Table) return [];
        return [.. table.Table.Pairs
            .Where(p => p.Key.Type == DataType.String && p.Value.Type == DataType.Table && N(p.Value.Table, "at") > 0)
            .Select(p => new GameNickname(p.Key.String, S(p.Value.Table, "name").Replace("||", "|"), (long)N(p.Value.Table, "at")))];
    }

    // friendOrder = { tags = { battleTag, ... }, at }: the Friends tab's order, dragged in game.
    internal static GameFriendOrder? ReadFriendOrder(DynValue value)
    {
        if (value.Type != DataType.Table) return null;
        var at = (long)N(value.Table, "at");
        var tags = value.Table.Get("tags");
        if (at <= 0 || tags.Type != DataType.Table) return null;
        var list = tags.Table;
        return new GameFriendOrder([.. Enumerable.Range(1, list.Length).Select(i => list.Get(i))
            .Where(v => v.Type == DataType.String).Select(v => v.String).Distinct()], at);
    }

    // ownPicks = { main = guid, pinned = { guid, ... }, at }: your main and pinned characters, picked in game.
    internal static GameOwnPicks? ReadOwnPicks(DynValue value)
    {
        if (value.Type != DataType.Table) return null;
        var at = (long)N(value.Table, "at");
        if (at <= 0) return null;
        var main = value.Table.Get("main") is { Type: DataType.String } m ? m.String : "";
        var pinned = value.Table.Get("pinned");
        List<string> list = pinned.Type != DataType.Table ? [] : [.. Enumerable.Range(1, pinned.Table.Length).Select(i => pinned.Table.Get(i))
            .Where(v => v.Type == DataType.String).Select(v => v.String).Distinct()];
        return new GameOwnPicks(main, list, at);
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
            var e = Entry.From(k => S(t, k), k => N(t, k));
            list.Add(Snapshot.Of(e) with
            {
                Guid = pair.Key.String,
                Char = S(t, "char"),
                Professions = S(t, "professions"),
                LastSeen = (long)N(t, "lastSeen"),
                Guild = t.Get("guild").IsNil() ? null : S(t, "guild"), // "" = no guild, nil = not known yet
                TalentPicks = S(t, "talentPicks"),
                Stats = S(t, "stats"),
                Xp = (long)N(t, "xp"),
                XpMax = (long)N(t, "xpMax"),
                MaxLevel = (int)N(t, "maxLevel"),
                LevelTime = (long)N(t, "levelTime"),
                Recipes = S(t, "recipes"),
                Reputations = S(t, "reputations"),
                OtherSpec = ReadSpecBuild(t.Get("otherSpec")),
            });
        }

        return list;
    }

    // chars[guid].otherSpec: the inactive dual spec build (Character.lua ns.dualSpec).
    static SpecBuild? ReadSpecBuild(DynValue v)
    {
        if (v.Type != DataType.Table) return null;
        var t = v.Table;
        var group = (int)N(t, "group");
        if (group is not (1 or 2) || S(t, "talents").Length == 0) return null;
        return new SpecBuild(group, S(t, "talents"), S(t, "spec"), S(t, "talentPicks"), S(t, "talentCalc"));
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
