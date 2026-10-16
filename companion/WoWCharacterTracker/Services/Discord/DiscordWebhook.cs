using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WoWCharacterTracker.Services.Discord;

// Talking to one Discord webhook: checking it exists, and sending a message with one embed and
// an optional image (multipart upload), with retries when Discord rate limits.
sealed partial class DiscordWebhook(string url)
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) }; // screenshots take a moment to upload

    // Discord's upload limit without server boosts (boosted servers allow 50 or 100 MB, and webhooks
    // get the same, but there's no way to ask which). Bigger files are tried first anyway.
    public const long FreeUploadLimit = 9_500_000;

    public enum Result { Sent, Failed, TooLarge }

    // The sender picture when there's no character portrait.
    const string LogoUrl = "https://raw.githubusercontent.com/JDybeck/wow-character-tracker/main/companion/WoWCharacterTracker/Assets/logo.png";

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

    // avatar: the sender picture for this message (the character's portrait), or the logo.
    public async Task<bool> SendAsync(Dictionary<string, object> embed, byte[]? image, string imageName, string? avatar) =>
        await SendOrReportAsync(embed, image, imageName, avatar) == Result.Sent;

    // Like SendAsync, but says when Discord turned the image down for its size (HTTP 413).
    public async Task<Result> SendOrReportAsync(Dictionary<string, object> embed, byte[]? image, string imageName, string? avatar)
    {
        var body = new Dictionary<string, object>
        {
            ["avatar_url"] = avatar ?? LogoUrl,
            ["embeds"] = new[] { embed },
            ["allowed_mentions"] = new { parse = Array.Empty<string>() },
        };
        var json = JsonSerializer.Serialize(body);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            HttpResponseMessage res;
            try
            {
                if (image is null)
                {
                    res = await Http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
                }
                else
                {
                    // A fresh form each attempt, since sending disposes it.
                    using var form = new MultipartFormDataContent();
                    form.Add(new StringContent(json, Encoding.UTF8, "application/json"), "payload_json");
                    var file = new ByteArrayContent(image);
                    file.Headers.ContentType = new MediaTypeHeaderValue(imageName.EndsWith(".png") ? "image/png" : "image/jpeg");
                    form.Add(file, "files[0]", imageName);
                    res = await Http.PostAsync(url, form);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                Log.Warn("Couldn't reach Discord. Will try again on the next sync.");
                return Result.Failed;
            }

            if (res.IsSuccessStatusCode) return Result.Sent;
            if (res.StatusCode == HttpStatusCode.RequestEntityTooLarge && image is not null) return Result.TooLarge;

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
