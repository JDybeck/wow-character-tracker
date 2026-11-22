using System.Text.Json;
using WoWCharacterTracker.Services.Discord;

namespace WoWCharacterTracker.Tests;

// The weekly recap (WeeklyRecap): when it's due, and what the post says.
public class WeeklyRecapTests
{
    static readonly TimeSpan Cet = TimeSpan.FromHours(1);
    // Sunday 22 November 2026.
    static DateTimeOffset At(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, Cet);

    [Fact]
    public void The_last_slot_is_the_latest_planned_time_at_or_before_now()
    {
        // Sunday 20:00: before it on Sunday the slot is last week's, from it on today's.
        Assert.Equal(At(15, 20), WeeklyRecap.LastSlot(At(22, 19, 59), DayOfWeek.Sunday, 20));
        Assert.Equal(At(22, 20), WeeklyRecap.LastSlot(At(22, 20), DayOfWeek.Sunday, 20));
        Assert.Equal(At(22, 20), WeeklyRecap.LastSlot(At(24, 9), DayOfWeek.Sunday, 20));
        // Wednesday 8:00, seen on a Sunday: the Wednesday before.
        Assert.Equal(At(18, 8), WeeklyRecap.LastSlot(At(22, 12), DayOfWeek.Wednesday, 8));
        // The hour stays a day's hour.
        Assert.Equal(At(22, 23), WeeklyRecap.LastSlot(At(22, 23, 30), DayOfWeek.Sunday, 99));
    }

    [Fact]
    public void A_recap_is_due_once_per_week_and_not_days_late()
    {
        var slot = At(22, 20).ToUnixTimeSeconds();
        var lastWeek = At(15, 20).ToUnixTimeSeconds();

        Assert.Null(WeeklyRecap.Due(At(22, 19), DayOfWeek.Sunday, 20, lastWeek)); // not yet
        Assert.Equal(At(22, 20), WeeklyRecap.Due(At(22, 20, 5), DayOfWeek.Sunday, 20, lastWeek));
        Assert.Null(WeeklyRecap.Due(At(22, 20, 10), DayOfWeek.Sunday, 20, slot)); // sent already

        // The app was closed on Sunday evening: it posts on Monday, for the week up to Sunday 20:00.
        Assert.Equal(At(22, 20), WeeklyRecap.Due(At(23, 9), DayOfWeek.Sunday, 20, lastWeek));
        // But not on Wednesday.
        Assert.Null(WeeklyRecap.Due(At(25, 9), DayOfWeek.Sunday, 20, lastWeek));
    }

    [Fact]
    public void The_week_reads_as_its_seven_days()
    {
        Assert.Equal("16 to 22 November", WeeklyRecap.WeekText(At(22, 20)));
        Assert.Equal("26 November to 2 December", WeeklyRecap.WeekText(new DateTimeOffset(2026, 12, 2, 20, 0, 0, Cet)));
    }

    static readonly Leaderboards.Player You = new("you", "You", true);
    static readonly Leaderboards.Player Rasmus = new("Rasmus#1", "Rasmus", false);
    static readonly Leaderboards.Player Benne = new("Benne#2", "Benne", false);

    static Leaderboards.Score Score(Leaderboards.Player who, int rank, string text, string detail = "") => new(who, rank, text, detail, rank);

    [Fact]
    public void The_post_has_the_podium_and_each_boards_top_three_with_your_name()
    {
        List<Leaderboards.Board> boards =
        [
            new("quests", "Most quests", "", [Score(Rasmus, 1, "40"), Score(You, 2, "30"), Score(Benne, 3, "20"), new(new("x#4", "Fourth", false), 10, "10", "", 4)]),
            new("epics", "Most epics", "", [Score(Rasmus, 1, "2")]),
            new("ilvl", "Most item level gained", "", [Score(You, 1, "+12.5", "Zeb Drygbeck")]),
            new("deaths", "Most deaths", "", [Score(Benne, 1, "3"), new(You, 0, "-", "", 0)]),
        ];

        var embed = WeeklyRecap.Embed(boards, "Zeb", "WoW Forever", At(22, 20))!;
        var json = JsonSerializer.SerializeToElement(embed);

        Assert.Equal("Weekly recap", json.GetProperty("title").GetString());
        var text = json.GetProperty("description").GetString()!;
        Assert.Contains("The week of 16 to 22 November.", text);
        // Rasmus: 3 + 3 = 6, Zeb: 2 + 3 = 5, Benne: 1 + 3 = 4.
        Assert.Contains("🥇 **Rasmus**, 6 points (won Most quests and Most epics)", text);
        Assert.Contains("🥈 **Zeb**, 5 points (won Most item level gained)", text);
        Assert.Contains("🥉 **Benne**, 4 points (won Most deaths)", text);
        Assert.DoesNotContain("You", text);

        var fields = json.GetProperty("fields").EnumerateArray().ToList();
        Assert.Equal(["Most quests", "Most epics", "Most item level gained", "Most deaths"], fields.Select(f => f.GetProperty("name").GetString()));
        // The top three only, and no dash for you when you're not on a board.
        Assert.Equal("1. Rasmus: **40**\n2. Zeb: **30**\n3. Benne: **20**", fields[0].GetProperty("value").GetString());
        Assert.Equal("1. Zeb: **+12.5** (Zeb Drygbeck)", fields[2].GetProperty("value").GetString());
        Assert.Equal("1. Benne: **3**", fields[3].GetProperty("value").GetString());
        Assert.All(fields, f => Assert.True(f.GetProperty("inline").GetBoolean()));
    }

    [Fact]
    public void An_empty_week_has_no_post()
    {
        Assert.Null(WeeklyRecap.Embed([], "Zeb", "WoW Forever", At(22, 20)));
    }

    // Built from the Stats page's data: a recap posted late still covers the week up to its planned time.
    [Fact]
    public void A_late_recap_covers_the_week_up_to_its_planned_time()
    {
        var slot = At(22, 20);
        Entry Epic(long at) => new() { Kind = "loot", Guid = "y1", Quality = 4, At = at };
        var source = new Leaderboards.Source(You, [Epic(slot.ToUnixTimeSeconds() - 3600), Epic(slot.ToUnixTimeSeconds() + 3600)], [],
            new Dictionary<string, string> { ["y1"] = "Zeb" });

        var boards = Leaderboards.Build([source], slot, Leaderboards.Period.Week);

        // The one after Sunday 20:00 belongs to next week.
        Assert.Equal(1, Assert.Single(boards, b => b.Key == "epics").Scores[0].Value);
    }
}
