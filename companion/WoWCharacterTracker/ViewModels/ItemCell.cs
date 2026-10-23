using System.Windows.Media;

namespace WoWCharacterTracker.ViewModels;

// One thing from the game as the app shows it: icon, name in its quality color and Wowhead's tooltip.
// Mostly items, but crests (type "currency") and talents ("spell") are shown the same way. Shared per
// type and id within a game tab (GameLookup), so each is only looked up once. Ui/ItemTip
// shows it on hover and opens its Wowhead page on click.
sealed class ItemInfo(int itemId, string url, string type = "item") : Observable
{
    public int ItemId { get; } = itemId;
    public string Url { get; } = url; // the Wowhead page (clicking it opens it)
    public string Type { get; } = type; // "item", "currency" or "spell", as Wowhead calls them

    string name = "";
    public string Name { get => name; private set => Set(ref name, value); }
    string? iconUrl;
    public string? IconUrl { get => iconUrl; private set => Set(ref iconUrl, value); }
    string? tooltip;
    public string? Tooltip { get => tooltip; private set => Set(ref tooltip, value); }
    string? html;
    public string? Html { get => html; private set => Set(ref html, value); } // Wowhead's tooltip, for WowTooltip
    public int Quality { get; private set; } = -1; // -1 until known (currencies and spells have none)
    SolidColorBrush brush = ClassColors.Hex(0xFFFFFF);
    public SolidColorBrush Brush { get => brush; private set => Set(ref brush, value); }

    // The game's item class (Inventory.lua's itemClasses), -1 until the addon has seen it.
    int classId = -1;
    public int ClassId { get => classId; set => Set(ref classId, value); }

    // "gear", "consumable", "trade", "quest" or "other", for the Items page's type chips. Items the
    // addon hasn't classed yet (scanned before 0.16.0) are guessed from the tooltip.
    public string Kind => ClassId switch
    {
        2 or 4 => "gear",
        0 => "consumable",
        3 or 5 or 7 or 9 => "trade", // gems, reagents, trade goods, recipes
        12 => "quest",
        -1 when Tooltip is { } t && (t.Contains("Durability") || t.Contains(" Armor\n")) => "gear",
        -1 when Tooltip is { } t && (t.Contains("Quest Item") || t.Contains("This Item Begins a Quest")) => "quest",
        _ => "other",
    };

    // The name the game gave (crests carry one), shown until or unless Wowhead answers.
    public void Fallback(string name)
    {
        if (Name.Length > 0 || name.Length == 0) return;
        Name = name;
        Tooltip ??= name;
    }

    // Looked up once; awaiting it again just waits for that lookup.
    Task? load;
    public Task LoadAsync(Flavor flavor) => load ??= Load(flavor);
    // The lookup started for this (GameLookup starts it), done when nothing was started.
    public Task WhenLoaded => load ?? Task.CompletedTask;

    async Task Load(Flavor flavor)
    {
        if (await Wowhead.GetAsync(Type, ItemId, flavor) is not { } info) return;
        Name = info.Name;
        IconUrl = Wowhead.IconUrl(info.Icon, "medium");
        Tooltip = info.Tooltip.Length > 0 ? info.Tooltip : info.Name;
        Html = info.Html;
        Quality = info.Quality;
        if (HighlightRow.QualityBrush(info.Quality) is { } q) Brush = q;
    }
}

// A slot in a bag, the bank or the character window: the item there (null when empty) and how many.
sealed class ItemCell(ItemInfo? item, int count = 1, string slotName = "")
{
    public ItemInfo? Item { get; } = item;
    public int Count { get; } = count;
    public string CountText => Count > 1 ? Count.ToString() : "";
    public bool IsEmpty => Item is null;
    public string SlotName { get; } = slotName; // "Head", "Main Hand"... for equipped gear
    // Equipped gear: its enchant under the name, and the enchant's Wowhead tooltip once found (GameLookup.Enchant).
    public string Enchant { get; init; } = "";
    public PendingInfo? EnchantInfo { get; init; }
    public bool HasEnchant => Enchant.Length > 0;
    // An Equipment Manager set's piece that isn't worn: "bags" or "bank" (tinted gray), "missing"
    // (red), or "ignored" (the set leaves the slot as it is). "" = worn.
    public string Place { get; init; } = "";
    public bool IsAway => Place is "bags" or "bank";
    public bool IsMissing => Place == "missing";
    public string PlaceNote => Place switch
    {
        "bags" => "in bags",
        "bank" => "in bank",
        "missing" => "not found",
        "ignored" => "not in set",
        _ => "",
    };
}

// One bag (or the bank's own slots or a bank tab): its items, and how many slots are free.
sealed class BagView(string title, IReadOnlyList<ItemCell> cells, int free = 0)
{
    public string Title { get; } = title;
    public IReadOnlyList<ItemCell> Cells { get; } = cells;
    public int Free { get; } = free;
    public string FreeText => Free switch { 0 => "full", 1 => "1 free slot", _ => $"{Free} free slots" };
}
