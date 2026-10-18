namespace WoWCharacterTracker.Models;

// Made-up Forever characters for trying the app without playing: the list, details, filters,
// tags, Test buttons and character cards. They only exist in the app (the "Test characters"
// switch) and their events are never posted on their own.
static class TestCharacters
{
    public const string GuidPrefix = "Test-";

    public static bool IsTest(string guid) => guid.StartsWith(GuidPrefix, StringComparison.Ordinal);

    public static TrackerData Build()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long Ago(double hours) => now - (long)(hours * 3600);

        var zeb = new Snapshot($"{GuidPrefix}Zeb", "Zeb", "Stormreach", 40, "Warrior", "WARRIOR", "Night Elf", "NightElf", "male",
            "Arms", "", 38.4, 3 * 86400 + 5 * 3600, "Scarlet Monastery", "Mining 210, Blacksmithing 205", "Arms 21 / Fury 10 / Protection 0",
            Ago(0.2), false, "Normal", 87_45_12, Guild: "Bloodhounds", Surname: "TESTCHAR", TalentPicks: "12282~3~3~1,16462~5~5~1,12292~1~1~1,12294~1~1~1,12320~5~5~2", Stats: "Strength~152,Agility~89,Stamina~141,Intellect~22,Spirit~48,Armor~2410,Attack Power~398,Crit~9.2%", Xp: 31200, XpMax: 90700, MaxLevel: 60, Region: "eu", ZoneType: "dungeon");
        var fagel = new Snapshot($"{GuidPrefix}Fagelmannen", "Fågelmannen", "Stormreach", 21, "Druid", "DRUID", "Tauren", "Tauren", "male",
            "Feral Combat", "", 18.1, 19 * 3600 + 40 * 60, "The Barrens", "Herbalism 95, Alchemy 80", "Balance 0 / Feral Combat 12 / Restoration 0",
            Ago(5), false, "Normal", 4_12_80, Guild: "Bloodhounds", Surname: "TESTCHAR", Xp: 6100, XpMax: 25200, MaxLevel: 60, Region: "eu", ZoneType: "zone");
        var goggo = new Snapshot($"{GuidPrefix}Goggo", "Goggo", "Ashfall", 60, "Hunter", "HUNTER", "Dwarf", "Dwarf", "male",
            "Beast Mastery", "", 61.7, 9 * 86400 + 14 * 3600, "Blackrock Spire", "Leatherworking 300, Skinning 300", "Beast Mastery 31 / Marksmanship 20 / Survival 0",
            Ago(26), true, "Hardcore", 512_30_10, Guild: "", Surname: "TESTCHAR", MaxLevel: 60, Region: "us", ZoneType: "dungeon");

        var events = new List<Entry>();
        Entry Base(Snapshot s, string kind, long at, int level) => new()
        {
            Id = $"{s.Guid}:{kind}:{at}", Kind = kind, Guid = s.Guid, Char = s.Char, Realm = s.Realm, Level = level, At = at,
            Zone = s.Zone, Professions = s.Professions, Talents = s.Talents, Class = s.Class, ClassFile = s.ClassFile,
            Race = s.Race, RaceFile = s.RaceFile, Sex = s.Sex, Hardcore = s.Hardcore, Ruleset = s.Ruleset, ItemLevel = s.ItemLevel,
            Money = s.Money, Surname = s.Surname,
        };
        void Levels(Snapshot s, int from, int to, double endHoursAgo, int minutesEach)
        {
            for (var l = from; l <= to; l++)
            {
                var at = Ago(endHoursAgo + (to - l) * (minutesEach / 60.0));
                // Gear and professions grow along the way (for the Leveling tab's progress lines).
                events.Add(Base(s, "level", at, l) with
                {
                    Id = $"{s.Guid}:{l}", Seconds = minutesEach * 60 + l * 37, Played = s.Played - (to - l) * minutesEach * 60,
                    ItemLevel = Math.Round(s.ItemLevel - (to - l) * 1.4, 1), Professions = Grown(s.Professions, to - l),
                });
            }
        }
        // "Mining 210, Blacksmithing 205" a few levels earlier: 12 points less per level.
        static string Grown(string professions, int levelsAgo) => string.Join(", ", professions.Split(", ").Select(p =>
        {
            var space = p.LastIndexOf(' ');
            return space > 0 && int.TryParse(p[(space + 1)..], out var skill) ? $"{p[..space]} {Math.Max(1, skill - levelsAgo * 12)}" : p;
        }));
        string Group(params (string Name, int Level, string ClassFile, string Class, string Race)[] members) =>
            string.Join(",", members.Select(m => $"{m.Name}~{m.Level}~{m.ClassFile}~{m.Class}~{m.Race}~"));

        // Zeb: a Scarlet Monastery Armory run, Herod down, Ravager and Herod's Shoulder.
        Levels(zeb, 35, 40, 0.5, 95);
        events.Add(Base(zeb, "boss", Ago(1.2), 39) with
        {
            Boss = "Herod", Instance = "Scarlet Monastery Armory", GroupSize = 5,
            Group = Group(("Fågelmannen", 38, "DRUID", "Druid", "Tauren"), ("Lumi", 39, "PRIEST", "Priest", "Human"),
                ("Skuggan", 40, "ROGUE", "Rogue", "Night Elf"), ("Brandur", 38, "MAGE", "Mage", "Gnome")),
        });
        events.Add(Base(zeb, "loot", Ago(1.19), 39) with { Item = "Ravager", ItemId = 7717, Quality = 3 });
        events.Add(Base(zeb, "loot", Ago(1.18), 39) with { Item = "Herod's Shoulder", ItemId = 7718, Quality = 3 });
        events.Add(Base(zeb, "duel", Ago(3), 38) with { Opponent = "Skuggan" });
        events.Add(Base(zeb, "duel", Ago(2.5), 38) with { Opponent = "Brandur", Fled = true });
        events.Add(Base(zeb, "duel", Ago(2.2), 38) with { Opponent = "Lumi", Lost = true });

