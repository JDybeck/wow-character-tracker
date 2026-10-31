namespace WoWCharacterTracker.Models;

// A ready-made followed list a game offers (Flavor.ReadyMadeLists), switched on by its key in
// FlavorSettings.ReadyMadeLists. Tag marks its items in the list. Legendary gear stays out of these,
// it's always posted anyway. Every id was checked against Wowhead's Forever tooltips on
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
        new(11000, "Shadowforge Key"), new(9394, "Horned Viking Helmet"), new(9425, "Pendulum of Doom"), new(9431, "Papal Fez"),
        new(9426, "Monolithic Bow"), new(9416, "Grimlok's Charge"), new(9415, "Grimlok's Tribal Vestments"), new(9380, "Jang'thraze the Protector"),
        new(2933, "Seal of Wrynn"), new(6469, "Venomstrike"), new(6463, "Deep Fathom Ring"), new(6472, "Stinging Viper"),
        new(7713, "Illusionary Rod"), new(7714, "Hypnotic Blade"), new(10410, "Leggings of the Fang"), new(9624, "Triprunner Dungarees"),
        new(7689, "Morbid Dawn"), new(7690, "Ebon Vise"), new(7688, "Ironspine's Ribcage"), new(7686, "Ironspine's Eye"),
        new(271767, "Healer's Staff"), new(4500, "Traveler's Backpack"), new(4984, "Skull of Impending Doom"), new(2820, "Nifty Stopwatch"),
        new(19106, "Ice Barbed Spear"), new(10775, "Carapace of Tuten'kash"), new(10763, "Icemetal Barbute"), new(274158, "Death Prophet Spine"),
        new(2816, "Death Speaker Scepter"), new(6687, "Corpsemaker"), new(6691, "Swinetusk Shank"), new(6689, "Wind Spirit Staff"),
        new(6692, "Pronged Reaver"), new(9459, "Thermaplugg's Left Arm"), new(9492, "Electromagnetic Gigaflux Reactivator"), new(7230, "Smite's Mighty Hammer"),
        new(271211, "Vilewalkers"), new(271217, "Corpse Chopper"), new(6633, "Butcher's Slicer"), new(1292, "Butcher's Cleaver"),
        new(273637, "Blade of Silverlaine"), new(6321, "Silverlaine's Family Seal"), new(6414, "Seal of Sylvanas"), new(3191, "Arced War Axe"),
        new(273643, "Worgenbane Talisman"), new(6641, "Haunting Blade"), new(15424, "Axe of Orgrimmar"), new(15444, "Staff of Orgrimmar"),
        new(279895, "Ironforge Greathammer"), new(279896, "Deepblaze"), new(270227, "Ephemeral Choker"), new(270256, "Durgen's Crescent Axe"),
        new(6505, "Crescent Staff"), new(2042, "Staff of Westfall"), new(17753, "Verdant Keeper's Aim"), new(11122, "Carrot on a Stick"),
        new(19141, "Luffa"), new(1191, "Bag of Marbles"), new(7001, "Gravestone Scepter"), new(19120, "Rune of the Guard Captain"), new(6468, "Deviate Scale Belt"),
        // Classic's ids, not in Wowhead's Forever database yet (ids are shared, the tooltip falls back to Classic's).
        new(6802, "Sword of Omen"), new(10823, "Vanquisher's Sword"), new(10758, "X'caliboar"), new(10768, "Boar Champion's Belt"),
        new(10761, "Coldrage Dagger"), new(6830, "Bonebiter"), new(15411, "Mark of Fordring"),
        new(22234, "Mantle of Lost Hope"), new(11803, "Force of Magma"), new(11802, "Lavacrest Leggings"), new(11623, "Spritecaster Cape"),
        new(11626, "Blackveil Cape"), new(11624, "Kentic Amice"), new(22240, "Greaves of Withering Despair"), new(11726, "Savage Gladiator Chain"),
        new(11686, "Girdle of Beastial Fury"), new(11662, "Ban'thok Sash"), new(11816, "Angerforge's Battle Axe"), new(11819, "Second Wind"),
        new(11822, "Omnicast Boots"), new(11742, "Wayfarer's Knapsack"), new(11735, "Ragefury Eyepatch"), new(11841, "Senior Designer's Pantaloons"),
        new(22241, "Dark Warder's Pauldrons"), new(11832, "Burst of Knowledge"), new(11925, "Ghostshroud"), new(11926, "Deathdealer Breastplate"),
        new(12553, "Swiftwalker Boots"), new(12554, "Hands of the Exalted Herald"), new(11815, "Hand of Justice"), new(22204, "Wristguards of Renown"),
        new(11933, "Imperial Jewel"), new(11928, "Thaurissan's Royal Scepter"), new(11932, "Guiding Stave of Wisdom"), new(11931, "Dreadforge Retaliator"),
        new(11924, "Robes of the Royal Crown"), new(9379, "Sang'thraze the Deflector"), new(9470, "Bad Mojo Mask"), new(9476, "Big Bad Pauldrons"),
        new(9477, "The Chief's Enforcer"), new(9478, "Ripsaw"), new(9479, "Embrace of the Lycan"), new(9480, "Eyegouger"),
        new(12470, "Sandstalker Ankleguards"), new(18082, "Zum'rah's Vexing Cane"),
        new(17713, "Blackstone Ring"), new(17719, "Inventor's Focal Sword"), new(17730, "Gatorbite Axe"), new(17732, "Rotgrip Mantle"),
    ]);

    public static readonly IReadOnlyList<ReadyMadeList> All = [RareAndFun, LevelingLegends];

    public bool Contains(int id) => Items.Any(i => i.Id == id);
}
