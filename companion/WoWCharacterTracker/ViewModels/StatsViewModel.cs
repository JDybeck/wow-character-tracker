using System.Windows.Input;

namespace WoWCharacterTracker.ViewModels;

// The Stats page of one game tab: the period's MVP podium and a leaderboard per stat (Leaderboards),
// you against every friend who shares with you. A board shows the top three and your own row; Show
// all lists everyone, and stays open across syncs. The period (today, week, month, all time) is kept
// for the session.
sealed class StatsViewModel : Observable
{
    const int Top = 3;
    readonly HashSet<string> expanded = [];
    List<Leaderboards.Source> sources = [];

    // The picker's periods: its key (CommandParameter), the page's heading, its line and the empty text's start.
    sealed record PeriodInfo(string Key, Leaderboards.Period Period, string Label, string Text, string Empty);
    static readonly PeriodInfo[] Periods =
    [
        new("today", Leaderboards.Period.Today, "TODAY", "Today so far", "Nothing yet today."),
        new("week", Leaderboards.Period.Week, "THIS WEEK", "The last 7 days", "Nothing yet this week."),
        new("month", Leaderboards.Period.Month, "THIS MONTH", "The last 30 days", "Nothing yet this month."),
        new("all", Leaderboards.Period.All, "ALL TIME", "Everything since the app started keeping your days", "Nothing yet."),
    ];

    PeriodInfo period = Periods[1];
    public string PeriodKey => period.Key;
    public string PeriodLabel => period.Label;
    public string PeriodText => $"{period.Text}, you against every friend who shares with you. A board won is 3 points, second 2, third 1.";
    public string EmptyText => $"{period.Empty} Level up, kill a boss or turn in a quest and the boards fill up, and so do your friends' once they share with you.";

    public ICommand ShowPeriodCommand => showPeriod ??= new RelayCommand(p =>
    {
        if (Periods.FirstOrDefault(x => x.Key == p as string) is not { } picked || picked == period) return;
        period = picked;
        Raise(nameof(PeriodKey));
        Raise(nameof(PeriodLabel));
        Raise(nameof(PeriodText));
        Raise(nameof(EmptyText));
        Rebuild();
    });
    ICommand? showPeriod;

    IReadOnlyList<BoardView> boards = [];
    public IReadOnlyList<BoardView> Boards
    {
        get => boards;
        private set { Set(ref boards, value); Raise(nameof(HasBoards)); Raise(nameof(Columns)); }
    }
    public bool HasBoards => boards.Count > 0;

    // The boards in three columns that stack on their own (left to right, then down), so a long board
    // doesn't stretch the ones beside it.
    public const int ColumnCount = 3;
    public IReadOnlyList<IReadOnlyList<BoardView>> Columns =>
        [.. Enumerable.Range(0, ColumnCount).Select(c => (IReadOnlyList<BoardView>)[.. boards.Where((_, i) => i % ColumnCount == c)])];

    // Second, first, third: the podium's order on screen.
    IReadOnlyList<PodiumPlace> podium = [];
    public IReadOnlyList<PodiumPlace> Podium { get => podium; private set => Set(ref podium, value); }

    // Your own place when it isn't on the podium ("You: 5th, 4 points").
    string yourPlace = "";
    public string YourPlace { get => yourPlace; private set { Set(ref yourPlace, value); Raise(nameof(HasYourPlace)); } }
    public bool HasYourPlace => yourPlace.Length > 0;

    // own: your rows (no test characters); days: yours and friends' (DayHistory); friends: the shown friends' rows.
    public void Show(IReadOnlyList<CharacterRow> own, IReadOnlyList<Entry> events, IReadOnlyList<DayTotals> days, IEnumerable<CharacterRow> friends)
    {
        var mine = own.Where(r => !r.IsTest).ToList();
        var mineGuids = mine.Select(r => r.Guid).ToHashSet();
        sources =
        [
            new(new("you", "You", true), [.. events.Where(e => mineGuids.Contains(e.Guid))], [.. days.Where(d => mineGuids.Contains(d.Guid))],
                mine.ToDictionary(r => r.Guid, r => r.FullName)),
        ];
        foreach (var g in friends.GroupBy(f => f.FriendTag))
        {
            var guids = g.Select(f => f.Guid).ToHashSet();
            sources.Add(new(new(g.Key, g.First().FriendName, false), [.. g.SelectMany(f => f.Events)], [.. days.Where(d => guids.Contains(d.Guid))],
                g.GroupBy(f => f.Guid).ToDictionary(x => x.Key, x => x.First().FullName)));
        }
        Rebuild();
    }

