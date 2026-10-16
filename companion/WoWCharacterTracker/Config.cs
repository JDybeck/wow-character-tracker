using System.Text.Json;
using System.Text.Json.Serialization;

namespace WoWCharacterTracker;

class WebhookInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";

    // Forever rulesets this webhook gets posts for. Null means all of them.
    public List<string>? ForeverRulesets { get; set; }

    public static readonly string[] AllRulesets = ["Normal", "Hardcore", "Self-found"];

    // Character tags this webhook posts for. Null or empty means every character ("standard").
    public List<string>? Tags { get; set; }

    public bool AcceptsTags(IReadOnlyCollection<string> characterTags) =>
        Tags is not { Count: > 0 } || Tags.Any(characterTags.Contains);

    // A character can be both hardcore and self-found ("Hardcore, Self-found"); either one counts.
    public bool AcceptsRuleset(Entry e)
    {
        if (ForeverRulesets is null) return true;
        var ruleset = e.Ruleset.Length > 0 ? e.Ruleset : e.Hardcore ? "Hardcore" : "Normal";
        return ruleset.Split(", ").Any(ForeverRulesets.Contains);
    }
}

// Per tab: which folder, which webhooks and what to post.
class FlavorSettings
{
    public string? Path { get; set; }

    // Webhooks this tab posts to. Null means never chosen: the first webhook is used.
    // An empty list means the user turned them all off.
    public List<string>? ActiveWebhookIds { get; set; }

    // From before several webhooks could be active. Moved into ActiveWebhookIds on load.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActiveWebhookId { get; set; }

    // Level-ups: up to LevelSplit, post every LevelEveryBefore levels; after that every LevelEveryAfter.
    public bool PostLevels { get; set; } = true;
    public int LevelSplit { get; set; } = 30;
    public int LevelEveryBefore { get; set; } = 5;
    public int LevelEveryAfter { get; set; } = 1;

    public bool PostRare { get; set; }
    public bool PostEpic { get; set; }   // epic and legendary
    public bool PostMounts { get; set; } = true;
    public bool PostBosses { get; set; }
    public bool PostDeaths { get; set; }

    // Boss kills aren't posted when someone in the group matches this regex (e.g. friends who run
    // the tracker too, so the same kill isn't posted several times). Empty means never skip.
    public string SkipBossGroupRegex { get; set; } = "";

    // Forever has surnames. Which name posts use: "full" (Ronnie Raket), "first" or "last".
    public string NameStyle { get; set; } = "full";

    // The member that matches, or null.
    public GroupMember? SkipBossBecause(Entry e)
    {
        if (string.IsNullOrWhiteSpace(SkipBossGroupRegex)) return null;
        try
        {
            var re = new System.Text.RegularExpressions.Regex(SkipBossGroupRegex,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
            return e.Members.FirstOrDefault(m => re.IsMatch(m.Name) || re.IsMatch(m.FullName));
        }
        catch (ArgumentException) { return null; } // invalid regex: the settings page says so
    }

    // Per kind of event: take a screenshot in game, and attach it to the Discord post.
    public Dictionary<string, bool> Screenshot { get; set; } = [];
    public Dictionary<string, bool> PostScreenshot { get; set; } = [];
    public bool HideChatInShots { get; set; } = true; // chat made see-through while a screenshot is taken

    // Characters removed in the app: guid -> when. A character played after that shows up again.
    public Dictionary<string, long> Removed { get; set; } = [];

    public bool Shoots(string kind) => Screenshot.GetValueOrDefault(kind);
    public bool PostsShot(string kind) => Shoots(kind) && PostScreenshot.GetValueOrDefault(kind);

    public bool LevelWanted(int level)
    {
        if (!PostLevels) return false;
        var every = Math.Max(1, level <= LevelSplit ? LevelEveryBefore : LevelEveryAfter);
        return level % every == 0;
    }

    public static FlavorSettings DefaultsFor(Flavor f) => f.Defaults();
}

// Settings remembered between runs, in %LOCALAPPDATA%\WoWCharacterTracker\config.json.
class Config
{
    public List<WebhookInfo> Webhooks { get; set; } = [];
    public Dictionary<string, FlavorSettings> Flavors { get; set; } = [];
    public string? SelectedFlavor { get; set; }

