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

    // CHARACTERTRACKER_FAKE_VERSION (tests only) pretends to be an older version, to see the banner.
    public static Version Current =>
        Config.IsTest && Version.TryParse(Environment.GetEnvironmentVariable("CHARACTERTRACKER_FAKE_VERSION"), out var fake) ? fake
        : Assembly.GetExecutingAssembly().GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(0, v.Build))
        : new Version(0, 0, 0);

    // A development build (dotnet build) has its dll next to the exe and can't replace itself.
    public static bool CanInstall =>
        Environment.ProcessPath is { } exe && !File.Exists(Path.ChangeExtension(exe, ".dll"));

    // Installs by itself only when the user turned it on, and never while a game runs: the restart
    // would miss what happens in game meanwhile. One try per version, a failed one leaves the banner.
    public static bool ShouldAutoInstall(bool optedIn, bool canInstall, bool gameRunning, bool triedThisVersion) =>
        optedIn && canInstall && !gameRunning && !triedThisVersion;

    public static Task<Release?> LatestAsync() => GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");

    public static Task<Release?> ForVersionAsync(Version v) => GetAsync($"https://api.github.com/repos/{Repo}/releases/tags/v{v}");

    static async Task<Release?> GetAsync(string url)
    {
        try
        {
            using var doc = JsonDocument.Parse(await Web.GetStringAsync(url, TimeSpan.FromSeconds(30)));
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

    // How an install went. Problem null: the new version was started (the caller then quits).
    // Installed: the new exe is in place even though something went wrong after (it didn't start).
    public sealed record Outcome(string? Problem, bool Installed = false);

    // background: the new version starts in the tray, like this one sits now.
    public static async Task<Outcome> InstallAsync(Release release, IProgress<Step>? progress = null, bool background = false)
    {
        if (!CanInstall) return new("This is a development build, so it can't update itself. Build it again instead.");
        if (release.DownloadUrl.Length == 0) return new("That release has no exe attached.");
        var exe = Environment.ProcessPath!;
        var newExe = exe + ".new";
        try
        {
            progress?.Report(new Step(0, 0));
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)); // the whole download
            using (var res = await Web.Http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token))
            {
                res.EnsureSuccessStatusCode();
                var total = res.Content.Headers.ContentLength;
                await using (var from = await res.Content.ReadAsStreamAsync(cts.Token))
                await using (var to = File.Create(newExe))
                {
                    var buffer = new byte[81920];
                    long done = 0;
                    var shown = DateTime.MinValue;
                    int n;
                    while ((n = await from.ReadAsync(buffer, cts.Token)) > 0)
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
        }
        catch (UnauthorizedAccessException)
        {
            PutBack(exe, newExe);
            return new($"Windows didn't let the app replace itself in {Path.GetDirectoryName(exe)}. Download it from the release page instead.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            PutBack(exe, newExe);
            return new("The update didn't work: " + ex.Message);
        }

        // The new version is in place from here, so a failed start doesn't undo the update.
        progress?.Report(new Step(3));
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, background ? "--updated --background" : "--updated") { UseShellExecute = true });
            return new(null);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return new($"The new version is installed, but Windows didn't let it start ({ex.Message}). Close the app and open it again.", Installed: true);
        }
    }

    // After a failed install: the running exe back under its own name (if it was moved), the download gone.
    static void PutBack(string exe, string newExe)
    {
        try
        {
            if (!File.Exists(exe) && File.Exists(exe + ".old")) File.Move(exe + ".old", exe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Couldn't put the app back after the failed update ({ex.Message}). Rename {Path.GetFileName(exe)}.old to {Path.GetFileName(exe)} in {Path.GetDirectoryName(exe)}.");
        }
        Files.TryDelete(newExe);
    }

    // Leftover from the last update.
    public static void CleanUp()
    {
        if (Environment.ProcessPath is not { } exe) return;
        foreach (var f in new[] { exe + ".old", exe + ".new" })
            Files.TryDelete(f);
    }
}
