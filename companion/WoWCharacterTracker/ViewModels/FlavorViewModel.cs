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
            ActionTooltip = $"Pick your World of Warcraft folder (usually {Wow.DefaultRoot}), or the {Wow.FolderHint(flavor)} folder inside it. Anything inside those works too.",
        };
        AddonStep = new SetupItem("Addon")
        {
            Detail = "Waiting for the game folder",
            ActionCommand = new RelayCommand(_ => SetUpAddon(force: true)),
        };
        DiscordStep = new SetupItem("Discord") { Detail = "Add a webhook" };
        Items = new ItemsViewModel(ItemFor);
        // Retail characters are filtered by realm, Forever ones by ruleset (hardcore, self-found...).
        Groups = new ChipFilter(flavor == Flavors.Forever ? "Ruleset" : "Realm", () => ShowData(lastData));
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
    public int Order => Flavors.All.ToList().IndexOf(Flavor); // tab order, for the slide direction (Ui/Motion)

    ImageSource? logo;
    public ImageSource? Logo { get => logo; private set { Set(ref logo, value); Raise(nameof(HasLogo)); } }
    public bool HasLogo => logo is not null;
    async Task LoadLogoAsync() => Logo = await Logos.LoadAsync(Flavor);
    FlavorSettings Settings => main.Config.For(Flavor);
    public Tracker? Tracker => tracker;
    public string? FolderPath => flavorDir; // the game folder in use
    // The folder in use, or for a game that's turned off the one it had (the Uninstall page lists what's in it).
    public string? KnownFolder => flavorDir ?? (Wow.IsFlavorDir(Settings.Path) ? Settings.Path : null);

    // The open game tab. Each tab keeps its own Characters and Items pages and only the open one
    // shows, so switching tabs doesn't build hundreds of rows and icons again.
    bool isSelected;
    public bool IsSelected { get => isSelected; set => Set(ref isSelected, value); }

    public SetupItem FolderStep { get; }
    public SetupItem AddonStep { get; }
    public SetupItem DiscordStep { get; }
    public IReadOnlyList<SetupItem> Steps => [FolderStep, AddonStep, DiscordStep];

    public ObservableCollection<CharacterRow> Characters { get; } = [];
    CharacterRow? selectedCharacter;
    public CharacterRow? SelectedCharacter
    {
        get => selectedCharacter;
        set { if (Set(ref selectedCharacter, value)) _ = value?.LoadIconsAsync(Flavor); } // only the opened one needs its icons
    }
    public bool HasCharacters => Characters.Count > 0;

    // Clicking the open page's tab again: that page as it is when first opened (the sort order is a
    // saved choice, so it stays).
    public void ResetPage(string page)
    {
        if (page == "characters")
        {
            SelectedCharacter = null;
            Groups.Reset();
            ShowData(lastData);
        }
        else if (page == "items") Items.Reset();
    }

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
    public bool ChatReloadReminder { get => Settings.ChatReloadReminder; set { Settings.ChatReloadReminder = value; Changed(); } }

    // Telling the guild and/or group in game chat (In game page; the addon's Announce.lua).
    public bool AnnounceGuild { get => Settings.AnnounceGuild; set { Settings.AnnounceGuild = value; Changed(); Raise(nameof(Announces)); } }
    public bool AnnounceParty { get => Settings.AnnounceParty; set { Settings.AnnounceParty = value; Changed(); Raise(nameof(Announces)); } }
    public bool Announces => AnnounceGuild || AnnounceParty;
    static readonly (string Kind, string Name, string Tip)[] AnnounceNames =
    [
        ("level", "Level-ups", "\"Ding! Level 23. 22 to 23 took 41m 12s.\""), ("maxlevel", "Max level", "\"Reached max level 60! Total played 6d 4h 12m.\""),
        ("boss", "Boss kills", "\"Killed Edwin VanCleef in The Deadmines!\" (without the group)"), ("loot", "Epic loot", "Epic and legendary items, as a link you can click"),
        ("mount", "Mounts", "\"Got a new mount: Swift Palomino!\""), ("duel", "Duel wins", "\"Won a duel against Grimtusk.\""),
        ("makgora", "Mak'gora wins", "\"Won a Mak'gora against Grimtusk!\""), ("bg", "Battleground wins", "\"Won Warsong Gulch!\""),
        ("mplus", "Mythic+", "Mythic+ rating steps (Retail)"), ("pvp", "PvP rating", "PvP rating steps (Retail)"), ("death", "Deaths", "Hardcore deaths"),
    ];
    ObservableCollection<ToggleOption>? announceKinds;
    public ObservableCollection<ToggleOption> AnnounceKinds => announceKinds ??=
        ToggleOption.Kinds(AnnounceNames.Where(k => IsRetail || k.Kind is not ("mplus" or "pvp")), Settings.AnnounceKinds, () => AddonSettingsChanged());

    // The data grid's corner (four radio buttons) and its label (In game page).
    public bool GridLabel { get => Settings.GridLabel; set { Settings.GridLabel = value; Changed(); } }
    public bool GridTopLeft { get => Corner == "TOPLEFT"; set { if (value) SetCorner("TOPLEFT"); } }
    public bool GridTopRight { get => Corner == "TOPRIGHT"; set { if (value) SetCorner("TOPRIGHT"); } }
    public bool GridBottomLeft { get => Corner == "BOTTOMLEFT"; set { if (value) SetCorner("BOTTOMLEFT"); } }
    public bool GridBottomRight { get => Corner == "BOTTOMRIGHT"; set { if (value) SetCorner("BOTTOMRIGHT"); } }
    string Corner => FlavorSettings.CornerOrDefault(Settings.GridCorner);
    void SetCorner(string corner)
    {
        if (Corner == corner) return;
        Settings.GridCorner = corner;
        Changed();
        Raise(nameof(GridTopLeft)); Raise(nameof(GridTopRight)); Raise(nameof(GridBottomLeft)); Raise(nameof(GridBottomRight));
    }

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

    // --- turning the game off (Config.TurnedOff) ---

    // Off: the app doesn't look for this game, read or watch its saved file, or install or update
    // the addon there. The tab stays, showing the Game off page.
    public bool IsOn => main.Config.IsOn(Flavor);
    public bool IsOff => !IsOn;

    // The settings menu's checkbox. The last game that's on can't be turned off.
    public bool TrackThis { get => IsOn; set { if (value) TurnOn(); else TurnOff(); } }
    public bool CanToggle => IsOff || main.FlavorTabs.Any(t => t != this && t.IsOn);

    public ICommand TurnOnCommand => new RelayCommand(_ => TurnOn());
    public ICommand RemoveAddonCommand => new RelayCommand(_ => RemoveAddon());

    // The game is off but the addon is still in its AddOns folder (from before), so it still saves
    // data and takes instant-post shots in game. Only checked where the folder is already known.
    public bool AddonLeftInstalled => IsOff && Settings.Path is { } p && Wow.IsFlavorDir(p) && AddonInstaller.InstalledVersion(p) is not null;
    public string AddonLeftPath => Settings.Path is { } p ? AddonInstaller.TargetDir(p) : "";

    void TurnOff()
    {
        if (IsOff || !CanToggle) return;
        main.Config.TurnedOff.Add(Flavor.Key);
        main.Config.Save();
        tracker?.Dispose();
        tracker = null;
        flavorDir = null;
        ReloadNeeded = false;
        ShowData(TrackerData.Empty);
        StatusText = $"{Name} is turned off";
        Log.Info($"[{Name}] Turned off: the app no longer looks at {Name}." + (AddonLeftInstalled ? " The addon is still installed there; remove it on the tab if you like." : ""));
        Toggled();
    }

    void TurnOn()
    {
        if (IsOn) return;
        main.Config.TurnedOff.Remove(Flavor.Key);
        main.Config.Save();
        Log.Info($"[{Name}] Turned on.");
        Toggled();
        Start();
    }

    void RemoveAddon()
    {
        if (Settings.Path is not { } path) return;
        try
        {
            AddonInstaller.Remove(path);
            Log.Success($"[{Name}] Removed the addon from {AddonInstaller.TargetDir(path)}. The data it saved stays in the game's WTF folder.");
            if (Wow.IsRunning(path)) Log.Warn($"[{Name}] The game is running: it keeps the addon loaded until you exit it.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"[{Name}] Couldn't remove the addon: {ex.Message}");
        }
        Raise(nameof(AddonLeftInstalled));
    }

    void Toggled()
    {
        foreach (var tab in main.FlavorTabs) tab.RaiseToggle();
        CommandManager.InvalidateRequerySuggested();
    }

    void RaiseToggle()
    {
        Raise(nameof(IsOn)); Raise(nameof(IsOff)); Raise(nameof(TrackThis)); Raise(nameof(CanToggle)); Raise(nameof(AddonLeftInstalled));
    }

    public void Start()
    {
        if (IsOff)
        {
            StatusText = $"{Name} is turned off";
            return;
        }
        var path = Wow.IsFlavorDir(Settings.Path) ? Settings.Path : Wow.Detect(Flavor);
        if (path is null)
        {
            FolderStep.Ok = false;
            FolderStep.Detail = $"Not found. Click Browse and pick your World of Warcraft folder (usually {Wow.DefaultRoot}); the app finds {Wow.FolderHint(Flavor)} in it.";
            StatusText = "Waiting for the game folder";
            Log.Warn($"[{Name}] Couldn't find the game folder. Click Browse on the Game folder card and pick your World of Warcraft folder, or the {Wow.FolderHint(Flavor)} folder inside it.");
            return;
        }
        UseFolder(path);
    }

    // The folder picker. Any of these work: the World of Warcraft folder, this game's folder in it, or
    // anything inside that (Wow.Resolve finds the right one, and says why when it can't).
    void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = $"Pick your World of Warcraft folder (or the {Wow.FolderHint(Flavor)} folder inside it)",
            InitialDirectory = flavorDir is not null ? Path.GetDirectoryName(flavorDir) : Wow.LikelyRoot(),
        };
        if (dialog.ShowDialog() != true) return;
        var (path, problem) = Wow.Resolve(dialog.FolderName, Flavor);
        if (path is null)
        {
            Log.Warn($"[{Name}] {problem}");
            if (flavorDir is null) FolderStep.Detail = problem ?? FolderStep.Detail;
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
            guid => main.Config.TagsOf(guid), w => main.PosterFor(this, w), main.Backfill);
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
        if (IsOff) return;
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

    // Names, icons and tooltips of items, crests and talents, one per type and id for the whole tab
    // (characters and the Items page).
    readonly Dictionary<(string Type, int Id), ItemInfo> items = [];
    Dictionary<int, int> itemClasses = [];
    ItemInfo ItemFor(int id) => InfoFor("item", id);
    ItemInfo InfoFor(string type, int id)
    {
        if (items.TryGetValue((type, id), out var info)) return info;
        items[(type, id)] = info = new ItemInfo(id, $"https://www.wowhead.com/{Flavor.WowheadPath}{type}={id}", type);
        if (type == "item" && itemClasses.TryGetValue(id, out var c)) info.ClassId = c;
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

    // The realm (Retail) or ruleset (Forever) chips over the character list.
    public ChipFilter Groups { get; }
    string GroupOf(CharacterRow r) => Flavor == Flavors.Forever ? r.Ruleset : r.Realm;

    // The list's order: clicking a column header (or race, played, rating) sorts by it, again flips it.
    CharacterSort? sort;
    public CharacterSort Sort => sort ??= CharacterSort.Parse(main.Config.CharacterSort.GetValueOrDefault(Flavor.Key));
    public ICommand SortCommand => sortCommand ??= new RelayCommand(p =>
    {
        if (p is not string key) return;
        Sort.Click(key);
        main.Config.CharacterSort[Flavor.Key] = Sort.ToString();
        main.Config.Save();
        ShowData(lastData);
    });
    ICommand? sortCommand;

    // Every character of the tab, whatever the filter shows (posts look up the one an event is about).
    List<CharacterRow> allRows = [];
    public CharacterRow? RowFor(string guid) => allRows.FirstOrDefault(r => r.Guid == guid);

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
                ItemClasses = data.ItemClasses,
            };
        }
        itemClasses = data.ItemClasses;
        foreach (var (id, c) in itemClasses) if (items.TryGetValue(("item", id), out var known)) known.ClassId = c;
        var keepTab = SelectedCharacter?.DetailTab;
        Motion.Quiet(); // the rows are rebuilt: what's open stays still
        var rows = CharacterRow.Build(data, Flavor, InfoFor)
            .Where(r => !(removed.TryGetValue(r.Guid, out var at) && r.LastSeen <= at))
            .ToList();

        allRows = rows;
        Groups.Fill(rows, GroupOf);

        Characters.Clear();
        foreach (var r in Sort.Apply(rows.Where(r => Groups.Matches(GroupOf(r)))))
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
        }
        SelectedCharacter = Characters.FirstOrDefault(c => c.Guid == keep);
        if (SelectedCharacter is not null && keepTab is not null) SelectedCharacter.DetailTab = keepTab; // the list refreshes on every sync
        Items.Show(rows);
        main.Stream.Update(Flavor, rows, data.Events);
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
            var where = Groups.Key.Length > 0 ? $" on {Groups.Key}" : "";
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
