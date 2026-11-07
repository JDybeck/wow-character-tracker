using MoonSharp.Interpreter;
using WoWCharacterTracker.Services;

namespace WoWCharacterTracker.Tests;

// Forever's stress test (before launch) left characters behind: they go, once, everywhere.
public class StressTestCleanupTests
{
    const long Before = 1793746800; // 2026-11-04 00:00 Swedish time

    static Script Addon(string db)
    {
        var script = new Script();
        script.DoString("WoWCharacterTrackerDB = " + db);
        var ns = new Table(script);
        script.Globals["ns"] = ns;
        foreach (var file in new[] { "Shared.lua", "Cleanup.lua" })
            script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, file))), "WoWCharacterTracker", ns);
        return script;
    }

    const string Db = """
        { chars = {
            ['Player-1-0000000A'] = { char = 'Old', lastSeen = 1793000000 },
            ['Player-1-0000000B'] = { char = 'New', lastSeen = 1793800000 },
            ['Player-1-0000000C'] = { char = 'Playing', lastSeen = 1793000000 },
            ['Player-1-0000000D'] = { char = 'OldSnapNewEvent', lastSeen = 1793000000 } },
          items = { ['Player-1-0000000A'] = { gear = '1~2' }, ['Player-1-0000000B'] = { gear = '1~3' } },
          events = {
            { guid = 'Player-1-0000000A', kind = 'level', at = 1793000000 },
            { guid = 'Player-1-0000000B', kind = 'level', at = 1793800000 },
            { guid = 'Player-1-0000000D', kind = 'loot', at = 1793800000 },
            { guid = 'Player-1-0000000E', kind = 'level', at = 1793000000 } },
          notes = { ['Player-1-0000000A'] = { text = 'x', at = 1 } },
          start = { ['Player-1-0000000A'] = 1 }, startLevel = { ['Player-1-0000000A'] = 2 }, logoutPlayed = { ['Player-1-0000000A'] = 3 },
          professions = { ['Player-1-0000000A'] = {} },
          friends = { ['Zeb#2222'] = { chars = {
            ['Player-1-0000000F'] = { char = 'StressZeb', at = 1793000000 },
            ['Player-1-00000010'] = { char = 'LiveZeb', at = 1793800000 } } } } }
        """;

    [Fact]
    public void The_addon_removes_stress_test_characters_and_everything_about_them()
    {
        var s = Addon(Db);
        var removed = s.DoString($"return ns.cleanStressTest(WoWCharacterTrackerDB, {Before}, 'Player-1-0000000C')").Number;
        bool Gone(string expr) => s.DoString("return " + expr).IsNil();
        Assert.Equal(2, removed); // A (snapshot) and E (events only)
        Assert.True(Gone("WoWCharacterTrackerDB.chars['Player-1-0000000A']"));
        Assert.True(Gone("WoWCharacterTrackerDB.items['Player-1-0000000A']"));
        Assert.True(Gone("WoWCharacterTrackerDB.notes['Player-1-0000000A']"));
        Assert.True(Gone("WoWCharacterTrackerDB.start['Player-1-0000000A']"));
        Assert.True(Gone("WoWCharacterTrackerDB.startLevel['Player-1-0000000A']"));
        Assert.True(Gone("WoWCharacterTrackerDB.logoutPlayed['Player-1-0000000A']"));
        Assert.True(Gone("WoWCharacterTrackerDB.professions['Player-1-0000000A']"));
        Assert.Equal(2, s.DoString("return #WoWCharacterTrackerDB.events").Number); // B's and D's stay
        Assert.False(Gone("WoWCharacterTrackerDB.chars['Player-1-0000000B']"));
        Assert.False(Gone("WoWCharacterTrackerDB.chars['Player-1-0000000C']")); // the one being played stays
        Assert.False(Gone("WoWCharacterTrackerDB.chars['Player-1-0000000D']")); // played after the cutoff
        Assert.True(Gone("WoWCharacterTrackerDB.friends['Zeb#2222'].chars['Player-1-0000000F']"));
        Assert.False(Gone("WoWCharacterTrackerDB.friends['Zeb#2222'].chars['Player-1-00000010']"));
    }

    [Fact]
    public void The_app_finds_the_same_old_characters()
    {
        var data = new TrackerData(
            [new Entry { Id = "e1", Guid = "Player-1-0000000E", Kind = "level", At = 1793000000 },
             new Entry { Id = "e2", Guid = "Player-1-0000000D", Kind = "loot", At = 1793800000 }],
            [Snap("Player-1-0000000A", 1793000000), Snap("Player-1-0000000B", 1793800000), Snap("Player-1-0000000D", 1793000000)]);
        Assert.Equal(["Player-1-0000000A", "Player-1-0000000E"], StressTestCleanup.OldCharacters(data, Before).Order());
    }

    [Fact]
    public void The_app_forgets_what_it_kept_about_them_and_hides_them()
    {
        var config = new Config();
        var settings = new FlavorSettings();
        config.CharacterTags["Player-1-0000000A"] = ["Main"];
        config.CharacterNotes["Player-1-0000000A"] = "x";
        config.CharacterNotesAt["Player-1-0000000A"] = 5;
        config.StandardPortraits.Add("Player-1-0000000A");
        config.CharacterTags["Player-1-0000000B"] = ["Alt"];
        StressTestCleanup.Apply(config, settings, ["Player-1-0000000A"], Before, deletePortrait: _ => { });
        Assert.False(config.CharacterTags.ContainsKey("Player-1-0000000A"));
        Assert.False(config.CharacterNotes.ContainsKey("Player-1-0000000A"));
        Assert.False(config.CharacterNotesAt.ContainsKey("Player-1-0000000A"));
        Assert.DoesNotContain("Player-1-0000000A", config.StandardPortraits);
        Assert.True(config.CharacterTags.ContainsKey("Player-1-0000000B"));
        Assert.Equal(Before, settings.Removed["Player-1-0000000A"]); // hidden until the addon has cleaned it
        Assert.True(settings.StressTestCleaned);
    }

    [Fact]
    public void Only_forever_had_a_stress_test()
    {
        Assert.Equal(Before, Flavors.Forever.StressTestBefore);
        Assert.Equal(0, Flavors.Retail.StressTestBefore);
    }

    static Snapshot Snap(string guid, long lastSeen) =>
        new(guid, "X", "Firemaw", 10, "Warrior", "WARRIOR", "Orc", "Orc", "male", "", "", 10, 0, "", "", "", lastSeen, false, "");
}
