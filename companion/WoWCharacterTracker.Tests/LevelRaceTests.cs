using WoWCharacterTracker.Services;
using WoWCharacterTracker.ViewModels;

namespace WoWCharacterTracker.Tests;

// A friend's level times against yours (LevelRace) and the Race box's text.
public class LevelRaceTests
{
    // Levels 2, 3, ... with these times, in seconds.
    static List<LevelStats.Level> From2(params long[] seconds) => [.. seconds.Select((s, i) => new LevelStats.Level(i + 2, s))];

    [Fact]
    public void Totals_stop_at_the_first_missing_level()
    {
        var totals = LevelRace.Totals([new(2, 100), new(3, 200), new(5, 400)]);

        Assert.Equal(new Dictionary<int, long> { [2] = 100, [3] = 300 }, totals);
        Assert.Empty(LevelRace.Totals([new(10, 100), new(11, 200)])); // installed at level 9
    }

    [Fact]
    public void Compares_played_time_to_the_highest_common_level_and_counts_faster_levels()
    {
        var race = LevelRace.Compare(From2(100, 300, 500, 900), From2(200, 200, 500));

        Assert.NotNull(race);
        Assert.Equal(4, race.Level);
        Assert.Equal(900, race.Mine);
        Assert.Equal(900, race.Theirs);
        Assert.Equal(1, race.MineFaster);   // level 2
        Assert.Equal(1, race.TheirsFaster); // level 3, level 4 a tie
    }

    [Fact]
    public void Without_a_full_history_only_the_splits_count()
    {
        var race = LevelRace.Compare(From2(100, 300), [new(3, 200), new(4, 50)]);

        Assert.NotNull(race);
        Assert.Equal(0, race.Level);
        Assert.Equal(0, race.MineFaster);
        Assert.Equal(1, race.TheirsFaster);
        Assert.Null(LevelRace.Compare(From2(100), [new(30, 100)]));
    }

    [Fact]
    public void Picks_your_character_that_got_furthest_alongside_theirs_then_the_one_played_last()
    {
        var theirs = From2(100, 100, 100, 100);
        List<(string, IReadOnlyList<LevelStats.Level>, long)> mine =
        [
            ("low", From2(100), 900),
            ("high", From2(100, 100, 100, 100, 100), 100),
            ("high-newer", From2(100, 100, 100, 100), 500),
            ("none", [new(40, 100)], 999),
        ];

        Assert.Equal("high-newer", LevelRace.PickRival(theirs, mine));
        Assert.Null(LevelRace.PickRival(theirs, [("none", [new(40, 100)], 999)]));
    }

    [Fact]
    public void A_bars_tooltip_lists_the_others_on_that_level_fastest_first()
    {
        var board = LevelRace.Board(
        [
            ("me", "Zeb", From2(300, 600)),
            ("alt", "Grim", From2(400)),
            ("friend", "Kitty Pryde (Rasmus)", From2(200, 700)),
        ]);
        Entry Ding(int level, long seconds) => new() { Kind = "level", Guid = "me", Level = level, Seconds = seconds };
        var leveling = CharacterLeveling.From([Ding(2, 300), Ding(3, 600)], 3, null, new Dictionary<int, long>(), estimate: false, thisLevel: 120);

        leveling.AddOthers("me", board);

        Assert.Equal("1 to 2: 5m 0s\nKitty Pryde (Rasmus): 3m 20s\nGrim: 6m 40s", leveling.Bars[0].Tooltip);
        Assert.Equal("2 to 3: 10m 0s\nKitty Pryde (Rasmus): 11m 40s", leveling.Bars[1].Tooltip);
        Assert.Equal("3 to 4 so far: 2m 0s", leveling.Bars[2].Tooltip); // the level being played stays yours alone
    }

    [Fact]
    public void The_race_box_says_who_got_there_sooner()
    {
        var box = LevelRaceRow.From(new LevelRace.Result(23, 50_520, 45_600, 10, 12), "Grimbo", "Zeb", 27);

        Assert.NotNull(box);
        Assert.Equal("Against your Zeb, level 27", box.Against);
        Assert.Equal(["To level 23: Grimbo 12h 40m played, you 14h 2m. Grimbo got there 1h 22m sooner.", "Faster levels: Grimbo 12, you 10."], box.Lines);

        Assert.EndsWith("You got there 1h 22m sooner.", LevelRaceRow.From(new LevelRace.Result(23, 45_600, 50_520, 0, 0), "Grimbo", "Zeb", 27)!.Lines.Single());
        Assert.EndsWith("Dead even.", LevelRaceRow.From(new LevelRace.Result(23, 45_600, 45_630, 0, 0), "Grimbo", "Zeb", 27)!.Lines.Single());
        Assert.Null(LevelRaceRow.From(null, "Grimbo", "Zeb", 27));
    }
}
