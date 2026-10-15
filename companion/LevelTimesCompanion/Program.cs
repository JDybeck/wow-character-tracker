using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MoonSharp.Interpreter;

// Usage: LevelTimesCompanion <path to LevelTimes.lua SavedVariables file> [--backfill]
// Env:   DISCORD_WEBHOOK_URL (required), LEVELTIMES_SAVEDVARS (alternative to the path argument)

var savedVarsPath = args.FirstOrDefault(a => !a.StartsWith("--"))
                    ?? Environment.GetEnvironmentVariable("LEVELTIMES_SAVEDVARS");
var webhookUrl = Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL");
var backfill = args.Contains("--backfill");

if (string.IsNullOrWhiteSpace(savedVarsPath) || string.IsNullOrWhiteSpace(webhookUrl))
{
    Console.Error.WriteLine("Usage: LevelTimesCompanion <path to LevelTimes.lua> [--backfill]");
    Console.Error.WriteLine("Set DISCORD_WEBHOOK_URL in the environment.");
    return 1;
}

var stateDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LevelTimesCompanion");
Directory.CreateDirectory(stateDir);
var statePath = Path.Combine(stateDir, "posted.json");

var firstRun = !File.Exists(statePath);
var posted = firstRun
    ? new HashSet<string>()
    : JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(statePath)) ?? new HashSet<string>();

using var http = new HttpClient();
var gate = new SemaphoreSlim(1, 1);

async Task ProcessAsync(bool initial)
{
    await gate.WaitAsync();
    try
    {
        if (!File.Exists(savedVarsPath))
        {
            Console.WriteLine("SavedVariables file not found yet, waiting for the game to write it.");
            return;
        }

        // On the very first run, mark old entries as posted instead of flooding the channel.
        var skipPosting = initial && firstRun && !backfill;

        foreach (var e in ReadEntries(savedVarsPath).OrderBy(x => x.At).ThenBy(x => x.Level))
        {
            if (posted.Contains(e.Id)) continue;

            if (skipPosting || await PostAsync(e))
                posted.Add(e.Id);
        }

        File.WriteAllText(statePath, JsonSerializer.Serialize(posted));
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error while processing: {ex.Message}");
    }
    finally
    {
        gate.Release();
    }
}

async Task<bool> PostAsync(Entry e)
{
    var msg = $"**{e.Char}** reached level {e.Level} (level {e.Level - 1} took {FormatDuration(e.Seconds)})";

    for (var attempt = 0; attempt < 3; attempt++)
    {
        var res = await http.PostAsJsonAsync(webhookUrl, new
        {
            content = msg,
            allowed_mentions = new { parse = Array.Empty<string>() },
        });

        if (res.IsSuccessStatusCode)
        {
            Console.WriteLine($"Posted: {msg}");
            return true;
        }

        if (res.StatusCode == HttpStatusCode.TooManyRequests)
        {
            await Task.Delay(res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2));
            continue;
        }

        Console.Error.WriteLine($"Discord returned {(int)res.StatusCode}, will retry on the next change.");
        return false;
    }

    return false;
}

static List<Entry> ReadEntries(string path)
{
    string text;
    // The game may still have the file open, so share it.
    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
    using (var sr = new StreamReader(fs))
    {
        text = sr.ReadToEnd();
    }

    var script = new Script(CoreModules.Preset_SoftSandbox);
    script.DoString(text);

    var list = new List<Entry>();
    var db = script.Globals.Get("LevelTimesDB");
    if (db.Type != DataType.Table) return list;

    var events = db.Table.Get("events");
    if (events.Type != DataType.Table) return list;

    foreach (var v in events.Table.Values)
    {
        if (v.Type != DataType.Table) continue;
        var t = v.Table;
        list.Add(new Entry(
            t.Get("id").CastToString() ?? "",
            t.Get("char").CastToString() ?? "?",
            t.Get("realm").CastToString() ?? "",
            (int)(t.Get("level").CastToNumber() ?? 0),
            (long)(t.Get("seconds").CastToNumber() ?? 0),
            (long)(t.Get("at").CastToNumber() ?? 0)));
    }

    return list.Where(x => x.Id.Length > 0).ToList();
}

static string FormatDuration(long seconds)
{
    var ts = TimeSpan.FromSeconds(seconds);
    if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
    if (ts.TotalMinutes >= 1) return $"{ts.Minutes}m {ts.Seconds}s";
    return $"{ts.Seconds}s";
}

// Process whatever is already in the file, then watch for changes.
await ProcessAsync(initial: true);

var dir = Path.GetDirectoryName(Path.GetFullPath(savedVarsPath))!;
var fileName = Path.GetFileName(savedVarsPath);

using var watcher = new FileSystemWatcher(dir, fileName)
{
    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
};

CancellationTokenSource? debounce = null;
void Schedule()
{
    debounce?.Cancel();
    var cts = debounce = new CancellationTokenSource();
    _ = Task.Run(async () =>
    {
        try
        {
            await Task.Delay(1000, cts.Token); // let the game finish writing
            await ProcessAsync(initial: false);
        }
        catch (TaskCanceledException) { }
    });
}

watcher.Changed += (_, _) => Schedule();
watcher.Created += (_, _) => Schedule();
watcher.Renamed += (_, _) => Schedule();
watcher.EnableRaisingEvents = true;

Console.WriteLine($"Watching {savedVarsPath}. Press Ctrl+C to stop.");
await Task.Delay(Timeout.Infinite);
return 0;

record Entry(string Id, string Char, string Realm, int Level, long Seconds, long At);
