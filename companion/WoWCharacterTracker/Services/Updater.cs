using System.Reflection;
using System.Text.Json;

namespace WoWCharacterTracker.Services;

// Looks for a newer release on GitHub and installs it: the new exe is downloaded next to the
// running one, the running one is renamed to .old (Windows allows that while it runs), the new one
// takes its place and is started, and this one quits. The .old file is removed on the next start.
static class Updater
{
    // Releases are built by the GitHub workflow when a v* tag is pushed (release.cmd).
    const string Repo = "JDybeck/wow-character-tracker";
    const string AssetName = "WoWCharacterTracker.exe";

    public sealed record Release(Version Version, string Tag, string Notes, string DownloadUrl, string PageUrl);

    static readonly HttpClient Http = Web.Client(TimeSpan.FromMinutes(5));

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

    // The steps of an install, shown in the update banner as they happen.
    public static readonly string[] Steps = ["Downloading", "Checking the download", "Putting the new version in place", "Restarting"];

    // Step: index into Steps. Fraction and Detail: the download's progress ("12.3 of 70.1 MB").
    public sealed record Step(int Index, double? Fraction = null, string Detail = "");

    // Returns null when the new version was started (the caller then quits), or a reason it couldn't.
    public static async Task<string?> InstallAsync(Release release, IProgress<Step>? progress = null)
    {
        if (!CanInstall) return "This is a development build, so it can't update itself. Build it again instead.";
        if (release.DownloadUrl.Length == 0) return "That release has no exe attached.";
        var exe = Environment.ProcessPath!;
        var newExe = exe + ".new";
        try
        {
            progress?.Report(new Step(0, 0));
            using (var res = await Http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                res.EnsureSuccessStatusCode();
                var total = res.Content.Headers.ContentLength;
                await using (var from = await res.Content.ReadAsStreamAsync())
                await using (var to = File.Create(newExe))
                {
                    var buffer = new byte[81920];
                    long done = 0;
                    var shown = DateTime.MinValue;
                    int n;
                    while ((n = await from.ReadAsync(buffer)) > 0)
                    {
                        await to.WriteAsync(buffer.AsMemory(0, n));
                        done += n;
                        if (DateTime.Now - shown < TimeSpan.FromMilliseconds(100)) continue;
                        shown = DateTime.Now;
                        progress?.Report(new Step(0, total > 0 ? (double)done / total : null,
                            total > 0 ? $"{done / 1e6:0.0} of {total / 1e6:0.0} MB" : $"{done / 1e6:0.0} MB"));
                    }
                }

                // A whole Windows program: the size GitHub said it would be, starting with "MZ".
                progress?.Report(new Step(1));
                var length = new FileInfo(newExe).Length;
                if (total is long expected && length != expected) throw new IOException("the download was cut off");
                await using var check = File.OpenRead(newExe);
                if (check.ReadByte() != 'M' || check.ReadByte() != 'Z') throw new IOException("the download isn't a Windows program");
            }

            progress?.Report(new Step(2));
            File.Move(exe, exe + ".old", overwrite: true);
            File.Move(newExe, exe);

            progress?.Report(new Step(3));
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
            try { if (File.Exists(newExe)) File.Delete(newExe); } catch (IOException) { }
            return "The update didn't work: " + ex.Message;
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
