namespace WoWCharacterTracker.Services;

// Two characters' level times side by side, like a speedrun's splits: the played time it took each to
// reach the highest level both have timed from the start, and who was faster on each level.
static class LevelRace
{
    // Level: the highest level both have a full history to (0: neither does, only the splits count).
    // Mine/Theirs: played seconds to reach it. Faster: levels each took less time on (ties count for nobody).
    public sealed record Result(int Level, long Mine, long Theirs, int MineFaster, int TheirsFaster)
    {
        public int Compared => MineFaster + TheirsFaster;
    }

    // Played seconds to reach each level, summed from level 2. Stops at the first level without a
    // time (the addon came later, or older data): a sum with a gap would be too small.
    public static Dictionary<int, long> Totals(IReadOnlyList<LevelStats.Level> timed)
    {
        var byLevel = timed.ToDictionary(l => l.Number, l => l.Seconds);
        var totals = new Dictionary<int, long>();
        long sum = 0;
        for (var level = 2; byLevel.TryGetValue(level, out var seconds); level++)
            totals[level] = sum += seconds;
        return totals;
    }

    // null: no level both have a time for.
    public static Result? Compare(IReadOnlyList<LevelStats.Level> mine, IReadOnlyList<LevelStats.Level> theirs)
    {
        var theirTimes = theirs.ToDictionary(l => l.Number, l => l.Seconds);
        var both = mine.Where(l => theirTimes.ContainsKey(l.Number)).ToList();
        if (both.Count == 0) return null;

        var mineTotals = Totals(mine);
        var theirTotals = Totals(theirs);
        var level = mineTotals.Keys.Intersect(theirTotals.Keys).DefaultIfEmpty(0).Max();
        return new Result(level, mineTotals.GetValueOrDefault(level), theirTotals.GetValueOrDefault(level),
            both.Count(l => l.Seconds < theirTimes[l.Number]), both.Count(l => l.Seconds > theirTimes[l.Number]));
    }

    public sealed record Time(string Guid, string Name, long Seconds);

    // Everyone's time on each level, fastest first (the chart's tooltips list the others on a bar).
    public static Dictionary<int, List<Time>> Board(IEnumerable<(string Guid, string Name, IReadOnlyList<LevelStats.Level> Timed)> characters) =>
        characters.SelectMany(c => c.Timed.Select(l => (l.Number, Time: new Time(c.Guid, c.Name, l.Seconds))))
            .GroupBy(x => x.Number)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Time).OrderBy(t => t.Seconds).ToList());

    // Which of your characters to race a friend's against: the one that shares the most levels with
    // it (the highest common level with a full history first), then the one played last.
    // Candidates: (guid, timed levels, last seen).
    public static string? PickRival(IReadOnlyList<LevelStats.Level> theirs, IEnumerable<(string Guid, IReadOnlyList<LevelStats.Level> Timed, long LastSeen)> candidates) =>
        candidates.Select(c => (c.Guid, c.LastSeen, Result: Compare(c.Timed, theirs)))
            .Where(c => c.Result is not null)
            .OrderByDescending(c => c.Result!.Level)
            .ThenByDescending(c => c.Result!.Compared)
            .ThenByDescending(c => c.LastSeen)
            .Select(c => c.Guid)
            .FirstOrDefault();
}
