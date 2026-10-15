using MoonSharp.Interpreter;

namespace WoWCharacterTracker;

// One thing that happened: a level-up ("level"), an end boss kill ("boss"), rare loot ("loot"),
// a new mount ("mount"), a hardcore death ("death") or a live-channel check ("test").
record Entry
{
    public string Id { get; init; } = "";
    public string Kind { get; init; } = "level";
    public string Guid { get; init; } = "";
    public string Char { get; init; } = "?";
    public string Realm { get; init; } = "";
    public int Level { get; init; }
    public long At { get; init; }
    public string Zone { get; init; } = "";
    public string Professions { get; init; } = "";
    public string Talents { get; init; } = "";
    public string Spec { get; init; } = "";
    public string Class { get; init; } = "";
    public string ClassFile { get; init; } = "";
    public string Race { get; init; } = "";
    public string RaceFile { get; init; } = "";
    public string Sex { get; init; } = "male";
    public double ItemLevel { get; init; }
    public bool Hardcore { get; init; }
    public string Ruleset { get; init; } = ""; // "Hardcore", "Self-found", "Normal"...

    public long Seconds { get; init; }   // level: time the level took
    public long Played { get; init; }    // total /played
    public string Boss { get; init; } = "";
    public string Instance { get; init; } = "";
    public string Item { get; init; } = "";
    public int ItemId { get; init; }
    public int Quality { get; init; }    // 3 rare, 4 epic, 5 legendary
    public long Gold { get; init; }      // copper, for deaths
    public string Killer { get; init; } = "";
    public string Mount { get; init; } = "";
    public bool Bought { get; init; }    // the mount showed up while a vendor was open
    public bool Shot { get; init; }      // the addon took a screenshot right after

    // Builds an entry from the addon's key/value data, wherever it came from.
    public static Entry From(Func<string, string> s, Func<string, double> n)
    {
        var id = s("id");
        var guid = s("guid");
        return new Entry
        {
            Id = id,
            Kind = s("kind") is { Length: > 0 } k ? k : "level",
            Guid = guid.Length > 0 ? guid : GuidFromId(id),
            Char = s("char") is { Length: > 0 } c ? c : "?",
            Realm = s("realm"),
            Level = (int)n("level"),
            At = (long)n("at"),
            Zone = s("zone"),
            Professions = s("professions") == "None" ? "" : s("professions"),
            Talents = s("talents"),
            Spec = s("spec"),
            Class = s("class"),
            ClassFile = s("classFile"),
            Race = s("race"),
            RaceFile = s("raceFile"),
            Sex = s("sex") == "female" ? "female" : "male",
            ItemLevel = n("ilvl"),
            Hardcore = s("hardcore") is "true" or "1",
            Ruleset = s("ruleset"),
            Seconds = (long)n("seconds"),
            Played = (long)n("played"),
            Boss = s("boss"),
            Instance = s("instance"),
            Item = s("item"),
            ItemId = (int)n("itemId"),
            Quality = (int)n("quality"),
            Gold = (long)n("gold"),
            Killer = s("killer"),
            Mount = s("mount"),
            Bought = s("bought") is "true" or "1",
            Shot = s("shot") is "true" or "1",
        };
    }

    // Level-up ids are "<guid>:<level>". Player GUIDs look like "Player-1403-0A1B2C3D".
    static string GuidFromId(string id) => id.IndexOf(':') is var i and > 0 ? id[..i] : id;
}

// The latest state of a character, saved by the addon on every /played, /reload and logout.
record Snapshot(string Guid, string Char, string Realm, int Level, string Class, string ClassFile,
    string Race, string RaceFile, string Sex, string Spec, string Role, double ItemLevel, long Played,
    string Zone, string Professions, string Talents, long LastSeen, bool Hardcore, string Ruleset);

record TrackerData(List<Entry> Events, List<Snapshot> Characters)
{
    public static TrackerData Empty { get; } = new([], []);
}

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

        return new TrackerData(ReadEvents(db.Table.Get("events")), ReadCharacters(db.Table.Get("chars")));
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
                S(t, "hardcore") == "true", S(t, "ruleset")));
        }

        return list;
    }

    // CastToString skips booleans, so those become "true"/"false" here (the same as the live channel sends).
    static string S(Table t, string key)
    {
        var v = t.Get(key);
        return v.Type == DataType.Boolean ? (v.Boolean ? "true" : "false") : v.CastToString() ?? "";
    }
    static double N(Table t, string key) => t.Get(key).CastToNumber() ?? 0;
}

static class Format
{
    public static string Duration(long seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        if (ts.TotalMinutes >= 1) return $"{ts.Minutes}m {ts.Seconds}s";
        return $"{ts.Seconds}s";
    }

    // Longer form for total /played, e.g. "2d 3h 10m".
    public static string Played(long seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
        return Duration(seconds);
    }

    // Copper as "12g 34s 56c", leaving out leading zero parts.
    public static string Money(long copper)
    {
        long g = copper / 10000, s = copper / 100 % 100, c = copper % 100;
        if (g > 0) return $"{g}g {s}s {c}c";
        if (s > 0) return $"{s}s {c}c";
        return $"{c}c";
    }

    public static string Quality(int q) => q switch { 3 => "Rare", 4 => "Epic", 5 => "Legendary", 6 => "Artifact", _ => "" };

    public static string Ago(long unixSeconds)
    {
        if (unixSeconds <= 0) return "";
        var span = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays}d ago";
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime.ToString("yyyy-MM-dd");
    }
}
