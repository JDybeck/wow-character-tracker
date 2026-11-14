using System.Globalization;

namespace WoWCharacterTracker.Services;

// The Stats page: the last seven days per person (you with all your characters, or a friend with all
// they share), one board per stat, and the MVP: 3 points for a board won, 2 for second, 1 for third.
//
// Levels and level times come from events (a friend's shared level times). Boss kills, loot, mounts,
// duels, battlegrounds and deaths from events too, or for a friend on 0.41.0+ from the counts in
// their days (a friend on an older addon: their newest 10 highlights, so a busy week comes out low).
// Quests, gathering, item level and gold come from the addon's days (Today.lua).
static class Leaderboards
{
    public const int Days = 7;
    const int FastestFrom = 10; // the first levels take a minute each, a race there says nothing

    // Key: who it is (the BattleTag, or "you"). Characters: their full names by guid.
    public sealed record Player(string Key, string Name, bool IsYou);
    public sealed record Source(Player Who, IReadOnlyList<Entry> Events, IReadOnlyList<DayTotals> Days, IReadOnlyDictionary<string, string> Characters);

    // Rank 0: you, with nothing on this board.
    public sealed record Score(Player Who, double Value, string Text, string Detail, int Rank);
    public sealed record Board(string Key, string Title, string Glyph, IReadOnlyList<Score> Scores);
    public sealed record Mvp(Player Who, int Points, int Firsts, IReadOnlyList<string> Won, int Rank);

    // One person's week, worked out once for every board.
    sealed record Week(Player Who, int Levels, long LevelSeconds, string Mover, (string Who, int Level, long Seconds)? Fastest,
        int Bosses, int Epics, int Mounts, int Duels, int Battlegrounds, int Deaths,
        int Quests, int Herbs, int Ore, int Skins, int Fish, (string Who, double Gain)? Ilvl, long Gold);

    sealed record Stat(string Key, string Title, string Glyph, Func<Week, double> Value, Func<double, string> Text,
        Func<Week, string>? Detail = null, bool LowerWins = false);

    static string Count(double v) => ((long)v).ToString("N0", CultureInfo.CurrentCulture);

    // The boards in the page's order. Glyphs: Segoe Fluent Icons.
    static readonly Stat[] Stats =
    [
        new("levels", "Most level-ups", "", w => w.Levels, Count, w => w.Mover),
        new("leveling", "Most time leveling", "", w => w.LevelSeconds, v => Format.Played((long)v)),
        new("fastest", "Fastest level", "", w => w.Fastest?.Seconds ?? 0, v => Format.Duration((long)v),
            w => w.Fastest is { } f ? $"{f.Who}, {f.Level - 1} to {f.Level}" : "", LowerWins: true),
        new("quests", "Most quests", "", w => w.Quests, Count),
        new("ilvl", "Most item level gained", "", w => w.Ilvl?.Gain ?? 0, v => "+" + v.ToString("0.#", CultureInfo.CurrentCulture),
            w => w.Ilvl?.Who ?? ""),
        new("bosses", "Most boss kills", "", w => w.Bosses, Count),
        new("epics", "Most epics", "", w => w.Epics, Count),
        new("mounts", "Most mounts", "", w => w.Mounts, Count),
        new("herbs", "Most herbs picked", "", w => w.Herbs, Count),
        new("ore", "Most veins mined", "", w => w.Ore, Count),
        new("skins", "Most skins", "", w => w.Skins, Count),
        new("fish", "Most fish caught", "", w => w.Fish, Count),
        new("gold", "Most gold earned", "", w => w.Gold, v => Format.Money((long)v)),
        new("duels", "Most duels won", "", w => w.Duels, Count),
        new("bg", "Most battlegrounds won", "", w => w.Battlegrounds, Count),
        new("deaths", "Most deaths", "", w => w.Deaths, Count),
    ];

