using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace WoWCharacterTracker.ViewModels;

// The Gear and bags tab of the character details: equipped gear laid out like the character window,
// the bags, and the bank as of the last time it was open.
sealed class CharacterGear
{
    // Inventory slots down the left and right of the character window, and the weapons below.
    static readonly (int Slot, string Name)[] LeftSlots = [(1, "Head"), (2, "Neck"), (3, "Shoulder"), (15, "Back"), (5, "Chest"), (4, "Shirt"), (19, "Tabard"), (9, "Wrist")];
    static readonly (int Slot, string Name)[] RightSlots = [(10, "Hands"), (6, "Waist"), (7, "Legs"), (8, "Feet"), (11, "Finger"), (12, "Finger"), (13, "Trinket"), (14, "Trinket")];
    static readonly (int Slot, string Name)[] Weapons = [(16, "Main Hand"), (17, "Off Hand")];
    // Slot 18: a relic (libram, idol, totem) for Paladins, Druids and Shamans, else ranged. Retail has
    // had no slot 18 since Legion.
    static (int, string)[] BottomSlots(string classFile, bool retail) =>
        retail ? Weapons : [.. Weapons, (18, classFile.ToUpperInvariant() is "PALADIN" or "DRUID" or "SHAMAN" ? "Relic" : "Ranged")];

    public IReadOnlyList<ItemCell> Left { get; init; } = [];
    public IReadOnlyList<ItemCell> Right { get; init; } = [];
    public IReadOnlyList<ItemCell> Bottom { get; init; } = [];
    public IReadOnlyList<BagView> Bags { get; init; } = [];
    public string BankText { get; init; } = "";
    // "Saved 12m ago, in Stormwind City": when the game last wrote the file this comes from.
    public string SavedText { get; init; } = "";
    public bool HasSavedText => SavedText.Length > 0;
    public bool HasInventory { get; init; }

    // The bank, a tab at a time: Retail's 5-6 bank tabs of 98 slots are most of the slots here, and
    // building them all at once froze the tab for over a second. The gear and bags show first, then
    // each bank tab is added when the window has a free moment (it stays usable in between).
    public ObservableCollection<BagView> Bank { get; } = [];
    IReadOnlyList<BagView> bankTabs = [];
    public bool HasBank => bankTabs.Count > 0;

    async void FillBank()
    {
        var dispatcher = Dispatcher.FromThread(Thread.CurrentThread);
        foreach (var tab in bankTabs)
        {
            if (dispatcher is not null) await Dispatcher.Yield(DispatcherPriority.Background);
            Bank.Add(tab);
        }
    }

    // Beside the gear: the sets it counts for and the items' Equip/Use effects, as the game's tooltips say.
    // Enchants sit under the item names (ItemCell.Enchant).
    public IReadOnlyList<SetView> Sets { get; init; } = [];
    public IReadOnlyList<EffectView> Effects { get; init; } = [];
    public bool HasSets => Sets.Count > 0;
    public bool HasEffects => Effects.Count > 0;
    public bool HasExtras => HasSets || HasEffects;
    // Saved by an addon from before set bonuses were read (0.23.0): say how to get them.
    public bool NeedsGearScan { get; init; }
    public bool ShowsSide => HasExtras || NeedsGearScan;
    // Retail's catalyst pieces don't always read as their set in the game's tooltips: say so under the sets.
    public bool IsRetail { get; init; }
    public bool ShowsCatalystNote => IsRetail && HasSets;
    // "Effects (9)", folded away by default: useful to look up, not worth the room.
    public string EffectsTitle => $"EFFECTS ({Effects.Count})";

    public static CharacterGear Empty { get; } = new();

    // spec: the character's spec, to pick its tier bonuses from Wowhead's (Retail lists every spec's).
    public static CharacterGear From(Inventory? inv, Func<int, ItemInfo> item, string classFile, bool retail, string spec = "")
    {
        if (inv is null) return Empty;
        var worn = inv.Gear.ToDictionary(g => g.Slot, g => g.ItemId);
        var wornInfos = worn.Values.Distinct().Select(item).ToList();
        var enchants = EnchantsBySlot(inv.Enchants);
        IReadOnlyList<ItemCell> Slots((int Slot, string Name)[] slots) => slots
            .Select(s => new ItemCell(worn.TryGetValue(s.Slot, out var id) ? item(id) : null, 1, s.Name)
            {
                Enchant = enchants.GetValueOrDefault(s.Slot, ""),
            })
            .ToList();

        var gear = new CharacterGear
        {
            Left = Slots(LeftSlots),
            Right = Slots(RightSlots),
            Bottom = Slots(BottomSlots(classFile, retail)),
            Bags = Grids(inv.Bags, inv.BagSizes, item, bag => bag switch { 0 => "Backpack", 5 => "Reagent bag", _ => $"Bag {bag}" }), // 5: retail's reagent bag slot
            bankTabs = Grids(inv.Bank, inv.BankSizes, item, bag => bag < 0 ? "Bank" : $"Bank bag {bag - 4}"),
            NeedsGearScan = !inv.HasGearScan,
            BankText = inv.BankAt > 0 ? $"Bank, as of {Format.Ago(inv.BankAt)}" : "Open your bank in game once and it shows up here.",
            SavedText = SavedTextOf(inv),
            HasInventory = true,
            Sets = inv.Sets.Select(s => new SetView(s, wornInfos, spec)).ToList(),
            Effects = EffectRows(inv.Effects, item),
            IsRetail = retail,
        };
        gear.FillBank();
        return gear;
    }

