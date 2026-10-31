namespace WoWCharacterTracker.Models;

// One thing that happened: a level-up ("level"), an end boss kill ("boss"), a Mythic+ run
// ("keyrun"), rare loot ("loot"), a new mount ("mount"), a hardcore death ("death") or a
// live-channel check ("test").
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
    public string Faction { get; init; } = ""; // "Alliance", "Horde", "Neutral"; "" before 0.26.0 (Factions.Of guesses from the race)
    public string Sex { get; init; } = "male";
    public double ItemLevel { get; init; }
    public bool Hardcore { get; init; }
    public string Ruleset { get; init; } = ""; // "Hardcore", "Self-found", "Normal"...
    public string Role { get; init; } = "";    // "TANK", "HEALER", "DAMAGER" on retail
    public bool MaxLevel { get; init; }         // this level-up reached the game's max level
    public long Money { get; init; }            // copper the character had
    public string Crests { get; init; } = "";  // retail: "Gilded Crest 45, Runed Crest 120"
    public string Keystone { get; init; } = ""; // retail: "level~mapID~dungeon", "none" without one
    public int MplusScore { get; init; }          // retail: the Mythic+ score now, 0 = none (from 0.21.0)
    public string PvpRatings { get; init; } = ""; // retail: "bracket~rating,..." (GetPersonalRatedInfo brackets)
    public string ZoneType { get; init; } = ""; // dungeon, raid, pvp, rest (inn or city), zone; "" before 0.20.0
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
    public int SpellId { get; init; }    // mount: the mount's spell, when it was learned without an item (from 0.26.0)
    public bool Bought { get; init; }    // the mount showed up while a vendor was open
    public bool Shot { get; init; }      // the addon took a screenshot right after
    public string Group { get; init; } = ""; // boss kills, Mythic+ runs, max level: the other party members, see Members
    public int GroupSize { get; init; }
    public string Opponent { get; init; } = ""; // duel / makgora
    public bool Fled { get; init; }              // duel: the loser ran away (the opponent, or this character when Lost)
    public bool Lost { get; init; }              // duel / makgora: this character lost
    public string OpponentInfo { get; init; } = ""; // duel / makgora: "level~CLASSFILE~Class~Race" (from 0.16.0)

    // "level 21 Human Warrior", or "" when the addon couldn't see the opponent.
    public string OpponentText
    {
        get
        {
            var p = OpponentInfo.Split('~');
            if (p.Length < 4) return "";
            var who = $"{p[3]} {p[2]}".Trim();
            return p[0].Length > 0 ? $"level {p[0]} {who}".TrimEnd() : who;
        }
    }
    public string OpponentClassFile => OpponentInfo.Split('~') is { Length: >= 2 } p ? p[1] : "";
    public int Rating { get; init; }             // mplus / pvp: the rating now; profession: the skill now
    public int Step { get; init; }               // mplus / pvp / profession: the milestone passed
    public string Bracket { get; init; } = "";  // pvp: "3v3", "Solo Shuffle"...
    public string Profession { get; init; } = ""; // profession: "Mining" (from 0.29.0)
    public bool Boe { get; init; }               // loot: binds when equipped (from 0.29.0; older loot reads as not)
    public bool Mat { get; init; }               // loot: can't be worn, so legendary doesn't mean always posted (from 0.30.0; older loot reads as gear)
    // On the tab's followed list. Not from the addon: the app marks it from the list as it is now
    // (FlavorSettings.Followed), so an item followed after the last /reload counts too.
    public bool Followed { get; init; }
    // The "Bind on Equip only" rule for rares, the one copy Discord posts and the overlay both use.
    public bool PassesRareRule(bool boeOnly) => Quality != 3 || Boe || !boeOnly;
    public int KeyLevel { get; init; }           // keyrun: the keystone's level (the dungeon is Instance, the run's time Seconds)
    public int Upgrades { get; init; }           // keyrun: 1-3 when timed, 0 over time
    public bool Timed => Upgrades > 0;
    public string TalentExport { get; init; } = ""; // check-ins: the talent loadout code
    public string TalentCalc { get; init; } = "";   // check-ins (Forever): Wowhead talent calculator code

    // "Name~level~CLASSFILE~Class~Race~Realm~Spec,..." as the addon writes it (Spec from 0.28.0, Retail, when known).
    public IReadOnlyList<GroupMember> Members => Group.Length == 0 ? [] : Group.Split(',')
        .Select(m => m.Split('~'))
        .Where(p => p.Length >= 5)
        .Select(p => new GroupMember(p[0], int.TryParse(p[1], out var l) ? l : 0, p[2], p[3], p[4], p.Length > 5 ? p[5] : "", p.Length > 6 ? p[6] : ""))
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
            Faction = s("faction"),
            Sex = s("sex") == "female" ? "female" : "male",
            ItemLevel = n("ilvl"),
            Hardcore = s("hardcore") is "true" or "1",
            Ruleset = s("ruleset"),
            Role = s("role"),
            MaxLevel = s("maxLevel") is "true" or "1",
            Money = (long)n("money"),
            Crests = s("crests"),
            Keystone = s("keystone"),
            MplusScore = (int)n("mplusScore"),
            PvpRatings = s("pvpRatings"),
            ZoneType = s("zoneType"),
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
            SpellId = (int)n("spellId"),
            Bought = s("bought") is "true" or "1",
            Shot = s("shot") is "true" or "1",
            Group = s("group"),
            GroupSize = (int)n("groupSize"),
            Opponent = s("opponent"),
            Fled = s("fled") is "true" or "1",
            Lost = s("lost") is "true" or "1",
            OpponentInfo = s("opponentInfo"),
            Rating = (int)n("rating"),
            Step = (int)n("step"),
            Bracket = s("bracket"),
            Profession = s("profession"),
            Boe = s("boe") is "true" or "1",
            Mat = s("mat") is "true" or "1",
            KeyLevel = (int)n("keyLevel"),
            Upgrades = (int)n("upgrades"),
            TalentExport = s("talentExport"),
            TalentCalc = s("talentCalc"),
        };
    }

    // Level-up ids are "<guid>:<level>". Player GUIDs look like "Player-1403-0A1B2C3D".
    static string GuidFromId(string id) => id.IndexOf(':') is var i and > 0 ? id[..i] : id;
}

record GroupMember(string Name, int Level, string ClassFile, string Class, string Race, string Realm, string Spec = "")
{
    public string FullName => Realm.Length > 0 ? $"{Name}-{Realm}" : Name;

    // "Havoc" when the spec is known, else "Demon Hunter" (the name's class color tells specs apart).
    public string SpecOrClass => Spec.Length > 0 ? Spec : Class;
}
