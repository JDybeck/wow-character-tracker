using System.Windows.Input;
using System.Windows.Media;

namespace WoWCharacterTracker.ViewModels;

// The header's search: your characters, friends' characters, items and settings of the open game tab,
// in a list under the box. Up/Down pick, Enter opens, Esc closes.
sealed class SearchViewModel : Observable
{
    readonly MainViewModel main;

    public SearchViewModel(MainViewModel main)
    {
        this.main = main;
        OpenCommand = new RelayCommand(p => { if (p is SearchResult r) Open(r); });
    }

    string query = "";
    public string Query
    {
        get => query;
        set
        {
            if (!Set(ref query, value)) return;
            Update();
            IsOpen = query.Trim().Length > 0;
        }
    }

    bool isOpen;
    public bool IsOpen { get => isOpen; set => Set(ref isOpen, value); }

    public IReadOnlyList<SearchResult> Results { get; private set; } = [];
    public bool HasResults => Results.Count > 0;
    public string EmptyText => $"Nothing matches \"{query.Trim()}\".";

    SearchResult? selected;
    public SearchResult? Selected
    {
        get => selected;
        private set
        {
            if (selected is not null) selected.IsSelected = false;
            selected = value;
            if (value is not null) value.IsSelected = true;
        }
    }

    public ICommand OpenCommand { get; }

    public void Move(int step)
    {
        if (Results.Count == 0) return;
        var at = selected is null ? -1 : Results.ToList().IndexOf(selected);
        Selected = Results[Math.Clamp(at + step, 0, Results.Count - 1)];
    }

    public void OpenSelected()
    {
        if (Selected is { } r) Open(r);
    }

    // Another game tab: its results instead. (A sync can leave older rows in the list; opening one
    // finds the character by its guid.)
    public void Refresh()
    {
        if (IsOpen) Update();
    }

    void Update()
    {
        var q = query.Trim();
        Results = q.Length == 0 || main.SelectedFlavor is not { } tab ? [] : [.. Find(tab, q)];
        Selected = Results.FirstOrDefault();
        Raise(nameof(Results));
        Raise(nameof(HasResults));
        Raise(nameof(EmptyText));
    }

    static IEnumerable<SearchResult> Find(FlavorViewModel tab, string q)
    {
        var own = Best(tab.AllCharacters, c => Search.Score(q, c.FullName, Words(c)), c => c.FullName, 6)
            .Select(c => SearchResult.ForCharacter("Characters", c, c.RealmText));
        var friends = Best(tab.Friends.Shown, c => Search.Score(q, c.FullName, $"{Words(c)} {c.FriendName}"), c => c.FullName, 4)
            .Select(c => SearchResult.ForCharacter("Friends", c, c.FriendName));
        return [.. Group(own), .. Group(friends), .. Group(Items(tab, q)), .. Group(Topics(tab, q))];
    }

    static string Words(CharacterRow c) => $"{c.ClassName} {c.Race} {c.Spec} {c.RealmText} {c.Guild} {c.Ruleset}";

    // Items whose names are known already (Items page, character gear), who has them, and one line that
    // searches every item on the Items page.
    static IEnumerable<SearchResult> Items(FlavorViewModel tab, string q)
    {
        var carried = tab.AllCharacters.Where(c => c.Inventory is not null).ToList();
        if (carried.Count == 0) yield break;
        var holders = carried
            .SelectMany(c => c.Inventory!.Placed.Select(p => (Id: p.Slot.ItemId, Character: c)))
            .GroupBy(x => x.Id)
            .Select(g => (Info: tab.Lookup.KnownItem(g.Key), Who: g.Select(x => x.Character.Name).Distinct().ToList()))
            .Where(x => x.Info is { Name.Length: > 0 })
            .ToList();
        foreach (var (info, who) in Best(holders, x => Search.Score(q, x.Info!.Name), x => x.Info!.Name, 5))
            yield return SearchResult.ForItem(info!, who.Count > 3 ? $"{string.Join(", ", who.Take(3))} +{who.Count - 3}" : string.Join(", ", who));
        yield return SearchResult.ForAllItems(q);
    }

