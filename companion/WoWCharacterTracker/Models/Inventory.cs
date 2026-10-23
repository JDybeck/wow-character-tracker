namespace WoWCharacterTracker.Models;

// One item in a bag slot, or equipped (Bag is then Inventory.Equipped and Slot the inventory slot).
record ItemSlot(int Bag, int Slot, int ItemId, int Count);

// A set the gear counts for, as the game's tooltips say: pieces worn of the set's total, and its
// bonuses (Need pieces each). Catalyst and redirected pieces count, the game tells us so.
record GearSet(string Name, int Have, int Max, IReadOnlyList<SetBonus> Bonuses);
// Need 0: the game shows the bonus as active, and then it leaves the count out.
record SetBonus(int Need, string Text)
{
    public bool IsActive(int have) => Need == 0 || have >= Need;
}

// An Equip: or Use: line of an equipped item.
record GearEffect(int Slot, int ItemId, string Text);
// An equipped item's enchant: the game's line ("Enchant Ring - Nature's Fury", or on Classic clients only
// "Stamina +2") and its id from the item link (0 when saved before 0.24.1).
record GearEnchant(int Slot, int ItemId, string Text, int EnchantId);

// A set from the game's Equipment Manager: its name, icon (a file id), whether it's what the character
// wears, and per slot the item and where the game last saw it.
record EquipmentSet(string Name, int Icon, bool IsEquipped, IReadOnlyList<EquipmentSetItem> Items);
// Place: "equipped", "bags", "bank", "missing", "empty" (the set leaves the slot empty) or "ignored"
// (the set doesn't touch the slot, ItemId 0).
record EquipmentSetItem(int Slot, int ItemId, string Place);

