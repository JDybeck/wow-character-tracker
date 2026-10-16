namespace WoWCharacterTracker.ViewModels;

// Samples of every kind of post, for the Test buttons. They use a real character from the list
// (the selected or most recently played one) so they look familiar; item and boss details are
// made up, and other characters in the list stand in as the group.
static class SampleEvents
{
    public static Entry For(string kind, Flavor flavor, CharacterRow? c, IEnumerable<CharacterRow> others)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var e = c is null
            ? new Entry
            {
                Id = "test", Char = "Testy", Level = 12, At = now, Zone = "Elwynn Forest",
                Professions = "Mining 75, Herbalism 60", Talents = "Arms 5 / Fury 2 / Protection 0", Class = "Warrior",
                ClassFile = "WARRIOR", Race = "Human", RaceFile = "Human", Played = 4 * 3600 + 12 * 60,
            }
            : new Entry
            {
                Id = "test", Char = c.Name, Surname = c.Surname, Realm = c.Realm, Level = Math.Max(c.Level, 2), At = now,
                Zone = c.Zone, Professions = c.Professions, Talents = c.Talents, Spec = c.Spec, Class = c.ClassName,
                ClassFile = c.ClassFile, Race = c.Race, RaceFile = c.RaceFile, Sex = c.Sex, Played = c.Played,
                Hardcore = c.Hardcore, Ruleset = c.Ruleset,
            };

        var group = string.Join(",", others.Where(o => o != c).Take(4)
            .Select(o => $"{o.Name}~{o.Level}~{o.ClassFile}~{o.ClassName}~{o.Race}~"));

        return kind switch
        {
            "rare" => e with { Kind = "loot", Item = "Cruel Barb", ItemId = 5191, Quality = 3, Zone = "The Deadmines" },
            "epic" => e with { Kind = "loot", Item = "Arcanite Reaper", ItemId = 12784, Quality = 4 },
            "legendary" => e with { Kind = "loot", Item = "Thunderfury, Blessed Blade of the Windseeker", ItemId = 19019, Quality = 5 },
            "max" => e with { Kind = "level", Level = flavor == Flavors.Forever ? 60 : 90, Seconds = 2 * 3600 + 14 * 60, MaxLevel = true, Group = group, GroupSize = 5 },
            "mount" => e with { Kind = "mount", Mount = "Pinto Bridle", ItemId = 2414, Bought = true },
            "boss" => e with { Kind = "boss", Boss = "Edwin VanCleef", Instance = "The Deadmines", Group = group, GroupSize = 5 },
            "death" => e with { Kind = "death", Killer = "Defias Pillager", Gold = 123456, Hardcore = true },
            "duel" => e with { Kind = "duel", Opponent = "Grimtusk" },
            "makgora" => e with { Kind = "makgora", Opponent = "Grimtusk", Hardcore = true },
            "mplus" => e with { Kind = "mplus", Step = 2000, Rating = 2034 },
            "pvp" => e with { Kind = "pvp", Step = 1800, Rating = 1812, Bracket = "3v3" },
            _ => e with { Kind = "level", Seconds = 18 * 60 + 4 },
        };
    }
}
