using System.Globalization;

namespace WoWCharacterTracker.Services;

// The Stats page: a period (today, the last 7 or 30 days, all time) per person (you with all your
// characters, or a friend with all they share), one board per stat, and the MVP: 3 points for a board
// won, 2 for second, 1 for third.
//
// Levels and level times come from events (a friend's shared level times, their newest 80). Boss kills,
// loot, mounts, duels, battlegrounds and deaths from events too, or for a friend on 0.41.0+ from the
// counts in their days (a friend on an older addon: their newest 10 highlights, so a busy week comes
// out low). Quests, gathering, item level and gold come from the days (Today.lua, kept by DayHistory).
static class Leaderboards
{
    public enum Period { Today, Week, Month, All }
    const int FastestFrom = 10; // the first levels take a minute each, a race there says nothing

    // The days a period covers, today included; null: all of them.
    static int? DaysIn(Period period) => period switch { Period.Today => 1, Period.Week => 7, Period.Month => 30, _ => null };

    // Key: who it is (the BattleTag, or "you"). Characters: their full names by guid.
    public sealed record Player(string Key, string Name, bool IsYou);
    // Character: the source is one character (Who is named after it), so a detail leaves its name out.
    public sealed record Source(Player Who, IReadOnlyList<Entry> Events, IReadOnlyList<DayTotals> Days, IReadOnlyDictionary<string, string> Characters,
        bool Character = false);

    // Who's on the boards. Versus: you (all your characters) against each friend (all theirs). You: your
    // characters one by one. Friends: every friend character one by one, with whose it is.
    public enum View { Versus, You, Friends }

    // The sources a view puts on the boards. A character is never "you", so no row of yours is added to a
    // board it isn't on.
    public static List<Source> For(View view, IEnumerable<Source> sources) => view switch
    {
        View.You => [.. sources.Where(s => s.Who.IsYou).SelectMany(s => PerCharacter(s, ""))],
        View.Friends => [.. sources.Where(s => !s.Who.IsYou).SelectMany(s => PerCharacter(s, s.Who.Name))],
        _ => [.. sources],
    };

    // owner: the friend's name after the character's ("Kitty Pryde (Rasmus)"); empty for yours.
    static IEnumerable<Source> PerCharacter(Source s, string owner) => s.Characters.Select(c => new Source(
        new Player(c.Key, owner.Length > 0 ? $"{c.Value} ({owner})" : c.Value, false),
        [.. s.Events.Where(e => e.Guid == c.Key)], [.. s.Days.Where(d => d.Guid == c.Key)], new Dictionary<string, string> { [c.Key] = c.Value }, true));

    // Rank 0: you, with nothing on this board.
    public sealed record Score(Player Who, double Value, string Text, string Detail, int Rank);
    // Total: the board added up ("+270g"), or empty.
    public sealed record Board(string Key, string Title, string Glyph, IReadOnlyList<Score> Scores, string Total = "");
    public sealed record Mvp(Player Who, int Points, int Firsts, IReadOnlyList<string> Won, int Rank);

    // One person's period, worked out once for every board.
    sealed record Tally(Player Who, int LevelScore, long LevelSeconds, string Mover, (string Who, int Level, long Seconds)? Fastest,
        int Bosses, int Epics, int Mounts, int Duels, int Battlegrounds, int Deaths,
        int Quests, int Herbs, int Ore, int Skins, int Fish, (string Who, double Gain)? Ilvl, long Gold);

    sealed record Stat(string Key, string Title, string Glyph, Func<Tally, double> Value, Func<double, string> Text,
        Func<Tally, string>? Detail = null, bool LowerWins = false, bool Signed = false, bool Summed = false);

    static string Count(double v) => ((long)v).ToString("N0", CultureInfo.CurrentCulture);

    // What a level-up is worth on the levels board: the higher, the longer it took. Below 10 nothing,
    // those take a minute each.
    public static int LevelScore(int level) => level switch { < 10 => 0, < 30 => 1, < 50 => 2, _ => 3 };

    // The boards in the page's order. Glyphs: Segoe Fluent Icons.
    static readonly Stat[] Stats =
    [
        new("levels", "Level-up score", "", w => w.LevelScore, Count, w => w.Mover),
        new("leveling", "Most time leveling", "", w => w.LevelSeconds, v => Format.Played((long)v)),
        new("fastest", "Fastest level", "", w => w.Fastest?.Seconds ?? 0, v => Format.Duration((long)v),
            w => w.Fastest is { } f ? (f.Who.Length > 0 ? $"{f.Who}, " : "") + $"{f.Level - 1} to {f.Level}" : "", LowerWins: true),
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
        new("gold", "Most gold made", "", w => w.Gold, v => (v < 0 ? "-" : "+") + Format.Money(Math.Abs((long)v)), Signed: true, Summed: true),
        new("duels", "Most duels won", "", w => w.Duels, Count),
        new("bg", "Most battlegrounds won", "", w => w.Battlegrounds, Count),
        new("deaths", "Most deaths", "", w => w.Deaths, Count),
    ];

