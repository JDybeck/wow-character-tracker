using System.Reflection;
using System.Text;
using System.Text.Json;
using WoWCharacterTracker;

// Character Tracker companion: checks that everything is set up, then watches the addon's
// SavedVariables and posts every new level-up to Discord.
//
//   WoWCharacterTracker.exe              normal start, asks for anything that's missing
//   WoWCharacterTracker.exe --setup      ask for the webhook and WoW folder again
//   WoWCharacterTracker.exe --test       send a test post and quit
//   WoWCharacterTracker.exe --backfill   on the first run, post old level-ups instead of skipping them
//
// DISCORD_WEBHOOK_URL in the environment overrides the saved webhook.

Console.OutputEncoding = Encoding.UTF8;
Console.Title = "Character Tracker";

if (args.Contains("--help") || args.Contains("-h") || args.Contains("/?"))
{
    Console.WriteLine("WoWCharacterTracker.exe [--setup] [--test] [--backfill]");
    Console.WriteLine("  --setup     ask for the Discord webhook and WoW folder again");
    Console.WriteLine("  --test      send a test post to Discord and quit");
    Console.WriteLine("  --backfill  on the first run, post old level-ups instead of skipping them");
    return 0;
}

using var singleInstance = new Mutex(true, @"Local\WoWCharacterTracker", out var isFirstInstance);
if (!isFirstInstance)
{
    Ui.Warn("Character Tracker is already running in another window. You only need one.");
    Ui.PressAnyKeyToExit();
    return 1;
}

try
{
    return await App.RunAsync(args);
}
catch (Exception ex)
{
    Ui.Error("Something went wrong: " + ex.Message);
    Ui.Hint("If this keeps happening, copy this window's text into an issue on GitHub.");
    Ui.PressAnyKeyToExit();
    return 1;
}

static class App
{
    static readonly string StatePath = Path.Combine(Config.Dir, "posted.json");

    public static async Task<int> RunAsync(string[] args)
    {
        var setup = args.Contains("--setup");
        var testOnly = args.Contains("--test");
        var backfill = args.Contains("--backfill");

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
        Ui.Title($"Character Tracker v{version}  -  posts your WoW level-ups to Discord");

        var config = Config.Load();

        // 1. World of Warcraft
        var wowPath = FindWow(config, setup);
        if (wowPath is null) return Quit();

        // 2. The addon
        CheckAddon(wowPath);

        // 3. Discord
        var webhook = await SetUpWebhookAsync(config, setup, wowPath);
        if (webhook is null) return Quit();
        var discord = new Discord(webhook);

        if (testOnly)
        {
            var ok = await discord.PostAsync(SampleEntry(wowPath), isTest: true);
            if (ok) Ui.Ok("Test message sent. Check your Discord channel.");
            return Quit(ok ? 0 : 1);
        }

        // 4. Level data from the game
        CheckData(wowPath);

        Console.WriteLine();
        Ui.Ok("All set! Leave this window open while you play (minimizing is fine).");
        Ui.Hint("After a ding, click \"Post now\" in the game (or type /reload) and the post shows up in Discord.");
        Ui.Hint("Close this window to stop.");
        Console.WriteLine();

        await WatchAsync(wowPath, discord, backfill);
        return 0;
    }

    static int Quit(int code = 1)
    {
        Ui.PressAnyKeyToExit();
        return code;
    }

    static string? FindWow(Config config, bool setup)
    {
        var path = !setup && Wow.IsValid(config.WowPath) ? config.WowPath : Wow.Detect();

        if (path is null)
        {
            Ui.Warn($"Couldn't find World of Warcraft (looked for a folder with {Wow.Flavor} in it).");
            Ui.Hint("In Battle.net: select WoW, click the gear next to Play, then \"Show in Explorer\".");
            Ui.Hint("Copy that folder's path from the address bar and paste it here.");
            while (path is null)
            {
                var input = Ui.Ask("World of Warcraft folder (leave empty to quit):");
                if (input.Length == 0) return null;
                path = Wow.Normalize(input);
                if (path is null) Ui.Warn($"That folder doesn't have a {Wow.Flavor} folder inside it. Try again.");
            }
        }

        if (config.WowPath != path)
        {
            config.WowPath = path;
            config.Save();
        }
        Ui.Ok($"World of Warcraft found: {path}");
        return path;
    }

