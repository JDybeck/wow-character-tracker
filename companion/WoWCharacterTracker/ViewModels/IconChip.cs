namespace WoWCharacterTracker.ViewModels;

// A boss kill, rare loot, new mount or death, shown in the character details.
// A small icon with text, like a crest and its count or a profession and its skill. The icon and
// tooltip fill in once Wowhead has answered.
sealed class IconChip(string text, string? iconUrl = null, string tooltip = "") : Observable
{
    string text = text;
    public string Text { get => text; set => Set(ref text, value); }

    string? iconUrl = iconUrl;
    public string? IconUrl { get => iconUrl; set => Set(ref iconUrl, value); }

    string tooltip = tooltip;
    public string Tooltip { get => tooltip; set => Set(ref tooltip, value); }

    // For crests: the currency id to look up.
    public int Tag { get; init; }
}
