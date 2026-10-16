using System.Text.Json;
using System.Text.Json.Serialization;

namespace WoWCharacterTracker.Models;

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

    // With Open with World of Warcraft on: hide the window again when the game closes.
    public bool HideWhenGameCloses { get; set; } = true;

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
            config.For(Models.Flavors.Retail).ActiveWebhookId ??= config.ActiveWebhookId;
            config.ActiveWebhookId = null;
            changed = true;
        }
        if (!string.IsNullOrWhiteSpace(config.WowPath))
        {
            config.For(Models.Flavors.Retail).Path ??= System.IO.Path.Combine(config.WowPath, "_retail_");
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

        foreach (var s in config.Flavors.Values)
        {
            if (s.MPlusSteps.SequenceEqual(FlavorSettings.OldMPlusSteps)) { s.MPlusSteps = [.. FlavorSettings.DefaultMPlusSteps]; changed = true; }
            if (s.PvpSteps.SequenceEqual(FlavorSettings.OldPvpSteps)) { s.PvpSteps = [.. FlavorSettings.DefaultPvpSteps]; changed = true; }
        }

        if (changed) config.Save();
        return config;
    }

    public void Save() => File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
}
