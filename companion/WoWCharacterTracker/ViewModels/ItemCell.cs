using System.Windows.Media;

namespace WoWCharacterTracker.ViewModels;

// One item as the app shows it: icon, name in its quality color and Wowhead's tooltip. Shared per
// item id within a game tab (ItemInfo), so each item is only looked up once.
sealed class ItemInfo(int itemId, string url) : Observable
{
    public int ItemId { get; } = itemId;
    public string Url { get; } = url; // the item's Wowhead page (clicking the item opens it)

    string name = "";
    public string Name { get => name; private set => Set(ref name, value); }
    string? iconUrl;
    public string? IconUrl { get => iconUrl; private set => Set(ref iconUrl, value); }
    string? tooltip;
    public string? Tooltip { get => tooltip; private set => Set(ref tooltip, value); }
    string? html;
    public string? Html { get => html; private set => Set(ref html, value); } // Wowhead's tooltip, for WowTooltip
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

    public async Task LoadAsync(Flavor flavor)
    {
        if (await Wowhead.GetAsync("item", ItemId, flavor) is not { } info) return;
        Name = info.Name;
        IconUrl = Wowhead.IconUrl(info.Icon, "medium");
        Tooltip = info.Tooltip;
        Html = info.Html;
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
}

// One bag (or the bank's own slots) as a grid of cells.
sealed class BagView(string title, IReadOnlyList<ItemCell> cells)
{
    public string Title { get; } = title;
    public IReadOnlyList<ItemCell> Cells { get; } = cells;
}
