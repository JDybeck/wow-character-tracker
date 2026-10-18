namespace WoWCharacterTracker.Services;

// What kind of place a zone is, for the icon next to a character's last zone. The addon says
// dungeon, raid, pvp, rest (an inn or a city) or zone; a resting spot is a city when it's one of the
// capitals, otherwise a town. Data from before 0.20.0 has no type: only capitals are known then.
static class Places
{
    public sealed record Kind(string Name, string Icon);

    static readonly Kind Dungeon = new("Dungeon", "achievement_dungeon_classicdungeonmaster");
    static readonly Kind Raid = new("Raid", "achievement_boss_ragnaros");
    static readonly Kind Battleground = new("Battleground", "achievement_bg_winwsg");
    static readonly Kind City = new("City", "ability_townwatch");
    static readonly Kind Town = new("Town", "inv_misc_rune_01");
    static readonly Kind Zone = new("Zone", "inv_misc_map_01");

    // Capital cities across Classic and Retail, as GetRealZoneText names them.
    static readonly HashSet<string> Capitals = new(StringComparer.OrdinalIgnoreCase)
    {
        "Stormwind City", "Ironforge", "Darnassus", "The Exodar", "Orgrimmar", "Thunder Bluff", "Undercity",
        "Silvermoon City", "Shattrath City", "Dalaran", "Boralus", "Dazar'alor", "Oribos", "Valdrakken", "Dornogal",
    };

    public static Kind? Of(string zone, string zoneType) => zoneType switch
    {
        "dungeon" => Dungeon,
        "raid" => Raid,
        "pvp" => Battleground,
        _ when Capitals.Contains(zone) => City,
        "rest" => Town,
        "zone" => Zone,
        _ => null,
    };
}
