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
        bool HideChatInShots, Dictionary<string, bool> Screenshot, Dictionary<string, bool> PostScreenshot, bool HideUiInShots = false);

    public static string Export(FlavorSettings s, Flavor flavor)
    {
        var dto = new Settings(flavor.Key,
            s.PostLevels, s.LevelSplit, s.LevelEveryBefore, s.LevelEveryAfter, s.PostMaxLevel,
            s.PostRare, s.PostEpic, s.PostMounts, s.PostBosses, s.PostDeaths,
            s.PostDuels, s.PostMakgora, s.PostMPlus, s.PostPvp,
            s.MPlusSteps, s.PvpSteps, s.SkipBossGroupRegex, s.NameStyle,
            s.HideChatInShots, s.Screenshot, s.PostScreenshot, s.HideUiInShots);
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

        target.PostLevels = d.PostLevels;
        target.LevelSplit = Math.Clamp(d.LevelSplit, 1, 100);
        target.LevelEveryBefore = Math.Clamp(d.LevelEveryBefore, 1, 100);
        target.LevelEveryAfter = Math.Clamp(d.LevelEveryAfter, 1, 100);
        target.PostMaxLevel = d.PostMaxLevel;
        (target.PostRare, target.PostEpic, target.PostMounts, target.PostBosses, target.PostDeaths) =
            (d.PostRare, d.PostEpic, d.PostMounts, d.PostBosses, d.PostDeaths);
        (target.PostDuels, target.PostMakgora, target.PostMPlus, target.PostPvp) = (d.PostDuels, d.PostMakgora, d.PostMPlus, d.PostPvp);
        if (d.MPlusSteps is { Count: > 0 }) target.MPlusSteps = [.. d.MPlusSteps.Where(n => n is > 0 and < 10000).Distinct().Order()];
        if (d.PvpSteps is { Count: > 0 }) target.PvpSteps = [.. d.PvpSteps.Where(n => n is > 0 and < 10000).Distinct().Order()];
        target.SkipBossGroupRegex = d.SkipBossGroupRegex ?? "";
        target.NameStyle = d.NameStyle is "first" or "last" ? d.NameStyle : "full";
        target.HideChatInShots = d.HideChatInShots;
        target.HideUiInShots = d.HideUiInShots;
        target.Screenshot = new(d.Screenshot ?? []);
        target.PostScreenshot = new(d.PostScreenshot ?? []);
        return (d.Game, null);
    }
}
