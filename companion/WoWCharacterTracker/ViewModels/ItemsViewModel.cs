using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;

namespace WoWCharacterTracker.ViewModels;

// The Items page: everything a game tab's characters carry (equipped, bags, bank), with icons,
// searchable by name, grouped per server. Rows is one list of server headers and items, so a big
// bank still scrolls smoothly.
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
    }

    public ObservableCollection<object> Rows { get; } = [];
    public ObservableCollection<GroupChip> Realms { get; } = [];
    public bool HasRealms => Realms.Count > 2; // "All" plus at least two servers

    string search = "";
    public string Search { get => search; set { if (Set(ref search, value)) Rebuild(); } }

    GroupChip? selectedRealm;
    public GroupChip? SelectedRealm { get => selectedRealm; set { if (value is not null && Set(ref selectedRealm, value)) Rebuild(); } }

    string emptyText = "";
    public string EmptyText { get => emptyText; private set => Set(ref emptyText, value); }
    public bool IsEmpty => Rows.Count == 0;

    public void Show(List<CharacterRow> rows)
    {
        characters = rows;
        var chosen = selectedRealm?.Key ?? "";
        var withItems = rows.Where(r => r.Inventory is not null).ToList();
        Realms.Clear();
        Realms.Add(new GroupChip("", $"All ({withItems.Count})"));
        foreach (var g in withItems.GroupBy(r => r.Realm).OrderBy(g => g.Key))
            Realms.Add(new GroupChip(g.Key, $"{g.Key} ({g.Count()})"));
        selectedRealm = Realms.FirstOrDefault(c => c.Key == chosen) ?? Realms[0];
        Raise(nameof(SelectedRealm));
        Raise(nameof(HasRealms));
        Rebuild();
    }

    public void NameLoaded()
    {
        refresh.Stop();
        refresh.Start();
    }

    // Where to look: everything, or only what's equipped, in bags or in the bank.
    public IReadOnlyList<GroupChip> PlaceChips { get; } =
        [new("", "All"), new("equipped", "Equipped"), new("bags", "Bags"), new("bank", "Bank")];
    GroupChip? selectedPlace;
    public GroupChip SelectedPlace { get => selectedPlace ?? PlaceChips[0]; set { if (value is not null && Set(ref selectedPlace, value)) Rebuild(); } }

    void Rebuild()
    {
        Rows.Clear();
        var shown = characters.Where(c => c.Inventory is not null && (selectedRealm is not { Key.Length: > 0 } r || c.Realm == r.Key));
        var query = Search.Trim();
        var place = SelectedPlace.Key;
        foreach (var realm in shown.GroupBy(c => c.Realm).OrderBy(g => g.Key))
        {
            var rows = realm
                .SelectMany(c => c.Inventory!.Placed.Where(p => place.Length == 0 || p.Place == place).Select(p => (Character: c, p.Slot, p.Place)))
                .GroupBy(x => x.Slot.ItemId)
                .Select(g => new ItemRow(item(g.Key), g.Sum(x => x.Slot.Count), Places(g)))
                .Where(i => query.Length == 0 || i.Info.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => i.Info.Name.Length == 0) // still loading: last
                .ThenBy(i => i.Info.Name)
                .ToList();
            if (rows.Count == 0) continue;
            // When each character's bags were last saved (and where), so it's clear how fresh this is.
            var saved = realm.Where(c => c.Gear.HasSavedText).Select(c => new ItemPlace(c.FullName, c.ClassBrush, c.Gear.SavedText)).ToList();
            Rows.Add(new ItemHeader($"{realm.Key}", $"{rows.Count} item{(rows.Count == 1 ? "" : "s")}, {realm.Count()} character{(realm.Count() == 1 ? "" : "s")}", saved));
            foreach (var row in rows) Rows.Add(row);
        }
        EmptyText = characters.All(c => c.Inventory is null)
            ? "Nothing yet. Log in with the updated addon and type /reload (or log out): gear and bags show up here, and the bank once you've opened it."
            : query.Length > 0 ? $"Nothing matches \"{query}\"."
            : place == "bank" ? "Nothing in the bank yet. Open your bank in game once and it shows up here."
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

// A server header in the Items list, with when each character's bags were last saved.
sealed record ItemHeader(string Realm, string Summary, IReadOnlyList<ItemPlace> Saved);

// One item in the Items list: how many in total, and where (per character).
sealed record ItemRow(ItemInfo Info, int Total, IReadOnlyList<ItemPlace> Places)
{
    public string TotalText => Total > 1 ? $"x{Total}" : "";
}

sealed record ItemPlace(string Character, SolidColorBrush Brush, string Where);