    static void CheckAddon(string wowPath)
    {
        var installed = AddonInstaller.InstalledVersion(wowPath);
        var bundled = AddonInstaller.BundledVersion;
        var interfaceVersion = Wow.InterfaceVersion(wowPath);

        try
        {
            if (installed is null)
            {
                Ui.Warn("The Character Tracker addon isn't installed yet.");
                if (!Ui.Confirm("Install it now?"))
                {
                    Ui.Hint($"Without it nothing gets recorded. Copy addon\\WoWCharacterTracker into {Wow.AddOnsDir(wowPath)} yourself.");
                    return;
                }
                AddonInstaller.Install(wowPath, interfaceVersion);
                Ui.Ok($"Addon installed (v{bundled}).");
                if (Wow.IsRunning())
                    Ui.Hint("WoW is running: exit the game fully and start it again so it finds the new addon.");
                return;
            }

            if (installed != bundled)
            {
                Ui.Warn($"Your addon is v{installed}, this companion comes with v{bundled}.");
                if (Ui.Confirm("Update it?"))
                {
                    AddonInstaller.Install(wowPath, interfaceVersion);
                    Ui.Ok($"Addon updated to v{bundled}.");
                    if (Wow.IsRunning())
                        Ui.Hint("WoW is running: type /reload in game to load the new version.");
                    return;
                }
            }

            // Keep the interface number in step with the game after patches, so the addon
            // isn't marked "out of date" and silently turned off.
            if (interfaceVersion is int v && AddonInstaller.InstalledInterface(wowPath) != v.ToString())
            {
                AddonInstaller.SetInterface(wowPath, v);
                Ui.Ok($"Addon installed (v{installed}), updated it for game version {v}.");
                if (Wow.IsRunning()) Ui.Hint("Restart WoW so it picks up the change.");
            }
            else
            {
                Ui.Ok($"Addon installed (v{installed}).");
            }
        }
        catch (UnauthorizedAccessException)
        {
            Ui.Error("Windows didn't let me write to the AddOns folder.");
            Ui.Hint("Right-click WoWCharacterTracker.exe and pick \"Run as administrator\" once, or copy the");
            Ui.Hint($"addon\\WoWCharacterTracker folder into {Wow.AddOnsDir(wowPath)} yourself.");
        }
    }

    static async Task<string?> SetUpWebhookAsync(Config config, bool setup, string wowPath)
    {
        var fromEnv = Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL");
        var url = !string.IsNullOrWhiteSpace(fromEnv) ? fromEnv.Trim() : setup ? null : config.WebhookUrl;

        if (url is not null)
        {
            var (name, problem) = await Discord.CheckAsync(url);
            if (name is not null)
            {
                Ui.Ok($"Discord webhook works (posting as \"{name}\").");
                return url;
            }
            if (problem!.StartsWith("Couldn't reach") || problem.Contains("too long"))
            {
                Ui.Warn(problem + " I'll keep going and post when Discord is reachable.");
                return url;
            }
            Ui.Error(problem);
            if (!string.IsNullOrWhiteSpace(fromEnv))
            {
                Ui.Hint("The webhook comes from the DISCORD_WEBHOOK_URL environment variable. Fix or remove it.");
                return null;
            }
        }
        else
        {
            Ui.Warn("No Discord webhook set up yet. That's the link Character Tracker posts through.");
        }

        Ui.Hint("1. In Discord, hover the channel you want the posts in and click the gear (Edit Channel).");
        Ui.Hint("2. Go to Integrations > Webhooks > New Webhook. Name it, e.g. \"Character Tracker\".");
        Ui.Hint("3. Click \"Copy Webhook URL\" and paste it below (right-click pastes in this window).");
        Ui.Hint("You need the Manage Webhooks permission on that server. Ask an admin if you don't see it.");

        while (true)
        {
            var input = Ui.Ask("Webhook URL (leave empty to quit):");
            if (input.Length == 0) return null;

            if (!Discord.LooksLikeWebhook(input))
            {
                Ui.Warn("That doesn't look like a webhook link. It starts with https://discord.com/api/webhooks/");
                continue;
            }

            var (name, problem) = await Discord.CheckAsync(input);
            if (name is null)
            {
                Ui.Warn(problem!);
                continue;
            }

            config.WebhookUrl = input.Trim();
            config.Save();
            Ui.Ok($"Discord webhook works (posting as \"{name}\"). Saved, you won't be asked again.");

            if (Ui.Confirm("Send a test message to the channel now?"))
            {
                if (await new Discord(config.WebhookUrl).PostAsync(SampleEntry(wowPath), isTest: true))
                    Ui.Ok("Test message sent. Check your Discord channel.");
            }
            return config.WebhookUrl;
        }
    }