        // Fågelmannen: Wailing Caverns, Mutanus down, Leggings of the Fang.
        Levels(fagel, 17, 21, 5.5, 55);
        events.Add(Base(fagel, "boss", Ago(7), 20) with
        {
            Boss = "Mutanus the Devourer", Instance = "Wailing Caverns", GroupSize = 5, Zone = "Wailing Caverns",
            Group = Group(("Zeb", 21, "WARRIOR", "Warrior", "Night Elf"), ("Lumi", 19, "PRIEST", "Priest", "Human"),
                ("Skuggan", 20, "ROGUE", "Rogue", "Night Elf"), ("Brandur", 19, "MAGE", "Mage", "Gnome")),
        });
        events.Add(Base(fagel, "loot", Ago(7.1), 20) with { Item = "Leggings of the Fang", ItemId = 10410, Quality = 3, Zone = "Wailing Caverns" });

        // Goggo: hardcore, max level, Rhok'delar, a ram, and General Drakkisath.
        Levels(goggo, 55, 59, 30, 140);
        events.Add(Base(goggo, "level", Ago(27), 60) with
        {
            Id = $"{goggo.Guid}:60", Seconds = 2 * 3600 + 51 * 60, Played = goggo.Played - 3600, MaxLevel = true,
            Group = Group(("Zeb", 58, "WARRIOR", "Warrior", "Night Elf")), GroupSize = 2,
        });
        events.Add(Base(goggo, "mount", Ago(400), 40) with { Mount = "Gray Ram", ItemId = 5864, Bought = true, Zone = "Dun Morogh" });
        events.Add(Base(goggo, "loot", Ago(26.5), 60) with { Item = "Rhok'delar, Longbow of the Ancient Keepers", ItemId = 18713, Quality = 4, Zone = "Moonglade" });
        events.Add(Base(goggo, "boss", Ago(26.2), 60) with
        {
            Boss = "General Drakkisath", Instance = "Upper Blackrock Spire", GroupSize = 10,
        });
        events.Add(Base(goggo, "makgora", Ago(26.1), 60) with { Opponent = "Krull" });
        events.Add(Base(goggo, "death", Ago(26), 60) with { Played = goggo.Played, Gold = 512_30_10, Zone = "Blackrock Spire" });

        // What they carry, for the Gear and bags tab and the Items page.
        var inventories = new List<Inventory>
        {
            Carrying(zeb, "1~7719,3~7718,5~6125,7~6087,8~7351,16~7717", "6948~1,2589~20,4306~32,2772~14,3575~8,929~5,4599~12,159~20", "4306~40,2772~20,7718~1,6125~1", now),
            Carrying(fagel, "7~10410,1~6505,16~1238", "6948~1,2589~11,2452~14,785~22,118~6,159~10", "", 0),
            Carrying(goggo, "1~16846,3~16848,5~16845,18~18713", "6948~1,13444~8,13446~12,12662~4,14047~60", "14047~120,12809~4,16846~1", now - 3 * 86400) with
            {
                // As the addon saves them (Inventory.lua scanGearEffects), for the gear tab's side column.
                Sets = Inventory.ParseSets("Giantstalker Armor~3~8~3~Increases the range of your Mend Pet spell by 50% and the effect by 10%.\n"
                    + "Giantstalker Armor~3~8~5~Increases the damage of Multi-Shot and Volley by 15%.\n"
                    + "Giantstalker Armor~3~8~8~Your normal ranged attacks have a 4% chance of restoring 200 mana."),
                Effects = Inventory.ParseEffects("1~16846~Equip: Improves your chance to hit by 1%.\n5~16845~Equip: Improves your chance to get a critical strike by 1%."),
                Enchants = Inventory.ParseEnchants("5~16845~+4 All Stats~1891\n18~18713~+7 Ranged Damage~664"),
            },
        };
        var xpTable = new Dictionary<int, long> { [20] = 23200, [21] = 25200, [39] = 85700, [40] = 90700 };
        return new TrackerData(events, [zeb, fagel, goggo]) { Inventories = inventories, XpTable = xpTable };
    }

    // Gear as "slot~itemId", bags and bank as "itemId~count" (laid out in slot order here).
    static Inventory Carrying(Snapshot s, string gear, string bags, string bank, long bankAt)
    {
        static List<ItemSlot> Fill(string items, int firstBag, int bagSize) => items.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select((p, i) => (Parts: p.Split('~'), Index: i))
            .Select(x => new ItemSlot(firstBag + x.Index / bagSize, x.Index % bagSize + 1, int.Parse(x.Parts[0]), int.Parse(x.Parts[1])))
            .ToList();
        var equipped = gear.Split(',').Select(p => p.Split('~')).Select(p => new ItemSlot(Inventory.Equipped, int.Parse(p[0]), int.Parse(p[1]), 1)).ToList();
        return new Inventory(s.Guid, equipped, Fill(bags, 0, 16), new Dictionary<int, int> { [0] = 16, [1] = 14, [2] = 12, [3] = 10 },
            bankAt > 0 ? Fill(bank, -1, 24) : [], bankAt > 0 ? new Dictionary<int, int> { [-1] = 24, [5] = 12 } : new Dictionary<int, int>(),
            s.LastSeen, bankAt, s.LastSeen, s.Zone) { HasGearScan = true };
    }
}
