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

    // A second, smaller line: what a death cost, who you dueled.
    public string Detail { get; set; } = "";
    public bool HasDetail => Detail.Length > 0;

    // Or who was at a boss kill or Mythic+ run: each name in its class color, then the spec.
    public IReadOnlyList<GroupPart> Group { get; set; } = [];
    public bool HasGroup => Group.Count > 0;

    // WoW's item quality colors (GameColors).
    public static SolidColorBrush? QualityBrush(int quality) => GameColors.Quality(quality) is int c ? ClassColors.Hex(c) : null;

    string? iconUrl;
    public string? IconUrl { get => iconUrl; set { Set(ref iconUrl, value); Raise(nameof(HasIcon)); } }
    public bool HasIcon => iconUrl is not null;

    // Loot and mounts: the item, for its tooltip and Wowhead link (Ui/ItemTip).
    ItemInfo? info;
    public ItemInfo? Info { get => info; set => Set(ref info, value); }
}

// One group member on a highlight's second line: "With " (Lead, first only), the name in its class
// color, then " (90 Havoc)," (Rest). Kept together so a line never breaks between name and spec.
sealed record GroupPart(string Lead, string Name, SolidColorBrush Brush, string Rest)
{
    public static IReadOnlyList<GroupPart> Of(IReadOnlyList<GroupMember> members) => [.. members.Select((m, i) =>
    {
        var who = string.Join(" ", new[] { m.Level > 0 ? m.Level.ToString() : "", m.SpecOrClass }.Where(s => s.Length > 0));
        var rest = (who.Length > 0 ? $" ({who})" : "") + (i < members.Count - 1 ? "," : "");
        return new GroupPart(i == 0 ? "With " : "", m.Name, ClassColors.Brush(ClassColors.For(m.ClassFile)), rest);
    })];
}