    // "Saved 12m ago, in Stormwind City", or "" when not known. The Items page shows it per character too.
    public static string SavedTextOf(Inventory? inv) =>
        inv is null || inv.SavedAt <= 0 ? "" : $"Saved {Format.Ago(inv.SavedAt)}" + (inv.SavedZone.Length > 0 ? $", in {inv.SavedZone}" : "");

    // One row per equipped item, its effects under each other (an item with an Equip: and a Use: line
    // showed its icon twice).
    static List<EffectView> EffectRows(IReadOnlyList<GearEffect> effects, Func<int, ItemInfo> item) => effects
        .GroupBy(e => (e.Slot, e.ItemId))
        .Select(g => new EffectView(new ItemCell(item(g.Key.ItemId)), string.Join("\n", g.Select(e => Format.TooltipLine(e.Text)))))
        .ToList();

    // Each slot's enchant text, shown under the item there (a slot with two enchants gets both lines).
    static Dictionary<int, string> EnchantsBySlot(IReadOnlyList<GearEffect> enchants) => enchants
        .GroupBy(e => e.Slot)
        .ToDictionary(g => g.Key, g => string.Join("\n", g.Select(e => Format.EnchantName(e.Text))));

    // Each bag as its items in slot order, and how many slots are free (empty slots aren't drawn:
    // they were most of the cells and told nothing a count doesn't).
    static List<BagView> Grids(IReadOnlyList<ItemSlot> items, IReadOnlyDictionary<int, int> sizes, Func<int, ItemInfo> item, Func<int, string> title)
    {
        var byBag = items.GroupBy(i => i.Bag).ToDictionary(g => g.Key, g => g.Where(i => i.Slot <= sizes.GetValueOrDefault(g.Key)).OrderBy(i => i.Slot).ToList());
        return sizes.OrderBy(s => s.Key == -1 ? -1 : s.Key)
            .Select(s => (Size: s.Value, Title: title(s.Key), Items: byBag.GetValueOrDefault(s.Key) ?? []))
            .Select(b => new BagView(b.Title, b.Items.Select(i => new ItemCell(item(i.ItemId), i.Count)).ToList(), b.Size - b.Items.Count))
            .ToList();
    }
}

// A set on the gear tab: "Giantstalker Armor 3/8" and its bonuses, the active ones lit like in game.
// The game leaves the count out of active bonuses, so once the worn items' Wowhead tooltips are in,
// the counts come from the one that lists this set (Services/SetBonuses).
sealed class SetView
{
    public string Name { get; }
    public string Count { get; }
    public IReadOnlyList<BonusView> Bonuses { get; }

    public SetView(GearSet set, IReadOnlyList<ItemInfo> worn, string spec)
    {
        Name = set.Name;
        Count = $"{set.Have}/{set.Max}";
        Bonuses = set.Bonuses.Select(b => new BonusView(b.Text)).ToList();
        Show(set.Bonuses.Select(b => b.Need).ToList(), set.Have);
        _ = FillCountsAsync(set, worn, spec);
    }

    async Task FillCountsAsync(GearSet set, IReadOnlyList<ItemInfo> worn, string spec)
    {
        var game = set.Bonuses.Select(b => b.Need).ToList();
        foreach (var info in worn)
        {
            await info.WhenLoaded;
            if (info.Html is not { Length: > 0 } html) continue;
            var counts = SetBonuses.Counts(html, set.Name, spec);
            if (counts.Count == 0) continue;
            Show(SetBonuses.Needs(game, counts), set.Have);
            return;
        }
    }

    void Show(IReadOnlyList<int> needs, int have)
    {
        for (var i = 0; i < Bonuses.Count; i++) Bonuses[i].Set(needs[i], have);
    }
}

// One bonus: "(2)" (or a check while the count isn't known yet) and whether it's active.
sealed class BonusView(string text) : Observable
{
    public string Text { get; } = text;
    string need = "";
    public string Need { get => need; private set => Set(ref need, value); }
    bool isActive;
    public bool IsActive { get => isActive; private set => Set(ref isActive, value); }

    public void Set(int pieces, int have)
    {
        Need = pieces > 0 ? $"({pieces})" : "✓";
        IsActive = new SetBonus(pieces, Text).IsActive(have);
    }
}

// An item's Equip/Use line on the gear tab, with the item (its icon, tooltip and link).
sealed record EffectView(ItemCell Cell, string Text);
