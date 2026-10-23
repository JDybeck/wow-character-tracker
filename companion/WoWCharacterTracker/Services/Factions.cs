namespace WoWCharacterTracker.Services;

// Alliance or Horde: what the addon recorded (UnitFactionGroup, from 0.26.0), else what the race
// says for older data. "" when neither knows (a Pandaren, Dracthyr or Earthen without a recorded
// faction, or a Pandaren who hasn't picked yet: "Neutral").
static class Factions
{
    public const string Alliance = "Alliance";
    public const string Horde = "Horde";

    // The races' file names (UnitRace's second value) that only one side can play.
    static readonly HashSet<string> AllianceRaces = new(StringComparer.OrdinalIgnoreCase)
    {
        "Human", "Dwarf", "NightElf", "Gnome", "Draenei", "Worgen",
        "VoidElf", "LightforgedDraenei", "DarkIronDwarf", "KulTiran", "Mechagnome",
    };
    static readonly HashSet<string> HordeRaces = new(StringComparer.OrdinalIgnoreCase)
    {
        "Orc", "Scourge", "Tauren", "Troll", "BloodElf", "Goblin",
        "Nightborne", "HighmountainTauren", "MagharOrc", "ZandalariTroll", "Vulpera",
    };

    public static string Of(string recorded, string raceFile) =>
        recorded is Alliance or Horde ? recorded
        : AllianceRaces.Contains(raceFile) ? Alliance
        : HordeRaces.Contains(raceFile) ? Horde
        : "";

    // The faction's crest as a Wowhead icon (fetched like every other icon, never shipped).
    public static string? CrestIcon(string faction) => faction switch
    {
        Alliance => "achievement_pvp_a_a",
        Horde => "achievement_pvp_h_h",
        _ => null,
    };
}
