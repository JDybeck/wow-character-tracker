using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WoWCharacterTracker.Services.Discord;

// Talking to one Discord webhook: checking it exists, and sending a message with embeds and
// optional images (multipart upload), with retries when Discord rate limits.
sealed partial class DiscordWebhook(string url)
{
    static readonly HttpClient Http = Web.Client(TimeSpan.FromSeconds(60)); // screenshots take a moment to upload

    // Discord's upload limit without server boosts (boosted servers allow 50 or 100 MB, and webhooks
    // get the same, but there's no way to ask which). Bigger files are tried first anyway.
    public const long FreeUploadLimit = 9_500_000;

    public enum Result { Sent, Failed, TooLarge }

    // The sender picture. Discord caches avatars by URL, so the query changes whenever the logo does.
    internal const string LogoUrl = "https://raw.githubusercontent.com/JDybeck/wow-character-tracker/main/companion/WoWCharacterTracker/Assets/logo.png?v=2";

    // Every post ends its footer with the app's name and logo, small, next to the time.
    // Footers can't hold links, and Discord always puts the time last.
    internal const string Credit = "WoW Character Tracker";

    internal static IReadOnlyList<Dictionary<string, object>> WithCredit(IReadOnlyList<Dictionary<string, object>> embeds)
    {
        if (embeds.Count == 0) return embeds;
        var main = new Dictionary<string, object>(embeds[0]); // a copy: the same embeds go to every webhook
        var text = main.TryGetValue("footer", out var f) ? FooterText(f) : null;
        main["footer"] = new Dictionary<string, string>
        {
            ["text"] = string.IsNullOrEmpty(text) ? Credit : $"{text} • {Credit}",
            ["icon_url"] = LogoUrl,
        };
        return [main, .. embeds.Skip(1)];
    }

    // Footers are built as anonymous objects ({ text }), so read the text through JSON.
    static string? FooterText(object footer) =>
        JsonSerializer.SerializeToElement(footer).TryGetProperty("text", out var t) ? t.GetString() : null;

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

    // The sender is always the app (its logo), so posts keep one identity; the character's picture is in the message.
    public async Task<bool> SendAsync(Dictionary<string, object> embed, byte[]? image, string imageName) =>
        await SendOrReportAsync(embed, image, imageName) == Result.Sent;

    public async Task<bool> SendAsync(IReadOnlyList<Dictionary<string, object>> embeds, IReadOnlyList<(byte[] Data, string Name)> files) =>
        await SendOrReportAsync(embeds, files) == Result.Sent;

    // Like SendAsync, but says when Discord turned the image down for its size (HTTP 413).
    public Task<Result> SendOrReportAsync(Dictionary<string, object> embed, byte[]? image, string imageName) =>
        SendOrReportAsync([embed], image is null ? [] : [(image, imageName)]);

    // Several embeds (a loot post's item picture and screenshot) and the files they show as
    // attachment://<name>, in one message.
    public async Task<Result> SendOrReportAsync(IReadOnlyList<Dictionary<string, object>> embeds, IReadOnlyList<(byte[] Data, string Name)> files)
    {
        var body = new Dictionary<string, object>
        {
            ["avatar_url"] = LogoUrl,
            ["embeds"] = WithCredit(embeds),
            ["allowed_mentions"] = new { parse = Array.Empty<string>() },
        };
        var json = JsonSerializer.Serialize(body);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            HttpResponseMessage res;
            try
            {
                if (files.Count == 0)
                {
                    res = await Http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
                }
                else
                {
                    // A fresh form each attempt, since sending disposes it.
                    using var form = new MultipartFormDataContent();
                    form.Add(new StringContent(json, Encoding.UTF8, "application/json"), "payload_json");
                    for (var i = 0; i < files.Count; i++)
                    {
                        var file = new ByteArrayContent(files[i].Data);
                        file.Headers.ContentType = new MediaTypeHeaderValue(files[i].Name.EndsWith(".png") ? "image/png" : "image/jpeg");
                        form.Add(file, $"files[{i}]", files[i].Name);
                    }
                    res = await Http.PostAsync(url, form);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                Log.Warn("Couldn't reach Discord. Will try again on the next sync.");
                return Result.Failed;
            }

            if (res.IsSuccessStatusCode) return Result.Sent;
            if (res.StatusCode == HttpStatusCode.RequestEntityTooLarge && files.Count > 0) return Result.TooLarge;

            if (res.StatusCode == HttpStatusCode.TooManyRequests)
            {
                await Task.Delay(res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2));
                continue;
            }

            if (res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
                Log.Error("The Discord webhook no longer works (was it deleted?). Add a new one in the Discord panel.");
            else
                Log.Warn($"Discord returned {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
            return Result.Failed;
        }

        return Result.Failed;
    }

    [GeneratedRegex(@"^https://(?:(?:canary|ptb)\.)?discord(?:app)?\.com/api/(?:v\d+/)?webhooks/\d+/[\w-]+/?$")]
    private static partial Regex WebhookPattern();
}
