using WoWCharacterTracker.Services;

namespace WoWCharacterTracker.Tests;

// The Stats page (Leaderboards): the last seven days per person, one board per stat, and the MVP.
public class LeaderboardsTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);
    const long Day = 86400;

    static readonly Leaderboards.Player You = new("you", "You", true);
    static readonly Leaderboards.Player Rasmus = new("Rasmus#1", "Rasmus", false);
    static readonly Leaderboards.Player Benne = new("Benne#2", "Benne", false);

    static Entry Ding(string guid, int level, long seconds, long ago) =>
        new() { Kind = "level", Guid = guid, Char = "?", Level = level, Seconds = seconds, At = Now.ToUnixTimeSeconds() - ago };

    static Entry Moment(string guid, string kind, long ago, int quality = 0, bool lost = false) =>
        new() { Kind = kind, Guid = guid, Quality = quality, Lost = lost, At = Now.ToUnixTimeSeconds() - ago };

    static DayTotals OnDay(string guid, string day, int quests = 0, int herbs = 0, double ilvlStart = 0, double ilvl = 0, long moneyStart = 0, long money = 0) =>
        new(guid, day, ilvlStart, ilvl, moneyStart, money, quests, herbs, 0, 0, 0);

    static Leaderboards.Source Source(Leaderboards.Player who, Entry[] events, DayTotals[]? days = null, string guid = "", string name = "") =>
        new(who, events, days ?? [], new Dictionary<string, string> { [guid] = name });

    static Leaderboards.Board Board(List<Leaderboards.Board> boards, string key) => Assert.Single(boards, b => b.Key == key);

    [Fact]
    public void Levels_count_once_per_character_and_level_in_the_last_seven_days()
    {
        var boards = Leaderboards.Build(
        [
            Source(You, [Ding("y1", 40, 3600, 8 * Day), Ding("y1", 41, 3600, 2 * Day), Ding("y1", 42, 5400, Day), Ding("y1", 42, 5400, Day)], guid: "y1", name: "Zeb Drygbeck"),
            Source(Rasmus, [Ding("r1", 50, 7200, Day), Ding("r1", 51, 1800, Day), Ding("r1", 52, 7200, Day)], guid: "r1", name: "Kitty Pryde"),
        ], Now);

        var levels = Board(boards, "levels");
        Assert.Equal(["Rasmus", "You"], levels.Scores.Select(s => s.Who.Name));
        Assert.Equal("Kitty Pryde 49 to 52", levels.Scores[0].Detail);
        Assert.Equal("Zeb Drygbeck 40 to 42", levels.Scores[1].Detail);

        // Fastest: the lowest wins.
        var fastest = Board(boards, "fastest");
        Assert.Equal(["Rasmus", "You"], fastest.Scores.Select(s => s.Who.Name));
        Assert.Equal("30m 0s", fastest.Scores[0].Text);
        Assert.Equal("Kitty Pryde, 50 to 51", fastest.Scores[0].Detail);
    }

    [Fact]
    public void Days_give_quests_gathering_item_level_and_gold()
    {
        var boards = Leaderboards.Build(
        [
            Source(You, [], [
                OnDay("y1", "2026-11-07", quests: 99), // eight days ago: out
                OnDay("y1", "2026-11-12", quests: 10, herbs: 4, ilvlStart: 30, ilvl: 33, moneyStart: 100_000, money: 150_000),
                OnDay("y1", "2026-11-14", quests: 5, ilvlStart: 33, ilvl: 35.5, moneyStart: 150_000, money: 120_000), // spent: no gold
            ], "y1", "Zeb"),
            Source(Benne, [], [OnDay("b1", "2026-11-13", quests: 30)], "b1", "Benne Benlös"),
        ], Now);

        Assert.Equal([30, 15], Board(boards, "quests").Scores.Select(s => s.Value));
        Assert.Equal(4, Board(boards, "herbs").Scores[0].Value);
        var ilvl = Board(boards, "ilvl").Scores[0];
        Assert.Equal(5.5, ilvl.Value);
        Assert.Equal("Zeb", ilvl.Detail);
        Assert.Equal(50_000, Board(boards, "gold").Scores[0].Value);
    }

    [Fact]
    public void You_are_always_on_a_board_and_empty_boards_are_left_out()
    {
        var boards = Leaderboards.Build(
        [
            Source(You, []),
            Source(Rasmus, [Moment("r1", "boss", Day), Moment("r1", "duel", Day, lost: true)]),
        ], Now);

        var bosses = Board(boards, "bosses");
        Assert.Equal([1, 0], bosses.Scores.Select(s => s.Rank));
        Assert.Equal("-", bosses.Scores[1].Text);
        Assert.DoesNotContain(boards, b => b.Key == "duels"); // a lost duel isn't a win
    }

    // A friend's highlights stop at the newest 10, so a busy week's bosses come from their day counts.
    [Fact]
    public void A_friends_day_counts_beat_their_cut_off_highlights()
    {
        var boards = Leaderboards.Build(
        [
            Source(Rasmus, [Moment("r1", "boss", Day), Moment("r2", "boss", Day), Moment("r2", "boss", 2 * Day)],
            [
                OnDay("r1", "2026-11-13") with { HasMoments = true, Bosses = 7, Epics = 2 },
                OnDay("r1", "2026-11-10") with { HasMoments = true, Bosses = 3 },
                OnDay("r1", "2026-11-01") with { HasMoments = true, Bosses = 50 }, // last month
            ]),
        ], Now);

        // r1: 7 + 3 from its days (its one highlight not counted again); r2, on an older addon: its 2 highlights.
        Assert.Equal(12, Board(boards, "bosses").Scores[0].Value);
        Assert.Equal(2, Board(boards, "epics").Scores[0].Value);
    }

    [Fact]
    public void A_tie_shares_the_place()
    {
        var boards = Leaderboards.Build(
        [
            Source(You, [Moment("y1", "mount", Day)]),
            Source(Rasmus, [Moment("r1", "mount", Day)]),
            Source(Benne, [Moment("b1", "mount", Day), Moment("b1", "mount", Day)]),
        ], Now);

        Assert.Equal([1, 2, 2], Board(boards, "mounts").Scores.Select(s => s.Rank));
        Assert.Equal([3, 2, 2], Leaderboards.Mvps(boards).Select(m => m.Points));
    }

    [Fact]
    public void The_mvp_gets_three_points_a_board_won_two_for_second_one_for_third()
    {
        var boards = Leaderboards.Build(
        [
            Source(You, [Moment("y1", "boss", Day), Moment("y1", "boss", Day), Moment("y1", "mount", Day)]),
            Source(Rasmus, [Moment("r1", "boss", Day), Moment("r1", "loot", Day, quality: 4)]),
            Source(Benne, [Moment("b1", "mount", Day), Moment("b1", "mount", Day)]),
        ], Now);

        var mvps = Leaderboards.Mvps(boards);

        // You: bosses 1st (3) + mounts 2nd (2) = 5. Rasmus: epics 1st (3) + bosses 2nd (2) = 5, one board won each, so by name.
        // Benne: mounts 1st (3).
        Assert.Equal(["Rasmus", "You", "Benne"], mvps.Select(m => m.Who.Name));
        Assert.Equal([5, 5, 3], mvps.Select(m => m.Points));
        Assert.Equal(["Most boss kills"], mvps[1].Won);
    }
}
