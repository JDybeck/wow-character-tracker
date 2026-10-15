using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WoWCharacterTracker;

partial class Discord(string webhookUrl)
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    // Class colors as the game uses them (RAID_CLASS_COLORS).
    public static readonly Dictionary<string, int> ClassColors = new()
    {
        ["WARRIOR"] = 0xC69B6D, ["PALADIN"] = 0xF48CBA, ["HUNTER"] = 0xAAD372, ["ROGUE"] = 0xFFF468,
        ["PRIEST"] = 0xFFFFFF, ["DEATHKNIGHT"] = 0xC41E3A, ["SHAMAN"] = 0x0070DD, ["MAGE"] = 0x3FC7EB,
        ["WARLOCK"] = 0x8788EE, ["MONK"] = 0x00FF98, ["DRUID"] = 0xFF7C0A, ["DEMONHUNTER"] = 0xA330C9,
        ["EVOKER"] = 0x33937F,
    };

    const string IconBase = "https://wow.zamimg.com/images/wow/icons/large/";

    // Remembers which portrait URLs exist so a new race we don't know falls back to the class icon.
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> UrlExists = new();

    public static bool LooksLikeWebhook(string url) => WebhookPattern().IsMatch(url.Trim());

    // Asks Discord about the webhook. Returns its display name, or null with a reason.
    public static async Task<(string? Name, string? Problem)> CheckAsync(string url)
    {
        try
        {
            var res = await Http.GetAsync(url.Trim());
            if (res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
                return (null, "Discord says this webhook doesn't exist. It may have been deleted, or the link was cut off when copying.");
            if (!res.IsSuccessStatusCode)
                return (null, $"Discord answered with an error ({(int)res.StatusCode}). Try again in a minute.");

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null;
            return (name ?? "your webhook", null);
        }
        catch (HttpRequestException)
        {
            return (null, "Couldn't reach Discord. Check your internet connection.");
        }
        catch (TaskCanceledException)
        {
            return (null, "Discord took too long to answer. Check your internet connection.");
        }
    }

    public async Task<bool> PostAsync(Entry e, bool isTest = false)
    {
        var embed = await BuildEmbedAsync(e, isTest);
        var body = new Dictionary<string, object>
        {
            ["embeds"] = new[] { embed },
            ["allowed_mentions"] = new { parse = Array.Empty<string>() },
        };

        for (var attempt = 0; attempt < 3; attempt++)
        {
            HttpResponseMessage res;
            try
            {
                res = await Http.PostAsJsonAsync(webhookUrl, body);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                Log.Warn("Couldn't reach Discord. Will try again on the next sync.");
                return false;
            }

            if (res.IsSuccessStatusCode) return true;

            if (res.StatusCode == HttpStatusCode.TooManyRequests)
            {
                await Task.Delay(res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2));
                continue;
            }

            if (res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
                Log.Error("The Discord webhook no longer works (was it deleted?). Add a new one in the Discord panel.");
            else
                Log.Warn($"Discord returned {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
            return false;
        }

        return false;
    }

    async Task<object> BuildEmbedAsync(Entry e, bool isTest)
    {
        var lines = new List<string> { $"{e.Level - 1} to {e.Level} took **{Format.Duration(e.Seconds)}**." };
        if (e.Played > 0) lines.Add($"Total played: **{Format.Played(e.Played)}**.");
        if (e.Zone.Length > 0) lines.Add($"They dinged in **{e.Zone}**.");

        // Older addon versions wrote "None" when there were no professions.
        var professions = e.Professions == "None" ? "" : e.Professions;
        if (professions.Length > 0 || e.Talents.Length > 0) lines.Add("");
        if (professions.Length > 0) lines.Add($"**Professions:** {professions}");
        if (e.Talents.Length > 0) lines.Add($"**Talents:** {e.Talents}");

        var embed = new Dictionary<string, object>
        {
            ["title"] = $"{e.Char} just dinged level {e.Level}.",
            ["description"] = string.Join("\n", lines),
            ["timestamp"] = DateTimeOffset.FromUnixTimeSeconds(e.At).ToString("o"),
        };

        if (ClassColors.TryGetValue(e.ClassFile.ToUpperInvariant(), out var color))
            embed["color"] = color;

        var classIcon = ClassIconUrl(e.ClassFile);

        // "Human Fury Warrior", leaving out whatever we don't know.
        var who = string.Join(" ", new[] { e.Race, e.Spec, e.Class }.Where(s => s.Length > 0));
        if (who.Length > 0)
        {
            var author = new Dictionary<string, object> { ["name"] = who };
            if (classIcon is not null) author["icon_url"] = classIcon;
            embed["author"] = author;
        }

        var picture = await PictureUrlAsync(e.RaceFile, e.Sex, e.ClassFile);
        if (picture is not null) embed["thumbnail"] = new { url = picture };

        var footer = isTest ? "Test message from Character Tracker" : e.Realm;
        if (footer.Length > 0) embed["footer"] = new { text = footer };

        return embed;
    }

    public static string? ClassIconUrl(string classFile) =>
        classFile.Length > 0 ? $"{IconBase}classicon_{classFile.ToLowerInvariant()}.jpg" : null;

    // The race portrait matches the character best; the class icon is the fallback.
    public static async Task<string?> PictureUrlAsync(string raceFile, string sex, string classFile)
    {
        if (raceFile.Length > 0)
        {
            var portrait = $"{IconBase}race_{raceFile.ToLowerInvariant()}_{sex}.jpg";
            if (await ExistsAsync(portrait)) return portrait;
        }
        return ClassIconUrl(classFile);
    }

    static async Task<bool> ExistsAsync(string url)
    {
        if (UrlExists.TryGetValue(url, out var known)) return known;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var res = await Http.SendAsync(req);
            return UrlExists[url] = res.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false; // don't cache, it may just be a hiccup
        }
    }

    [GeneratedRegex(@"^https://(?:(?:canary|ptb)\.)?discord(?:app)?\.com/api/(?:v\d+/)?webhooks/\d+/[\w-]+/?$")]
    private static partial Regex WebhookPattern();
}
