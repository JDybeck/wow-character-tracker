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

    // The user's own notes per character (by GUID), written on the character's Notes tab or in game,
    // and when each was last written (unix seconds; kept for a cleared note too, so clearing wins).
    public Dictionary<string, string> CharacterNotes { get; set; } = [];
    public Dictionary<string, long> CharacterNotesAt { get; set; } = [];

    public string NoteOf(string guid) => CharacterNotes.TryGetValue(guid, out var n) ? n : "";

    public void SetNote(string guid, string text, long at)
    {
        if (string.IsNullOrWhiteSpace(text)) CharacterNotes.Remove(guid);
        else CharacterNotes[guid] = text;
        CharacterNotesAt[guid] = at;
    }

    // A note from the game counts only when it was written after the app's.
    public bool TakeNewerNote(GameNote note)
    {
        if (note.At <= CharacterNotesAt.GetValueOrDefault(note.Guid)) return false;
        SetNote(note.Guid, note.Text, note.At);
        return true;
    }

    public IEnumerable<GameNote> Notes() =>
        CharacterNotes.Keys.Union(CharacterNotesAt.Keys).Select(g => new GameNote(g, NoteOf(g), CharacterNotesAt.GetValueOrDefault(g)));

    public IReadOnlyCollection<string> TagsOf(string guid) =>
        CharacterTags.TryGetValue(guid, out var t) ? t : [];

    // Open with World of Warcraft: a tray icon starts at login and opens the window when WoW starts.
    // Null until the app has run once; it then turns this on.
    public bool? OpenWithWow { get; set; }

    // With Open with World of Warcraft on: hide the window again when the game closes.
    public bool HideWhenGameCloses { get; set; } = true;

    // Parts of the character card (Post to Discord) the user turned off. Guild is off unless picked.
    public List<string> CardHidden { get; set; } = ["guild"];

    // Games the user turned off (flavor keys): not looked for, not read, no addon installed or
    // updated there. Their tab stays, with a way to turn them back on.
    public List<string> TurnedOff { get; set; } = [];
    public bool IsOn(Flavor flavor) => !TurnedOff.Contains(flavor.Key);

    // Slides, fades and the tab orb (Ui/Motion). Off: everything switches instantly.
    public bool Animations { get; set; } = true;

    // Where the setup checks and the update banner sit (Settings menu): "right" (above the Discord
    // panel, the default), "top" (over the pages) or "bottom" (under them). Anything else reads as right.
    public string SetupPlacement { get; set; } = "right";
    public static readonly string[] SetupPlaces = ["right", "top", "bottom"];
    public string SetupPlace => SetupPlaces.Contains(SetupPlacement) ? SetupPlacement : "right";

    // The first-run tour (ViewModels/IntroViewModel) was finished or closed with "Don't show again".
    public bool IntroDone { get; set; }

    // The tour opens by itself only while the app looks new: no webhook yet, or no game folder picked.
    // People updating from an older version have both, so they don't get it pushed at them.
    public bool WantsIntro => !IntroDone && (Webhooks.Count == 0 || !Flavors.Values.Any(f => !string.IsNullOrEmpty(f.Path)));

    // Stream overlay (a localhost page for OBS) and stream text files, off until turned on.
    public bool StreamEnabled { get; set; }
    public int StreamPort { get; set; } = 8721;
    public StreamOptions StreamOptions { get; set; } = new();

    // The color theme picked per game tab (flavor key -> Theme option key); missing = the game's default.
    public Dictionary<string, string> Themes { get; set; } = [];

    // How the character list is sorted, per game tab (flavor key -> "key:asc"/"key:desc", ViewModels/CharacterSort).
    public Dictionary<string, string> CharacterSort { get; set; } = [];

    // Forever characters (GUIDs) shown with their standard race portrait instead of the in-game one.
    public List<string> StandardPortraits { get; set; } = [];

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
            Flavors[f.Key] = s = f.Defaults();
        return s;
    }

    public List<WebhookInfo> ActiveWebhooks(Flavor f)
    {
        var ids = For(f).ActiveWebhookIds;
        if (ids is null) return Webhooks.Take(1).ToList();
        return Webhooks.Where(w => ids.Contains(w.Id)).ToList();
    }

    // The tab's webhooks that take this character: only those for its ruleset (games with rulesets)
    // and only those that post for one of its tags. Events and posts from the app both go by this.
    public List<WebhookInfo> WebhooksFor(Flavor f, string guid, string ruleset, bool hardcore)
    {
        var tags = TagsOf(guid);
        return ActiveWebhooks(f)
            .Where(w => !f.HasRulesets || w.AcceptsRuleset(ruleset, hardcore))
            .Where(w => w.AcceptsTags(tags))
            .ToList();
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
            // Kept as config.json.broken, not overwritten by the next save: it has every webhook in it,
            // and a hand edit with a typo is easy to fix and put back.
            File.Move(FilePath, FilePath + ".broken", overwrite: true);
            Log.Warn($"Your settings file couldn't be read, so the app started with default settings. The old one is saved as {FilePath}.broken: fix it and rename it back to config.json (with the app closed) to get your webhooks and settings back.");
            config = new Config();
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

        var stream = config.StreamOptions;
        if (!stream.HasBattlegrounds)
        {
            if (!stream.HighlightKinds.Contains("bg")) stream.HighlightKinds.Add("bg");
            if (!stream.AlertKinds.Contains("bg")) stream.AlertKinds.Add("bg");
            stream.HasBattlegrounds = true;
            changed = true;
        }
        if (!stream.HasKeyRuns)
        {
            if (!stream.HighlightKinds.Contains("keyrun")) stream.HighlightKinds.Add("keyrun");
            if (!stream.AlertKinds.Contains("keyrun")) stream.AlertKinds.Add("keyrun");
            stream.HasKeyRuns = true;
            changed = true;
        }

        if (changed) config.Save();
        return config;
    }

    public void Save() => Files.WriteAtomic(FilePath, JsonSerializer.Serialize(this, Json));
}
