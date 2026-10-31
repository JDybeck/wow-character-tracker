namespace WoWCharacterTracker.Models;

// A ready-made followed list a game offers (Flavor.ReadyMadeLists), switched on by its key in
// FlavorSettings.ReadyMadeLists. Tag marks its items in the list. Legendaries stay out of these,
// they're always posted anyway. Every id was checked against Wowhead's Forever tooltips on
// 2026-10-31; see the launch todo in backlog.md.
sealed record ReadyMadeList(string Key, string Name, string Tag, string Hint, IReadOnlyList<FollowedItem> Items)
{
    public static readonly ReadyMadeList RareAndFun = new("rarefun", "Exceedingly rare and fun items", "Rare and fun",
        "Famous drops and fun finds.",
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
    ]);

    public static readonly ReadyMadeList LevelingLegends = new("leveling", "Leveling legends", "Leveling legend",
        "The dungeon drops and quest rewards people remember from leveling.",
    [
        new(5191, "Cruel Barb"), new(5196, "Smite's Reaver"), new(5197, "Cookie's Tenderizer"), new(5193, "Cape of the Brotherhood"),
        new(7717, "Ravager"), new(7719, "Raging Berserker's Helm"), new(7724, "Gauntlets of Divinity"), new(7726, "Aegis of the Scarlet Commander"),
        new(6975, "Whirlwind Axe"), new(6976, "Whirlwind Warhammer"), new(6977, "Whirlwind Sword"), new(6953, "Verigan's Fist"),
        new(6220, "Meteor Shard"), new(1482, "Shadowfang"), new(6909, "Strike of the Hydra"), new(1155, "Rod of the Sleepwalker"),
        new(7683, "Bloody Brass Knuckles"), new(9449, "Manual Crowd Pummeler"),
    ]);

    public static readonly IReadOnlyList<ReadyMadeList> All = [RareAndFun, LevelingLegends];

    public bool Contains(int id) => Items.Any(i => i.Id == id);
}