// What a character carries, from the addon's items[guid] (Inventory.lua): equipped gear, bags, and
// the bank as of the last time it was open (BankAt, 0 = never seen). SavedAt and SavedZone: when and
// where the game last wrote the saved file (/reload or logout), which is how fresh this is.
record Inventory(string Guid, IReadOnlyList<ItemSlot> Gear, IReadOnlyList<ItemSlot> Bags, IReadOnlyDictionary<int, int> BagSizes,
    IReadOnlyList<ItemSlot> Bank, IReadOnlyDictionary<int, int> BankSizes, long ScannedAt, long BankAt, long SavedAt = 0, string SavedZone = "")
{
    public const int Equipped = -100;

    public IReadOnlyList<GearSet> Sets { get; init; } = [];
    public IReadOnlyList<GearEffect> Effects { get; init; } = [];
    public IReadOnlyList<GearEnchant> Enchants { get; init; } = [];
    public IReadOnlyList<EquipmentSet> EquipmentSets { get; init; } = [];
    // Whether the addon has read the sets and effects at all (0.23.0 and later): none read is not "no sets".
    public bool HasGearScan { get; init; }

    // From the saved strings: gear "slot~itemId,...", bags and bank "bag~slot~itemId~count,...",
    // sizes "bag~size,...", sets and effects one per line (see ParseSets, ParseEffects).
    public static Inventory Parse(string guid, Func<string, string> s, Func<string, double> n) => new(
        guid,
        Split(s("gear")).Where(p => p.Length >= 2).Select(p => new ItemSlot(Equipped, p[0], p[1], 1)).ToList(),
        Slots(s("bags")), Sizes(s("bagSizes")),
        Slots(s("bank")), Sizes(s("bankSizes")),
        (long)n("scannedAt"), (long)n("bankAt"), (long)n("savedAt"), s("savedZone"))
    {
        Sets = ParseSets(s("sets")),
        Effects = ParseEffects(s("effects")),
        Enchants = ParseEnchants(s("enchants")),
        EquipmentSets = ParseEquipmentSets(s("equipSets")),
        HasGearScan = n("setsAt") > 0 || s("sets").Length > 0 || s("effects").Length > 0,
    };

    // "set name~worn~in set~needed~bonus text", one bonus per line, grouped into sets in the order seen.
    public static List<GearSet> ParseSets(string text) => Lines(text)
        .Select(l => l.Split('~', 5))
        .Where(p => p.Length == 5 && int.TryParse(p[1], out _) && int.TryParse(p[2], out _) && int.TryParse(p[3], out _))
        .GroupBy(p => p[0])
        .Select(g => new GearSet(g.Key, g.Max(p => int.Parse(p[1])), int.Parse(g.First()[2]),
            g.Select(p => new SetBonus(int.Parse(p[3]), p[4])).ToList())) // the game's order (fewest pieces first), Services/SetBonuses matches on it
        .ToList();

    // "slot~itemId~Equip: ...", one per line.
    public static List<GearEffect> ParseEffects(string text) => Lines(text)
        .Select(l => l.Split('~', 3))
        .Where(p => p.Length == 3 && int.TryParse(p[0], out _) && int.TryParse(p[1], out _))
        .Select(p => new GearEffect(int.Parse(p[0]), int.Parse(p[1]), p[2]))
        .ToList();

    // "slot~itemId~enchant~enchantId", one per line (the id came in 0.24.1, older lines have none).
    public static List<GearEnchant> ParseEnchants(string text) => ParseEffects(text)
        .Select(e =>
        {
            var idAt = e.Text.LastIndexOf('~');
            var hasId = idAt >= 0 && int.TryParse(e.Text[(idAt + 1)..], out _);
            return hasId
                ? new GearEnchant(e.Slot, e.ItemId, e.Text[..idAt], int.Parse(e.Text[(idAt + 1)..]))
                : new GearEnchant(e.Slot, e.ItemId, e.Text, 0);
        })
        .ToList();

    // "icon~equipped (1/0)~slot:itemId:place,...~set name", one set per line, the name last (it may hold a ~).
    public static List<EquipmentSet> ParseEquipmentSets(string text) => Lines(text)
        .Select(l => l.Split('~', 4))
        .Where(p => p.Length == 4 && p[3].Length > 0 && int.TryParse(p[0], out _))
        .Select(p => new EquipmentSet(p[3], int.Parse(p[0]), p[1] == "1", p[2]
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(i => i.Split(':'))
            .Where(f => f.Length == 3 && int.TryParse(f[0], out _) && int.TryParse(f[1], out _))
            .Select(f => new EquipmentSetItem(int.Parse(f[0]), int.Parse(f[1]), f[2]))
            .ToList()))
        .ToList();

    // Where a set's item is: the game's word for it, except that an item it couldn't find may be in
    // the bank as of the last visit (the game only knows the bank while it's open) or in the bags.
    public string PlaceOf(EquipmentSetItem item)
    {
        if (item.Place != "missing" || item.ItemId <= 0) return item.Place;
        if (Bags.Any(s => s.ItemId == item.ItemId)) return "bags";
        if (Bank.Any(s => s.ItemId == item.ItemId)) return "bank";
        return "missing";
    }

    static IEnumerable<string> Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // Every item with where it is: "equipped", "bags" or "bank".
    public IEnumerable<(ItemSlot Slot, string Place)> Placed =>
        Gear.Select(s => (s, "equipped")).Concat(Bags.Select(s => (s, "bags"))).Concat(Bank.Select(s => (s, "bank")));

    static List<ItemSlot> Slots(string text) =>
        Split(text).Where(p => p.Length >= 4).Select(p => new ItemSlot(p[0], p[1], p[2], p[3])).ToList();

    static Dictionary<int, int> Sizes(string text) =>
        Split(text).Where(p => p.Length >= 2).GroupBy(p => p[0]).ToDictionary(g => g.Key, g => g.First()[1]);

    // "a~b~c,d~e~f" as rows of numbers; anything that isn't a number makes its row empty.
    static IEnumerable<int[]> Split(string text) => text.Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('~'))
        .Select(f => f.All(x => int.TryParse(x, out _)) ? f.Select(int.Parse).ToArray() : []);
}
