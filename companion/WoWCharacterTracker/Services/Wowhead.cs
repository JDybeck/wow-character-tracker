using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace WoWCharacterTracker.Services;

// Names, icons and tooltip text for items and currencies, from Wowhead's public tooltip endpoint
// (the one their site tooltips use). Results are kept in wowhead.json next to the config, so each
// thing is only looked up once.
static class Wowhead
{
    // Tooltip is the plain text; Html the tooltip as Wowhead sends it (colors and layout, for the item picture).
    public sealed record Info(string Name, string Icon, int Quality, string Tooltip, string? Html = null);

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    static readonly ConcurrentDictionary<string, Task<Info?>> Lookups = new();
    static readonly object DiskLock = new();
    // Bags and banks mean hundreds of items at once; a few lookups at a time keeps Wowhead happy.
    static readonly SemaphoreSlim Parallel = new(6);
    static Dictionary<string, Info>? disk;
    static string CachePath => Path.Combine(Config.Dir, "wowhead.json");

    public static string IconUrl(string icon, string size = "medium") =>
        $"https://wow.zamimg.com/images/wow/icons/{size}/{icon}.jpg";

    // type is "item" or "currency".
    public static Task<Info?> GetAsync(string type, int id, Flavor flavor)
    {
        if (id <= 0) return Task.FromResult<Info?>(null);
        var key = $"{flavor.WowheadPath}{type}/{id}";
        // On a worker thread: the first lookup reads the whole cache file and every hit parses its
        // tooltip HTML, which on the UI thread froze the window with thousands of items.
        return Lookups.GetOrAdd(key, k => Task.Run(() => FetchAsync(k)));
    }

    static async Task<Info?> FetchAsync(string key)
    {
        lock (DiskLock)
        {
            disk ??= LoadDisk();
            // Entries cached before the HTML was kept are fetched once more.
            // The text is made again from the HTML, so cached entries get fixes to ToText too.
            if (disk.TryGetValue(key, out var cached) && cached.Html is not null) return cached with { Tooltip = ToText(cached.Html) };
        }
        await Parallel.WaitAsync();
        try
        {
            string json;
            try { json = await Http.GetStringAsync($"https://nether.wowhead.com/{TooltipPath(key)}"); }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound && key.StartsWith("forever/"))
            {
                // Wowhead's Forever database only has part of the items so far (no raid gear yet);
                // Forever's items are mostly Classic's, so Classic's tooltip stands in.
                json = await Http.GetStringAsync($"https://nether.wowhead.com/{TooltipPath("classic/" + key["forever/".Length..])}");
            }
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            var quality = root.TryGetProperty("quality", out var q) && q.ValueKind == JsonValueKind.Number ? q.GetInt32() : -1;
            var info = new Info(Str("name"), Str("icon"), quality, ToText(Str("tooltip")), Str("tooltip"));
            if (info.Name.Length == 0) return null;
            lock (DiskLock) disk![key] = info;
            SaveSoon();
            return info;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Lookups.TryRemove(key, out _); // try again another time
            lock (DiskLock) return disk!.GetValueOrDefault(key); // an older cached entry still beats nothing
        }
        finally { Parallel.Release(); }
    }

    // The cache file is written once lookups go quiet, not after each of hundreds of items.
    static int saveVersion;
    static async void SaveSoon()
    {
        var mine = Interlocked.Increment(ref saveVersion);
        await Task.Delay(2000);
        if (mine != saveVersion) return;
        lock (DiskLock)
        {
            try { File.WriteAllText(CachePath, JsonSerializer.Serialize(disk)); } catch (IOException) { }
        }
    }

    // "classic/item/19019" -> "classic/tooltip/item/19019"
    static string TooltipPath(string key)
    {
        var parts = key.Split('/');
        return parts.Length == 3 ? $"{parts[0]}/tooltip/{parts[1]}/{parts[2]}" : $"tooltip/{key}";
    }

    static Dictionary<string, Info> LoadDisk()
    {
        try { return File.Exists(CachePath) ? JsonSerializer.Deserialize<Dictionary<string, Info>>(File.ReadAllText(CachePath)) ?? [] : []; }
        catch (Exception) { return []; }
    }

    // A Forever build in Wowhead's talent calculator: talent-calc/<class>/v2<ranks per talent, per tree>
    // (trees joined by "-", so "v2--05203021201" is 16 points in the third tree). Checked against
    // Wowhead's own "Copy talents" link. Without a code it opens the empty calculator for the class.
    public static string ForeverTalentUrl(string classFile, string talentCalc) =>
        $"https://www.wowhead.com/forever/talent-calc/{classFile.ToLowerInvariant()}" + (talentCalc.Length > 0 ? "/v2" + talentCalc : "");

    // A character's build in Wowhead's talent calculator, or null without one. Forever: Wowhead's
    // classic-style calculator code. Retail: the game's own loadout code.
    public static string? TalentCalcUrl(Flavor flavor, string classFile, string talentCalc, string talentExport) =>
        flavor == Flavors.Forever
            ? (talentCalc.Length > 0 ? ForeverTalentUrl(classFile, talentCalc) : null)
            : (talentExport.Length > 0 ? $"https://www.wowhead.com/talent-calc/blizzard/{Uri.EscapeDataString(talentExport)}" : null);

    // The tooltip comes as HTML; this keeps the text, one line per row (see TooltipHtml).
    static string ToText(string html) => html.Length == 0 ? "" : TooltipHtml.ToText(html);

    // Profession icons by English name.
    public static readonly Dictionary<string, string> ProfessionIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Alchemy"] = "trade_alchemy", ["Blacksmithing"] = "trade_blacksmithing", ["Enchanting"] = "trade_engraving",
        ["Engineering"] = "trade_engineering", ["Herbalism"] = "trade_herbalism", ["Leatherworking"] = "trade_leatherworking",
        ["Mining"] = "trade_mining", ["Skinning"] = "inv_misc_pelt_wolf_01", ["Tailoring"] = "trade_tailoring",
        ["Inscription"] = "inv_inscription_tradeskill01", ["Jewelcrafting"] = "inv_misc_gem_01",
        ["Cooking"] = "inv_misc_food_15", ["Fishing"] = "trade_fishing", ["First Aid"] = "spell_holy_sealofsacrifice",
    };

    // Character sheet stat icons by the names Character.lua's getStats uses (the buff or ability that
    // stands for each).
    public static readonly Dictionary<string, string> StatIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Strength"] = "spell_nature_strength", ["Agility"] = "spell_holy_blessingofagility",
        ["Stamina"] = "spell_nature_unyeildingstamina", ["Intellect"] = "spell_holy_magicalsentry",
        ["Spirit"] = "spell_holy_divinespirit", ["Armor"] = "inv_shield_06", ["Attack Power"] = "ability_warrior_battleshout",
        ["Spell Power"] = "spell_fire_flamebolt", ["Crit"] = "ability_criticalstrike", ["Haste"] = "spell_nature_bloodlust",
        ["Mastery"] = "spell_holy_championsbond", ["Versatility"] = "ability_paladin_artofwar",
    };

    // Rating icons by the names Format.Ratings uses (the keystone, the arena and battleground achievements).
    public static readonly Dictionary<string, string> RatingIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Mythic+"] = "inv_relics_hourglass", ["2v2"] = "achievement_arena_2v2_7", ["3v3"] = "achievement_arena_3v3_7",
        ["RBG"] = "achievement_bg_killxenemies_generalsroom", ["Solo Shuffle"] = "achievement_featsofstrength_gladiator_10",
        ["Blitz"] = "achievement_bg_masterofallbgs",
    };
}
