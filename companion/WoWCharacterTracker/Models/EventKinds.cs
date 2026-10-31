namespace WoWCharacterTracker.Models;

// Every kind of moment the addon records, in one place: its key (as the addon names it), what the
// app calls it, and where it can show up. The guild announcement chips, the screenshot options and
// the highlight lists come from here; the stream panel's chips check against it in StreamTests.
// RatingsOnly: only in a game with Mythic+ and ratings (Retail); NoRatings: only in one without
// (end boss kills, which Retail swaps for Mythic+ runs; hardcore deaths and Mak'gora, which it doesn't have).
sealed record EventKind(string Key, string Name, bool Shot = false, bool Highlight = false, bool RatingsOnly = false, bool NoRatings = false);

static class EventKinds
{
    public static readonly IReadOnlyList<EventKind> All =
    [
        new("level", "Level-ups", Shot: true),
        new("loot", "Loot", Shot: true, Highlight: true),
        new("mount", "Mounts", Shot: true, Highlight: true),
        new("boss", "Boss kills", Shot: true, Highlight: true, NoRatings: true),
        new("keyrun", "Mythic+ runs", Shot: true, Highlight: true, RatingsOnly: true),
        new("death", "Deaths", Shot: true, Highlight: true, NoRatings: true),
        new("duel", "Duels", Shot: true, Highlight: true),
        new("makgora", "Mak'gora", Shot: true, Highlight: true, NoRatings: true),
        new("bg", "Battlegrounds", Highlight: true),
        new("profession", "Professions", Highlight: true),
        new("mplus", "Mythic+", Highlight: true, RatingsOnly: true),
        new("pvp", "PvP rating", Highlight: true, RatingsOnly: true),
    ];

    // The kinds that can get a screenshot, as the addon names them.
    public static readonly string[] ShotKinds = [.. All.Where(k => k.Shot).Select(k => k.Key)];

    // The kinds that are highlights (level-ups aren't, they have their own lists).
    public static readonly string[] Highlights = [.. All.Where(k => k.Highlight).Select(k => k.Key)];

    // Mythic+ runs and rating milestones only happen in a game with ratings (Retail), end boss kills,
    // hardcore deaths and Mak'gora only in one without. Alert kinds that aren't event kinds (maxlevel, legendary, epic, rare) are
    // offered everywhere.
    public static bool OfferedIn(string key, bool hasRatings) =>
        All.FirstOrDefault(k => k.Key == key) is not { } kind || (hasRatings ? !kind.NoRatings : !kind.RatingsOnly);

    // On/off chips for the kinds a game offers: (kind, name, tip) tables filtered the same way everywhere.
    public static IEnumerable<(string Kind, string Name, string Tip)> Offered(IEnumerable<(string Kind, string Name, string Tip)> names, bool hasRatings) =>
        names.Where(n => OfferedIn(n.Kind, hasRatings));
}
