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

    static readonly DateTimeOffset Slot = At(22, 20);
    static readonly WeeklyRecap.Character Zeb = new("y1", "Zeb Drygbeck", 48);
    static readonly WeeklyRecap.Character Ronnie = new("y2", "Ronnie", 12);
    static readonly WeeklyRecap.Character Idle = new("y3", "Idle", 60);

    static Entry E(string guid, string kind, long hoursAgo) => new() { Kind = kind, Guid = guid, At = Slot.ToUnixTimeSeconds() - hoursAgo * 3600 };
    static Entry Ding(string guid, int level, long seconds, long hoursAgo) => E(guid, "level", hoursAgo) with { Level = level, Seconds = seconds };

    static DayTotals Day(string guid, int day, int quests = 0, double ilvlStart = 0, double ilvl = 0, long moneyStart = 0, long money = 0, int herbs = 0) =>
        new(guid, $"2026-11-{day:00}", ilvlStart, ilvl, moneyStart, money, quests, herbs, 0, 0, 0);

    [Fact]
    public void Each_character_gets_what_it_did_this_week()
    {
        Entry[] events =
        [
            Ding("y1", 41, 3600, 100), Ding("y1", 42, 5400, 50), Ding("y1", 42, 5400, 50), Ding("y1", 48, 7200, 2),
            E("y1", "loot", 30) with { Quality = 4, Item = "Gutgore Ripper" },
            E("y1", "loot", 20) with { Quality = 3, Item = "A blue" },
            E("y1", "mount", 10) with { Mount = "Swift Razzashi Raptor" },
            E("y1", "boss", 9) with { Boss = "Onyxia" }, E("y1", "boss", 8) with { Boss = "Onyxia" },
            E("y1", "profession", 7) with { Profession = "Mining", Step = 150, Rating = 151 },
            E("y1", "death", 6),
            Ding("y1", 40, 3600, 8 * 24), // last week
            Ding("y2", 12, 600, 1),
        ];
        DayTotals[] days =
        [
            Day("y1", 15, quests: 99), // the Sunday before: not this week
            Day("y1", 16, quests: 12, ilvlStart: 40, ilvl: 42, moneyStart: 100_0000, money: 150_0000, herbs: 3),
            Day("y1", 22, quests: 8, ilvl: 45.5, moneyStart: 150_0000, money: 250_0000),
        ];

        var weeks = WeeklyRecap.Build([Ronnie, Zeb, Idle], events, days, Slot);

        // The busiest first, and nothing for a character that did nothing.
        Assert.Equal(["Zeb Drygbeck, level 48", "Ronnie, level 12"], weeks.Select(w => w.Title));
        Assert.Equal(
        [
            "⬆️ Levelled from **40** to **48** (3 levels) in 4h 30m",
            "💜 New epic: **Gutgore Ripper**",
            "🐎 New mount: **Swift Razzashi Raptor**",
            "💀 Killed **Onyxia**",
            "⚒️ Reached Mining 151",
            "📜 20 quests done",
            "🌿 Gathered 3 herbs",
            "🛡️ Item level 40 to **45.5**",
            "💰 **+150g**",
            "🪦 Died once",
        ], weeks[0].Lines);
        Assert.Equal(["⬆️ Levelled from **11** to **12** in 10m 0s"], weeks[1].Lines);
    }

    [Fact]
    public void The_post_has_a_field_per_character_and_says_when_its_max_level()
    {
        var weeks = WeeklyRecap.Build([Zeb], [Ding("y1", 60, 9000, 3) with { MaxLevel = true }], [], Slot);
        var json = JsonSerializer.SerializeToElement(WeeklyRecap.Embed(weeks, "WoW Forever", Slot));

        Assert.Equal("Weekly recap", json.GetProperty("title").GetString());
        Assert.Equal("The week of 16 to 22 November: 1 character played.", json.GetProperty("description").GetString());
        var field = Assert.Single(json.GetProperty("fields").EnumerateArray());
        Assert.Equal("Zeb Drygbeck, level 48", field.GetProperty("name").GetString());
        Assert.Contains("reaching max level!", field.GetProperty("value").GetString());
    }

    [Fact]
    public void Many_epics_name_the_first_three()
    {
        var loot = Enumerable.Range(1, 5).Select(i => E("y1", "loot", i) with { Quality = 4, Item = $"Epic {i}" }).ToArray();
        var line = Assert.Single(Assert.Single(WeeklyRecap.Build([Zeb], loot, [], Slot)).Lines);
        Assert.Equal("💜 5 new epics: **Epic 5**, **Epic 4**, **Epic 3** and 2 more", line);
    }

    [Fact]
    public void A_quiet_week_has_no_post()
    {
        var weeks = WeeklyRecap.Build([Zeb], [Ding("y1", 40, 3600, 8 * 24)], [], Slot);
        Assert.Empty(weeks);
        Assert.Null(WeeklyRecap.Embed(weeks, "WoW Forever", Slot));
    }

    // A recap posted late still covers the week up to its planned time.
    [Fact]
    public void A_late_recap_covers_the_week_up_to_its_planned_time()
    {
        var weeks = WeeklyRecap.Build([Zeb], [Ding("y1", 41, 60, 1), Ding("y1", 42, 60, -1)], [], Slot);
        Assert.Equal("⬆️ Levelled from **40** to **41** in 1m 0s", Assert.Single(Assert.Single(weeks).Lines));
    }
}
