namespace WoWCharacterTracker.ViewModels;

// The Leveling tab of the character details: average, fastest and slowest level, a chart of level
// times, the time-to-max estimate (Forever), how gear and professions grew, and the duel record.
sealed class CharacterLeveling
{
    const double ChartHeight = 70;

    public string AverageText { get; init; } = "";
    public string FastestText { get; init; } = "";
    public string SlowestText { get; init; } = "";
    public bool HasStats { get; init; }
    public string EstimateText { get; init; } = "";
    public bool HasEstimate => EstimateText.Length > 0;
    public IReadOnlyList<LevelBar> Bars { get; init; } = [];
    public IReadOnlyList<string> Progress { get; init; } = [];
    public bool HasProgress => Progress.Count > 0;
    public string DuelText { get; init; } = "";
    public bool HasDuels => DuelText.Length > 0;

    // all: the character's events, oldest first. xp: the newest snapshot that has XP (saved file).
    public static CharacterLeveling From(IReadOnlyList<Entry> all, int level, Snapshot? xp, IReadOnlyDictionary<int, long> xpTable, bool estimate)
    {
        var timed = all.Where(e => e.Kind == "level" && e.Seconds > 0)
            .GroupBy(e => e.Level).Select(g => new LevelStats.Level(g.Key, g.Last().Seconds))
            .OrderBy(l => l.Number).ToList();
        var summary = LevelStats.Summarize(timed);

        var shown = timed.TakeLast(40).ToList();
        var slowest = shown.Count > 0 ? shown.Max(l => l.Seconds) : 1;
        var bars = shown.Select(l => new LevelBar(l.Number, Math.Max(3, ChartHeight * l.Seconds / slowest),
            $"{l.Number - 1} to {l.Number}: {Format.Duration(l.Seconds)}", l == summary?.Fastest || l == summary?.Slowest)).ToList();

        var toMax = estimate && xp is not null
            ? LevelStats.TimeToMax(timed, xpTable, level, xp.Xp, xp.XpMax, xp.MaxLevel > 0 ? xp.MaxLevel : 60)
            : null;

        return new CharacterLeveling
        {
            HasStats = summary is not null,
            AverageText = summary is null ? "" : Format.Duration(summary.Average),
            FastestText = summary is null ? "" : $"{Format.Duration(summary.Fastest.Seconds)} ({summary.Fastest.Number - 1} to {summary.Fastest.Number})",
            SlowestText = summary is null ? "" : $"{Format.Duration(summary.Slowest.Seconds)} ({summary.Slowest.Number - 1} to {summary.Slowest.Number})",
            EstimateText = toMax is long left ? $"About {Format.Played(left)} played to level {(xp!.MaxLevel > 0 ? xp.MaxLevel : 60)}, at your recent pace" : "",
            Bars = bars,
            Progress = ProgressLines(all),
            DuelText = Duels(all),
        };
    }

    // "Item level 24 to 38 (levels 30 to 40)" and "Mining 75 to 210", from what each event recorded.
    static List<string> ProgressLines(IReadOnlyList<Entry> all)
    {
        var lines = new List<string>();
        var geared = all.Where(e => e.ItemLevel > 0).ToList();
        if (geared.Count > 1 && geared[^1].ItemLevel > geared[0].ItemLevel)
            lines.Add($"Item level {geared[0].ItemLevel:0} to {geared[^1].ItemLevel:0} (levels {geared[0].Level} to {geared[^1].Level})");

        var skills = new Dictionary<string, (int First, int Last)>();
        foreach (var e in all)
            foreach (var p in e.Professions.Split(", ", StringSplitOptions.RemoveEmptyEntries))
            {
                var space = p.LastIndexOf(' ');
                if (space <= 0 || !int.TryParse(p[(space + 1)..], out var skill)) continue;
                var name = p[..space];
                skills[name] = skills.TryGetValue(name, out var s) ? (s.First, skill) : (skill, skill);
            }
        lines.AddRange(skills.Where(s => s.Value.Last > s.Value.First).Select(s => $"{s.Key} {s.Value.First} to {s.Value.Last}"));
        return lines;
    }

    // "Duels: 5 won, 2 lost (1 ran away). Mak'gora: 1 won."
    static string Duels(IReadOnlyList<Entry> all)
    {
        string Record(string kind, string label)
        {
            var fights = all.Where(e => e.Kind == kind).ToList();
            if (fights.Count == 0) return "";
            var won = fights.Count(f => !f.Lost);
            var lost = fights.Count - won;
            var fled = fights.Count(f => f.Lost && f.Fled);
            return $"{label}: {won} won" + (lost > 0 ? $", {lost} lost" : "") + (fled > 0 ? $" ({fled} ran away)" : "");
        }
        var parts = new[] { Record("duel", "Duels"), Record("makgora", "Mak'gora") }.Where(p => p.Length > 0).ToList();
        return parts.Count == 0 ? "" : string.Join(". ", parts) + ".";
    }
}
