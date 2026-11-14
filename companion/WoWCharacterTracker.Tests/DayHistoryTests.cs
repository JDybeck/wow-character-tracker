using WoWCharacterTracker.Services;

namespace WoWCharacterTracker.Tests;

// DayHistory: the days kept for the Stats page's month and all time, after the addon has dropped them.
public sealed class DayHistoryTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "wct-dayhistory-" + Guid.NewGuid().ToString("N"));
    string FilePath => Path.Combine(dir, "days-test.json");

    public DayHistoryTests() => Directory.CreateDirectory(dir);
    public void Dispose() => Directory.Delete(dir, recursive: true);

    static DayTotals OnDay(string guid, string day, int quests = 0, long money = 0) => new(guid, day, 0, 0, 0, money, quests, 0, 0, 0, 0);

    [Fact]
    public void Days_the_addon_dropped_are_kept_and_survive_a_restart()
    {
        new DayHistory(FilePath).Add([OnDay("y1", "2026-10-01", quests: 9), OnDay("y1", "2026-11-13", quests: 3)]);

        // Later the addon only has the newest day; the old one stays, also for a new app run.
        var days = new DayHistory(FilePath).Add([OnDay("y1", "2026-11-13", quests: 4)]);

        Assert.Equal([("2026-10-01", 9), ("2026-11-13", 4)], days.Select(d => (d.Day, d.Quests)));
    }

    [Fact]
    public void One_per_character_and_day_the_busiest_and_of_equals_the_latest()
    {
        var days = DayHistory.OnePerDay(
        [
            OnDay("y1", "2026-11-14", quests: 5, money: 100),
            OnDay("y1", "2026-11-14", quests: 5, money: 300), // same counts, gold moved on
            OnDay("y1", "2026-11-14", quests: 2, money: 900), // an older copy with fewer quests
            OnDay("r1", "2026-11-14", quests: 1),
        ]);

        Assert.Equal(2, days.Count);
        var mine = Assert.Single(days, d => d.Guid == "y1");
        Assert.Equal(300, mine.Money);
    }

    [Fact]
    public void A_friends_moment_counts_round_trip()
    {
        var day = OnDay("r1", "2026-11-14", quests: 2) with { HasMoments = true, Bosses = 3, Deaths = 1 };

        Assert.Equal([day], DayHistory.Deserialize(DayHistory.Serialize([day])));
    }

    [Fact]
    public void A_broken_file_starts_over_instead_of_failing()
    {
        File.WriteAllText(FilePath, "{ not json");

        var days = new DayHistory(FilePath).Add([OnDay("y1", "2026-11-14", quests: 1)]);

        Assert.Single(days);
    }
}
