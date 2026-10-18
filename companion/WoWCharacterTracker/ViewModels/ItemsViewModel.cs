using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WoWCharacterTracker.ViewModels;

// The Items page: everything a game tab's characters carry (equipped, bags, bank), with icons,
// searchable by name, grouped per server (each server's items wrap into as many columns as fit).
sealed class ItemsViewModel : Observable
{
    readonly Func<int, ItemInfo> item;
    readonly DispatcherTimer refresh;
    List<CharacterRow> characters = [];

    public ItemsViewModel(Func<int, ItemInfo> item)
    {
        this.item = item;
        // Names arrive from Wowhead one by one; the list follows a moment after the last one.
        refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        refresh.Tick += (_, _) => { refresh.Stop(); Rebuild(); };
        Realms = new ChipFilter("Realm", Rebuild);
    }

    // The servers with their items, and the same as the list shows it: each server's header, then
    // its items cut into lines of Columns, so the list can be virtualized (only the lines on screen
    // are built; thousands of items in a WrapPanel froze the window for seconds).
    List<ItemGroup> groups = [];
    public IReadOnlyList<object> Lines { get; private set; } = [];
    public ChipFilter Realms { get; }

    // How many items fit side by side; the page sets it from its width.
    int columns = 1;
    public int Columns
    {
        get => columns;
        set
        {
            if (value < 1 || value == columns) return;
            columns = value;
            Relayout();
        }
    }

    void Relayout()
    {
        var lines = new List<object>();
        foreach (var g in groups)
        {
            lines.Add(g.Header);
            for (var i = 0; i < g.Items.Count; i += columns)
                lines.Add(new ItemLine(g.Items.Skip(i).Take(columns).ToList()));
        }
        Lines = lines;
        Raise(nameof(Lines));
    }

    string search = "";
    public string Search { get => search; set { if (Set(ref search, value)) Rebuild(); } }

    string emptyText = "";
    public string EmptyText { get => emptyText; private set => Set(ref emptyText, value); }
    public bool IsEmpty => groups.Count == 0;

    public void Show(List<CharacterRow> rows)
    {
        characters = rows;
        Realms.Fill(rows.Where(r => r.Inventory is not null).ToList(), r => r.Realm);
        Rebuild();
    }

    public void NameLoaded()
    {
        if (!onScreen)
        {
            stale = true;
            return;
        }
        refresh.Stop();
        refresh.Start();
    }

    // Where to look: everything, only one place (equipped, bags, bank), or everything but one place.
    // A place's chip cycles: only it, then all but it, then everything (PlaceChip).
    public IReadOnlyList<PlaceChip> PlaceChips { get; } =
        [new("", "All") { State = "only" }, new("equipped", "Equipped"), new("bags", "Bags"), new("bank", "Bank")];
    public ICommand PlaceCommand => placeCommand ??= new RelayCommand(p => { if (p is PlaceChip chip) ClickPlace(chip); });
    ICommand? placeCommand;
    string place = "";
    bool placeOut;

    void ClickPlace(PlaceChip chip)
    {
        if (chip.Key.Length == 0 || (chip.Key == place && placeOut)) (place, placeOut) = ("", false);
        else if (chip.Key == place) placeOut = true;
        else (place, placeOut) = (chip.Key, false);
        ShowPlace();
        Rebuild();
    }

    void ShowPlace()
    {
        foreach (var c in PlaceChips)
            c.State = c.Key.Length == 0 ? (place.Length == 0 ? "only" : "") : c.Key != place ? "" : placeOut ? "out" : "only";
    }

    // The page as it opens the first time: no search, every place, type and realm.
    public void Reset()
    {
        (place, placeOut) = ("", false);
        ShowPlace();
        search = "";
        Raise(nameof(Search));
        selectedKind = null;
        Raise(nameof(SelectedKind));
        Realms.Reset();
        Rebuild();
    }

    bool Wanted(string where) => place.Length == 0 || (where == place) != placeOut;

    // What kind of item: gear, consumables, trade goods and reagents, quest items, the rest
    // (ItemInfo.Kind, from the game's item class).
    public IReadOnlyList<GroupChip> KindChips { get; } =
        [new("", "All types"), new("gear", "Gear"), new("consumable", "Consumables"), new("trade", "Trade goods"), new("quest", "Quest"), new("other", "Other")];
    GroupChip? selectedKind;
    public GroupChip SelectedKind { get => selectedKind ?? KindChips[0]; set { if (value is not null && Set(ref selectedKind, value)) Rebuild(); } }

