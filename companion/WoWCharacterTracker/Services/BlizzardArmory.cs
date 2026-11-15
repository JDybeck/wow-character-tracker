using System.Globalization;

namespace WoWCharacterTracker.Services;

// Blizzard's own armory for games that have one without a realm in the address (Forever): the page is
// found by name and surname, the picture by the number at the end of the GUID. Both need the region.
// Seen with real characters (2026-11-16): Player-6948-003D4BBD is character 0x3D4BBD = 4017085, its
// picture sits under 4017085 % 256 = 189. A character the armory has no picture for answers 403.
static class BlizzardArmory
{
    // game: Flavor.ArmoryGame ("forever"), region: "eu", "us"... sd: the old character models
    // ("-avatar-sd"), else the new ones; the armory has both for every character with a picture.
    public static string? AvatarUrl(string game, string region, string guid, bool sd = false)
    {
        if (game.Length == 0 || region.Length == 0 || CharacterId(guid) is not { } id) return null;
        return $"https://render.worldofwarcraft.com/{game}-{region}/character/{id % 256}/{id}-avatar{(sd ? "-sd" : "")}.jpg";
    }

    // "Zeb" "Drygbeck" -> .../armory/character/zeb-drygbeck. Without a surname there's no address to make.
    public static string? ProfileUrl(string game, string region, string name, string surname)
    {
        if (game.Length == 0 || region.Length == 0 || name.Length == 0 || surname.Length == 0) return null;
        var slug = Uri.EscapeDataString($"{name}-{surname}".ToLowerInvariant());
        return $"https://worldofwarcraft.blizzard.com/en-us/{game}/{region}/armory/character/{slug}";
    }

    // "Player-6948-003D4BBD" -> 4017085
    internal static long? CharacterId(string guid)
    {
        var parts = guid.Split('-');
        if (parts.Length != 3 || parts[0] != "Player") return null;
        return long.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
    }
}