    static void CheckData(string wowPath)
    {
        var accounts = Wow.Accounts(wowPath).ToList();
        if (accounts.Count == 0)
        {
            Ui.Warn("WoW hasn't created your account folder yet.");
            Ui.Hint("Log in to a character and type /reload once. After that every ding gets posted.");
            return;
        }

        var withData = accounts.Where(a => File.Exists(Wow.SavedVarsFile(wowPath, a))).ToList();
        if (withData.Count == 0)
        {
            Ui.Warn("No Character Tracker data from the game yet.");
            Ui.Hint("Log in to a character and type /reload once. After that every ding gets posted.");
            return;
        }

        var count = withData.Sum(a => TryRead(Wow.SavedVarsFile(wowPath, a))?.Count ?? 0);
        Ui.Ok($"Level data found ({count} level-up{(count == 1 ? "" : "s")} recorded so far).");
    }

    // Uses your most recent real level-up for the test post if there is one, so you see your own character.
    static Entry SampleEntry(string wowPath)
    {
        var latest = Wow.Accounts(wowPath)
            .Select(a => Wow.SavedVarsFile(wowPath, a))
            .Where(File.Exists)
            .SelectMany(f => TryRead(f) ?? [])
            .MaxBy(e => e.At);

        return latest ?? new Entry("test", "Testy", "", 12, 18 * 60 + 4, DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            "Elwynn Forest", "Mining 75, Herbalism 60", "Class 4 / Arms 2",
            "Arms", "Warrior", "WARRIOR", "Human", "Human", "male");
    }

    static List<Entry>? TryRead(string file)
    {
        try { return SavedVariables.Read(file); }
        catch (Exception) { return null; }
    }

    static async Task WatchAsync(string wowPath, Discord discord, bool backfill)
    {
        var firstRun = !File.Exists(StatePath);
        var posted = firstRun
            ? new HashSet<string>()
            : JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(StatePath)) ?? [];
        var gate = new SemaphoreSlim(1, 1);

        async Task ProcessAllAsync(bool initial)
        {
            await gate.WaitAsync();
            try
            {
                // On the very first run, mark old entries as posted instead of flooding the channel.
                var skipPosting = initial && firstRun && !backfill;
                var skipped = 0;

                foreach (var account in Wow.Accounts(wowPath))
                {
                    var file = Wow.SavedVarsFile(wowPath, account);
                    if (!File.Exists(file)) continue;

                    List<Entry> entries;
                    try { entries = SavedVariables.Read(file); }
                    catch (Exception ex)
                    {
                        Ui.Warn($"Couldn't read {file} ({ex.Message}). Will try again on the next change.");
                        continue;
                    }

                    foreach (var e in entries.OrderBy(x => x.At).ThenBy(x => x.Level))
                    {
                        if (posted.Contains(e.Id)) continue;

                        if (skipPosting)
                        {
                            posted.Add(e.Id);
                            skipped++;
                        }
                        else if (await discord.PostAsync(e))
                        {
                            posted.Add(e.Id);
                            Ui.Ok($"{DateTime.Now:HH:mm}  Posted: {e.Char} dinged level {e.Level} ({Format.Duration(e.Seconds)}).");
                        }
                    }
                }

                if (skipped > 0)
                    Ui.Info($"Skipped {skipped} older level-up{(skipped == 1 ? "" : "s")} so the channel isn't flooded. Start with --backfill to post them.");

                File.WriteAllText(StatePath, JsonSerializer.Serialize(posted));
            }
            finally
            {
                gate.Release();
            }
        }

        await ProcessAllAsync(initial: true);

        // Watch the whole WTF folder so accounts (and the file itself) that appear later are picked up.
        var watchDir = Path.Combine(Wow.FlavorDir(wowPath), "WTF");
        if (!Directory.Exists(watchDir)) watchDir = Wow.FlavorDir(wowPath);
        using var watcher = new FileSystemWatcher(watchDir, "WoWCharacterTracker.lua")
        {
            IncludeSubdirectories = true,
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
                    await ProcessAllAsync(initial: false);
                }
                catch (TaskCanceledException) { }
            });
        }

        watcher.Changed += (_, _) => Schedule();
        watcher.Created += (_, _) => Schedule();
        watcher.Renamed += (_, _) => Schedule();
        watcher.EnableRaisingEvents = true;

        Ui.Info("Watching for level-ups...");

        // A slow re-check as a safety net in case a file change notification gets lost.
        while (true)
        {
            await Task.Delay(TimeSpan.FromMinutes(1));
            await ProcessAllAsync(initial: false);
        }
    }
}
