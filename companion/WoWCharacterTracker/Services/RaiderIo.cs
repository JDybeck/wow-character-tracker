using System.Collections.Concurrent;
using System.Text.Json;

namespace WoWCharacterTracker.Services;

// Retail character renders (the picture Raider.IO and Warcraft Logs show), from Raider.IO's public
// profile API. Blizzard's own Character Media API needs a client secret, which a public app can't
// keep. Answers are cached in renders.json for a day; characters Raider.IO doesn't know get none.
static class RaiderIo
{
    static readonly HttpClient Http = Web.Client(TimeSpan.FromSeconds(10));
    static readonly ConcurrentDictionary<string, Task<string?>> Lookups = new();
    static readonly object DiskLock = new();
    static Dictionary<string, Cached>? disk;
    static string CachePath => Path.Combine(Config.Dir, "renders.json");
    static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);

    sealed record Cached(string? Url, DateTimeOffset At);

    // "Tarren Mill" -> "tarren-mill", "Azjol-Nerub" -> "azjol-nerub", "Kel'Thuzad" -> "kelthuzad"
    public static string RealmSlug(string realm) =>
        realm.Trim().ToLowerInvariant().Replace("'", "").Replace(' ', '-');

    // region: "eu", "us", "kr", "tw". Returns the render (avatar size, 84 px), or null.
    public static Task<string?> AvatarUrlAsync(string region, string realm, string name)
    {
        if (region.Length == 0 || realm.Length == 0 || name.Length == 0) return Task.FromResult<string?>(null);
        var key = $"{region}/{RealmSlug(realm)}/{name.ToLowerInvariant()}";
        return Lookups.GetOrAdd(key, FetchAsync);
    }

    static async Task<string?> FetchAsync(string key)
    {
        lock (DiskLock)
        {
            disk ??= LoadDisk();
            if (disk.TryGetValue(key, out var c) && DateTimeOffset.UtcNow - c.At < MaxAge) return c.Url;
        }
        var parts = key.Split('/');
        string? url = null;
        try
        {
            var json = await Http.GetStringAsync(
                $"https://raider.io/api/v1/characters/profile?region={parts[0]}&realm={Uri.EscapeDataString(parts[1])}&name={Uri.EscapeDataString(parts[2])}");
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("thumbnail_url", out var t) && t.GetString() is { Length: > 0 } thumb)
                url = thumb.Split('?')[0]; // drop Blizzard's "alt" fallback picture
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.NotFound)
        {
            // Raider.IO doesn't know the character (new, or never seen): remembered as "none".
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Lookups.TryRemove(key, out _); // a hiccup: try again another time
            return null;
        }
        lock (DiskLock)
        {
            disk![key] = new Cached(url, DateTimeOffset.UtcNow);
            try { Files.WriteAtomic(CachePath, JsonSerializer.Serialize(disk)); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return url;
    }

    static Dictionary<string, Cached> LoadDisk()
    {
        try { return File.Exists(CachePath) ? JsonSerializer.Deserialize<Dictionary<string, Cached>>(File.ReadAllText(CachePath)) ?? [] : []; }
        catch (Exception) { return []; }
    }
}