    // Tags the user made ("Main", "Alts", "Guild"...), and which characters (by GUID) have which.
    public List<string> Tags { get; set; } = [];
    public Dictionary<string, List<string>> CharacterTags { get; set; } = [];

    public IReadOnlyCollection<string> TagsOf(string guid) =>
        CharacterTags.TryGetValue(guid, out var t) ? t : [];

    // Open with World of Warcraft: a tray icon starts at login and opens the window when WoW starts.
    // Null until the app has run once; it then turns this on.
    public bool? OpenWithWow { get; set; }

    // Parts of the character card (Post to Discord) the user turned off. Guild is off unless picked.
    public List<string> CardHidden { get; set; } = ["guild"];

    // Older name, from when the window itself started with Windows. Moved on load.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? StartWithWindows { get; set; }

    // Older versions: one WoW folder (retail only) and one webhook. Moved on load.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WowPath { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WebhookUrl { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActiveWebhookId { get; set; }

    public FlavorSettings For(Flavor f)
    {
        if (!Flavors.TryGetValue(f.Key, out var s))
            Flavors[f.Key] = s = FlavorSettings.DefaultsFor(f);
        return s;
    }

    public List<WebhookInfo> ActiveWebhooks(Flavor f)
    {
        var ids = For(f).ActiveWebhookIds;
        if (ids is null) return Webhooks.Take(1).ToList();
        return Webhooks.Where(w => ids.Contains(w.Id)).ToList();
    }

    public bool IsActive(Flavor f, WebhookInfo w) => ActiveWebhooks(f).Contains(w);

    public void SetActive(Flavor f, WebhookInfo w, bool active)
    {
        var s = For(f);
        s.ActiveWebhookIds ??= ActiveWebhooks(f).Select(x => x.Id).ToList();
        s.ActiveWebhookIds.Remove(w.Id);
        if (active) s.ActiveWebhookIds.Add(w.Id);
    }

    // CHARACTERTRACKER_DATA_DIR lets tests run without touching the real settings.
    public static bool IsTest { get; } = Environment.GetEnvironmentVariable("CHARACTERTRACKER_DATA_DIR") is { Length: > 0 };

    public static string Dir { get; } = IsTest
        ? Environment.GetEnvironmentVariable("CHARACTERTRACKER_DATA_DIR")!
        : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WoWCharacterTracker");

    static string FilePath => System.IO.Path.Combine(Dir, "config.json");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Config Load()
    {
        Directory.CreateDirectory(Dir);
        Config config;
        try
        {
            config = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath)) ?? new Config()
                : new Config();
        }
        catch (JsonException)
        {
            config = new Config(); // a broken file just means we ask again
        }

        var changed = false;
        if (!string.IsNullOrWhiteSpace(config.WebhookUrl))
        {
            var moved = new WebhookInfo { Name = "Discord", Url = config.WebhookUrl };
            config.Webhooks.Add(moved);
            config.ActiveWebhookId ??= moved.Id;
            config.WebhookUrl = null;
            changed = true;
        }
        if (config.ActiveWebhookId is not null)
        {
            config.For(WoWCharacterTracker.Flavors.Retail).ActiveWebhookId ??= config.ActiveWebhookId;
            config.ActiveWebhookId = null;
            changed = true;
        }
        if (!string.IsNullOrWhiteSpace(config.WowPath))
        {
            config.For(WoWCharacterTracker.Flavors.Retail).Path ??= System.IO.Path.Combine(config.WowPath, "_retail_");
            config.WowPath = null;
            changed = true;
        }
        if (config.StartWithWindows is not null)
        {
            config.OpenWithWow ??= config.StartWithWindows;
            config.StartWithWindows = null;
            changed = true;
        }
        foreach (var s in config.Flavors.Values.Where(s => s.ActiveWebhookId is not null))
        {
            s.ActiveWebhookIds ??= [s.ActiveWebhookId!];
            s.ActiveWebhookId = null;
            changed = true;
        }

        if (changed) config.Save();
        return config;
    }

    public void Save() => File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
}
