namespace WoWCharacterTracker.Services;

// What a character does in a group, for its icon: tank, healer, or which kind of damage, melee,
// ranged (bows and guns) or caster. Retail says tank/healer/damage itself (role); Forever doesn't,
// so there it comes from the top talent tree (spec).
static class Roles
{
    public const string Tank = "tank", Healer = "healer", Melee = "melee", Ranged = "ranged", Caster = "caster";

    static readonly HashSet<string> TankSpecs = new(StringComparer.OrdinalIgnoreCase) { "Protection", "Blood", "Guardian", "Brewmaster", "Vengeance" };
    static readonly HashSet<string> HealerSpecs = new(StringComparer.OrdinalIgnoreCase) { "Holy", "Discipline", "Restoration", "Mistweaver", "Preservation" };
    static readonly HashSet<string> CasterSpecs = new(StringComparer.OrdinalIgnoreCase) { "Shadow", "Balance", "Elemental", "Devastation", "Augmentation" };
    static readonly HashSet<string> CasterClasses = new(StringComparer.OrdinalIgnoreCase) { "MAGE", "WARLOCK", "EVOKER" };

    // role: "TANK", "HEALER", "DAMAGER" (Retail) or "". Returns one of the constants, or "" when
    // there's nothing to go on.
    public static string Of(string role, string classFile, string spec, bool retail) => role.ToUpperInvariant() switch
    {
        "TANK" => Tank,
        "HEALER" => Healer,
        "DAMAGER" => DamageKind(classFile, spec, retail),
        _ when spec.Length == 0 => "", // nothing to go on (Forever before any talent point)
        _ when TankSpecs.Contains(spec) => Tank,
        _ when HealerSpecs.Contains(spec) => Healer,
        _ => DamageKind(classFile, spec, retail),
    };

    // Hunters shoot (Survival went melee on Retail); the casting classes and specs cast; the rest hit things.
    public static string DamageKind(string classFile, string spec, bool retail)
    {
        if (classFile.Equals("HUNTER", StringComparison.OrdinalIgnoreCase))
            return retail && spec.Equals("Survival", StringComparison.OrdinalIgnoreCase) ? Melee : Ranged;
        if (CasterClasses.Contains(classFile) || CasterSpecs.Contains(spec)) return Caster;
        if (classFile.Equals("PRIEST", StringComparison.OrdinalIgnoreCase)) return Caster;
        return Melee;
    }

    public static string Name(string kind) => kind switch
    {
        Tank => "Tank", Healer => "Healer", Melee => "Melee damage", Ranged => "Ranged damage", Caster => "Caster damage", _ => "",
    };

    // Wowhead icon names (Wowhead.IconUrl).
    public static string? Icon(string kind) => kind switch
    {
        Tank => "ability_warrior_defensivestance",
        Healer => "spell_holy_flashheal",
        Melee => "ability_meleedamage",
        Ranged => "inv_weapon_bow_07",
        Caster => "spell_fire_flamebolt",
        _ => null,
    };
}
