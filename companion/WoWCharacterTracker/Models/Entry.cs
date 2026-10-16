using MoonSharp.Interpreter;

namespace WoWCharacterTracker.Models;

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
    public string Region { get; init; } = "";  // "eu", "us", "kr", "tw", "cn"

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
    public string TalentExport { get; init; } = ""; // check-ins: the talent loadout code
    public string TalentCalc { get; init; } = "";   // check-ins (Forever): Wowhead talent calculator code

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
            Region = s("region"),
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
            TalentExport = s("talentExport"),
            TalentCalc = s("talentCalc"),
        };
    }

    // Level-up ids are "<guid>:<level>". Player GUIDs look like "Player-1403-0A1B2C3D".
    static string GuidFromId(string id) => id.IndexOf(':') is var i and > 0 ? id[..i] : id;
}

record GroupMember(string Name, int Level, string ClassFile, string Class, string Race, string Realm)
{
    public string FullName => Realm.Length > 0 ? $"{Name}-{Realm}" : Name;
}
