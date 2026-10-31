using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace WoWCharacterTracker.ViewModels;

// The Followed items page, one per game tab: search Wowhead (or paste an address or id), follow what
// comes up, see and remove what's followed. The list lives in the tab's settings (config.json).
sealed class FollowedItemsViewModel : Observable
{
    readonly Flavor flavor;
    readonly GameLookup lookup;
    readonly Func<FlavorSettings> settings;
    readonly Action changed;
    int searchNumber; // only the newest search shows its answer
    const int MinTypedLength = 2; // one letter finds nothing useful, Enter still searches it

    public FollowedItemsViewModel(Flavor flavor, GameLookup lookup, Func<FlavorSettings> settings, Action changed)
    {
        this.flavor = flavor;
        this.lookup = lookup;
        this.settings = settings;
        this.changed = changed;
        SearchCommand = new RelayCommand(_ => _ = SearchAsync(1));
        FollowCommand = new RelayCommand(p => { if (p is FollowedRow row) Toggle(row); });
        CopyListCommand = new RelayCommand(_ => CopyList());
        PasteListCommand = new RelayCommand(_ => PasteList());
        BuildList();
    }

    public ICommand SearchCommand { get; }
    public ICommand FollowCommand { get; }
    public ObservableCollection<FollowedRow> Results { get; } = [];

    // The list, in its categories (the empty ones left out).
    IReadOnlyList<FollowedGroup> groups = [];
    public IReadOnlyList<FollowedGroup> Groups { get => groups; private set { Set(ref groups, value); Raise(nameof(IsEmpty)); } }
    public bool IsEmpty => Groups.Count == 0;

    // The ready-made lists the game offers, as switches.
    IReadOnlyList<ToggleOption>? readyMade;
    public IReadOnlyList<ToggleOption> ReadyMadeLists => readyMade ??=
        [.. flavor.ReadyMadeLists.Select(l => new ToggleOption(l.Name, settings().ReadyMadeLists.Contains(l.Key), on => SwitchList(l, on), l.Hint))];

    void SwitchList(ReadyMadeList list, bool on)
    {
        var keys = settings().ReadyMadeLists;
        keys.Remove(list.Key);
        if (on) keys.Add(list.Key);
        BuildList();
        changed();
    }

    // Your own items as a code to share, and a shared code added to them.
    public ICommand CopyListCommand { get; }
    public ICommand PasteListCommand { get; }
    string listStatus = "";
    public string ListStatus { get => listStatus; private set => Set(ref listStatus, value); }

    void CopyList()
    {
        var own = settings().FollowedItems;
        if (own.Count == 0) { ListStatus = "Nothing to copy yet: the code holds the items you added yourself."; return; }
        try
        {
            Clipboard.SetText(FollowedListCode.Export(own, flavor));
            ListStatus = $"Copied {own.Count} {(own.Count == 1 ? "item" : "items")} as a code. Share it; others click Paste list.";
        }
        catch (System.Runtime.InteropServices.ExternalException) { ListStatus = "The clipboard was busy. Try again."; }
    }

    void PasteList()
    {
        string text;
        try { text = Clipboard.ContainsText() ? Clipboard.GetText() : ""; }
        catch (System.Runtime.InteropServices.ExternalException) { ListStatus = "The clipboard was busy. Try again."; return; }
        var (items, problem) = FollowedListCode.Import(text, flavor);
        if (problem is not null) { ListStatus = problem; return; }
        var added = FollowedListCode.Merge(settings(), items);
        ListStatus = added > 0 ? $"Added {added} {(added == 1 ? "item" : "items")} to your list." : "You already follow everything in that list.";
        if (added == 0) return;
        BuildList();
        changed();
    }

    string query = "";
    public string Query
    {
        get => query;
        set { if (Set(ref query, value)) _ = SearchAsync(MinTypedLength); } // the box waits for a pause in the typing
    }

    // Under the search box: what's going on, or why there's nothing to show.
    string status = "";
    public string Status { get => status; private set => Set(ref status, value); }
    bool failed;
    public bool Failed { get => failed; private set => Set(ref failed, value); }

    FollowedRow Row(int id, string name)
    {
        var info = lookup.Item(id);
        info.Fallback(name);
        return new FollowedRow(info, name, settings().FollowedItems.Any(i => i.Id == id));
    }

