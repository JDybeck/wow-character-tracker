using System.Collections.Concurrent;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace WoWCharacterTracker.Services;

// Names, icons and tooltip text for items and currencies, from Wowhead's public tooltip endpoint
// (the one their site tooltips use). Results are kept in wowhead.json next to the config, so each
// thing is only looked up once.
static class Wowhead
{
    // Tooltip is the plain text; Html the tooltip as Wowhead sends it (colors and layout, for the item picture).
    public sealed record Info(string Name, string Icon, int Quality, string Tooltip, string? Html = null);

    static readonly HttpClient Http = Web.Client(TimeSpan.FromSeconds(10));
    static readonly ConcurrentDictionary<string, Task<Info?>> Lookups = new();
    static readonly object DiskLock = new();
    // Bags and banks mean hundreds of items at once; a few lookups at a time keeps Wowhead happy.
    static readonly SemaphoreSlim Parallel = new(6);
    static Dictionary<string, Info>? disk;
    static string CachePath => Path.Combine(Config.Dir, "wowhead.json");
    // The cache is only read by us, so the tooltip HTML goes in as is: the default escaping
    // (< as < and so on) about doubled the file.
    static readonly JsonSerializerOptions CacheJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string IconUrl(string icon, string size = "medium") =>
        $"https://wow.zamimg.com/images/wow/icons/{size}/{icon}.jpg";

    // type is "item", "currency" or "spell".
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
            if (disk.TryGetValue(key, out var cached) && cached.Html is not null) return WithText(cached);
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
            lock (DiskLock) disk![key] = WithoutText(info);
            SaveSoon();
            return info;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Lookups.TryRemove(key, out _); // try again another time
            // An older cached entry still beats nothing.
            lock (DiskLock) return disk!.TryGetValue(key, out var old) ? WithText(old) : null;
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
            try { Files.WriteAtomic(CachePath, SerializeCache(disk!)); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    // The plain text isn't kept in the cache when there's HTML: it's made again from the HTML when
    // read (so cached entries get fixes to ToText too), and it took a tenth of the file and memory.
    static Info WithText(Info cached) => cached.Html is null ? cached : cached with { Tooltip = ToText(cached.Html) };
    static Info WithoutText(Info info) => info.Html is null ? info : info with { Tooltip = "" };

    internal static string SerializeCache(Dictionary<string, Info> cache) => JsonSerializer.Serialize(cache, CacheJson);

    // Old files still carry the text; it's dropped here so it doesn't sit in memory.
    internal static Dictionary<string, Info> DeserializeCache(string json) =>
        (JsonSerializer.Deserialize<Dictionary<string, Info>>(json) ?? [])
            .ToDictionary(kv => kv.Key, kv => WithoutText(kv.Value));

    // "classic/item/19019" -> "classic/tooltip/item/19019"
    static string TooltipPath(string key)
    {
        var parts = key.Split('/');
        return parts.Length == 3 ? $"{parts[0]}/tooltip/{parts[1]}/{parts[2]}" : $"tooltip/{key}";
    }

    static Dictionary<string, Info> LoadDisk()
    {
        try { return File.Exists(CachePath) ? DeserializeCache(File.ReadAllText(CachePath)) : []; }
        catch (Exception) { return []; }
    }

    // An enchant's Wowhead page by its name ("Enchant Ring - Nature's Fury"): the game only gives the
    // name, and Wowhead has no tooltips by enchant id. Uses the search box's suggestions; kept for the
    // session only (the tooltip it leads to is cached like any other). Null when nothing matches.
    static readonly ConcurrentDictionary<string, Task<(string Type, int Id)?>> Searches = new();

    public static Task<(string Type, int Id)?> FindAsync(string name, Flavor flavor) =>
        Searches.GetOrAdd(flavor.WowheadPath + name, _ => Task.Run(() => SearchAsync(name, flavor)));

    static async Task<(string Type, int Id)?> SearchAsync(string name, Flavor flavor)
    {
        await Parallel.WaitAsync();
        try
        {
            var url = $"https://www.wowhead.com/{flavor.WowheadPath}search/suggestions-template?q={Uri.EscapeDataString(name)}";
            return PickByName(await Http.GetStringAsync(url), name);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Searches.TryRemove(flavor.WowheadPath + name, out _); // try again another time
            return null;
        }
        finally { Parallel.Release(); }
    }

    // The result named exactly so: an item first (an enchant's scroll says what it does, plainly), else
    // a spell (Death Knight runes have no item). Anything only partly matching ("Formula: ...") is left out.
    internal static (string Type, int Id)? PickByName(string json, string name)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return null;
        var named = results.EnumerateArray()
            .Where(r => r.TryGetProperty("name", out var n) && string.Equals(n.GetString(), name, StringComparison.OrdinalIgnoreCase))
            .Select(r => (Type: r.GetProperty("type").GetInt32(), Id: r.GetProperty("id").GetInt32()))
            .ToList();
        foreach (var (type, label) in new[] { (3, "item"), (6, "spell") })
            if (named.FirstOrDefault(r => r.Type == type) is { Id: > 0 } hit) return (label, hit.Id);
        return null;
    }

    // A Forever build in Wowhead's talent calculator: talent-calc/<class>/v2<ranks per talent, per tree>
    // (trees joined by "-", so "v2--05203021201" is 16 points in the third tree). Checked against
    // Wowhead's own "Copy talents" link. Without a code it opens the empty calculator for the class.
    public static string ForeverTalentUrl(string classFile, string talentCalc) =>
        $"https://www.wowhead.com/forever/talent-calc/{classFile.ToLowerInvariant()}" + (talentCalc.Length > 0 ? "/v2" + talentCalc : "");

    // A character's build in Wowhead's talent calculator, or null without one. Forever: Wowhead's
    // classic-style calculator code. Retail: the game's own loadout code.
    public static string? TalentCalcUrl(Flavor flavor, string classFile, string talentCalc, string talentExport) =>
        flavor.HasClassicTalents
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

    // Highlight icons by event kind, for lines without a thing of their own (loot and mounts show the
    // item's icon once it's looked up; these stand in until then). Ratings use RatingIcons.
    public static readonly Dictionary<string, string> HighlightIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["boss"] = "achievement_dungeon_classicdungeonmaster", ["loot"] = "inv_misc_bag_10", ["mount"] = "ability_mount_ridinghorse",
        ["duel"] = "ability_dualwield", ["makgora"] = "ability_warrior_challange", ["bg"] = "achievement_bg_winwsg",
        ["death"] = "inv_misc_bone_humanskull_01",
    };
}