    // Whether the page is on screen (its game tab and the Items page are open). Building the list
    // is costly with many characters (thousands of rows, rebuilt as names arrive), so while it's not
    // shown it waits and is built once when it is.
    bool onScreen;
    bool stale;
    public bool IsShown
    {
        get => onScreen;
        set
        {
            if (onScreen == value) return;
            onScreen = value;
            if (onScreen && stale) Rebuild();
        }
    }

    void Rebuild()
    {
        if (!onScreen)
        {
            stale = true;
            return;
        }
        stale = false;
        var built = new List<ItemGroup>();
        var shown = characters.Where(c => c.Inventory is not null && Realms.Matches(c.Realm));
        var query = Search.Trim();
        var kind = SelectedKind.Key;
        foreach (var realm in shown.GroupBy(c => c.Realm).OrderBy(g => g.Key))
        {
            var rows = realm
                .SelectMany(c => c.Inventory!.Placed.Where(p => Wanted(p.Place)).Select(p => (Character: c, p.Slot, p.Place)))
                .GroupBy(x => x.Slot.ItemId)
                .Select(g => new ItemRow(item(g.Key), g.Sum(x => x.Slot.Count), Places(g)))
                .Where(i => query.Length == 0 || i.Info.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Where(i => kind.Length == 0 || i.Info.Kind == kind)
                .OrderBy(i => i.Info.Name.Length == 0) // still loading: last
                .ThenBy(i => i.Info.Name)
                .ToList();
            if (rows.Count == 0) continue;
            // When each character's bags were last saved (and where), so it's clear how fresh this is.
            var saved = realm.Select(c => (c, Text: CharacterGear.SavedTextOf(c.Inventory))).Where(x => x.Text.Length > 0)
                .Select(x => new ItemPlace(x.c.FullName, x.c.ClassBrush, x.Text)).ToList();
            built.Add(new ItemGroup(new ItemHeader(realm.First().RealmText, $"{Format.Count(rows.Count, "item")}, {Format.Count(realm.Count(), "character")}", saved), rows));
        }
        groups = built;
        Relayout();
        EmptyText = characters.All(c => c.Inventory is null)
            ? "Nothing yet. Log in with the updated addon and type /reload (or log out): gear and bags show up here, and the bank once you've opened it."
            : query.Length > 0 ? $"Nothing matches \"{query}\"."
            : kind.Length > 0 ? $"No {SelectedKind.Label.ToLowerInvariant()} here. Types come from the addon: after the update, /reload once so it can tell what each item is."
            : place == "bank" && !placeOut ? "Nothing in the bank yet. Open your bank in game once and it shows up here."
            : "Nothing here.";
        Raise(nameof(IsEmpty));
    }

    // "Zeb: equipped" / "Goggo: bags 12, bank 120", one per character that has the item.
    static List<ItemPlace> Places(IEnumerable<(CharacterRow Character, ItemSlot Slot, string Place)> where) => where
        .GroupBy(x => x.Character)
        .Select(c =>
        {
            int Count(string place) => c.Where(x => x.Place == place).Sum(x => x.Slot.Count);
            var parts = new List<string>();
            if (Count("equipped") > 0) parts.Add("equipped");
            if (Count("bags") is var bags and > 0) parts.Add($"bags {bags}");
            if (Count("bank") is var bank and > 0) parts.Add($"bank {bank}");
            return new ItemPlace(c.Key.FullName, c.Key.ClassBrush, string.Join(", ", parts));
        })
        .ToList();
}

// A server in the Items list: its header (with when each character's bags were last saved) and items.
sealed record ItemHeader(string Realm, string Summary, IReadOnlyList<ItemPlace> Saved);
sealed record ItemGroup(ItemHeader Header, IReadOnlyList<ItemRow> Items);

// One line of the Items list: up to Columns items side by side.
sealed record ItemLine(IReadOnlyList<ItemRow> Items);

// One item in the Items list: how many in total, and where (per character).
sealed record ItemRow(ItemInfo Info, int Total, IReadOnlyList<ItemPlace> Places)
{
    public string TotalText => Total > 1 ? $"x{Total}" : "";
}

sealed record ItemPlace(string Character, SolidColorBrush Brush, string Where);
