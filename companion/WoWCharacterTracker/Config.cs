using System.Text.Json;
using System.Text.Json.Serialization;

namespace WoWCharacterTracker;

class WebhookInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
}

// Settings remembered between runs, in %LOCALAPPDATA%\WoWCharacterTracker\config.json.
class Config
{
    public string? WowPath { get; set; }
    public List<WebhookInfo> Webhooks { get; set; } = [];
    public string? ActiveWebhookId { get; set; }

    // Older versions kept a single webhook here. Moved into Webhooks on load.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WebhookUrl { get; set; }

    [JsonIgnore]
    public WebhookInfo? ActiveWebhook => Webhooks.FirstOrDefault(w => w.Id == ActiveWebhookId);

    // CHARACTERTRACKER_DATA_DIR lets tests run without touching the real settings.
    public static string Dir { get; } = Environment.GetEnvironmentVariable("CHARACTERTRACKER_DATA_DIR") is { Length: > 0 } d
        ? d
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WoWCharacterTracker");

    static string FilePath => Path.Combine(Dir, "config.json");

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

        if (!string.IsNullOrWhiteSpace(config.WebhookUrl))
        {
            var moved = new WebhookInfo { Name = "Discord", Url = config.WebhookUrl };
            config.Webhooks.Add(moved);
            config.ActiveWebhookId ??= moved.Id;
            config.WebhookUrl = null;
            config.Save();
        }

        if (config.ActiveWebhook is null && config.Webhooks.Count > 0)
            config.ActiveWebhookId = config.Webhooks[0].Id;

        return config;
    }

    public void Save() => File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
}
