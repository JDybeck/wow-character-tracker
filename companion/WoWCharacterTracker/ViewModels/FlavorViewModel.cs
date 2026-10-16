using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WoWCharacterTracker.ViewModels;

// Everything for one game version tab: its folder, addon, characters and what it posts.
sealed class FlavorViewModel : Observable
{
    readonly MainViewModel main;
    Tracker? tracker;
    string? flavorDir;
    Entry? lastPost;

    // The character that just logged in, picked once its row is in the list.
    string? pendingSelect;
    void SelectCheckedIn()
    {
        if (pendingSelect is null || Characters.FirstOrDefault(c => c.Guid == pendingSelect) is not { } row) return;
        SelectedCharacter = row;
        pendingSelect = null;
    }

    public FlavorViewModel(MainViewModel main, Flavor flavor)
    {
        this.main = main;
        Flavor = flavor;
        FolderStep = new SetupItem("Game folder")
        {
            Detail = "Looking for the game...",
            ActionText = "Browse",
            ActionCommand = new RelayCommand(_ => Browse()),
        };
        AddonStep = new SetupItem("Addon")
        {
            Detail = "Waiting for the game folder",
            ActionCommand = new RelayCommand(_ => SetUpAddon(force: true)),
        };
        DiscordStep = new SetupItem("Discord") { Detail = "Add a webhook" };
        _ = LoadLogoAsync();
        Shots = Flavors.ShotKinds.ToDictionary(k => k, k => new ShotOption(Settings, k, ShotsChanged));
    }

    // Screenshot options per kind of event, bound as Shots[level].Take and so on.
    public Dictionary<string, ShotOption> Shots { get; }

    void ShotsChanged()
    {
        main.Config.Save();
        WriteAddonSettings();
        HintReload();
    }

    // The addon reads Settings.lua when it loads, so changes made while playing wait for a /reload.
    // Said once per session.
    bool hintedReload;
    void HintReload()
    {
        if (hintedReload || flavorDir is null || !Wow.IsRunning(flavorDir)) return;
        hintedReload = true;
        Log.Info($"[{Name}] The game picks up these changes on your next /reload or login. Until then it uses the old ones for screenshots and instant posts.");
    }

    void WriteAddonSettings()
    {
        if (flavorDir is null) return;
        try { AddonInstaller.WriteSettings(flavorDir, Settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"[{Name}] Couldn't save the screenshot settings for the addon: {ex.Message}");
        }
    }

    public Flavor Flavor { get; }
    public string Name => Flavor.Name;

    System.Windows.Media.ImageSource? logo;
    public System.Windows.Media.ImageSource? Logo { get => logo; private set { Set(ref logo, value); Raise(nameof(HasLogo)); } }
    public bool HasLogo => logo is not null;
    async Task LoadLogoAsync() => Logo = await Logos.LoadAsync(Flavor);
    FlavorSettings Settings => main.Config.For(Flavor);
    public Tracker? Tracker => tracker;

    public SetupItem FolderStep { get; }
    public SetupItem AddonStep { get; }
    public SetupItem DiscordStep { get; }
    public IReadOnlyList<SetupItem> Steps => [FolderStep, AddonStep, DiscordStep];

    public ObservableCollection<CharacterRow> Characters { get; } = [];
    CharacterRow? selectedCharacter;
    public CharacterRow? SelectedCharacter { get => selectedCharacter; set => Set(ref selectedCharacter, value); }
    public bool HasCharacters => Characters.Count > 0;

    string statusText = "Starting up...";
    public string StatusText { get => statusText; set => Set(ref statusText, value); }

    // --- what gets posted (bound two-way from the settings page) ---

