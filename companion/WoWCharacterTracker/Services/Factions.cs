namespace WoWCharacterTracker.Services;

// Alliance or Horde: what the addon recorded (UnitFactionGroup, from 0.26.0), else what the race
// says for older data. "" when neither knows (a Pandaren, Dracthyr or Earthen without a recorded
// faction, a Pandaren who hasn't picked yet: "Neutral", or a Skyborne on a non-English client).
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

    // Forever's Skyborne (race file "Skyborne") come in one variant per side, told apart by the race's
    // name (English client): "High Order Skyborne" (Alliance) and "Windshaper Skyborne" (Horde).
    // High Order and Windshapers are their home reputations, like Darnassus or Thunder Bluff; the
    // sides are still Alliance and Horde (UnitFactionGroup).
    static readonly (string Prefix, string Faction)[] RaceNamePrefixes = [("High Order", Alliance), ("Windshaper", Horde)];

    public static string Of(string recorded, string raceFile, string race = "") =>
        recorded is Alliance or Horde ? recorded
        : AllianceRaces.Contains(raceFile) ? Alliance
        : HordeRaces.Contains(raceFile) ? Horde
        : RaceNamePrefixes.FirstOrDefault(p => race.StartsWith(p.Prefix, StringComparison.OrdinalIgnoreCase)).Faction ?? "";

    // The faction's crest as a Wowhead icon (fetched like every other icon, never shipped).
    public static string? CrestIcon(string faction) => faction switch
    {
        Alliance => "achievement_pvp_a_a",
        Horde => "achievement_pvp_h_h",
        _ => null,
    };
}
