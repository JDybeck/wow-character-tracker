using System.Collections.ObjectModel;
using System.Windows.Input;

namespace WoWCharacterTracker.ViewModels;

// Home for one game tab: the character played last (Playing now while the game runs), today's
// moments counted over every character, and a feed of the newest moments to share. The character's
// leveling numbers come from the same StreamSnapshot the stream overlay shows, so both agree.
sealed class HomeViewModel(Flavor flavor, GameLookup lookup) : Observable
{
    const int FeedSize = 30;

    CharacterRow? hero;
    public CharacterRow? Hero { get => hero; private set { Set(ref hero, value); Raise(nameof(HasHero)); } }
    public bool HasHero => hero is not null;

    StreamSnapshot card = StreamSnapshot.Empty;
    public StreamSnapshot Card { get => card; private set { Set(ref card, value); Raise(nameof(ThisLevelText)); } }
    // Played since the last ding: nothing to say right after one (or before /played answers).
    public string ThisLevelText => card.LevelPlayed is { Length: > 0 } t && t != "0s" ? t : "-";

    bool playing;
    public bool Playing { get => playing; private set { Set(ref playing, value); Raise(nameof(HeroTitle)); } }
    public string HeroTitle => Playing ? "PLAYING NOW" : "LAST PLAYED";

    // "Reached level 40 in 1h 12m · average 1h 40m"
    string paceText = "";
    public string PaceText { get => paceText; private set => Set(ref paceText, value); }

    // Today for the hero, and every character of the game below it once more than one was played.
    public TodaySection Today { get; } = new("TODAY");
    public TodaySection AccountToday { get; } = new("ACCOUNT TODAY");

    public ObservableCollection<MomentRow> Feed { get; } = [];
    public bool HasFeed => Feed.Count > 0;

    // Under your own moments, those of friends who share them.
    public ObservableCollection<MomentRow> FriendFeed { get; } = [];
    public bool HasFriendFeed => FriendFeed.Count > 0;

    // Friends who share with you, the one seen last first (when isn't Battle.net's online: friends'
    // updates reach the app through your saved file, on /reload or logout).
    const int FriendsShown = 6;
    public ObservableCollection<CharacterRow> Friends { get; } = [];
    public bool HasFriends => Friends.Count > 0;

    public void Show(IReadOnlyList<CharacterRow> rows, IReadOnlyList<Entry> events, IReadOnlyList<DayTotals> days, IReadOnlySet<string> sent, bool gameRunning,
        IEnumerable<CharacterRow> friends)
    {
        Friends.Clear();
        foreach (var f in LatestFriends(friends, FriendsShown))
        {
            Friends.Add(f);
            _ = f.LoadPictureAsync(flavor);
        }
        Raise(nameof(HasFriends));

        // Real characters first: the test ones only lead when there's nothing else.
        Hero = rows.Where(r => !r.IsTest).MaxBy(r => r.LastSeen) ?? rows.MaxBy(r => r.LastSeen);
        Playing = gameRunning && Hero is not null;
        Card = Hero is { } h ? StreamSnapshot.Build(StreamViewModel.Character(h, flavor), events, DateTimeOffset.Now) : StreamSnapshot.Empty;
        PaceText = Hero is { } p ? Pace(p) : "";

        ShowToday(events, days, sent, DateTime.Now);

        Fill(Feed, rows.ToDictionary(r => r.Guid), events, sent);
        Raise(nameof(HasFeed));

        // Friends' moments come with their rows (the highlights and level times they share).
        var friendRows = friends.GroupBy(f => f.Guid).ToDictionary(g => g.Key, g => g.First());
        Fill(FriendFeed, friendRows, friendRows.Values.SelectMany(f => f.Events), sent);
        Raise(nameof(HasFriendFeed));
    }

    void ShowToday(IReadOnlyList<Entry> events, IReadOnlyList<DayTotals> days, IReadOnlySet<string> sent, DateTime now)
    {
        var keys = flavor.HasRatings;
        var hero = Hero?.Guid;
        var played = TodayStats.Characters(events, days, now);
        Today.Show(TodayStats.Build(events, days, sent, keys, now, hero));
        // Only worth a row when someone else was played too.
        AccountToday.Visible = hero is not null && played.Any(g => g != hero);
        AccountToday.Note = played.Count == 1 ? "· 1 character" : $"· {played.Count} characters";
        if (AccountToday.Visible) AccountToday.Show(TodayStats.Build(events, days, sent, keys, now));
    }

    // The newest level-ups and highlights of these characters, newest first.
    void Fill(ObservableCollection<MomentRow> feed, Dictionary<string, CharacterRow> byGuid, IEnumerable<Entry> events, IReadOnlySet<string> sent)
    {
        var newest = events
            .Where(e => byGuid.ContainsKey(e.Guid) && (e.Kind == "level" || EventKinds.Highlights.Contains(e.Kind)))
            .OrderByDescending(e => e.At)
            .Take(FeedSize);
        feed.Clear();
        var portraits = new HashSet<CharacterRow>();
        foreach (var e in newest)
        {
            var row = HighlightRow.From(e);
            if (row.Text.Length == 0) continue;
            row.Info = row.ItemId > 0 ? lookup.For("item", row.ItemId) : null;
            var who = byGuid[e.Guid];
            feed.Add(new MomentRow(row, who, who.IsOwn && sent.Contains(e.Id)));
            if (e.Kind == "level") { if (portraits.Add(who)) _ = who.LoadPictureAsync(flavor); } // once per character
            else _ = row.LoadIconAsync(flavor);
        }
    }

    // Each friend once, with the character they played last; the one seen last first.
    internal static IEnumerable<CharacterRow> LatestFriends(IEnumerable<CharacterRow> friends, int count) =>
        friends.GroupBy(f => f.FriendTag)
            .Select(g => g.MaxBy(f => f.LastSeen)!)
            .OrderByDescending(f => f.LastSeen)
            .Take(count);

    // Posted just now (by the tracker or a Share click): the row says so.
    public void MarkPosted(string id)
    {
        foreach (var m in Feed.Where(m => m.Row.Source?.Id == id)) m.Posted = true;
    }

    static string Pace(CharacterRow c)
    {
        var last = c.Events.LastOrDefault(e => e.Kind == "level" && e.Seconds > 0);
        var parts = new List<string>();
        if (last is not null) parts.Add($"{last.Level - 1} to {last.Level} took {Format.Duration(last.Seconds)}");
        if (c.Leveling.AverageText.Length > 0) parts.Add($"average {c.Leveling.AverageText}");
        return string.Join(" · ", parts);
    }
}

// A moment in Home's feed: the highlight line, whose it is and whether it went to Discord.
sealed class MomentRow(HighlightRow row, CharacterRow character, bool posted) : Observable
{
    public HighlightRow Row { get; } = row;
    public CharacterRow Character { get; } = character;
    public string Who => Character.IsFriend
        ? $"{Character.FullName} ({Character.FriendName}) · {Format.Ago(Row.Source?.At ?? 0)}"
        : $"{Character.FullName} · {Format.Ago(Row.Source?.At ?? 0)}";

    bool isPosted = posted;
    public bool Posted { get => isPosted; set => Set(ref isPosted, value); }

    // A level-up shows the character's portrait instead of an icon.
    public bool IsLevel => Row.Source?.Kind == "level";

    // Epic and legendary loot, max level, a hardcore death: the rows that stand out.
    public bool Big => Row.Source is { } e && (e.Kind == "loot" && e.Quality >= 4 || e.MaxLevel || e.Kind is "death" or "makgora" && !e.Lost);
}
