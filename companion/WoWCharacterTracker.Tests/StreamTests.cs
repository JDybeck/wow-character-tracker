using WoWCharacterTracker.Services.Streaming;

namespace WoWCharacterTracker.Tests;

public class StreamTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 17, 20, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 17)));
    static readonly StreamCharacter Ronnie = new("P-1", "Ronnie Raket", "Forever", 22, "Paladin", "#F48CBA", "Dwarf", "Classic Beta PvP",
        "Redridge Mountains", "1d 6h", "21", Now.ToUnixTimeSeconds(), ["Won a duel against Grimtusk", "Looted [Sul'thraze]", "Killed X", "Old one"]);

    static Entry Level(int level, long seconds, TimeSpan ago, string guid = "P-1") =>
        new() { Id = $"{guid}:{level}", Guid = guid, Kind = "level", Level = level, Seconds = seconds, At = (Now - ago).ToUnixTimeSeconds() };

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
        Assert.Equal(new StreamDing("P-1:22", 22, "23m 20s", Now.ToUnixTimeSeconds() - 5), s.Ding);
        Assert.Equal(3, s.Highlights.Count);
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
    }

    [Fact]
    public void Text_files_are_empty_without_a_character()
    {
        Assert.All(StreamFiles.Contents(StreamSnapshot.Empty), f => Assert.Equal("", f.Text));
        var files = StreamFiles.Contents(StreamSnapshot.Build(Ronnie, [Level(22, 1400, TimeSpan.FromSeconds(5))], Now)).ToDictionary(f => f.File, f => f.Text);
        Assert.Equal("22", files["level.txt"]);
        Assert.Equal("Level 22: 23m 20s", files["levels.txt"]);
    }
}
