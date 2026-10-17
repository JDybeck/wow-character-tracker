using System.Collections.Concurrent;

namespace WoWCharacterTracker.Services;

// Class colors and the pictures that stand for a character: the race portrait, with the class
// icon as the fallback. Both come from Wowhead's icon CDN.
static class CharacterArt
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    // Class colors as the game uses them (RAID_CLASS_COLORS).
    public static readonly Dictionary<string, int> ClassColors = new()
    {
        ["WARRIOR"] = 0xC69B6D, ["PALADIN"] = 0xF48CBA, ["HUNTER"] = 0xAAD372, ["ROGUE"] = 0xFFF468,
        ["PRIEST"] = 0xFFFFFF, ["DEATHKNIGHT"] = 0xC41E3A, ["SHAMAN"] = 0x0070DD, ["MAGE"] = 0x3FC7EB,
        ["WARLOCK"] = 0x8788EE, ["MONK"] = 0x00FF98, ["DRUID"] = 0xFF7C0A, ["DEMONHUNTER"] = 0xA330C9,
        ["EVOKER"] = 0x33937F,
    };

    const string IconBase = "https://wow.zamimg.com/images/wow/icons/large/";

    // Remembers which portrait URLs exist so a new race we don't know falls back to the class icon.
    static readonly ConcurrentDictionary<string, bool> UrlExists = new();

    public static string? ClassIconUrl(string classFile) =>
        classFile.Length > 0 ? $"{IconBase}classicon_{classFile.ToLowerInvariant()}.jpg" : null;

    // The best picture of the character: on Retail its actual render (from Raider.IO, needs the
    // region), otherwise the race portrait, and the class icon as the last fallback.
    public static async Task<string?> PictureUrlAsync(Flavor flavor, string region, string realm, string name,
        string raceFile, string sex, string classFile)
    {
        if (flavor == Flavors.Retail && await RaiderIo.AvatarUrlAsync(region, realm, name) is { } render) return render;
        return await PictureUrlAsync(raceFile, sex, classFile);
    }

    // The race portrait matches the character best; the class icon is the fallback.
    public static async Task<string?> PictureUrlAsync(string raceFile, string sex, string classFile)
    {
        if (raceFile.Length > 0)
        {
            var portrait = $"{IconBase}race_{raceFile.ToLowerInvariant()}_{sex}.jpg";
            if (await ExistsAsync(portrait)) return portrait;
        }
        return ClassIconUrl(classFile);
    }

    // Forever has no armory to get a render from, so the addon shows the character's own portrait in
    // its check-in shot after login, and the app keeps it here (<data dir>\portraits\<guid>.png). Only
    // used in the app: Discord needs a web address for pictures.
    static string PortraitPath(string guid) => Path.Combine(Config.Dir, "portraits", string.Concat(guid.Split(Path.GetInvalidFileNameChars())) + ".png");

    public static string? LocalPortrait(string guid) => File.Exists(PortraitPath(guid)) ? PortraitPath(guid) : null;

    public static void SavePortrait(string guid, byte[] png)
    {
        try
        {
            var path = PortraitPath(guid);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, png);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    static async Task<bool> ExistsAsync(string url)
    {
        if (UrlExists.TryGetValue(url, out var known)) return known;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var res = await Http.SendAsync(req);
            return UrlExists[url] = res.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false; // don't cache, it may just be a hiccup
        }
    }
}
