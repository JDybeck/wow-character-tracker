namespace WoWCharacterTracker.Models;

// Forever's ready-made followed list, for FlavorSettings.FollowNotable: drops the community knows as
// exceedingly rare or just fun to get. Legendaries aren't here, they're always posted anyway. Every
// id was checked against Wowhead's Forever tooltips on 2026-10-31; see the launch todo in backlog.md.
static class NotableItems
{
    public static readonly IReadOnlyList<FollowedItem> All =
    [
        new(13335, "Deathcharger's Reins"), new(19872, "Swift Razzashi Raptor"), new(19902, "Swift Zulian Tiger"),
        new(21321, "Red Qiraji Resonating Crystal"),
        new(1728, "Teebu's Blazing Longsword"), new(2243, "Hand of Edward the Odd"), new(2801, "Blade of Hanna"),
        new(944, "Elemental Mage Staff"), new(868, "Ardent Custodian"), new(2164, "Gut Ripper"), new(21134, "Dark Edge of Insanity"),
        new(3475, "Cloak of Flames"), new(14554, "Cloudkeeper Legplates"), new(17007, "Stonerender Gauntlets"),
        new(284266, "Wintersaber Hide Lined Gloves"),
        new(18665, "The Eye of Shadow"), new(18646, "The Eye of Divinity"),
        new(12717, "Plans: Lionheart Helm"), new(22389, "Plans: Sageblade"), new(22390, "Plans: Persuader"),
        new(8499, "Tiny Crimson Whelpling"), new(10822, "Dark Whelpling"), new(282047, "Baby Crocolisk"),
        new(284176, "Tallstrider Hatchling"), new(11110, "Chicken Egg"),
    ];

    static readonly HashSet<int> Ids = [.. All.Select(i => i.Id)];
    public static bool Contains(int id) => Ids.Contains(id);
}