    static IEnumerable<SearchResult> Topics(FlavorViewModel tab, string q) =>
        Best(SearchTopic.All.Where(t => t.Shows?.Invoke(tab.Flavor) ?? true), t => Search.Score(q, t.Title, t.Words), t => t.Title, 5)
            .Select(SearchResult.ForTopic);

    static IEnumerable<T> Best<T>(IEnumerable<T> all, Func<T, int?> score, Func<T, string> name, int count) => all
        .Select(x => (Item: x, Score: score(x)))
        .Where(x => x.Score is not null)
        .OrderBy(x => x.Score)
        .ThenBy(x => name(x.Item), StringComparer.CurrentCultureIgnoreCase)
        .Take(count)
        .Select(x => x.Item);

    // The first of each group gets its header.
    static IEnumerable<SearchResult> Group(IEnumerable<SearchResult> results)
    {
        var first = true;
        foreach (var r in results)
        {
            r.ShowsGroup = first;
            first = false;
            yield return r;
        }
    }

    void Open(SearchResult r)
    {
        if (main.SelectedFlavor is not { } tab) return;
        switch (r)
        {
            case { Character: { IsFriend: true } friend }:
                main.OpenFriendCommand.Execute(friend);
                break;
            case { Character: { } own }:
                main.OpenCharacterCommand.Execute(own);
                break;
            case { ItemSearch: { } text }:
                tab.Items.Reset();
                tab.Items.Search = text;
                main.Page = "items";
                break;
            case { Topic: { } topic }:
                OpenTopic(tab, topic);
                break;
        }
        Query = "";
    }

    void OpenTopic(FlavorViewModel tab, SearchTopic topic)
    {
        if (topic.Page == "data") main.ShowDataCommand.Execute(null);
        else if (topic.Page == "uninstall") main.ShowUninstallCommand.Execute(null);
        else main.Page = topic.Page;
        if (topic.Place.Length > 0) tab.OpenPlace = topic.Place;
    }
}

// One line in the search list: a character (yours or a friend's), an item, or a place in the app.
sealed class SearchResult : Observable
{
    public required string Group { get; init; }
    public required string Title { get; init; }
    public string Detail { get; init; } = "";
    public Brush? TitleBrush { get; init; }
    public string Glyph { get; init; } = "";      // a Segoe icon, when there's no picture
    public CharacterRow? Character { get; init; }
    public ItemInfo? Info { get; init; }           // an item: its icon and tooltip (ui:ItemTip)
    public string? ItemSearch { get; init; }       // opens the Items page searching for this
    public SearchTopic? Topic { get; init; }
    public bool ShowsGroup { get; set; }

    bool isSelected;
    public bool IsSelected { get => isSelected; set => Set(ref isSelected, value); }

    public static SearchResult ForCharacter(string group, CharacterRow c, string where) => new()
    {
        Group = group,
        Title = c.FullName,
        Detail = string.Join(" · ", new[] { $"Level {c.Level} {c.ClassName}", where }.Where(s => s.Length > 0)),
        TitleBrush = c.ClassBrush,
        Character = c,
    };

    public static SearchResult ForItem(ItemInfo info, string who) => new()
    {
        Group = "Items",
        Title = info.Name,
        Detail = who,
        TitleBrush = info.Brush,
        Info = info,
        ItemSearch = info.Name,
    };

    public static SearchResult ForAllItems(string query) => new()
    {
        Group = "Items",
        Title = $"Search all items for \"{query}\"",
        Glyph = "",
        ItemSearch = query,
    };

    public static SearchResult ForTopic(SearchTopic topic) => new()
    {
        Group = "Settings and pages",
        Title = topic.Title,
        Detail = topic.Where,
        Glyph = topic.Page == "sharing" ? "" : topic.Where == "Page" ? "" : "",
        Topic = topic,
    };
}
