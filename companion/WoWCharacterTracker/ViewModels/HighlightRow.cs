using System.Windows.Media;

namespace WoWCharacterTracker.ViewModels;

// A boss kill, rare loot, new mount or death, shown in the character details. Loot and mounts
// get the item's real icon and tooltip from Wowhead.
// The verb ("Looted ") is plain text, the thing after it gets the color (item quality for loot).
sealed class HighlightRow(string glyph, SolidColorBrush brush, string verb, string subject, string when, int itemId = 0) : Observable
{
    public string Glyph { get; } = glyph;
    SolidColorBrush brush = brush;
    public SolidColorBrush Brush { get => brush; set => Set(ref brush, value); }
    public string Verb { get; } = verb;
    public string Subject { get; } = subject;
    public string Text => Verb + Subject;
    public string When { get; } = when;
    public int ItemId { get; } = itemId;

    // The event this row shows, so it can be posted to Discord from the details.
    public Entry? Source { get; set; }

    // A second, smaller line: who was at a boss kill, what a death cost.
    public string Detail { get; set; } = "";
    public bool HasDetail => Detail.Length > 0;

    // WoW's item quality colors: poor, common, uncommon, rare, epic, legendary, artifact, heirloom.
    static readonly int[] QualityColors = [0x9D9D9D, 0xFFFFFF, 0x1EFF00, 0x0070DD, 0xA335EE, 0xFF8000, 0xE6CC80, 0x00CCFF];
    public static SolidColorBrush? QualityBrush(int quality) =>
        quality >= 0 && quality < QualityColors.Length ? ClassColors.Hex(QualityColors[quality]) : null;

    string? iconUrl;
    public string? IconUrl { get => iconUrl; set { Set(ref iconUrl, value); Raise(nameof(HasIcon)); } }
    public bool HasIcon => iconUrl is not null;

    string? tooltip; // null until Wowhead answers, so rows without one show no empty tooltip
    public string? Tooltip { get => tooltip; set => Set(ref tooltip, string.IsNullOrEmpty(value) ? null : value); }
}
