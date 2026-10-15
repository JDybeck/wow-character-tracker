using System.Text.Json;
using System.Text.Json.Serialization;

namespace WoWCharacterTracker;

class WebhookInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
}

// Per tab: which folder, which webhook and what to post.
class FlavorSettings
{
    public string? Path { get; set; }
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

    // Per kind of event: take a screenshot in game, and attach it to the Discord post.
    public Dictionary<string, bool> Screenshot { get; set; } = [];
    public Dictionary<string, bool> PostScreenshot { get; set; } = [];

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

    public WebhookInfo? ActiveWebhook(Flavor f) => Webhooks.FirstOrDefault(w => w.Id == For(f).ActiveWebhookId);

    // CHARACTERTRACKER_DATA_DIR lets tests run without touching the real settings.
    public static string Dir { get; } = Environment.GetEnvironmentVariable("CHARACTERTRACKER_DATA_DIR") is { Length: > 0 } d
        ? d
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

        // New tabs start out posting to the first webhook, so adding one webhook is enough.
        foreach (var f in WoWCharacterTracker.Flavors.All)
        {
            var s = config.For(f);
            if (config.ActiveWebhook(f) is null && config.Webhooks.Count > 0)
            {
                s.ActiveWebhookId = config.Webhooks[0].Id;
                changed = true;
            }
        }

        if (changed) config.Save();
        return config;
    }

    public void Save() => File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
}
