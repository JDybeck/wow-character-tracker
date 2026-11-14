using System.Windows.Input;

namespace WoWCharacterTracker.ViewModels;

// The Stats page of one game tab: the week's MVP podium and a leaderboard per stat (Leaderboards),
// you against every friend who shares with you. A board shows the top three and your own row; Show
// all lists everyone, and stays open across syncs.
sealed class StatsViewModel : Observable
{
    const int Top = 3;
    readonly HashSet<string> expanded = [];

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

    // own: your rows (no test characters); days: your today and finished days; friends: the shown friends'
    // rows; contacts: what friends sent (their days).
    public void Show(IReadOnlyList<CharacterRow> own, IReadOnlyList<Entry> events, IEnumerable<DayTotals> days,
        IEnumerable<CharacterRow> friends, IEnumerable<FriendContact> contacts)
    {
        var mine = own.Where(r => !r.IsTest).ToList();
        var mineGuids = mine.Select(r => r.Guid).ToHashSet();
        var sources = new List<Leaderboards.Source>
        {
            new(new("you", "You", true), [.. events.Where(e => mineGuids.Contains(e.Guid))], [.. days.Where(d => mineGuids.Contains(d.Guid))],
                mine.ToDictionary(r => r.Guid, r => r.FullName)),
        };
        var friendDays = contacts.SelectMany(c => c.Days).ToList();
        foreach (var g in friends.GroupBy(f => f.FriendTag))
        {
            var guids = g.Select(f => f.Guid).ToHashSet();
            sources.Add(new(new(g.Key, g.First().FriendName, false), [.. g.SelectMany(f => f.Events)], [.. friendDays.Where(d => guids.Contains(d.Guid))],
                g.GroupBy(f => f.Guid).ToDictionary(x => x.Key, x => x.First().FullName)));
        }

        var built = Leaderboards.Build(sources, DateTimeOffset.Now);
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
