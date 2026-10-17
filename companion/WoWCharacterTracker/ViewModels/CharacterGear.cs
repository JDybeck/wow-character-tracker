namespace WoWCharacterTracker.ViewModels;

// The Gear and bags tab of the character details: equipped gear laid out like the character window,
// the bags, and the bank as of the last time it was open.
sealed class CharacterGear
{
    // Inventory slots down the left and right of the character window, and the weapons below.
    static readonly (int Slot, string Name)[] LeftSlots = [(1, "Head"), (2, "Neck"), (3, "Shoulder"), (15, "Back"), (5, "Chest"), (4, "Shirt"), (19, "Tabard"), (9, "Wrist")];
    static readonly (int Slot, string Name)[] RightSlots = [(10, "Hands"), (6, "Waist"), (7, "Legs"), (8, "Feet"), (11, "Finger"), (12, "Finger"), (13, "Trinket"), (14, "Trinket")];
    static readonly (int Slot, string Name)[] BottomSlots = [(16, "Main Hand"), (17, "Off Hand"), (18, "Ranged")];

    public IReadOnlyList<ItemCell> Left { get; init; } = [];
    public IReadOnlyList<ItemCell> Right { get; init; } = [];
    public IReadOnlyList<ItemCell> Bottom { get; init; } = [];
    public IReadOnlyList<BagView> Bags { get; init; } = [];
    public IReadOnlyList<BagView> Bank { get; init; } = [];
    public string BankText { get; init; } = "";
    // "Saved 12m ago, in Stormwind City": when the game last wrote the file this comes from.
    public string SavedText { get; init; } = "";
    public bool HasSavedText => SavedText.Length > 0;
    public bool HasInventory { get; init; }
    public bool HasBank => Bank.Count > 0;

    public static CharacterGear Empty { get; } = new();

    // ranged: Classic-style clients have a ranged slot; Retail hasn't since Legion.
    public static CharacterGear From(Inventory? inv, Func<int, ItemInfo> item, bool ranged = true)
    {
        if (inv is null) return Empty;
        var worn = inv.Gear.ToDictionary(g => g.Slot, g => g.ItemId);
        IReadOnlyList<ItemCell> Slots((int Slot, string Name)[] slots) =>
            slots.Select(s => new ItemCell(worn.TryGetValue(s.Slot, out var id) ? item(id) : null, 1, s.Name)).ToList();

        return new CharacterGear
        {
            Left = Slots(LeftSlots),
            Right = Slots(RightSlots),
            Bottom = Slots(ranged ? BottomSlots : BottomSlots[..2]),
            Bags = Grids(inv.Bags, inv.BagSizes, item, bag => bag == 0 ? "Backpack" : $"Bag {bag}"),
            Bank = Grids(inv.Bank, inv.BankSizes, item, bag => bag < 0 ? "Bank" : $"Bank bag {bag - 4}"),
            BankText = inv.BankAt > 0 ? $"Bank, as of {Format.Ago(inv.BankAt)}" : "Open your bank in game once and it shows up here.",
            SavedText = inv.SavedAt <= 0 ? "" : $"Saved {Format.Ago(inv.SavedAt)}" + (inv.SavedZone.Length > 0 ? $", in {inv.SavedZone}" : ""),
            HasInventory = true,
        };
    }

    // Each bag as a full grid: its items in their slots, empty slots in between.
    static List<BagView> Grids(IReadOnlyList<ItemSlot> items, IReadOnlyDictionary<int, int> sizes, Func<int, ItemInfo> item, Func<int, string> title)
    {
        var bySlot = items.ToDictionary(i => (i.Bag, i.Slot));
        return sizes.OrderBy(s => s.Key == -1 ? -1 : s.Key)
            .Select(s => new BagView(title(s.Key), Enumerable.Range(1, s.Value)
                .Select(slot => bySlot.TryGetValue((s.Key, slot), out var i) ? new ItemCell(item(i.ItemId), i.Count) : new ItemCell(null, 0))
                .ToList()))
            .ToList();
    }
}
