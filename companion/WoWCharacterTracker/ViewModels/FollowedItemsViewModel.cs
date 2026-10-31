using System.Collections.ObjectModel;
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
        foreach (var item in settings().FollowedItems) Followed.Add(Row(item.Id, item.Name));
    }

    public ICommand SearchCommand { get; }
    public ICommand FollowCommand { get; }
    public ObservableCollection<FollowedRow> Results { get; } = [];
    public ObservableCollection<FollowedRow> Followed { get; } = [];
    public bool IsEmpty => Followed.Count == 0;

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

        if (follow) Followed.Insert(0, Row(id, row.Name));
        else if (Followed.FirstOrDefault(r => r.Info.ItemId == id) is { } shown) Followed.Remove(shown);
        if (Results.FirstOrDefault(r => r.Info.ItemId == id) is { } result) result.IsFollowed = follow;
        Raise(nameof(IsEmpty));
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

// An item on the page, in the search results or the list.
sealed class FollowedRow(ItemInfo info, string name, bool followed) : Observable
{
    public ItemInfo Info { get; } = info;
    public string Name => Info.Name.Length > 0 ? Info.Name : name;

    bool isFollowed = followed;
    public bool IsFollowed
    {
        get => isFollowed;
        set { if (Set(ref isFollowed, value)) Raise(nameof(ActionText)); }
    }
    public string ActionText => IsFollowed ? "Remove" : "Follow";
}