    // Boards with at least one score. now: local time; days count by the addon's local date.
    public static List<Board> Build(IEnumerable<Source> sources, DateTimeOffset now)
    {
        var weeks = sources.Select(s => Of(s, now)).ToList();
        var boards = new List<Board>();
        foreach (var stat in Stats)
        {
            var ordered = weeks.Where(w => stat.Value(w) > 0)
                .OrderBy(w => stat.LowerWins ? stat.Value(w) : -stat.Value(w))
                .ThenByDescending(w => w.Who.IsYou)
                .ThenBy(w => w.Who.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            // A tie shares the place (1, 1, 3), and its points.
            var scored = ordered
                .Select(w => new Score(w.Who, stat.Value(w), stat.Text(stat.Value(w)), stat.Detail?.Invoke(w) ?? "",
                    ordered.FindIndex(o => stat.Value(o) == stat.Value(w)) + 1))
                .ToList();
            if (scored.Count == 0) continue;
            // You're always on the board, with a dash when you have nothing on it.
            if (!scored.Any(s => s.Who.IsYou) && weeks.FirstOrDefault(w => w.Who.IsYou) is { } you)
                scored.Add(new Score(you.Who, 0, "-", "", 0));
            boards.Add(new Board(stat.Key, stat.Title, stat.Glyph, scored));
        }
        return boards;
    }

    // 3 points per board won, 2 for second, 1 for third; the most points first, then the most boards won.
    public static List<Mvp> Mvps(IEnumerable<Board> boards)
    {
        var points = new Dictionary<string, (Player Who, int Points, int Firsts, List<string> Won)>();
        foreach (var board in boards)
            foreach (var s in board.Scores.Where(s => s.Rank is >= 1 and <= 3))
            {
                var had = points.TryGetValue(s.Who.Key, out var p) ? p : (s.Who, 0, 0, new List<string>());
                if (s.Rank == 1) had.Item4.Add(board.Title);
                points[s.Who.Key] = (s.Who, had.Item2 + 4 - s.Rank, had.Item3 + (s.Rank == 1 ? 1 : 0), had.Item4);
            }
        return [.. points.Values
            .OrderByDescending(p => p.Points).ThenByDescending(p => p.Firsts).ThenBy(p => p.Who.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select((p, i) => new Mvp(p.Who, p.Points, p.Firsts, p.Won, i + 1))];
    }

    static Week Of(Source s, DateTimeOffset now)
    {
        var from = now.ToUnixTimeSeconds() - Days * 86400L;
        var until = now.ToUnixTimeSeconds() + 300;
        var events = s.Events.Where(e => e.At > from && e.At <= until).ToList();
        string Name(string guid, string fallback) => s.Characters.GetValueOrDefault(guid) ?? fallback;

        var levels = events.Where(e => e.Kind == "level").GroupBy(e => (e.Guid, e.Level)).Select(g => g.First()).ToList();
        var mover = levels.GroupBy(e => e.Guid).MaxBy(g => g.Count());
        var fastest = levels.Where(e => e.Level >= FastestFrom && e.Seconds > 0).MinBy(e => e.Seconds);

        // Days by the local date, the last seven including today; one per character and day.
        var firstDay = now.Date.AddDays(-(Days - 1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var days = s.Days.Where(d => string.CompareOrdinal(d.Day, firstDay) >= 0)
            .GroupBy(d => (d.Guid, d.Day)).Select(g => g.MaxBy(d => d.Quests + d.Herbs + d.Ore + d.Skins + d.Fish)!)
            .ToList();
        // Item level: the character that gained the most, from its first day's start to its last day's end.
        var ilvl = days.GroupBy(d => d.Guid)
            .Select(g =>
            {
                var ordered = g.OrderBy(d => d.Day, StringComparer.Ordinal).ToList();
                var start = ordered.Select(d => d.IlvlStart > 0 ? d.IlvlStart : d.Ilvl).FirstOrDefault(v => v > 0);
                var end = ordered.Select(d => d.Ilvl).LastOrDefault(v => v > 0);
                return (Who: Name(g.Key, ""), Gain: start > 0 && end > start ? Math.Round(end - start, 1) : 0);
            })
            .Where(x => x.Gain >= 0.1)
            .OrderByDescending(x => x.Gain)
            .Select(x => ((string, double)?)x)
            .FirstOrDefault();

        // Moments: a friend's character that sends day counts (all its events) goes by those, since its
        // highlights are only the newest 10; the rest (yours, friends on older addons) by their events.
        // The same kinds as the addon's ns.dayCountKey.
        var counted = days.Where(d => d.HasMoments).ToList();
        var countedGuids = counted.Select(d => d.Guid).ToHashSet();
        var moments = events.Where(e => !countedGuids.Contains(e.Guid)).ToList();
        int Moments(Func<Entry, bool> isOne, Func<DayTotals, int> perDay) => moments.Count(isOne) + counted.Sum(perDay);

        return new Week(s.Who, levels.Count, levels.Sum(e => e.Seconds),
            mover is null ? "" : $"{Name(mover.Key, mover.First().Char)} {mover.Min(e => e.Level) - 1} to {mover.Max(e => e.Level)}",
            fastest is null ? null : (Name(fastest.Guid, fastest.Char), fastest.Level, fastest.Seconds),
            Moments(e => e.Kind is "boss" or "keyrun", d => d.Bosses),
            Moments(e => e.Kind == "loot" && e.Quality >= 4 && !e.Mat, d => d.Epics),
            Moments(e => e.Kind == "mount", d => d.Mounts),
            Moments(e => e.Kind is "duel" or "makgora" && !e.Lost, d => d.Duels),
            Moments(e => e.Kind == "bg" && !e.Lost, d => d.Battlegrounds),
            Moments(e => e.Kind == "death", d => d.Deaths),
            days.Sum(d => d.Quests), days.Sum(d => d.Herbs), days.Sum(d => d.Ore), days.Sum(d => d.Skins), days.Sum(d => d.Fish),
            ilvl,
            days.Sum(d => Math.Max(0, d.Money - d.MoneyStart)));
    }
}
