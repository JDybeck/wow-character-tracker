using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
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
        Items = new ItemsViewModel(ItemFor);
        _ = LoadLogoAsync();
        Shots = Flavors.ShotKinds.ToDictionary(k => k, k => new ShotOption(Settings, k, AddonSettingsChanged));
    }

    // Screenshot options per kind of event, bound as Shots[level].Take and so on.
    public Dictionary<string, ShotOption> Shots { get; }

    // Settings the addon reads (Settings.lua): saved, written for the addon, and the reload banner
    // shown if the game is running.
    void AddonSettingsChanged()
    {
        main.Config.Save();
        WriteAddonSettings();
        HintReload();
    }

    // The addon reads Settings.lua when it loads, so changes made while playing wait for a /reload.
    // A banner says so until the game has loaded them: a login check-in, the game writing its saved
    // file (it does on /reload and logout) or the game closing. (The app has no way to tell the game
    // to reload itself.)
    bool reloadNeeded;
    public bool ReloadNeeded { get => reloadNeeded; set => Set(ref reloadNeeded, value); }
    DateTime settingsWrittenAt;
    public ICommand DismissReloadCommand => new RelayCommand(_ => ReloadNeeded = false);

    void HintReload()
    {
        if (flavorDir is null || !Wow.IsRunning(flavorDir)) return;
        settingsWrittenAt = DateTime.Now;
        if (!ReloadNeeded) Log.Info($"[{Name}] Type /reload in game to use the new settings. Until then the game keeps the old ones for screenshots and instant posts.");
        ReloadNeeded = true;
    }

    void CheckReloaded()
    {
        if (!ReloadNeeded || flavorDir is null) return;
        if (!Wow.IsRunning(flavorDir) || NewestSavedFile() > settingsWrittenAt) ReloadNeeded = false;
    }

    DateTime NewestSavedFile()
    {
        var wtf = Path.Combine(flavorDir!, "WTF");
        if (!Directory.Exists(wtf)) return DateTime.MinValue;
        try
        {
            return Directory.EnumerateFiles(wtf, Wow.SavedVarsName, SearchOption.AllDirectories)
                .Select(File.GetLastWriteTime).DefaultIfEmpty(DateTime.MinValue).Max();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return DateTime.MinValue; }
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

    ImageSource? logo;
    public ImageSource? Logo { get => logo; private set { Set(ref logo, value); Raise(nameof(HasLogo)); } }
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
    public bool PostMaxLevel { get => Settings.PostMaxLevel; set { Settings.PostMaxLevel = value; Changed(); } }
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
    public bool HideUiInShots { get => Settings.HideUiInShots; set { Settings.HideUiInShots = value; Changed(); } }
    public bool AdvancedCombatLog { get => Settings.AdvancedCombatLog == true; set { Settings.AdvancedCombatLog = value; Changed(); } }
    public bool FlightReloadPrompt { get => Settings.FlightReloadPrompt; set { Settings.FlightReloadPrompt = value; Changed(); } }

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
        AddonSettingsChanged(); // the addon only sends live (and screenshots) what will be posted
        Raise(name!);
        Raise(nameof(LevelPreview));
    }

    public void ResetPosting()
    {
        var d = Flavor.Defaults();
        var s = Settings;
        (s.PostLevels, s.LevelSplit, s.LevelEveryBefore, s.LevelEveryAfter) = (d.PostLevels, d.LevelSplit, d.LevelEveryBefore, d.LevelEveryAfter);
        (s.PostRare, s.PostEpic, s.PostMounts, s.PostBosses, s.PostDeaths) = (d.PostRare, d.PostEpic, d.PostMounts, d.PostBosses, d.PostDeaths);
        (s.PostMaxLevel, s.PostDuels, s.PostMakgora, s.PostMPlus, s.PostPvp) = (d.PostMaxLevel, d.PostDuels, d.PostMakgora, d.PostMPlus, d.PostPvp);
        (s.MPlusSteps, s.PvpSteps) = (d.MPlusSteps, d.PvpSteps);
        s.Screenshot = new(d.Screenshot);
        s.PostScreenshot = new(d.PostScreenshot);
        PostingReplaced();
        Log.Info($"[{Name}] Posting settings reset to the defaults.");
    }

    // The What gets posted settings as a code to share (see PostingCode).
    public string PostingCodeText => PostingCode.Export(Settings, Flavor);

    // Applies a shared settings code to this tab.
    public void ApplyPostingCode(string text)
    {
        var (game, problem) = PostingCode.Import(text, Settings);
        if (problem is not null) { Log.Warn($"[{Name}] {problem}"); return; }
        PostingReplaced();
        var from = Flavors.All.FirstOrDefault(f => f.Key == game);
        Log.Success($"[{Name}] Pasted the posting settings" + (from is not null && from != Flavor ? $" (they were made for {from.Name})." : "."));
    }

    // After all posting settings changed at once: save, tell the addon, refresh the whole page.
    void PostingReplaced()
    {
        AddonSettingsChanged();
        foreach (var o in Shots.Values) o.Refresh();
        Raise(string.Empty); // every property
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
        tracker.DataChanged += data => MainViewModel.OnUi(() => { ShowData(data); CheckReloaded(); });
        tracker.Posted += e => MainViewModel.OnUi(() => { lastPost = e; UpdateStatus(); });
        tracker.LiveTest += _ => MainViewModel.OnUi(() => AddonStep.Detail = $"v{AddonInstaller.BundledVersion}, instant posting works");
        // Logging in on a game switches the app to that game's tab, with the character picked.
        tracker.CheckedIn += e => MainViewModel.OnUi(() =>
        {
            main.SelectedFlavor = this;
            ReloadNeeded = false; // a fresh login or /reload has read the new Settings.lua
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

        try
        {
            switch (force ? -1 : AddonInstaller.Compare(installed, bundled))
            {
                case null: // not installed: waits for the user's click
                    AddonStep.Set(ok: false, "Not installed in this game folder yet", action: "Install");
                    Log.Info($"[{Name}] The addon isn't installed in this game folder yet. Click Install on the Addon card to add it.");
                    return;

                case > 0: // newer than this app's (a test build put there by hand): left alone
                    KeepInterfaceCurrent(installed!, interfaceVersion);
                    AddonStep.Set(ok: true, $"v{installed} installed (newer than this app's v{bundled})");
                    break;

                case 0:
                    KeepInterfaceCurrent(installed!, interfaceVersion);
                    AddonStep.Set(ok: true, $"v{bundled} installed. Type /wct test in game to check instant posting");
                    break;

                default: // older (the app and the addon have to speak the same version), or the user clicked
                    AddonInstaller.Install(flavorDir, interfaceVersion);
                    Log.Success($"[{Name}] " + (installed is null ? $"Installed the addon (v{bundled})." : $"Updated the addon to v{bundled}."));
                    if (Wow.IsRunning(flavorDir))
                        Log.Warn(installed is null
                            ? $"[{Name}] The game is running: exit it fully and start it again so it finds the new addon."
                            : $"[{Name}] The game is running: type /reload in game to load the new addon version.");
                    AddonStep.Set(ok: true, $"v{bundled} installed. Type /wct test in game to check instant posting");
                    break;
            }
            WriteAddonSettings(); // installing puts the default Settings.lua back
        }
        catch (UnauthorizedAccessException)
        {
            AddonStep.Set(ok: false, "Windows blocked writing to the AddOns folder", action: "Retry");
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

    // Item names, icons and tooltips, one per item id for the whole tab (characters and the Items page).
    readonly Dictionary<int, ItemInfo> items = [];
    ItemInfo ItemFor(int id)
    {
        if (items.TryGetValue(id, out var info)) return info;
        items[id] = info = new ItemInfo(id, $"https://www.wowhead.com/{Flavor.WowheadPath}item={id}");
        _ = LoadItemAsync(info);
        return info;
    }
    async Task LoadItemAsync(ItemInfo info)
    {
        await info.LoadAsync(Flavor);
        Items.NameLoaded();
    }

    // The Items page: everything the tab's characters carry, searchable, per server.
    public ItemsViewModel Items { get; }

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
            data = new TrackerData([.. data.Events, .. test.Events], [.. data.Characters, .. test.Characters])
            {
                Inventories = [.. data.Inventories, .. test.Inventories],
                XpTable = data.XpTable.Count > 0 ? data.XpTable : test.XpTable,
            };
        }
        var keepTab = SelectedCharacter?.DetailTab;
        var rows = CharacterRow.Build(data, Flavor, ItemFor)
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
            r.SetPortraitChoice(main.Config.StandardPortraits.Contains(r.Guid), standard =>
            {
                main.Config.StandardPortraits.Remove(r.Guid);
                if (standard) main.Config.StandardPortraits.Add(r.Guid);
                main.Config.Save();
            });
            Characters.Add(r);
            _ = r.LoadPictureAsync(Flavor);
            _ = r.LoadIconsAsync(Flavor);
        }
        SelectedCharacter = Characters.FirstOrDefault(c => c.Guid == keep);
        if (SelectedCharacter is not null && keepTab is not null) SelectedCharacter.DetailTab = keepTab; // the list refreshes on every sync
        Items.Show(rows);
        SelectCheckedIn(); // a character that just logged in wins
        Raise(nameof(HasCharacters));
        Raise(nameof(TotalGoldText));
        Raise(nameof(TotalCoins));
        Raise(nameof(TotalPlayedText));
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

    // Played time of the characters the filter shows, added up.
    public string TotalPlayedText => Characters.Sum(c => c.Played) is var played and > 0 ? $"Total played: {Format.Played(played)}" : "";

    // Hides the character in the app. The game's own data isn't touched (the game would just write
    // it back), and playing the character again brings it back.
    public void RemoveCharacter(CharacterRow row)
    {
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
