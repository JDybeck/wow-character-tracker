using System.Text.RegularExpressions;

namespace WoWCharacterTracker.Services;

// Which group of the Followed items list an item goes in, from its Wowhead tooltip text. The slot has
// a line of its own ("Back", "Main Hand   Sword", "Mount (Account-wide)"); a whole line is matched, so
// an item's name (Head of Onyxia) doesn't count. A recipe also carries what it makes, so it goes first.
static partial class ItemCategories
{
    public const string Weapons = "Weapons", Armor = "Armor", Jewelry = "Jewelry and trinkets", Mounts = "Mounts", Other = "Other";

    // In the order the list shows them.
    public static readonly string[] All = [Weapons, Armor, Jewelry, Mounts, Other];

    public static string Of(string? tooltip)
    {
        if (tooltip is null || tooltip.Contains("Teaches you how to make")) return Other;
        if (MountLine().IsMatch(tooltip)) return Mounts;
        if (ShieldLine().IsMatch(tooltip)) return Armor;
        if (WeaponLine().IsMatch(tooltip)) return Weapons;
        if (ArmorLine().IsMatch(tooltip)) return Armor;
        return JewelryLine().IsMatch(tooltip) ? Jewelry : Other;
    }

    [GeneratedRegex(@"^Mount\b.*$", RegexOptions.Multiline)]
    private static partial Regex MountLine();
    [GeneratedRegex(@"^Off Hand\s+Shield\s*$", RegexOptions.Multiline)]
    private static partial Regex ShieldLine();
    [GeneratedRegex(@"^(Main Hand|Off Hand|One-Hand|Two-Hand|Ranged|Thrown)(\s{2,}.*)?\s*$", RegexOptions.Multiline)]
    private static partial Regex WeaponLine();
    [GeneratedRegex(@"^(Head|Shoulder|Back|Chest|Shirt|Tabard|Wrist|Hands|Waist|Legs|Feet|Held In Off-hand)(\s{2,}.*)?\s*$", RegexOptions.Multiline)]
    private static partial Regex ArmorLine();
    [GeneratedRegex(@"^(Neck|Finger|Trinket)(\s{2,}.*)?\s*$", RegexOptions.Multiline)]
    private static partial Regex JewelryLine();
}