    public bool PostLevels { get => Settings.PostLevels; set { Settings.PostLevels = value; Changed(); } }
    public int LevelSplit { get => Settings.LevelSplit; set { Settings.LevelSplit = Math.Clamp(value, 1, 100); Changed(); } }
    public int LevelEveryBefore { get => Settings.LevelEveryBefore; set { Settings.LevelEveryBefore = Math.Clamp(value, 1, 100); Changed(); } }
    public int LevelEveryAfter { get => Settings.LevelEveryAfter; set { Settings.LevelEveryAfter = Math.Clamp(value, 1, 100); Changed(); } }
    public bool PostRare { get => Settings.PostRare; set { Settings.PostRare = value; Changed(); } }
    public bool PostEpic { get => Settings.PostEpic; set { Settings.PostEpic = value; Changed(); } }
    public bool PostMounts { get => Settings.PostMounts; set { Settings.PostMounts = value; Changed(); } }
    public bool PostBosses { get => Settings.PostBosses; set { Settings.PostBosses = value; Changed(); } }
    public bool PostDeaths { get => Settings.PostDeaths; set { Settings.PostDeaths = value; Changed(); } }
    public bool PostDuels { get => Settings.PostDuels; set { Settings.PostDuels = value; Changed(); } }
    public bool PostMakgora { get => Settings.PostMakgora; set { Settings.PostMakgora = value; Changed(); } }
    public bool PostMPlus { get => Settings.PostMPlus; set { Settings.PostMPlus = value; Changed(); } }
    public bool PostPvp { get => Settings.PostPvp; set { Settings.PostPvp = value; Changed(); } }
    public bool IsRetail => Flavor == Flavors.Retail;

