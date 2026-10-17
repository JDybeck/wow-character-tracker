using WoWCharacterTracker.Services.Streaming;

namespace WoWCharacterTracker.Tests;

public class StreamTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 18, 20, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 18)));
    static readonly StreamCharacter Ronnie = new("P-1", "Ronnie Raket", "Forever", 22, "Paladin", "#F48CBA", "Dwarf", "Classic Beta PvP",
        "Redridge Mountains", "1d 6h", "21", Now.ToUnixTimeSeconds());

    static long Ago(TimeSpan t) => (Now - t).ToUnixTimeSeconds();
    static Entry Level(int level, long seconds, TimeSpan ago, string guid = "P-1") =>
        new() { Id = $"{guid}:{level}", Guid = guid, Kind = "level", Level = level, Seconds = seconds, At = Ago(ago) };
    static Entry Duel(string opponent, bool lost, TimeSpan ago, string kind = "duel") =>
        new() { Id = $"P-1:{kind}:{opponent}:{ago.Ticks}", Guid = "P-1", Kind = kind, Opponent = opponent, Lost = lost, At = Ago(ago) };
    static Entry Loot(string item, TimeSpan ago) => new() { Id = $"P-1:loot:{item}", Guid = "P-1", Kind = "loot", Item = item, Quality = 5, At = Ago(ago) };

    [Fact]
    public void Newest_levels_first_with_the_ding_and_todays_average()
    {
        Entry[] events =
        [
            Level(20, 1300, TimeSpan.FromDays(2)),     // not today
            Level(21, 1500, TimeSpan.FromHours(2)),
            Level(22, 1400, TimeSpan.FromSeconds(5)),
            Level(30, 999, TimeSpan.FromSeconds(1), guid: "P-2"), // someone else
        ];
        var s = StreamSnapshot.Build(Ronnie, events, Now);

        Assert.Equal([22, 21, 20], s.Levels.Select(l => l.Level));
        Assert.Equal("21 to 22 took 23m 20s", s.LastLevel);
        Assert.Equal("2 levels today, 24m 10s average", s.Today);
        Assert.Equal(new StreamDing("P-1:22", 22, "23m 20s", Ago(TimeSpan.FromSeconds(5))), s.Ding);
        Assert.Equal("Ronnie Raket, level 22 Paladin", s.CharacterLine);
    }

    [Fact]
    public void Untimed_levels_and_no_levels()
    {
        var s = StreamSnapshot.Build(Ronnie, [Level(22, 0, TimeSpan.FromMinutes(1))], Now);
        Assert.Equal("21 to 22 took ?", s.LastLevel);
        Assert.Equal("1 level today", s.Today);

        s = StreamSnapshot.Build(Ronnie, [], Now);
        Assert.Equal("No levels yet today", s.Today);
        Assert.Null(s.Ding);
        Assert.Equal("", s.LastLevel);
        Assert.Null(s.Duels);
    }

    [Fact]
    public void Duel_and_makgora_records_count_wins_and_losses()
    {
        Entry[] events =
        [
            Duel("Grimtusk", lost: false, TimeSpan.FromHours(3)), Duel("Pellia", lost: false, TimeSpan.FromHours(2)),
            Duel("Grimtusk", lost: true, TimeSpan.FromHours(1)), Duel("Brandur", lost: false, TimeSpan.FromMinutes(5), kind: "makgora"),
        ];
        var s = StreamSnapshot.Build(Ronnie, events, Now);
        Assert.Equal(new StreamRecord(2, 1), s.Duels);
        Assert.Equal(new StreamRecord(1, 0), s.Makgora);
        // Losses count in the record but aren't highlights.
        Assert.Equal(["Won a Mak'gora against Brandur", "Won a duel against Pellia", "Won a duel against Grimtusk"], s.Highlights);
    }

    [Fact]
    public void Highlights_follow_the_chosen_kinds()
    {
        Entry[] events = [Loot("Thunderfury, Blessed Blade of the Windseeker", TimeSpan.FromMinutes(10)), Duel("Pellia", false, TimeSpan.FromMinutes(1))];
        var s = StreamSnapshot.Build(Ronnie, events, Now, new StreamOptions { HighlightKinds = ["loot"] });
        Assert.Equal(["Looted Thunderfury, Blessed Blade of the Windseeker"], s.Highlights);
    }

    [Fact]
    public void Start_from_now_only_counts_what_came_after()
    {
        Entry[] events =
        [
            Level(21, 1500, TimeSpan.FromHours(2)), Duel("Grimtusk", false, TimeSpan.FromHours(2)),
            Level(22, 1400, TimeSpan.FromMinutes(5)), Duel("Pellia", true, TimeSpan.FromMinutes(4)),
        ];
        var s = StreamSnapshot.Build(Ronnie, events, Now, new StreamOptions { Since = Ago(TimeSpan.FromHours(1)) });
        Assert.Equal([22], s.Levels.Select(l => l.Level));
        Assert.Equal(new StreamRecord(0, 1), s.Duels);
        Assert.Empty(s.Highlights); // the only win was before
        Assert.StartsWith("Since ", s.SinceText);
    }

    [Fact]
    public void The_look_comes_from_the_options()
    {
        var s = StreamSnapshot.Build(Ronnie, [], Now, new StreamOptions { CardOpacity = 140, ShowDuels = false });
        Assert.Equal(100, s.Look!.CardOpacity); // clamped
        Assert.False(s.Look.Duels);
    }

    [Fact]
    public void No_characters_at_all_is_an_empty_overlay_not_a_crash()
    {
        // A fresh install (no characters on any tab) used to throw "Sequence contains no elements".
        var s = StreamSnapshot.Latest([], Now);
        Assert.False(s.HasCharacter);
        Assert.NotNull(s.Look);
    }

    [Fact]
    public void The_character_played_last_wins_across_games()
    {
        var retail = Ronnie with { Guid = "R-1", Name = "Drygbeck", Game = "Retail", LastSeen = Now.ToUnixTimeSeconds() - 3600 };
        var s = StreamSnapshot.Latest([(retail, []), (Ronnie, [])], Now);
        Assert.Equal("Ronnie Raket", s.Character);
    }

    [Fact]
    public void Text_files_are_empty_without_a_character()
    {
        Assert.All(StreamFiles.Contents(StreamSnapshot.Empty), f => Assert.Equal("", f.Text));
        var files = StreamFiles.Contents(StreamSnapshot.Build(Ronnie, [Level(22, 1400, TimeSpan.FromSeconds(5)), Duel("Pellia", false, TimeSpan.FromSeconds(3))], Now))
            .ToDictionary(f => f.File, f => f.Text);
        Assert.Equal("22", files["level.txt"]);
        Assert.Equal("Level 22: 23m 20s", files["levels.txt"]);
        Assert.Equal("1 won, 0 lost", files["duels.txt"]);
    }
}
