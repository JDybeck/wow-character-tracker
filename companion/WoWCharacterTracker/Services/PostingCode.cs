using System.Text;
using System.Text.Json;

namespace WoWCharacterTracker.Services;

// "What gets posted" as a short text code, so friends can share and sync their settings:
// "WCT1:" followed by the settings as base64 JSON. Webhooks, folders and characters are never
// part of it.
static class PostingCode
{
    const string Prefix = "WCT1:";

    sealed record Settings(
        string Game,
        bool PostLevels, int LevelSplit, int LevelEveryBefore, int LevelEveryAfter, bool PostMaxLevel,
        bool PostRare, bool PostEpic, bool PostMounts, bool PostBosses, bool PostDeaths,
        bool PostDuels, bool PostMakgora, bool PostMPlus, bool PostPvp,
        List<int> MPlusSteps, List<int> PvpSteps, string SkipBossGroupRegex, string NameStyle,
        bool HideChatInShots, Dictionary<string, bool> Screenshot, Dictionary<string, bool> PostScreenshot, bool HideUiInShots = false, bool FirstBossKillOnly = true,
        bool PostKeyRuns = true, int KeyRunMinLevel = 10, bool KeyRunTimedOnly = false);

    public static string Export(FlavorSettings s, Flavor flavor)
    {
        var dto = new Settings(flavor.Key,
            s.PostLevels, s.LevelSplit, s.LevelEveryBefore, s.LevelEveryAfter, s.PostMaxLevel,
            s.PostRare, s.PostEpic, s.PostMounts, s.PostBosses, s.PostDeaths,
            s.PostDuels, s.PostMakgora, s.PostMPlus, s.PostPvp,
            s.MPlusSteps, s.PvpSteps, s.SkipBossGroupRegex, s.NameStyle,
            s.HideChatInShots, s.Screenshot, s.PostScreenshot, s.HideUiInShots, s.FirstBossKillOnly,
            s.PostKeyRuns, s.KeyRunMinLevel, s.KeyRunTimedOnly);
        return Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dto)));
    }

    // Applies a code to the settings. Returns the game the code was made for, or null with a
    // reason when the text isn't a settings code.
    public static (string? Game, string? Problem) Import(string text, FlavorSettings target)
    {
        text = text.Trim();
        var start = text.IndexOf(Prefix, StringComparison.Ordinal);
        if (start < 0) return (null, "That isn't a settings code. Codes start with WCT1:");
        var b64 = new string(text[(start + Prefix.Length)..].TakeWhile(c => char.IsLetterOrDigit(c) || c is '+' or '/' or '=').ToArray());
        Settings? d;
        try { d = JsonSerializer.Deserialize<Settings>(Encoding.UTF8.GetString(Convert.FromBase64String(b64))); }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException) { d = null; }
        if (d is null) return (null, "The settings code is damaged. Copy it again, the whole line.");

        // The code's settings, checked, then taken over the same way Reset does (FlavorSettings.CopyPostingFrom).
        static List<int> Steps(List<int>? steps, List<int> fallback) =>
            steps?.Where(n => n is > 0 and < 10000).Distinct().Order().ToList() is { Count: > 0 } ok ? ok : fallback;
        target.CopyPostingFrom(new FlavorSettings
        {
            PostLevels = d.PostLevels,
            LevelSplit = Math.Clamp(d.LevelSplit, 1, 100),
            LevelEveryBefore = Math.Clamp(d.LevelEveryBefore, 1, 100),
            LevelEveryAfter = Math.Clamp(d.LevelEveryAfter, 1, 100),
            PostMaxLevel = d.PostMaxLevel,
            PostRare = d.PostRare, PostEpic = d.PostEpic, PostMounts = d.PostMounts, PostBosses = d.PostBosses, PostDeaths = d.PostDeaths, FirstBossKillOnly = d.FirstBossKillOnly,
            PostKeyRuns = d.PostKeyRuns, KeyRunMinLevel = Math.Clamp(d.KeyRunMinLevel, FlavorSettings.KeyLevelMin, FlavorSettings.KeyLevelMax), KeyRunTimedOnly = d.KeyRunTimedOnly,
            PostDuels = d.PostDuels, PostMakgora = d.PostMakgora, PostMPlus = d.PostMPlus, PostPvp = d.PostPvp,
            MPlusSteps = Steps(d.MPlusSteps, target.MPlusSteps),
            PvpSteps = Steps(d.PvpSteps, target.PvpSteps),
            Screenshot = d.Screenshot ?? [],
            PostScreenshot = d.PostScreenshot ?? [],
        });
        // Also in a code, not part of a Reset.
        target.SkipBossGroupRegex = d.SkipBossGroupRegex ?? "";
        target.NameStyle = d.NameStyle is "first" or "last" ? d.NameStyle : "full";
        target.HideChatInShots = d.HideChatInShots;
        target.HideUiInShots = d.HideUiInShots;
        return (d.Game, null);
    }
}
