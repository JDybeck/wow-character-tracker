namespace WoWCharacterTracker.Models;

// WoW's colors as 0xRRGGBB, in one place: item qualities, classes (by the game's class file and by
// Wowhead's c1-c13 tooltip classes) and the rest of Wowhead's tooltip colors. Free of WPF, so posts
// and the overlay use them too; Ui/ClassColors makes (shared, frozen) brushes of them.
static class GameColors
{
    // Poor, common, uncommon, rare, epic, legendary, artifact, heirloom, WoW Token.
    static readonly int[] Qualities = [0x9D9D9D, 0xFFFFFF, 0x1EFF00, 0x0070DD, 0xA335EE, 0xFF8000, 0xE6CC80, 0x00CCFF, 0x00CCFF];

    public static int? Quality(int quality) => quality >= 0 && quality < Qualities.Length ? Qualities[quality] : null;

    // "#A335EE" for epic, null when it isn't a quality.
    public static string? QualityHex(int quality) => Quality(quality) is int c ? Hex(c) : null;

    // In the game's class id order (1 Warrior ... 13 Evoker), which is also Wowhead's c1-c13. As RAID_CLASS_COLORS.
    static readonly (string File, int Color)[] Classes =
    [
        ("WARRIOR", 0xC69B6D), ("PALADIN", 0xF48CBA), ("HUNTER", 0xAAD372), ("ROGUE", 0xFFF468), ("PRIEST", 0xFFFFFF),
        ("DEATHKNIGHT", 0xC41E3A), ("SHAMAN", 0x0070DD), ("MAGE", 0x3FC7EB), ("WARLOCK", 0x8788EE), ("MONK", 0x00FF98),
        ("DRUID", 0xFF7C0A), ("DEMONHUNTER", 0xA330C9), ("EVOKER", 0x33937F),
    ];
    static readonly Dictionary<string, int> ByClassFile = Classes.ToDictionary(c => c.File, c => c.Color, StringComparer.OrdinalIgnoreCase);

    // A class's color, or null for an unknown class. Any letter case works.
    public static int? Class(string classFile) => ByClassFile.TryGetValue(classFile, out var c) ? c : null;

    // Wowhead's tooltip classes: q0-q8 qualities, q the item level's yellow, c1-c13 classes, toys and coins.
    public static readonly IReadOnlyDictionary<string, int> Wowhead = BuildWowhead();

    static Dictionary<string, int> BuildWowhead()
    {
        var colors = new Dictionary<string, int> { ["q"] = 0xFFD100, ["toycolor"] = 0x68CCEF,
            ["moneygold"] = 0xFFD700, ["moneysilver"] = 0xC7C7CF, ["moneycopper"] = 0xC87533 };
        for (var i = 0; i < Qualities.Length; i++) colors[$"q{i}"] = Qualities[i];
        for (var i = 0; i < Classes.Length; i++) colors[$"c{i + 1}"] = Classes[i].Color;
        return colors;
    }

    public static string Hex(int rgb) => $"#{rgb:X6}";
}
