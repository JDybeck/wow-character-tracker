using System.Reflection;
using System.Text.Json;

namespace WoWCharacterTracker;

// Looks for a newer release on GitHub and installs it: the new exe is downloaded next to the
// running one, the running one is renamed to .old (Windows allows that while it runs), the new one
// takes its place and is started, and this one quits. The .old file is removed on the next start.
static class Updater
{
    // Releases are built by the GitHub workflow when a v* tag is pushed (release.cmd).
    const string Repo = "JDybeck/wow-character-tracker";
    const string AssetName = "WoWCharacterTracker.exe";

    public sealed record Release(Version Version, string Tag, string Notes, string DownloadUrl, string PageUrl);

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("WoWCharacterTracker"); // GitHub's API wants one
        return http;
    }

    // CHARACTERTRACKER_FAKE_VERSION (tests only) pretends to be an older version, to see the banner.
    public static Version Current =>
        Config.IsTest && Version.TryParse(Environment.GetEnvironmentVariable("CHARACTERTRACKER_FAKE_VERSION"), out var fake) ? fake
        : Assembly.GetExecutingAssembly().GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(0, v.Build))
        : new Version(0, 0, 0);

    // A development build (dotnet build) has its dll next to the exe and can't replace itself.
    public static bool CanInstall =>
        Environment.ProcessPath is { } exe && !File.Exists(Path.ChangeExtension(exe, ".dll"));

    public static Task<Release?> LatestAsync() => GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");

    public static Task<Release?> ForVersionAsync(Version v) => GetAsync($"https://api.github.com/repos/{Repo}/releases/tags/v{v}");

    static async Task<Release?> GetAsync(string url)
    {
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v'), out var version)) return null;
            var download = root.GetProperty("assets").EnumerateArray()
                .Where(a => a.GetProperty("name").GetString() == AssetName)
                .Select(a => a.GetProperty("browser_download_url").GetString())
                .FirstOrDefault() ?? "";
            return new Release(version, tag,
                root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                download,
                root.TryGetProperty("html_url", out var page) ? page.GetString() ?? "" : "");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            return null;
        }
    }

    // Returns null when the new version was started (the caller then quits), or a reason it couldn't.
    public static async Task<string?> InstallAsync(Release release)
    {
        if (!CanInstall) return "This is a development build, so it can't update itself. Build it again instead.";
        if (release.DownloadUrl.Length == 0) return "That release has no exe attached.";
        var exe = Environment.ProcessPath!;
        var newExe = exe + ".new";
        try
        {
            var bytes = await Http.GetByteArrayAsync(release.DownloadUrl);
            await File.WriteAllBytesAsync(newExe, bytes);
            File.Move(exe, exe + ".old", overwrite: true);
            File.Move(newExe, exe);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "--updated") { UseShellExecute = true });
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return $"Windows didn't let the app replace itself in {Path.GetDirectoryName(exe)}. Download it from the release page instead.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            if (!File.Exists(exe) && File.Exists(exe + ".old")) File.Move(exe + ".old", exe); // put it back
            return "The download didn't work: " + ex.Message;
        }
    }

    // Leftover from the last update.
    public static void CleanUp()
    {
        if (Environment.ProcessPath is not { } exe) return;
        foreach (var f in new[] { exe + ".old", exe + ".new" })
            try { if (File.Exists(f)) File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
