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

// An Equip: or Use: line of an equipped item, or its enchant.
record GearEffect(int Slot, int ItemId, string Text);

// What a character carries, from the addon's items[guid] (Inventory.lua): equipped gear, bags, and
// the bank as of the last time it was open (BankAt, 0 = never seen). SavedAt and SavedZone: when and
// where the game last wrote the saved file (/reload or logout), which is how fresh this is.
record Inventory(string Guid, IReadOnlyList<ItemSlot> Gear, IReadOnlyList<ItemSlot> Bags, IReadOnlyDictionary<int, int> BagSizes,
    IReadOnlyList<ItemSlot> Bank, IReadOnlyDictionary<int, int> BankSizes, long ScannedAt, long BankAt, long SavedAt = 0, string SavedZone = "")
{
    public const int Equipped = -100;

    public IReadOnlyList<GearSet> Sets { get; init; } = [];
    public IReadOnlyList<GearEffect> Effects { get; init; } = [];
    public IReadOnlyList<GearEffect> Enchants { get; init; } = []; // the same shape: slot, item, the enchant
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
        Enchants = ParseEffects(s("enchants")),
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