    void Rebuild()
    {
        var built = Leaderboards.Build(sources, DateTimeOffset.Now, period.Period);
        Boards = [.. built.Select(b => new BoardView(b, expanded.Contains(b.Key), open => { if (open) expanded.Add(b.Key); else expanded.Remove(b.Key); }))];

        var mvps = Leaderboards.Mvps(built);
        Podium = [.. new[] { 1, 0, 2 }.Where(i => i < mvps.Count).Select(i => new PodiumPlace(mvps[i]))];
        YourPlace = mvps.FirstOrDefault(m => m.Who.IsYou) is { Rank: > Top } you
            ? $"You: {Ordinal(you.Rank)}, {Format.Count(you.Points, "point")}"
            : "";
    }

    internal static string Ordinal(int n) => n % 100 is 11 or 12 or 13 ? $"{n}th" : (n % 10) switch { 1 => $"{n}st", 2 => $"{n}nd", 3 => $"{n}rd", _ => $"{n}th" };

    // The top three and your row (set apart when you're further down); everyone when open.
    internal static List<ScoreRow> Visible(IReadOnlyList<Leaderboards.Score> scores, bool open)
    {
        var shown = open ? scores : [.. scores.Where(s => s.Rank is >= 1 and <= Top || s.Who.IsYou)];
        return [.. shown.Select(s => new ScoreRow(s, Gap: !open && s.Who.IsYou && s.Rank is 0 or > Top))];
    }
}

// One board on the page.
sealed class BoardView(Leaderboards.Board board, bool open, Action<bool> remember) : Observable
{
    public string Title => board.Title;
    public string Glyph => board.Glyph;
    public int Count => board.Scores.Count(s => s.Rank > 0);

    bool isOpen = open;
    public IReadOnlyList<ScoreRow> Rows => StatsViewModel.Visible(board.Scores, isOpen);
    public bool HasMore => board.Scores.Count > StatsViewModel.Visible(board.Scores, false).Count;
    public string ToggleText => isOpen ? "Show less" : $"Show all {Count}";

    public ICommand ToggleCommand => toggle ??= new RelayCommand(_ =>
    {
        isOpen = !isOpen;
        remember(isOpen);
        Raise(nameof(Rows));
        Raise(nameof(ToggleText));
    });
    ICommand? toggle;
}

// A row on a board. Gap: your row under the top three, with a line over it.
sealed record ScoreRow(Leaderboards.Score Score, bool Gap)
{
    public string RankText => Score.Rank > 0 ? Score.Rank.ToString(System.Globalization.CultureInfo.CurrentCulture) : "-";
    public string Name => Score.Who.Name;
    public bool IsYou => Score.Who.IsYou;
    public bool IsFirst => Score.Rank == 1;
    public string Value => Score.Text;
    public string Detail => Score.Detail;
    public bool HasDetail => Score.Detail.Length > 0;
}

// One step of the podium: the MVP in the middle, highest.
sealed record PodiumPlace(Leaderboards.Mvp Mvp)
{
    public string RankText => Mvp.Rank.ToString(System.Globalization.CultureInfo.CurrentCulture);
    public string Name => Mvp.Who.Name;
    public bool IsYou => Mvp.Who.IsYou;
    public bool IsFirst => Mvp.Rank == 1;
    public string PointsText => Format.Count(Mvp.Points, "point");
    public string WonText => Mvp.Won.Count == 0 ? "" : Mvp.Won.Count <= 2 ? string.Join(", ", Mvp.Won) : $"{Mvp.Won[0]}, {Mvp.Won[1]} and {Mvp.Won.Count - 2} more";
    public double Height => Mvp.Rank switch { 1 => 96, 2 => 68, _ => 48 };
}
