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

    // The row for an event, keeping the event so it can be posted (the details, Home). Boss kills and Mythic+
    // runs say who was there, deaths what was lost.
    public static HighlightRow From(Entry e)
    {
        var row = Row(e);
        row.Source = e;
        if (e.Kind is "boss" or "keyrun") row.Group = GroupPart.Of(e.Members);
        row.Detail = e.Kind switch
        {
            "level" when e.Seconds > 0 => $"Took {Format.Duration(e.Seconds)}",
            "boss" when e.GroupSize > 5 => $"With a raid of {e.GroupSize}",
            "duel" or "makgora" when e.OpponentText.Length > 0 => char.ToUpperInvariant(e.OpponentText[0]) + e.OpponentText[1..],
            "death" => string.Join(", ", new[] { e.Played > 0 ? $"{Format.Played(e.Played)} played" : "", $"lost {Format.Money(e.Gold)}", e.Zone }.Where(p => p.Length > 0)),
            _ => "",
        };
        return row;
    }

    // The text is HighlightText's (shared with the stream overlay); the glyph and color are the app's.
    static HighlightRow Row(Entry e)
    {
        var (verb, subject) = HighlightText.Of(e);
        var (glyph, color) = e.Kind switch
        {
            "level" => ("", 0x93C5FD), // an arrow up
            "boss" => ("", 0xFBBF24),
            "loot" => ("", 0),
            "duel" when e.Lost => ("", 0x94A3B8),
            "duel" => ("", 0x93C5FD),
            "bg" when e.Lost => ("", 0x94A3B8),
            "bg" => ("", 0x93C5FD),
            "makgora" when e.Lost => ("", 0x94A3B8),
            "mplus" or "keyrun" => ("", 0xC084FC),
            "pvp" => ("", 0xF87171),
            "mount" => ("", 0x4ADE80),
            "profession" => ("", 0xFCD34D), // a wrench
            _ => ("", 0xF87171), // Mak'gora wins, deaths
        };
        var brush = e.Kind == "loot" ? QualityBrush(e.Quality) ?? ClassColors.Hex(0x0070DD) : ClassColors.Hex(color);
        return new(glyph, brush, verb, subject, Stamp(e.At), e.Kind is "loot" or "mount" ? e.ItemId : 0);
    }

    static string Stamp(long at) => DateTimeOffset.FromUnixTimeSeconds(at).LocalDateTime.ToString("MMM d, HH:mm");

    // WoW's item quality colors (GameColors).
    public static SolidColorBrush? QualityBrush(int quality) => GameColors.Quality(quality) is int c ? ClassColors.Hex(c) : null;

    string? iconUrl;
    public string? IconUrl { get => iconUrl; set { Set(ref iconUrl, value); Raise(nameof(HasIcon)); } }
    public bool HasIcon => iconUrl is not null;

    // Loot and mounts: the item, for its tooltip and Wowhead link (Ui/ItemTip).
    ItemInfo? info;
    public ItemInfo? Info { get => info; set => Set(ref info, value); }

    // The item's real icon and quality color, once Wowhead has them (Info set from the tab's GameLookup).
    public async Task LoadIconAsync(Flavor flavor)
    {
        if (Info is null) return;
        await Info.LoadAsync(flavor);
        IconUrl = Info.IconUrl;
        if (Info.Quality >= 0) Brush = Info.Brush;
    }
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
