namespace WoWCharacterTracker;

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

        var zeb = new Snapshot($"{GuidPrefix}Zeb", "Zeb", "Forever", 40, "Warrior", "WARRIOR", "Night Elf", "NightElf", "male",
            "", "", 38.4, 3 * 86400 + 5 * 3600, "Scarlet Monastery", "Mining 210, Blacksmithing 205", "Arms 21 / Fury 10 / Protection 0",
            Ago(0.2), false, "Normal", 87_45_12, Guild: "Bloodhounds");
        var fagel = new Snapshot($"{GuidPrefix}Fagelmannen", "Fågelmannen", "Forever", 21, "Druid", "DRUID", "Tauren", "Tauren", "male",
            "", "", 18.1, 19 * 3600 + 40 * 60, "The Barrens", "Herbalism 95, Alchemy 80", "Balance 0 / Feral Combat 12 / Restoration 0",
            Ago(5), false, "Normal", 4_12_80, Guild: "Bloodhounds");
        var goggo = new Snapshot($"{GuidPrefix}Goggo", "Goggo", "Forever", 60, "Hunter", "HUNTER", "Dwarf", "Dwarf", "male",
            "", "", 61.7, 9 * 86400 + 14 * 3600, "Blackrock Spire", "Leatherworking 300, Skinning 300", "Beast Mastery 31 / Marksmanship 20 / Survival 0",
            Ago(26), true, "Hardcore", 512_30_10, Guild: "");

        var events = new List<Entry>();
        Entry Base(Snapshot s, string kind, long at, int level) => new()
        {
            Id = $"{s.Guid}:{kind}:{at}", Kind = kind, Guid = s.Guid, Char = s.Char, Realm = s.Realm, Level = level, At = at,
            Zone = s.Zone, Professions = s.Professions, Talents = s.Talents, Class = s.Class, ClassFile = s.ClassFile,
            Race = s.Race, RaceFile = s.RaceFile, Sex = s.Sex, Hardcore = s.Hardcore, Ruleset = s.Ruleset, ItemLevel = s.ItemLevel,
            Money = s.Money,
        };
        void Levels(Snapshot s, int from, int to, double endHoursAgo, int minutesEach)
        {
            for (var l = from; l <= to; l++)
            {
                var at = Ago(endHoursAgo + (to - l) * (minutesEach / 60.0));
                events.Add(Base(s, "level", at, l) with
                {
                    Id = $"{s.Guid}:{l}", Seconds = minutesEach * 60 + l * 37, Played = s.Played - (to - l) * minutesEach * 60,
                });
            }
        }
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

        return new TrackerData(events, [zeb, fagel, goggo]);
    }
}
