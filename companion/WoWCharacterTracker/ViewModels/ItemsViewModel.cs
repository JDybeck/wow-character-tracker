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

    void Rebuild()
    {
        Rows.Clear();
        var shown = characters.Where(c => c.Inventory is not null && (selectedRealm is not { Key.Length: > 0 } r || c.Realm == r.Key));
        var query = Search.Trim();
        foreach (var realm in shown.GroupBy(c => c.Realm).OrderBy(g => g.Key))
        {
            var rows = realm
                .SelectMany(c => c.Inventory!.All.Select(slot => (Character: c, Slot: slot)))
                .GroupBy(x => x.Slot.ItemId)
                .Select(g => new ItemRow(item(g.Key), g.Sum(x => x.Slot.Count), Places(g)))
                .Where(i => query.Length == 0 || i.Info.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => i.Info.Name.Length == 0) // still loading: last
                .ThenBy(i => i.Info.Name)
                .ToList();
            if (rows.Count == 0) continue;
            Rows.Add(new ItemHeader($"{realm.Key}", $"{rows.Count} item{(rows.Count == 1 ? "" : "s")}, {realm.Count()} character{(realm.Count() == 1 ? "" : "s")}"));
            foreach (var row in rows) Rows.Add(row);
        }
        EmptyText = characters.All(c => c.Inventory is null)
            ? "Nothing yet. Log in with the updated addon and type /reload (or log out): gear and bags show up here, and the bank once you've opened it."
            : $"Nothing matches \"{query}\".";
        Raise(nameof(IsEmpty));
    }

    // "Zeb: equipped" / "Goggo: bags 12, bank 120", one per character that has the item.
    static List<ItemPlace> Places(IEnumerable<(CharacterRow Character, ItemSlot Slot)> where) => where
        .GroupBy(x => x.Character)
        .Select(c =>
        {
            int Count(Func<ItemSlot, bool> place) => c.Where(x => place(x.Slot)).Sum(x => x.Slot.Count);
            var equipped = c.Count(x => x.Slot.Bag == Inventory.Equipped);
            var bank = Count(s => c.Key.Inventory!.Bank.Contains(s));
            var bags = Count(s => s.Bag != Inventory.Equipped) - bank;
            var parts = new List<string>();
            if (equipped > 0) parts.Add("equipped");
            if (bags > 0) parts.Add($"bags {bags}");
            if (bank > 0) parts.Add($"bank {bank}");
            return new ItemPlace(c.Key.FullName, c.Key.ClassBrush, string.Join(", ", parts));
        })
        .ToList();
}

// A server header in the Items list.
sealed record ItemHeader(string Realm, string Summary);

// One item in the Items list: how many in total, and where (per character).
sealed record ItemRow(ItemInfo Info, int Total, IReadOnlyList<ItemPlace> Places)
{
    public string TotalText => Total > 1 ? $"x{Total}" : "";
}

sealed record ItemPlace(string Character, SolidColorBrush Brush, string Where);
