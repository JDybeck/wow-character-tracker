using System.Globalization;

namespace WoWCharacterTracker.Services;

// The header's search: how well a query fits a thing. Every word of the query has to be in the name or
// its extra words; case and accents don't count ("fagel" finds Fågelmannen).
static class Search
{
    static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;
    const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    // null: no match. Lower ranks first: 0 the name starts with the query, 1 a word in the name does,
    // 2 the name has it somewhere, 3 only the extra words (realm, class, keywords) have it.
    public static int? Score(string query, string name, string words = "")
    {
        var q = query.Trim();
        if (q.Length == 0) return null;
        var all = $"{name} {words}";
        if (!q.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(part => Has(all, part))) return null;
        if (Compare.IsPrefix(name, q, Loose)) return 0;
        if (Has(name, " " + q)) return 1;
        if (Has(name, q)) return 2;
        return 3;
    }

    static bool Has(string text, string part) => Compare.IndexOf(text, part, Loose) >= 0;
}

// A place in the app the search can open: a setting or a page, with the words people may type for it.
// Page is MainViewModel.Page; Place opens that part of Sharing (FlavorViewModel.OpenPlace).
sealed record SearchTopic(string Title, string Where, string Page, string Words, string Place = "", Func<Flavor, bool>? Shows = null)
{
    public static readonly IReadOnlyList<SearchTopic> All =
    [
        new("Open with World of Warcraft", "Settings", "settings", "start startup tray autostart launch login"),
        new("Hide the window when the game closes", "Settings", "settings", "tray minimize close"),
        new("Install updates automatically", "Settings", "settings", "update auto version"),
        new("Animations", "Settings", "settings", "motion slide fade orb"),
        new("Test characters", "Settings", "settings", "demo sample try fake"),
        new("Track a game", "Settings", "settings", "turn off on forever retail game tab"),
        new("Theme", "Settings", "settings", "colors color appearance look dark palette"),
        new("Keep all history", "Settings, In game", "settings", "history loot duels battlegrounds saved file"),
        new("Advanced combat logging", "Settings, In game", "settings", "combat log warcraftlogs"),
        new("Distances on the map", "Settings, In game", "settings", "meters yards metric units friends map distance"),
        new("Instant posting pattern", "Settings, In game", "settings", "corner grid label live pattern"),
        new("Manage data", "Settings", "data", "remove delete characters history friends caches clean"),
        new("Sync now", "Settings", "settings", "sync refresh reload read saved data again"),
        new("Uninstall", "Settings", "uninstall", "remove delete addon app"),
        new("Discord webhooks", "Sharing", "sharing", "discord webhook channel post server", Place: "discord"),
        new("Stream overlay", "Sharing", "sharing", "stream obs streamlabs overlay alert twitch", Place: "stream"),
        new("How much gets shared", "Sharing", "sharing", "preset quiet normal everything custom"),
        new("What gets posted", "Sharing", "sharing", "level levels loot rare epic boss bosses mount mounts death deaths duel battleground profession max level"),
        new("Screenshots", "Sharing", "sharing", "screenshot picture image hide chat ui attach"),
        new("Guild and party chat", "Sharing", "sharing", "announce guild party raid chat"),
        new("Name in posts", "Sharing", "sharing", "surname first name full name", Shows: f => f.HasSurnames),
        new("Mythic+ and ratings", "Sharing", "sharing", "mythic keystone key rating pvp arena", Shows: f => f.HasRatings),
        new("Copy or paste posting settings", "Sharing", "sharing", "copy paste share code reset defaults"),
        new("Friends", "Page", "friends", "battle.net battletag share nickname"),
        new("Stats", "Page", "stats", "leaderboard today week month all time score"),
        new("Professions", "Page", "professions", "craft crafting recipes trade skill"),
        new("Followed items", "Page", "followed", "wish list wishlist watch follow"),
    ];
}
