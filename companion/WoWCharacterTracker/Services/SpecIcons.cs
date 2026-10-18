namespace WoWCharacterTracker.Services;

// The spec's own icon (Wowhead icon names, Wowhead.IconUrl), by class and spec: Retail's specs and
// Forever's classic tree names (Feral Combat, Combat). Keyed by class too, since Frost, Holy,
// Protection and Restoration are specs of more than one class.
static class SpecIcons
{
    static readonly Dictionary<string, string> Icons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DEATHKNIGHT:Blood"] = "spell_deathknight_bloodpresence",
        ["DEATHKNIGHT:Frost"] = "spell_deathknight_frostpresence",
        ["DEATHKNIGHT:Unholy"] = "spell_deathknight_unholypresence",
        ["DEMONHUNTER:Havoc"] = "ability_demonhunter_specdps",
        ["DEMONHUNTER:Vengeance"] = "ability_demonhunter_spectank",
        ["DEMONHUNTER:Devourer"] = "classicon_demonhunter_void", // Midnight's void spec
        ["DRUID:Balance"] = "spell_nature_starfall",
        ["DRUID:Feral"] = "ability_druid_catform",
        ["DRUID:Feral Combat"] = "ability_druid_catform",
        ["DRUID:Guardian"] = "ability_racial_bearform",
        ["DRUID:Restoration"] = "spell_nature_healingtouch",
        ["EVOKER:Devastation"] = "classicon_evoker_devastation",
        ["EVOKER:Preservation"] = "classicon_evoker_preservation",
        ["EVOKER:Augmentation"] = "classicon_evoker_augmentation",
        ["HUNTER:Beast Mastery"] = "ability_hunter_bestialdiscipline",
        ["HUNTER:Marksmanship"] = "ability_hunter_focusedaim",
        ["HUNTER:Survival"] = "ability_hunter_camouflage",
        ["MAGE:Arcane"] = "spell_holy_magicalsentry",
        ["MAGE:Fire"] = "spell_fire_firebolt02",
        ["MAGE:Frost"] = "spell_frost_frostbolt02",
        ["MONK:Brewmaster"] = "spell_monk_brewmaster_spec",
        ["MONK:Mistweaver"] = "spell_monk_mistweaver_spec",
        ["MONK:Windwalker"] = "spell_monk_windwalker_spec",
        ["PALADIN:Holy"] = "spell_holy_holybolt",
        ["PALADIN:Protection"] = "ability_paladin_shieldofthetemplar",
        ["PALADIN:Retribution"] = "spell_holy_auraoflight",
        ["PRIEST:Discipline"] = "spell_holy_powerwordshield",
        ["PRIEST:Holy"] = "spell_holy_guardianspirit",
        ["PRIEST:Shadow"] = "spell_shadow_shadowwordpain",
        ["ROGUE:Assassination"] = "ability_rogue_deadlybrew",
        ["ROGUE:Outlaw"] = "ability_rogue_waylay",
        ["ROGUE:Combat"] = "ability_backstab",
        ["ROGUE:Subtlety"] = "ability_stealth",
        ["SHAMAN:Elemental"] = "spell_nature_lightning",
        ["SHAMAN:Enhancement"] = "spell_shaman_improvedstormstrike",
        ["SHAMAN:Restoration"] = "spell_nature_magicimmunity",
        ["WARLOCK:Affliction"] = "spell_shadow_deathcoil",
        ["WARLOCK:Demonology"] = "spell_shadow_metamorphosis",
        ["WARLOCK:Destruction"] = "spell_shadow_rainoffire",
        ["WARRIOR:Arms"] = "ability_warrior_savageblow",
        ["WARRIOR:Fury"] = "ability_warrior_innerrage",
        ["WARRIOR:Protection"] = "ability_warrior_defensivestance",
    };

    // The icon name, or null for a spec this doesn't know (the row shows the role icon then).
    public static string? Of(string classFile, string spec) =>
        spec.Length > 0 && Icons.TryGetValue($"{classFile}:{spec}", out var icon) ? icon : null;
}
