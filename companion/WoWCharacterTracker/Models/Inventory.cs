namespace WoWCharacterTracker.Models;

// One item in a bag slot, or equipped (Bag is then Inventory.Equipped and Slot the inventory slot).
record ItemSlot(int Bag, int Slot, int ItemId, int Count);

// What a character carries, from the addon's items[guid] (Inventory.lua): equipped gear, bags, and
// the bank as of the last time it was open (BankAt, 0 = never seen). SavedAt and SavedZone: when and
// where the game last wrote the saved file (/reload or logout), which is how fresh this is.
record Inventory(string Guid, IReadOnlyList<ItemSlot> Gear, IReadOnlyList<ItemSlot> Bags, IReadOnlyDictionary<int, int> BagSizes,
    IReadOnlyList<ItemSlot> Bank, IReadOnlyDictionary<int, int> BankSizes, long ScannedAt, long BankAt, long SavedAt = 0, string SavedZone = "")
{
    public const int Equipped = -100;

    // From the saved strings: gear "slot~itemId,...", bags and bank "bag~slot~itemId~count,...",
    // sizes "bag~size,...".
    public static Inventory Parse(string guid, Func<string, string> s, Func<string, double> n) => new(
        guid,
        Split(s("gear")).Where(p => p.Length >= 2).Select(p => new ItemSlot(Equipped, p[0], p[1], 1)).ToList(),
        Slots(s("bags")), Sizes(s("bagSizes")),
        Slots(s("bank")), Sizes(s("bankSizes")),
        (long)n("scannedAt"), (long)n("bankAt"), (long)n("savedAt"), s("savedZone"));

    public IEnumerable<ItemSlot> All => Gear.Concat(Bags).Concat(Bank);

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