    // Boards with at least one score. now: local time; days count by the addon's local date. totals: a
    // board that adds up (gold) gets its total when two or more are on it.
    public static List<Board> Build(IEnumerable<Source> sources, DateTimeOffset now, Period period = Period.Week, bool totals = false)
    {
        var tallies = sources.Select(s => Of(s, now, period)).ToList();
        var boards = new List<Board>();
        foreach (var stat in Stats)
        {
            // Signed (gold, the net): a loss is on the board too, below everyone who made some.
            var ordered = tallies.Where(w => stat.Signed ? stat.Value(w) != 0 : stat.Value(w) > 0)
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
            if (!scored.Any(s => s.Who.IsYou) && tallies.FirstOrDefault(w => w.Who.IsYou) is { } you)
                scored.Add(new Score(you.Who, 0, "-", "", 0));
            var total = totals && stat.Summed && scored.Count(s => s.Rank > 0) >= 2 ? stat.Text(scored.Sum(s => s.Value)) : "";
            boards.Add(new Board(stat.Key, stat.Title, stat.Glyph, scored, total));
        }
        return boards;
    }

    // 3 points per board won, 2 for second, 1 for third (never for a loss, like gold below 0); the most
    // points first, then the most boards won.
    public static List<Mvp> Mvps(IEnumerable<Board> boards)
    {
        var points = new Dictionary<string, (Player Who, int Points, int Firsts, List<string> Won)>();
        foreach (var board in boards)
            foreach (var s in board.Scores.Where(s => s.Rank is >= 1 and <= 3 && s.Value > 0))
            {
                var had = points.TryGetValue(s.Who.Key, out var p) ? p : (s.Who, 0, 0, new List<string>());
                if (s.Rank == 1) had.Item4.Add(board.Title);
                points[s.Who.Key] = (s.Who, had.Item2 + 4 - s.Rank, had.Item3 + (s.Rank == 1 ? 1 : 0), had.Item4);
            }
        return [.. points.Values
            .OrderByDescending(p => p.Points).ThenByDescending(p => p.Firsts).ThenBy(p => p.Who.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select((p, i) => new Mvp(p.Who, p.Points, p.Firsts, p.Won, i + 1))];
    }

    static Tally Of(Source s, DateTimeOffset now, Period period)
    {
        // Today: since midnight. The others roll: the last n times 24 hours, and the last n dates.
        var n = DaysIn(period);
        var from = n switch
        {
            null => long.MinValue,
            1 => new DateTimeOffset(now.Date, now.Offset).ToUnixTimeSeconds() - 1,
            _ => now.ToUnixTimeSeconds() - n.Value * 86400L,
        };
        var until = now.ToUnixTimeSeconds() + 300;
        var events = s.Events.Where(e => e.At > from && e.At <= until).ToList();
        string Name(string guid, string fallback) => s.Characters.GetValueOrDefault(guid) ?? fallback;
        // A detail's character: left out when the row already is that character.
        string Who(string guid, string fallback) => s.Character ? "" : Name(guid, fallback);

        var levels = events.Where(e => e.Kind == "level").GroupBy(e => (e.Guid, e.Level)).Select(g => g.First()).ToList();
        // The levels board's detail: the character that scored the most, with its levels that scored.
        var mover = levels.Where(e => LevelScore(e.Level) > 0).GroupBy(e => e.Guid).MaxBy(g => g.Sum(e => LevelScore(e.Level)));
        var fastest = levels.Where(e => e.Level >= FastestFrom && e.Seconds > 0).MinBy(e => e.Seconds);

        // Days by the local date, the period's last n including today; one per character and day.
        var firstDay = n is { } count ? now.Date.AddDays(-(count - 1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
        var days = DayHistory.OnePerDay(s.Days.Where(d => string.CompareOrdinal(d.Day, firstDay) >= 0));
        // Item level: the character that gained the most, from its first day's start to its last day's end.
        var ilvl = days.GroupBy(d => d.Guid)
            .Select(g =>
            {
                var ordered = g.OrderBy(d => d.Day, StringComparer.Ordinal).ToList();
                var start = ordered.Select(d => d.IlvlStart > 0 ? d.IlvlStart : d.Ilvl).FirstOrDefault(v => v > 0);
                var end = ordered.Select(d => d.Ilvl).LastOrDefault(v => v > 0);
                return (Who: Who(g.Key, ""), Gain: start > 0 && end > start ? Math.Round(end - start, 1) : 0);
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

        return new Tally(s.Who, levels.Sum(e => LevelScore(e.Level)), levels.Sum(e => e.Seconds),
            mover is null ? "" : $"{Who(mover.Key, mover.First().Char)} {mover.Min(e => e.Level) - 1} to {mover.Max(e => e.Level)}, {Format.Count(mover.Count(), "level")}".TrimStart(),
            fastest is null ? null : (Who(fastest.Guid, fastest.Char), fastest.Level, fastest.Seconds),
            Moments(e => e.Kind is "boss" or "keyrun", d => d.Bosses),
            Moments(e => e.Kind == "loot" && e.Quality >= 4 && !e.Mat, d => d.Epics),
            Moments(e => e.Kind == "mount", d => d.Mounts),
            Moments(e => e.Kind is "duel" or "makgora" && !e.Lost, d => d.Duels),
            Moments(e => e.Kind == "bg" && !e.Lost, d => d.Battlegrounds),
            Moments(e => e.Kind == "death", d => d.Deaths),
            days.Sum(d => d.Quests), days.Sum(d => d.Herbs), days.Sum(d => d.Ore), days.Sum(d => d.Skins), days.Sum(d => d.Fish),
            ilvl,
            days.Sum(d => d.Money - d.MoneyStart)); // the net, so days that spent more count as minus
    }
}
