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
    public string Role { get; init; } = "";    // "TANK", "HEALER", "DAMAGER" on retail
    public bool MaxLevel { get; init; }         // this level-up reached the game's max level
    public long Money { get; init; }            // copper the character had
    public string Crests { get; init; } = "";  // retail: "Gilded Crest 45, Runed Crest 120"
    public string? Guild { get; init; }         // "" = no guild, null = not known
    public string Surname { get; init; } = ""; // Forever: "Raket" for Ronnie Raket

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
    public string Group { get; init; } = ""; // boss kills: the other party members, see Members
    public int GroupSize { get; init; }
    public string Opponent { get; init; } = ""; // duel / makgora
    public bool Fled { get; init; }              // duel: the opponent ran away
    public int Rating { get; init; }             // mplus / pvp: the rating now
    public int Step { get; init; }               // mplus / pvp: the milestone passed
    public string Bracket { get; init; } = "";  // pvp: "3v3", "Solo Shuffle"...

    // "Name~level~CLASSFILE~Class~Race~Realm,..." as the addon writes it.
    public IReadOnlyList<GroupMember> Members => Group.Length == 0 ? [] : Group.Split(',')
        .Select(m => m.Split('~'))
        .Where(p => p.Length >= 5)
        .Select(p => new GroupMember(p[0], int.TryParse(p[1], out var l) ? l : 0, p[2], p[3], p[4], p.Length > 5 ? p[5] : ""))
        .ToList();

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
            Role = s("role"),
            MaxLevel = s("maxLevel") is "true" or "1",
            Money = (long)n("money"),
            Crests = s("crests"),
            Guild = s("guild") is { Length: > 0 } g ? g : null,
            Surname = s("surname"),
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
            Group = s("group"),
            GroupSize = (int)n("groupSize"),
            Opponent = s("opponent"),
            Fled = s("fled") is "true" or "1",
            Rating = (int)n("rating"),
            Step = (int)n("step"),
            Bracket = s("bracket"),
        };
    }

    // Level-up ids are "<guid>:<level>". Player GUIDs look like "Player-1403-0A1B2C3D".
    static string GuidFromId(string id) => id.IndexOf(':') is var i and > 0 ? id[..i] : id;
}

record GroupMember(string Name, int Level, string ClassFile, string Class, string Race, string Realm)
{
    public string FullName => Realm.Length > 0 ? $"{Name}-{Realm}" : Name;
}

// The latest state of a character, saved by the addon on every /played, /reload and logout.
record Snapshot(string Guid, string Char, string Realm, int Level, string Class, string ClassFile,
    string Race, string RaceFile, string Sex, string Spec, string Role, double ItemLevel, long Played,
    string Zone, string Professions, string Talents, long LastSeen, bool Hardcore, string Ruleset,
    long Money = 0, string Crests = "", string? Guild = null, string Surname = "", string TalentPicks = "", string TalentExport = "", string Stats = "");

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
                S(t, "hardcore") == "true", S(t, "ruleset"), (long)N(t, "money"), S(t, "crests"),
                t.Get("guild").IsNil() ? null : S(t, "guild"), S(t, "surname"), S(t, "talentPicks"), S(t, "talentExport"), S(t, "stats")));
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
    // The name posts use, from the tab's NameStyle: "full", "first" or "last" (surname, if there is one).
    public static string Name(string first, string surname, string style) => style switch
    {
        _ when surname.Length == 0 => first,
        "first" => first,
        "last" => surname,
        _ => $"{first} {surname}",
    };

    // Discord can't show item icons in text, so professions get a plain emoji each.
    static readonly Dictionary<string, string> ProfessionEmoji = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Alchemy"] = "⚗️", ["Blacksmithing"] = "🔨", ["Enchanting"] = "✨", ["Engineering"] = "⚙️",
        ["Herbalism"] = "🌿", ["Leatherworking"] = "🧥", ["Mining"] = "⛏️", ["Skinning"] = "🔪",
        ["Tailoring"] = "🧵", ["Inscription"] = "📜", ["Jewelcrafting"] = "💎", ["Cooking"] = "🍳",
        ["Fishing"] = "🎣", ["First Aid"] = "🩹", ["Archaeology"] = "🏺",
    };

    // "Mining 150, Herbalism 75" -> "⛏️ Mining 150, 🌿 Herbalism 75"
    public static string ProfessionsWithEmoji(string professions)
    {
        if (professions is "" or "None") return "";
        return string.Join(", ", professions.Split(", ").Select(p =>
        {
            var name = p.TrimEnd("0123456789 ".ToCharArray());
            return ProfessionEmoji.TryGetValue(name, out var emoji) ? $"{emoji} {p}" : p;
        }));
    }

    // Crests as text: "id~qty~name,..." (or the older "Name 45, Name 120") -> "Name 45, Name 120"
    public static string Crests(string crests)
    {
        if (!crests.Contains('~')) return crests;
        return string.Join(", ", crests.Split(',').Select(c => c.Split('~')).Where(p => p.Length >= 3).Select(p => $"{p[2]} {p[1]}"));
    }

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

    // Copper as "12,345g 34s 56c", leaving out leading zero parts.
    public static string Money(long copper)
    {
        long g = copper / 10000, s = copper / 100 % 100, c = copper % 100;
        if (g > 0) return $"{g.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}g {s}s {c}c";
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
