namespace WoWCharacterTracker.ViewModels;

// A small icon with text, like a crest and its count or a profession and its skill. Chips for things
// from the game (crests, talents) carry an ItemInfo, so they show Wowhead's tooltip the way items do;
// the others just have a plain tooltip text.
sealed class IconChip(string text, string? iconUrl = null, string tooltip = "") : Observable
{
    string text = text;
    public string Text { get => text; set => Set(ref text, value); }

    string? iconUrl = iconUrl;
    public string? IconUrl { get => iconUrl; set => Set(ref iconUrl, value); }

    string tooltip = tooltip;
    public string Tooltip { get => tooltip; set => Set(ref tooltip, value); }

    // For crests and talents: the currency or spell id to look up.
    public int Tag { get; init; }

    ItemInfo? info;
    public ItemInfo? Info { get => info; set => Set(ref info, value); }
}