    // Rating milestones as "1500, 2000, 2500": whole numbers, sorted, typos ignored.
    public string MPlusStepsText { get => string.Join(", ", Settings.MPlusSteps); set { Settings.MPlusSteps = ParseSteps(value, Settings.MPlusSteps); Changed(); } }
    public string PvpStepsText { get => string.Join(", ", Settings.PvpSteps); set { Settings.PvpSteps = ParseSteps(value, Settings.PvpSteps); Changed(); } }
    static List<int> ParseSteps(string text, List<int> fallback)
    {
        var steps = text.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0).Where(n => n > 0 && n < 10000).Distinct().Order().ToList();
        return steps.Count > 0 ? steps : fallback;
    }

    public bool HideChatInShots { get => Settings.HideChatInShots; set { Settings.HideChatInShots = value; Changed(); } }

    // Name in posts (Forever has surnames): three chips that act like radio buttons.
    public bool HasSurnames => Flavor == Flavors.Forever;
    public bool NameFull { get => Settings.NameStyle is not ("first" or "last"); set { if (value) SetNameStyle("full"); } }
    public bool NameFirst { get => Settings.NameStyle == "first"; set { if (value) SetNameStyle("first"); } }
    public bool NameLast { get => Settings.NameStyle == "last"; set { if (value) SetNameStyle("last"); } }
    void SetNameStyle(string style)
    {
        Settings.NameStyle = style;
        main.Config.Save();
        Raise(nameof(NameFull)); Raise(nameof(NameFirst)); Raise(nameof(NameLast));
    }

    // Regex of group members that make a boss kill not get posted (they post it themselves).
    public string SkipBossGroupRegex
    {
        get => Settings.SkipBossGroupRegex;
        set
        {
            Settings.SkipBossGroupRegex = value;
            main.Config.Save();
            Raise(nameof(SkipBossGroupRegex));
            Raise(nameof(SkipRegexProblem));
        }
    }

    public string SkipRegexProblem
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Settings.SkipBossGroupRegex)) return "";
            try { _ = new System.Text.RegularExpressions.Regex(Settings.SkipBossGroupRegex); return ""; }
            catch (ArgumentException) { return "That isn't a valid pattern, so nothing gets skipped."; }
        }
    }

    // "Posts at levels 5, 10, 15, 20, 25, 30, 31, 32, 33 and so on."
    public string LevelPreview
    {
        get
        {
            if (!Settings.PostLevels) return "Level-ups aren't posted.";
            var levels = Enumerable.Range(2, 99).Where(Settings.LevelWanted).Take(12).ToList();
            return levels.Count == 0 ? "No levels match." : $"Posts at levels {string.Join(", ", levels)} and so on.";
        }
    }

    void Changed([CallerMemberName] string? name = null)
    {
        main.Config.Save();
        WriteAddonSettings(); // the addon only sends live (and screenshots) what will be posted
        HintReload();
        Raise(name!);
        Raise(nameof(LevelPreview));
    }

    public void ResetPosting()
    {
        var d = Flavor.Defaults();
        var s = Settings;
        (s.PostLevels, s.LevelSplit, s.LevelEveryBefore, s.LevelEveryAfter) = (d.PostLevels, d.LevelSplit, d.LevelEveryBefore, d.LevelEveryAfter);
        (s.PostRare, s.PostEpic, s.PostMounts, s.PostBosses, s.PostDeaths) = (d.PostRare, d.PostEpic, d.PostMounts, d.PostBosses, d.PostDeaths);
        s.Screenshot = new(d.Screenshot);
        s.PostScreenshot = new(d.PostScreenshot);
        main.Config.Save();
        WriteAddonSettings();
        foreach (var o in Shots.Values) o.Refresh();
        foreach (var p in new[] { nameof(PostLevels), nameof(LevelSplit), nameof(LevelEveryBefore), nameof(LevelEveryAfter),
                     nameof(PostRare), nameof(PostEpic), nameof(PostMounts), nameof(PostBosses), nameof(PostDeaths), nameof(LevelPreview) })
            Raise(p);
        Log.Info($"[{Name}] Posting settings reset to the defaults.");
    }

    // --- startup and folder ---

    public void Start()
    {
        var path = Wow.IsFlavorDir(Settings.Path) ? Settings.Path : Wow.Detect(Flavor);
        if (path is null)
        {
            FolderStep.Ok = false;
            FolderStep.Detail = Flavor == Flavors.Forever
                ? "Not found. Click Browse and pick the Forever folder (for the beta that's _classic_beta_)."
                : $"Not found. Click Browse and pick the {Flavor.Folders[0]} folder in your WoW folder.";
            StatusText = "Waiting for the game folder";
            Log.Warn($"[{Name}] Couldn't find the game folder. Click Browse next to Game folder to pick it.");
            return;
        }
        UseFolder(path);
    }

    void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = $"Pick the {Name} folder (inside your World of Warcraft folder)",
            InitialDirectory = flavorDir is not null ? Path.GetDirectoryName(flavorDir) : null,
        };
        if (dialog.ShowDialog() != true) return;
        var path = Wow.Normalize(dialog.FolderName, Flavor);
        if (path is null)
        {
            Log.Warn($"[{Name}] That isn't a game folder. Pick the one that starts with an underscore, like {Flavor.Folders[^1]}.");
            return;
        }
        UseFolder(path);
    }

    void UseFolder(string path)
    {
        flavorDir = path;
        if (Settings.Path != path)
        {
            Settings.Path = path;
            main.Config.Save();
        }

        var version = Wow.ClientVersion(path);
        var product = Wow.Product(path);
        FolderStep.Ok = true;
        FolderStep.ActionText = "Change";
        FolderStep.Detail = version is null ? path : $"{version} ({product}) in {Path.GetFileName(path)}";
        Log.Success($"[{Name}] Game folder: {path}" + (version is null ? "" : $", version {version} ({product})."));

        SetUpAddon(force: false);

        tracker?.Dispose();
        tracker = new Tracker(Flavor, path, () => main.Config.ActiveWebhooks(Flavor), () => Settings,
            guid => main.Config.TagsOf(guid), main.Backfill);
        tracker.DataChanged += data => MainViewModel.OnUi(() => ShowData(data));
        tracker.Posted += e => MainViewModel.OnUi(() => { lastPost = e; UpdateStatus(); });
        tracker.LiveTest += _ => MainViewModel.OnUi(() => AddonStep.Detail = $"v{AddonInstaller.BundledVersion}, instant posting works");
        // Logging in on a game switches the app to that game's tab, with the character picked.
        tracker.CheckedIn += e => MainViewModel.OnUi(() =>
        {
            main.SelectedFlavor = this;
            pendingSelect = e.Guid;
            SelectCheckedIn();
        });
        tracker.Start();
        UpdateStatus();
        CommandManager.InvalidateRequerySuggested();
    }

    // Installs the addon when the user clicks Install on the Addon card (force), since that adds
    // something to their game folder. An installed addon that's older than the one in this app is
    // updated right away: the app and the addon have to speak the same version. Also keeps the
    // interface number in step with game patches, so the game doesn't call it out of date.
    void SetUpAddon(bool force)
    {
        if (flavorDir is null) return;
        var installed = AddonInstaller.InstalledVersion(flavorDir);
        var bundled = AddonInstaller.BundledVersion;
        var interfaceVersion = Wow.InterfaceVersion(flavorDir);
        var olderInstalled = installed is not null && Version.TryParse(installed, out var iv) && Version.TryParse(bundled, out var bv) && iv < bv;

        if (!force && !olderInstalled && (installed is null || installed != bundled))
        {
            AddonStep.Ok = false;
            AddonStep.ActionText = installed is null ? "Install" : "Update";
            AddonStep.Detail = installed is null
                ? "Not installed in this game folder yet"
                : $"v{installed} installed, v{bundled} is ready";
            Log.Info($"[{Name}] " + (installed is null
                ? "The addon isn't installed in this game folder yet. Click Install on the Addon card to add it."
                : $"Addon update ready (v{installed} to v{bundled}). Click Update on the Addon card when you want it."));
            if (installed is not null)
            {
                KeepInterfaceCurrent(installed, interfaceVersion);
                WriteAddonSettings(); // the installed version still follows the What gets posted page
            }
            return;
        }

        try
        {
            if (installed is null || installed != bundled || force)
            {
                AddonInstaller.Install(flavorDir, interfaceVersion);
                Log.Success($"[{Name}] " + (installed is null ? $"Installed the addon (v{bundled})." : $"Updated the addon to v{bundled}."));
                if (Wow.IsRunning(flavorDir))
                    Log.Warn(installed is null
                        ? $"[{Name}] The game is running: exit it fully and start it again so it finds the new addon."
                        : $"[{Name}] The game is running: type /reload in game to load the new addon version.");
            }
            else KeepInterfaceCurrent(installed, interfaceVersion);

            WriteAddonSettings(); // installing puts the default Settings.lua back
            AddonStep.Ok = true;
            AddonStep.Detail = $"v{bundled} installed. Type /wct test in game to check instant posting";
            AddonStep.ActionText = null;
        }
        catch (UnauthorizedAccessException)
        {
            AddonStep.Ok = false;
            AddonStep.Detail = "Windows blocked writing to the AddOns folder";
            AddonStep.ActionText = "Retry";
            Log.Error($"[{Name}] Couldn't write to {Wow.AddOnsDir(flavorDir)}. Right-click the app and pick Run as administrator once, then it works normally.");
        }
    }

    void KeepInterfaceCurrent(string installed, int? interfaceVersion)
    {
        if (flavorDir is null || interfaceVersion is not int v || AddonInstaller.InstalledInterface(flavorDir) == v.ToString()) return;
        try
        {
            AddonInstaller.SetInterface(flavorDir, v);
            Log.Info($"[{Name}] Marked the addon (v{installed}) as made for game version {v}, so the game loads it.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public async Task CheckDiscordAsync()
    {
        var active = main.Config.ActiveWebhooks(Flavor);
        if (active.Count == 0)
        {
            DiscordStep.Ok = false;
            DiscordStep.Detail = main.Config.Webhooks.Count == 0 ? "Add a webhook" : "Turn on a webhook for this tab";
            return;
        }

        var problems = new List<string>();
        foreach (var w in active)
        {
            var (name, problem) = await DiscordWebhook.CheckAsync(w.Url);
            if (name is null && !problem!.StartsWith("Couldn't reach") && !problem.Contains("too long"))
            {
                problems.Add(w.Name);
                Log.Warn($"[{Name}] \"{w.Name}\": {problem}");
            }
        }

        DiscordStep.Ok = problems.Count == 0;
        DiscordStep.Detail = problems.Count > 0 ? $"\"{problems[0]}\" doesn't work, see the console"
            : active.Count == 1 ? $"Posting to \"{active[0].Name}\""
            : $"Posting to {active.Count} webhooks";
    }

    TrackerData lastData = TrackerData.Empty;

    public void Refresh() => ShowData(lastData);

    // Retail characters are grouped by realm, Forever ones by ruleset (hardcore, self-found...).
    public string GroupLabel => Flavor == Flavors.Forever ? "Ruleset" : "Realm";
    string GroupOf(CharacterRow r) => Flavor == Flavors.Forever ? r.Ruleset : r.Realm;

    public ObservableCollection<GroupChip> Groups { get; } = [];
    public bool HasGroups => Groups.Count > 2; // "All" plus at least two groups

    GroupChip? selectedGroup;
    public GroupChip? SelectedGroup
    {
        get => selectedGroup;
        set
        {
            if (value is null || !Set(ref selectedGroup, value)) return;
            ShowData(lastData);
        }
    }

    void ShowData(TrackerData data)
    {
        lastData = data;
        var keep = SelectedCharacter?.Guid;
        var removed = Settings.Removed;
        // The made-up characters, when switched on (Forever only). They're only shown, never posted.
        if (main.ShowTestCharacters && Flavor == Flavors.Forever)
        {
            var test = TestCharacters.Build();
            data = new TrackerData([.. data.Events, .. test.Events], [.. data.Characters, .. test.Characters]);
        }
        var rows = CharacterRow.Build(data)
            .Where(r => !(removed.TryGetValue(r.Guid, out var at) && r.LastSeen <= at))
            .ToList();

        // Rebuild the chips, keeping the chosen one if it still exists.
        var chosen = selectedGroup?.Key ?? "";
        var chips = new List<GroupChip> { new("", $"All ({rows.Count})") };
        chips.AddRange(rows.GroupBy(GroupOf).OrderBy(g => g.Key)
            .Select(g => new GroupChip(g.Key, $"{(g.Key.Length > 0 ? g.Key : "Unknown")} ({g.Count()})")));
        Groups.Clear();
        foreach (var c in chips) Groups.Add(c);
        selectedGroup = Groups.FirstOrDefault(c => c.Key == chosen) ?? Groups[0];
        Raise(nameof(SelectedGroup));
        Raise(nameof(HasGroups));

        Characters.Clear();
        foreach (var r in rows.Where(r => selectedGroup.Key.Length == 0 || GroupOf(r) == selectedGroup.Key))
        {
            r.TagOptions = main.TagOptionsFor(r.Guid);
            Characters.Add(r);
            _ = r.LoadPictureAsync(Flavor);
            _ = r.LoadIconsAsync(Flavor);
        }
        SelectedCharacter = Characters.FirstOrDefault(c => c.Guid == keep);
        SelectCheckedIn(); // a character that just logged in wins
        Raise(nameof(HasCharacters));
        Raise(nameof(TotalGoldText));
        Raise(nameof(TotalCoins));
        UpdateStatus();
    }

    // "Total gold on Tarren Mill (3 characters):" followed by the coins, for what the filter shows.
    public string TotalGoldText
    {
        get
        {
            var where = selectedGroup is { Key.Length: > 0 } g ? $" on {g.Key}" : "";
            var count = Characters.Count;
            return $"Total gold{where} ({count} character{(count == 1 ? "" : "s")}):";
        }
    }

    public IReadOnlyList<Coin> TotalCoins => Coin.Full(Characters.Sum(c => c.Money));

    // Hides the character in the app. The game's own data isn't touched (the game would just write
    // it back), and playing the character again brings it back.
    public void RemoveCharacter(CharacterRow row)
    {
        var answer = MessageBox.Show(
            $"Remove {row.Name} from the list?\n\nIf you play {row.Name} again, it shows up again.",
            "Character Tracker", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        Settings.Removed[row.Guid] = Math.Max(row.LastSeen, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        main.Config.Save();
        Log.Info($"[{Name}] Removed {row.Name}. Playing it again brings it back.");
        ShowData(lastData);
    }

    void UpdateStatus()
    {
        if (tracker is null) { StatusText = "Waiting for the game folder"; return; }
        var count = Characters.Count;
        var chars = count == 0 ? "no characters yet" : $"{count} character{(count == 1 ? "" : "s")}";
        StatusText = lastPost is null
            ? $"Watching {Name}, {chars}"
            : $"Watching {Name}, {chars}, last post: {Tracker.Describe(lastPost)}";
    }

    // A sample of that kind of post for the Test buttons, with the selected (or latest) character.
    public Entry SampleEntry(string kind = "level") => SampleEvents.For(kind, Flavor, SelectedCharacter ?? Characters.FirstOrDefault(), Characters);

    public void Shutdown() => tracker?.Dispose();
}
