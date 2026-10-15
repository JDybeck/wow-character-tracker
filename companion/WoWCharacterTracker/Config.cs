using System.Text.Json;

namespace WoWCharacterTracker;

// Settings remembered between runs, in %LOCALAPPDATA%\WoWCharacterTracker\config.json.
class Config
{
    public string? WebhookUrl { get; set; }
    public string? WowPath { get; set; }

    // CHARACTERTRACKER_DATA_DIR lets tests run without touching the real settings.
    public static string Dir { get; } = Environment.GetEnvironmentVariable("CHARACTERTRACKER_DATA_DIR") is { Length: > 0 } d
        ? d
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WoWCharacterTracker");

    static string FilePath => Path.Combine(Dir, "config.json");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Config Load()
    {
        Directory.CreateDirectory(Dir);
        if (!File.Exists(FilePath)) return new Config();
        try
        {
            return JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath)) ?? new Config();
        }
        catch (JsonException)
        {
            return new Config(); // a broken file just means we ask again
        }
    }

    public void Save() => File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
}