    int listNumber; // only the newest list gets sorted again

    ItemInfo ListedInfo(FollowedItem item)
    {
        var info = lookup.Item(item.Id);
        info.Fallback(item.Name);
        return info;
    }

    // A category comes from the tooltip, so the rows sort again once their lookups are done (without
    // an answer they stay under Other).
    void BuildList()
    {
        var number = ++listNumber;
        var rows = settings().AllFollowed
            .Select(i => settings().ListWith(i.Id) is { } list ? new FollowedRow(ListedInfo(i), i.Name, false) { ListTag = list.Tag } : Row(i.Id, i.Name))
            .ToList();
        Groups = Grouped(rows);
        _ = GroupWhenLoadedAsync(rows, number);
    }

    async Task GroupWhenLoadedAsync(List<FollowedRow> rows, int number)
    {
        await Task.WhenAll(rows.Select(r => r.Info.WhenLoaded));
        if (number == listNumber) Groups = Grouped(rows);
    }

    static List<FollowedGroup> Grouped(List<FollowedRow> rows) =>
    [
        .. ItemCategories.All
            .Select(c => new FollowedGroup(c, [.. rows.Where(r => ItemCategories.Of(r.Info.Tooltip) == c).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)]))
            .Where(g => g.Rows.Count > 0),
    ];

    // The old results stay until the new ones are in, so the list doesn't blink while typing.
    async Task SearchAsync(int minLength)
    {
        var text = Query.Trim();
        var number = ++searchNumber;
        Failed = false;
        if (text.Length < minLength) { Results.Clear(); Status = ""; return; }
        Status = "Searching...";
        var hits = await FindAsync(text);
        if (number != searchNumber) return;
        Results.Clear();
        if (hits is null)
        {
            Failed = true;
            Status = "Couldn't reach Wowhead. Check your connection and try again.";
            return;
        }
        foreach (var hit in hits) Results.Add(Row(hit.Id, hit.Name));
        Status = hits.Count > 0 ? "" : $"Nothing found for \"{text}\". Try another word, or paste the item's Wowhead address.";
    }

    // A pasted address or id goes straight to the item: without an answer that's as good as not found,
    // since a lookup can't tell a wrong id from no connection. Anything else is a search.
    async Task<IReadOnlyList<Wowhead.ItemHit>?> FindAsync(string text)
    {
        if (Wowhead.ItemIdFrom(text) is not { } id) return await Wowhead.SearchItemsAsync(text, flavor);
        return await Wowhead.GetAsync("item", id, flavor) is { } info ? [new(id, info.Name, info.Icon, info.Quality)] : [];
    }

    void Toggle(FollowedRow row)
    {
        var id = row.Info.ItemId;
        var s = settings();
        var follow = s.FollowedItems.All(i => i.Id != id);
        s.FollowedItems = follow
            ? [new FollowedItem(id, row.Name), .. s.FollowedItems]
            : [.. s.FollowedItems.Where(i => i.Id != id)];

        BuildList();
        if (Results.FirstOrDefault(r => r.Info.ItemId == id) is { } result) result.IsFollowed = follow;
        changed();
    }

    // Clicking the open page's tab again: the search starts over, the list stays.
    public void Reset()
    {
        searchNumber++;
        Query = "";
        Status = "";
        Failed = false;
        Results.Clear();
    }
}

// A category of the list: its title and items.
sealed record FollowedGroup(string Title, IReadOnlyList<FollowedRow> Rows);

// An item on the page, in the search results or the list.
sealed class FollowedRow(ItemInfo info, string name, bool followed) : Observable
{
    public ItemInfo Info { get; } = info;
    public string Name => Info.Name.Length > 0 ? Info.Name : name;

    bool isFollowed = followed;
    public bool IsFollowed
    {
        get => isFollowed;
        set { if (Set(ref isFollowed, value)) { Raise(nameof(ActionText)); Raise(nameof(IsListed)); } }
    }
    // In the list only through the ready-made list: nothing to remove, that list has its own switch.
    public string ListTag { get; init; } = "";
    public bool IsListed => ListTag.Length > 0 && !IsFollowed;
    public string ActionText => IsFollowed ? "Remove" : "Follow";
}
