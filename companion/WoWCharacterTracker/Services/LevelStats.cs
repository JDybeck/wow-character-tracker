namespace WoWCharacterTracker.Services;

// Numbers from a character's level history: average, fastest and slowest level, and an estimate of
// the played time left to max level.
//
// The estimate works in XP, since later levels need far more of it than early ones, and monsters give
// more XP the higher you are: the character's recent pace over its last few timed levels, carried over
// the XP still to go. The XP each level needs comes from the client (xpTable, saved by the addon as
// characters level); levels nobody has reached yet use Classic's table, scaled to match the levels
// the client has shown.
static class LevelStats
{
    public sealed record Level(int Number, long Seconds);
    public sealed record Summary(int Count, long Average, Level Fastest, Level Slowest);

    public static Summary? Summarize(IReadOnlyList<Level> timed)
    {
        if (timed.Count == 0) return null;
        return new Summary(timed.Count, (long)timed.Average(l => l.Seconds),
            timed.MinBy(l => l.Seconds)!, timed.MaxBy(l => l.Seconds)!);
    }

    // Played seconds left to max level at the recent pace, or null when there's too little to go on
    // (fewer than two timed levels, or levels past Classic's table with nothing known about them).
    public static long? TimeToMax(IReadOnlyList<Level> timed, IReadOnlyDictionary<int, long> xpTable,
        int level, long xp, long xpMax, int maxLevel)
    {
        if (maxLevel <= level || timed.Count < 2) return null;
        var scale = Scale(xpTable);
        long? Need(int l) => xpTable.TryGetValue(l, out var known) ? known
            : l >= 1 && l < ClassicXp.Length ? (long)(ClassicXp[l] * scale) : null;

        // Monsters give more XP as you level (Classic: 45 + 5 x level for a kill at your level), so XP
        // per hour grows too. The pace is counted in "kills": a level's XP divided by a kill's XP.
        static double Kill(int l) => 45 + 5 * l;

        // Level N's time is the climb from N-1 to N, which takes the XP level N-1 needs.
        var recent = timed.OrderByDescending(l => l.Number).Take(5).ToList();
        double seconds = 0, kills = 0;
        foreach (var l in recent)
        {
            if (Need(l.Number - 1) is not long need) return null;
            seconds += l.Seconds;
            kills += need / Kill(l.Number - 1);
        }
        if (kills == 0) return null;

        var left = (xpMax > 0 ? Math.Max(0, xpMax - xp) : Need(level) ?? 0) / Kill(level);
        for (var l = level + 1; l < maxLevel; l++)
        {
            if (Need(l) is not long need) return null;
            left += need / Kill(l);
        }
        return (long)(left * (seconds / kills));
    }

    // How the client's XP compares to Classic's, from the levels it has shown (1 when none).
    static double Scale(IReadOnlyDictionary<int, long> xpTable)
    {
        var ratios = xpTable.Where(p => p.Key >= 1 && p.Key < ClassicXp.Length)
            .Select(p => (double)p.Value / ClassicXp[p.Key]).Order().ToList();
        return ratios.Count == 0 ? 1 : ratios[ratios.Count / 2];
    }

    // XP needed at each level in Classic (index = level, 1-59).
    static readonly long[] ClassicXp =
    [
        0, 400, 900, 1400, 2100, 2800, 3600, 4500, 5400, 6500, 7600, 8800, 10100, 11400, 12900, 14400, 16000, 17700, 19400, 21300,
        23200, 25200, 27300, 29400, 31700, 34000, 36400, 38900, 41400, 44300, 47400, 50800, 54500, 58600, 62800, 67100, 71600, 76100,
        80800, 85700, 90700, 95800, 101000, 106300, 111800, 117500, 123200, 129100, 135100, 141200, 147500, 153900, 160400, 167100,
        173900, 180800, 187900, 195000, 202300, 209800,
    ];
}
