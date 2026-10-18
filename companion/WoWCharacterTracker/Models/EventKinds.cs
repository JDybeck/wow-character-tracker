namespace WoWCharacterTracker.Models;

// Every kind of moment the addon records, in one place: its key (as the addon names it), what the
// app calls it, and where it can show up. The chip lists (stream highlights and alerts, guild
// announcements), the screenshot options and the highlight lists all come from here.
sealed record EventKind(string Key, string Name, bool Shot = false, bool Highlight = false, bool RatingsOnly = false);

static class EventKinds
{
    public static readonly IReadOnlyList<EventKind> All =
    [
        new("level", "Level-ups", Shot: true),
        new("loot", "Loot", Shot: true, Highlight: true),
        new("mount", "Mounts", Shot: true, Highlight: true),
        new("boss", "Boss kills", Shot: true, Highlight: true),
        new("death", "Deaths", Shot: true, Highlight: true),
        new("duel", "Duels", Shot: true, Highlight: true),
        new("makgora", "Mak'gora", Shot: true, Highlight: true),
        new("bg", "Battlegrounds", Highlight: true),
        new("mplus", "Mythic+", Highlight: true, RatingsOnly: true),
        new("pvp", "PvP rating", Highlight: true, RatingsOnly: true),
    ];

    // The kinds that can get a screenshot, as the addon names them.
    public static readonly string[] ShotKinds = [.. All.Where(k => k.Shot).Select(k => k.Key)];

    // The kinds that are highlights (level-ups aren't, they have their own lists).
    public static readonly string[] Highlights = [.. All.Where(k => k.Highlight).Select(k => k.Key)];

    // Mythic+ and PvP rating milestones only happen in a game with ratings (Retail). Alert kinds that
    // aren't event kinds (maxlevel, legendary, epic, rare) are offered everywhere.
    public static bool OfferedIn(string key, bool hasRatings) =>
        hasRatings || All.FirstOrDefault(k => k.Key == key) is not { RatingsOnly: true };

    // On/off chips for the kinds a game offers: (kind, name, tip) tables filtered the same way everywhere.
    public static IEnumerable<(string Kind, string Name, string Tip)> Offered(IEnumerable<(string Kind, string Name, string Tip)> names, bool hasRatings) =>
        names.Where(n => OfferedIn(n.Kind, hasRatings));
}
