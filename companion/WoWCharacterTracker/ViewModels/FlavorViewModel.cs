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
            Headline = $"Choose your {flavor.Name} folder",
            Why = "The app reads your characters from the game folder and installs its addon there. Until it has one, nothing is tracked or posted.",
            Icon = "",
        };
        AddonStep = new SetupItem("Addon")
        {
            Detail = "Waiting for the game folder",
            ActionCommand = new RelayCommand(_ => SetUpAddon(force: true)),
            Headline = $"Install the addon for {flavor.Name}",
            Why = "The addon notices your dings, kills, loot and mounts in game and hands them to the app. Nothing is written to the game folder until you click Install.",
            Icon = "",
        };
        DiscordStep = new SetupItem("Discord") { Detail = "Add a webhook" };
        WatchSetup();
        Lookup = new GameLookup(flavor, NameLoaded);
        Items = new ItemsViewModel(Lookup.Item);
        Home = new HomeViewModel(flavor, Lookup);
        FollowedItems = new FollowedItemsViewModel(flavor, Lookup, () => Settings, FollowedChanged);
        Friends = new FriendsViewModel(flavor, Lookup, main.Config, () => Settings, AddonSettingsChanged, main.Config.Save);
        Friends.OrderChanged += main.SetFriendOrder; // dragged: every tab and every game's addon
        // Retail characters are filtered by realm, Forever ones by ruleset (hardcore, self-found...).
        Groups = new ChipFilter(flavor.GroupLabel, () => ShowData(lastData));
        FactionChips = new ChipFilter("Faction", () => ShowData(lastData)); // hidden while all are on one side
        _ = LoadLogoAsync();
    }

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

    // The newest of the files the tracker reads (one per account).
    DateTime NewestSavedFile()
    {
        try
        {
            return Wow.Accounts(flavorDir!)
                .Select(account => Wow.SavedVarsFile(flavorDir!, account))
                .Where(File.Exists)
                .Select(File.GetLastWriteTime)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return DateTime.MinValue; }
    }

    public void WriteAddonSettings()
    {
        if (flavorDir is null) return;
        try
        {
            AddonInstaller.WriteSettings(flavorDir, Settings, main.Config.Notes(), main.Config.ActiveWebhooks(Flavor).Count > 0, main.Config.FriendNicknames, main.Updates.Available,
                SharingPresets.Detect(Settings, Flavor), main.Config.FriendNicknamesAt,
                new GameFriendOrder(main.Config.FriendOrder, main.Config.FriendOrderAt), Flavor);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"[{Name}] Couldn't save the screenshot settings for the addon: {ex.Message}");
        }
    }

    // The addon window's portraits (AddonPortraits): your characters, newest first, then your
    // friends' (the ones you keep and haven't hidden), each with the picture the app shows for it.
    // Written a few seconds after a sync, one run at a time, so a burst of syncs makes one run.
    bool portraitsQueued;
    readonly SemaphoreSlim portraitsRun = new(1, 1);

    void QueueAddonPortraits()
    {
        if (portraitsQueued || flavorDir is null) return;
        portraitsQueued = true;
        _ = WriteAddonPortraitsAsync();
    }

    async Task WriteAddonPortraitsAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(3));
        await portraitsRun.WaitAsync();
        try
        {
            portraitsQueued = false; // a sync from here on queues the next run
            if (flavorDir is not { } dir) return;
            var standard = main.Config.StandardPortraits.ToHashSet(StringComparer.Ordinal);
            var who = allRows.Where(r => !r.IsTest).OrderByDescending(r => r.LastSeen)
                .Concat(Friends.Shown.OrderByDescending(r => r.LastSeen))
                .Take(AddonPortraits.SlotCount)
                .Select(r => (r.Picture, r.IsFriend, Standard: standard.Contains(r.Guid), r.ArmorySd))
                .ToList();
            var pictures = new List<(string Guid, byte[] Tga)>();
            foreach (var c in who)
                if (await CharacterPicture.ShownAsync(Flavor, c.Picture, c.IsFriend, c.Standard, c.ArmorySd) is { } source
                    && await AddonPortraits.ConvertAsync(source) is { } tga)
                    pictures.Add((c.Picture.Guid, tga));
            var made = await Task.Run(() => AddonPortraits.Write(dir, pictures));
            if (made && Wow.IsRunning(dir))
                Log.Info($"[{Name}] Characters' portraits are ready for the addon window. Exit the game fully and start it again once to see them (after that, new ones show on /reload).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"[{Name}] Couldn't save the characters' portraits for the addon: {ex.Message}");
        }
        finally { portraitsRun.Release(); }
    }

    public Flavor Flavor { get; }
    public string Name => Flavor.Name;
    public int Order => Flavors.All.ToList().IndexOf(Flavor); // tab order, for the slide direction (Ui/Motion)

    ImageSource? logo;
    public ImageSource? Logo { get => logo; private set { Set(ref logo, value); Raise(nameof(HasLogo)); } }
    public bool HasLogo => logo is not null;
    // Blizzard's art, so it's never shipped: downloaded once and kept (ImageCache). Without a connection the tab shows its name.
    async Task LoadLogoAsync() => Logo = Flavor.LogoUrl.Length > 0 ? await ImageCache.LoadAsync(Flavor.LogoUrl) : null;
    FlavorSettings Settings => main.Config.For(Flavor);
    public Tracker? Tracker => tracker;
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
    // Without a game folder, or with the addon missing, the app can't track anything: that check gets
    // the big card over the pages (Views/SetupCard), the folder first since the addon goes into it.
    public SetupItem? Blocker => !FolderStep.Ok ? FolderStep : !AddonStep.Ok ? AddonStep : null;
    // The other checks that need something done (Discord): the setup bar spells these out, all fine is
    // one quiet line. The ones with a big card (a Headline) only show as chips there.
    public IReadOnlyList<SetupItem> SetupProblems => Steps.Where(s => !s.Ok && s.Headline is null).ToList();
    public bool HasSetupProblems => SetupProblems.Count > 0;

    void WatchSetup()
    {
        foreach (var step in Steps)
            step.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(SetupItem.Ok)) return;
                foreach (var name in new[] { nameof(Blocker), nameof(SetupProblems), nameof(HasSetupProblems) }) Raise(name);
            };
    }

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
    // A character from Home: open on the Characters page, its filters reset when they hide it.
    public void OpenCharacter(CharacterRow row)
    {
        if (!Characters.Any(c => c.Guid == row.Guid)) ResetPage("characters");
        SelectedCharacter = Characters.FirstOrDefault(c => c.Guid == row.Guid);
    }

    public void ResetPage(string page)
    {
        if (page == "characters")
        {
            SelectedCharacter = null;
            Groups.Reset();
            FactionChips.Reset();
            ShowData(lastData);
        }
        else if (page == "items") Items.Reset();
        else if (page == "followed") FollowedItems.Reset();
        else if (page == "friends") Friends.Reset();
    }

    string statusText = "Starting up...";
    public string StatusText { get => statusText; set => Set(ref statusText, value); }

    // --- what gets posted (bound two-way from the settings page) ---

    public bool PostLevels { get => Settings.PostLevels; set { Settings.PostLevels = value; InGameChanged(); } }
    // The level steps and the two filters can be changed in the addon too (Settings.lua `filters`).
    public int LevelSplit { get => Settings.LevelSplit; set { Settings.LevelSplit = Math.Clamp(value, 1, 100); MarkInGameChanged(); TypedChanged(); } }
    public int LevelEveryBefore { get => Settings.LevelEveryBefore; set { Settings.LevelEveryBefore = Math.Clamp(value, 1, 100); MarkInGameChanged(); TypedChanged(); } }
    public int LevelEveryAfter { get => Settings.LevelEveryAfter; set { Settings.LevelEveryAfter = Math.Clamp(value, 1, 100); MarkInGameChanged(); TypedChanged(); } }
    public bool PostRare { get => Settings.PostRare; set { Settings.PostRare = value; InGameChanged(); } }
    public bool RareBoeOnly { get => Settings.RareBoeOnly; set { Settings.RareBoeOnly = value; InGameChanged(); } }
    public bool PostBattlegrounds { get => Settings.PostBattlegrounds; set { Settings.PostBattlegrounds = value; InGameChanged(); } }
    public bool PostProfessions { get => Settings.PostProfessions; set { Settings.PostProfessions = value; InGameChanged(); } }
    public bool PostEpic { get => Settings.PostEpic; set { Settings.PostEpic = value; InGameChanged(); } }
    public bool PostMounts { get => Settings.PostMounts; set { Settings.PostMounts = value; InGameChanged(); } }
    public bool PostBosses { get => Settings.PostBosses; set { Settings.PostBosses = value; InGameChanged(); } }
    public bool PostDeaths { get => Settings.PostDeaths; set { Settings.PostDeaths = value; InGameChanged(); } }
    public bool FirstBossKillOnly { get => Settings.FirstBossKillOnly; set { Settings.FirstBossKillOnly = value; InGameChanged(); } }
    public bool PostMaxLevel { get => Settings.PostMaxLevel; set { Settings.PostMaxLevel = value; InGameChanged(); } }
    public bool PostDuels { get => Settings.PostDuels; set { Settings.PostDuels = value; InGameChanged(); } }
    public bool PostMakgora { get => Settings.PostMakgora; set { Settings.PostMakgora = value; InGameChanged(); } }
    public bool PostMPlus { get => Settings.PostMPlus; set { Settings.PostMPlus = value; InGameChanged(); } }
    public bool PostPvp { get => Settings.PostPvp; set { Settings.PostPvp = value; InGameChanged(); } }
    public bool PostKeyRuns { get => Settings.PostKeyRuns; set { Settings.PostKeyRuns = value; InGameChanged(); } }
    public int KeyRunMinLevel { get => Settings.KeyRunMinLevel; set { Settings.KeyRunMinLevel = Math.Clamp(value, FlavorSettings.KeyLevelMin, FlavorSettings.KeyLevelMax); TypedChanged(); } }
    public bool KeyRunTimedOnly { get => Settings.KeyRunTimedOnly; set { Settings.KeyRunTimedOnly = value; Changed(); } }
    public bool IsRetail => Flavor.HasRatings; // Mythic+ runs (instead of end boss kills) and the rating options

    // Rating and profession milestones as "1500, 2000, 2500": whole numbers, sorted, typos ignored.
    public string ProfessionStepsText { get => string.Join(", ", Settings.ProfessionSteps); set { Settings.ProfessionSteps = ParseSteps(value, Settings.ProfessionSteps); Changed(); } }
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
    public bool FlightReloadPrompt { get => Settings.FlightReloadPrompt; set { Settings.FlightReloadPrompt = value; InGameChanged(); } }
    public bool ChatReloadReminder { get => Settings.ChatReloadReminder; set { Settings.ChatReloadReminder = value; InGameChanged(); } }
    public bool KeepAllHistory { get => Settings.KeepAllHistory; set { Settings.KeepAllHistory = value; InGameChanged(); } }

    // A setting that can be changed in game too changed here: the newest change wins (FlavorSettings.TakeInGame).
    void InGameChanged([CallerMemberName] string? name = null)
    {
        MarkInGameChanged();
        Changed(name);
    }

    void MarkInGameChanged() => Settings.InGameAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // The In game settings as changed in the addon's options page, when that's the newest change.
    void TakeInGameSettings(InGameSettings? game)
    {
        if (!SharingPresets.TakeInGame(Settings, Flavor, game, out var applied)) return;
        if (applied is not null) Log.Info($"[{Name}] Sharing set to {applied} in game.");
        main.Config.Save();
        WriteAddonSettings();
        RefreshAskSeconds();
        SharingChanged();
        foreach (var name in new[] { nameof(AnnounceGuild), nameof(AnnounceParty), nameof(Announces),
            nameof(FlightReloadPrompt), nameof(ChatReloadReminder), nameof(KeepAllHistory), nameof(GridLabel),
            nameof(GridTopLeft), nameof(GridTopRight), nameof(GridBottomLeft), nameof(GridBottomRight),
            nameof(DistanceAuto), nameof(DistanceMeters), nameof(DistanceYards),
            nameof(LevelSplit), nameof(LevelEveryBefore), nameof(LevelEveryAfter), nameof(LevelPreview), nameof(RareBoeOnly), nameof(FirstBossKillOnly) })
            Raise(name);
        Log.Info($"[{Name}] Took the In game settings you changed in the game.");
    }

    // The Sharing table (Models/Moments): one row per moment this game has.
    IReadOnlyList<SharingRow>? sharingRows;
    public IReadOnlyList<SharingRow> SharingRows => sharingRows ??=
        [.. Moments.For(Flavor).Select(m => new SharingRow(m, Settings, () => main.Config.StreamOptions.AlertKinds,
            () => InGameChanged(nameof(Preset)), () => InGameChanged(nameof(Preset)), main.Stream.AlertsChanged))];

    // How much gets shared: Quiet, Normal or Everything, or Custom when the table matches none of them.
    // Picking one over custom choices asks first (ConfirmPreset).
    public string Preset => SharingPresets.Detect(Settings, Flavor);
    public bool IsCustom => Preset == SharingPresets.Custom;
    public bool PresetQuiet { get => Preset == SharingPresets.Quiet; set { if (value) PickPreset(SharingPresets.Quiet); } }
    public bool PresetNormal { get => Preset == SharingPresets.Normal; set { if (value) PickPreset(SharingPresets.Normal); } }
    public bool PresetEverything { get => Preset == SharingPresets.Everything; set { if (value) PickPreset(SharingPresets.Everything); } }

    string? pendingPreset;
    public bool ConfirmPreset => pendingPreset is not null;
    public ICommand ConfirmPresetCommand => new RelayCommand(_ => { if (pendingPreset is { } key) ApplyPreset(key); });
    public ICommand CancelPresetCommand => new RelayCommand(_ => { pendingPreset = null; PresetChanged(); });

    void PickPreset(string key)
    {
        if (Preset == key) return;
        if (IsCustom)
        {
            pendingPreset = key;
            PresetChanged();
            return;
        }
        ApplyPreset(key);
    }

    void ApplyPreset(string key)
    {
        pendingPreset = null;
        SharingPresets.Apply(Settings, Flavor, key);
        PostingReplaced();
        Log.Info($"[{Name}] Sharing set to {key}.");
    }

    void PresetChanged()
    {
        foreach (var name in new[] { nameof(Preset), nameof(IsCustom), nameof(PresetQuiet), nameof(PresetNormal), nameof(PresetEverything), nameof(ConfirmPreset) })
            Raise(name);
    }

    // Which place's own settings show under the Sharing page's top row: "discord" (webhooks), "stream"
    // (the overlay) or "" (none). One at a time; clicking the open one closes it.
    string openPlace = "";
    public string OpenPlace
    {
        get => openPlace;
        set
        {
            if (!Set(ref openPlace, value)) return;
            Raise(nameof(DiscordOpen));
            Raise(nameof(StreamOpen));
            if (value == "stream") main.Stream.PanelOpen = true; // its options, not just its header
        }
    }
    public bool DiscordOpen => openPlace == "discord";
    public bool StreamOpen => openPlace == "stream";
    public ICommand TogglePlaceCommand => new RelayCommand(p => OpenPlace = OpenPlace == (string)p! ? "" : (string)p!);

    // The Sharing page's fold: the table opens by itself when the choices are custom (Advanced is always open).
    bool? showCustomize;
    public bool ShowCustomize { get => showCustomize ??= IsCustom; set { showCustomize = value; Raise(nameof(ShowCustomize)); } }
    public ICommand ToggleCustomizeCommand => new RelayCommand(_ => ShowCustomize = !ShowCustomize);

    // The table's screenshot box takes and attaches; off here keeps them in the Screenshots folder only.
    public bool AttachShots
    {
        get => EventKinds.ShotKinds.Where(Settings.Shoots).All(Settings.PostsShot);
        set
        {
            Settings.SetAttachShots(value);
            Changed();
        }
    }

    // The Discord switch: off pauses this game's posts without forgetting its webhooks.
    public bool DiscordOn
    {
        get => !Settings.DiscordPaused;
        set
        {
            Settings.DiscordPaused = !value;
            main.Config.Save();
            Raise(nameof(DiscordOn));
            Log.Info(value ? $"[{Name}] Posting to Discord again." : $"[{Name}] Discord posts paused. What happens meanwhile won't be posted later.");
        }
    }

    // --- the weekly recap (Advanced on Sharing; WeeklyRecap) ---

    // Turning it on, or moving its day or hour, starts from the next planned time: a time already
    // passed this week doesn't post right away (Post now does).
    public bool PostRecap
    {
        get => Settings.PostRecap;
        set
        {
            Settings.PostRecap = value;
            if (value) SkipToNextRecap();
            main.Config.Save();
            Raise(nameof(PostRecap));
            Raise(nameof(RecapText));
        }
    }

    public int RecapHour
    {
        get => Settings.RecapHour;
        set
        {
            Settings.RecapHour = Math.Clamp(value, 0, 23);
            SkipToNextRecap();
            main.SaveSoon();
            Raise(nameof(RecapHour));
            Raise(nameof(RecapText));
        }
    }

    IReadOnlyList<DayChoice>? recapDays;
    // Monday first, like the game's calendar in Europe.
    public IReadOnlyList<DayChoice> RecapDays => recapDays ??=
        [.. Enumerable.Range(1, 7).Select(i => (DayOfWeek)(i % 7)).Select(d => new DayChoice(d, $"recapday-{Name}", () => Settings.RecapDay == d, () =>
        {
            Settings.RecapDay = d;
            SkipToNextRecap();
            main.Config.Save();
            foreach (var c in RecapDays) c.Refresh();
            Raise(nameof(RecapText));
        }))];

    void SkipToNextRecap() =>
        Settings.RecapSentAt = WeeklyRecap.LastSlot(DateTimeOffset.Now, Settings.RecapDay, Settings.RecapHour).ToUnixTimeSeconds();

    // "Next one: Sunday 23 November at 20:00."
    public string RecapText
    {
        get
        {
            if (!Settings.PostRecap) return "Every week, one post to this game's webhooks with what each of your characters did: levels, epics, mounts, bosses, quests, gold and more.";
            var next = WeeklyRecap.LastSlot(DateTimeOffset.Now, Settings.RecapDay, Settings.RecapHour).AddDays(7);
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return $"Next one: {next.ToString("dddd d MMMM", c)} at {next.ToString("HH:mm", c)}. If the app is closed then, it posts when it opens (up to two days late).";
        }
    }

    bool postingRecap;

    // Checked every few minutes (MainViewModel): posts the week's recap when its time has come. Marked
    // sent once it went out (or the week had nothing), so a failed post tries again on the next check.
    public async Task PostRecapIfDueAsync(DateTimeOffset now)
    {
        if (!Settings.PostRecap || postingRecap || !IsOn || Stats.Yours is null) return;
        if (WeeklyRecap.Due(now, Settings.RecapDay, Settings.RecapHour, Settings.RecapSentAt) is not { } slot) return;
        if (await PostRecapAsync(slot) is not false)
        {
            Settings.RecapSentAt = slot.ToUnixTimeSeconds();
            main.Config.Save();
        }
    }

    // Post now (the recap's button): the last seven days, right away.
    public ICommand PostRecapNowCommand => postRecapNow ??= new RelayCommand(_ => PostRecapAsync(DateTimeOffset.Now));
    ICommand? postRecapNow;

    // True: sent to at least one webhook; null: nothing to send (no webhook, paused, an empty week); false: it failed.
    async Task<bool?> PostRecapAsync(DateTimeOffset at)
    {
        if (Settings.DiscordPaused)
        {
            Log.Info($"[{Name}] Discord posts are paused, so the weekly recap wasn't posted.");
            return null;
        }
        var hooks = main.Config.ActiveWebhooks(Flavor);
        if (hooks.Count == 0)
        {
            Log.Warn($"[{Name}] No webhook for the weekly recap. Turn one on under Sharing.");
            return null;
        }
        var weeks = Stats.Yours is { } yours
            ? WeeklyRecap.Build(allRows.Where(r => !r.IsTest).Select(r => new WeeklyRecap.Character(r.Guid, Format.Name(r.Name, r.Surname, Settings.NameStyle), r.Level)),
                yours.Events, yours.Days, at)
            : [];
        if (WeeklyRecap.Embed(weeks, Flavor.Name, at) is not { } embed)
        {
            Log.Info($"[{Name}] None of your characters did anything this week, so there's no weekly recap.");
            return null;
        }
        postingRecap = true;
        try
        {
            var sent = false;
            foreach (var w in hooks)
                if (await main.PosterFor(this, w).PostRecapAsync(embed))
                {
                    sent = true;
                    Log.Success($"[{Name}] Posted the weekly recap to \"{w.Name}\".");
                }
            return sent;
        }
        finally { postingRecap = false; }
    }

    // A character's note changed: the addon gets it in Settings.lua for /wct note. No /reload hint
    // for a note that came from the game: it has it already.
    public void NotesChanged(bool hint = true)
    {
        if (!IsOn) return;
        WriteAddonSettings();
        if (hint) HintReload();
    }

    // Telling the guild and/or group in game chat (In game page; the addon's Announce.lua).
    public bool AnnounceGuild { get => Settings.AnnounceGuild; set { Settings.AnnounceGuild = value; InGameChanged(); Raise(nameof(Announces)); } }
    public bool AnnounceParty { get => Settings.AnnounceParty; set { Settings.AnnounceParty = value; InGameChanged(); Raise(nameof(Announces)); } }
    public bool Announces => (ShowGuildChat && AnnounceGuild) || AnnounceParty;
    public bool ShowGuildChat => FlavorSettings.GuildChatOn;
    public int PlaceColumns => ShowGuildChat ? 4 : 3; // the Sharing page's place cards
    // The data grid's corner (four radio buttons) and its label (In game page).
    public bool GridLabel { get => Settings.GridLabel; set { Settings.GridLabel = value; InGameChanged(); } }
    public bool GridTopLeft { get => Corner == "TOPLEFT"; set { if (value) SetCorner("TOPLEFT"); } }
    public bool GridTopRight { get => Corner == "TOPRIGHT"; set { if (value) SetCorner("TOPRIGHT"); } }
    public bool GridBottomLeft { get => Corner == "BOTTOMLEFT"; set { if (value) SetCorner("BOTTOMLEFT"); } }
    public bool GridBottomRight { get => Corner == "BOTTOMRIGHT"; set { if (value) SetCorner("BOTTOMRIGHT"); } }
    // How long the in-game post box stays: one chip per choice, picked like radio buttons.
    IReadOnlyList<SecondsChoice>? askSecondsChoices;
    public IReadOnlyList<SecondsChoice> AskSecondsChoices => askSecondsChoices ??=
        [.. FlavorSettings.AskSecondsChoices.Select(n => new SecondsChoice(n, $"askseconds-{Name}",
            () => Settings.AskSeconds == n, () => { Settings.AskSeconds = n; InGameChanged(nameof(AskSecondsChoices)); RefreshAskSeconds(); }))];
    void RefreshAskSeconds() { foreach (var c in AskSecondsChoices) c.Refresh(); }

    // The lowest standing a reputation milestone posts (Friendly to Exalted), each standing above it too.
    IReadOnlyList<StandingChoice>? reputationFromChoices;
    public IReadOnlyList<StandingChoice> ReputationFromChoices => reputationFromChoices ??=
        [.. Enumerable.Range(Reputations.LowestStep, Reputations.Exalted - Reputations.LowestStep + 1).Select(n => new StandingChoice(n, $"reputationfrom-{Name}",
            () => Settings.ReputationFrom == n, () =>
            {
                Settings.ReputationFrom = n;
                Changed(nameof(ReputationFromChoices));
                foreach (var c in ReputationFromChoices) c.Refresh();
            }))];

    // Distances to friends on the in-game map: three chips that act like radio buttons.
    public bool DistanceAuto { get => DistanceUnit == "auto"; set { if (value) SetDistanceUnit("auto"); } }
    public bool DistanceMeters { get => DistanceUnit == "meters"; set { if (value) SetDistanceUnit("meters"); } }
    public bool DistanceYards { get => DistanceUnit == "yards"; set { if (value) SetDistanceUnit("yards"); } }
    string DistanceUnit => FlavorSettings.DistanceUnitOrDefault(Settings.DistanceUnit);
    void SetDistanceUnit(string unit)
    {
        if (DistanceUnit == unit) return;
        Settings.DistanceUnit = unit;
        InGameChanged();
        Raise(nameof(DistanceAuto)); Raise(nameof(DistanceMeters)); Raise(nameof(DistanceYards));
    }

    string Corner => FlavorSettings.CornerOrDefault(Settings.GridCorner);
    void SetCorner(string corner)
    {
        if (Corner == corner) return;
        Settings.GridCorner = corner;
        InGameChanged();
        Raise(nameof(GridTopLeft)); Raise(nameof(GridTopRight)); Raise(nameof(GridBottomLeft)); Raise(nameof(GridBottomRight));
    }

    // Name in posts (Forever has surnames): three chips that act like radio buttons.
    public bool HasSurnames => Flavor.HasSurnames;
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
            main.SaveSoon(); // typed
            Raise(nameof(SkipBossGroupRegex));
            Raise(nameof(SkipRegexProblem));
        }
    }

    public string SkipRegexProblem => Settings.SkipRegexValid ? "" : "That isn't a valid pattern, so nothing gets skipped.";

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
        SharingChanged();
    }

    // Rules under the table's names and which preset matches follow every posting change.
    void SharingChanged()
    {
        foreach (var row in sharingRows ?? []) row.Refresh();
        PresetChanged();
    }

    // Like Changed, for boxes that change with every keystroke: the config and the addon's Settings.lua
    // are written a moment after the last one (MainViewModel.SaveSoon), the preview right away.
    void TypedChanged([CallerMemberName] string? name = null)
    {
        main.SaveSoon(addonSettings: true);
        Raise(name!);
        Raise(nameof(LevelPreview));
        SharingChanged();
    }

    public void ResetPosting()
    {
        Settings.CopyPostingFrom(Flavor.Defaults());
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
        MarkInGameChanged(); // the filters, chat and ask lists changed too
        AddonSettingsChanged();
        SharingChanged();
        foreach (var c in reputationFromChoices ?? []) c.Refresh(); // chips cache their own state
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
        OldFolderNote = null;
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
        var saved = Wow.IsFlavorDir(Settings.Path) ? Settings.Path : null;
        var path = saved is not null ? Wow.NewestFolder(saved, Flavor) : Wow.Detect(Flavor);
        if (path is null)
        {
            ShowFolderMissing();
            Log.Warn($"[{Name}] Couldn't find the game folder. Click Browse and pick your World of Warcraft folder, or the {Wow.FolderHint(Flavor)} folder inside it.");
            return;
        }
        // Moved on from a beta folder: the addon goes along when it was installed there (the user said yes once already).
        var moved = saved is not null && path != saved;
        if (moved) Log.Info($"[{Name}] Found {Path.GetFileName(path)} next to {Path.GetFileName(saved)}, using it from now on.");
        UseFolder(path, installAddon: moved && AddonInstaller.InstalledVersion(saved!) is not null);
    }

    // On an older folder for this game (Forever's beta, which is over): a banner on every tab says so
    // (MainViewModel.OldFolderTab) until the user picks the live folder.
    string? oldFolderNote;
    public string? OldFolderNote { get => oldFolderNote; private set => Set(ref oldFolderNote, value); }

    // Screenshot mode fakes it with this too.
    internal void ShowOldFolder(string path) => OldFolderNote =
        $"{Name} is watching the beta's folder ({Path.GetFileName(path)}), but the beta is over: nothing you play now is tracked. Click Browse and pick {Flavor.Folders[0]} (install {Name} in Battle.net first if it isn't there).";

    // No game folder: the checks say so (the big card over the pages asks for one). Screenshot mode
    // fakes this state with it, so test runs never go looking for the real game.
    internal void ShowFolderMissing()
    {
        FolderStep.Set(ok: false, $"Not found. Click Browse and pick your World of Warcraft folder (usually {Wow.DefaultRoot}); the app finds {Wow.FolderHint(Flavor)} in it.", action: "Browse");
        AddonStep.Set(ok: false, "Waiting for the game folder");
        StatusText = "Waiting for the game folder";
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

    void UseFolder(string path, bool installAddon = false)
    {
        flavorDir = path;
        if (Settings.Path != path)
        {
            Settings.Path = path;
            main.Config.Save();
        }

        OldFolderNote = null;
        if (Wow.IsOlderFolder(path, Flavor))
        {
            ShowOldFolder(path);
            Log.Warn($"[{Name}] {OldFolderNote}");
        }

        var version = Wow.ClientVersion(path);
        var product = Wow.Product(path);
        FolderStep.Ok = true;
        FolderStep.ActionText = "Change";
        FolderStep.Detail = version is null ? path : $"{version} ({product}) in {Path.GetFileName(path)}";
        Log.Success($"[{Name}] Game folder: {path}" + (version is null ? "" : $", version {version} ({product})."));

        SetUpAddon(force: installAddon && AddonInstaller.InstalledVersion(path) is null);

        tracker?.Dispose();
        tracker = new Tracker(Flavor, path, () => main.Config.ActiveWebhooks(Flavor), () => Settings,
            e => main.Config.WebhooksFor(Flavor, e.Guid, e.Ruleset, e.Hardcore), w => main.PosterFor(this, w), main.Backfill);
        tracker.DataChanged += data => MainViewModel.OnUi(() => { ShowData(data); CheckReloaded(); });
        // A sync that finds nothing new doesn't raise DataChanged, so a game that closed without
        // writing its saved file (a crash) clears the reload banner this way.
        tracker.GameClosed += () => MainViewModel.OnUi(CheckReloaded);
        tracker.Posted += e => MainViewModel.OnUi(() => { lastPost = e; UpdateStatus(); Home.MarkPosted(e.Id); });
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

    // Installs the addon when the user clicks Install in the setup checks (force), since that adds
    // something to their game folder. An installed addon that's older than the one in this app, or the
    // same version with other files (an app update that changed the addon without raising its version),
    // is replaced right away, so the game always runs this app's addon. Also keeps the interface number
    // in step with game patches, so the game doesn't call it out of date.
    void SetUpAddon(bool force)
    {
        if (flavorDir is null) return;
        var installed = AddonInstaller.InstalledVersion(flavorDir);
        var bundled = AddonInstaller.BundledVersion;
        var interfaceVersion = Wow.InterfaceVersion(flavorDir);

        try
        {
            var compare = force ? -1 : AddonInstaller.Compare(installed, bundled);
            var refresh = compare == 0 && !AddonInstaller.MatchesBundled(flavorDir);
            switch (refresh ? -1 : compare)
            {
                case null: // not installed: waits for the user's click
                    AddonStep.Set(ok: false, "Not installed in this game folder yet", action: "Install");
                    Log.Info($"[{Name}] The addon isn't installed in this game folder yet. Click Install next to Addon in the setup checks to add it.");
                    return;

                case > 0: // newer than this app's (a test build, or later an addon site's update): left alone
                    KeepInterfaceCurrent(installed!, interfaceVersion);
                    AddonStep.Set(ok: true, $"v{installed} installed (newer than this app's v{bundled})");
                    break;

                case 0:
                    KeepInterfaceCurrent(installed!, interfaceVersion);
                    AddonStep.Set(ok: true, $"v{bundled} installed. Type /wct test in game to check instant posting");
                    break;

                default: // older, the same version with other files, or the user clicked
                    AddonInstaller.Install(flavorDir, interfaceVersion);
                    Log.Success($"[{Name}] " + (installed is null ? $"Installed the addon (v{bundled})."
                        : refresh ? $"Refreshed the addon (v{bundled}) with this app's copy." : $"Updated the addon to v{bundled}."));
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
            if (name is null && !problem!.StartsWith("Couldn't reach", StringComparison.Ordinal) && !problem.Contains("too long"))
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
    // lastData without what pending Data page cleanups cover (DataCleanup.Hide): what every page shows.
    public TrackerData ShownData { get; private set; } = TrackerData.Empty;

    // Names, icons and tooltips of everything from the game, shared by the whole tab (characters and the Items page).
    public GameLookup Lookup { get; }
    void NameLoaded() => Items.NameLoaded(); // the Items page sorts by name

    // The Items page: everything the tab's characters carry, searchable, per server.
    public ItemsViewModel Items { get; }

    // The Followed items page, and the switch for its post (What gets posted).
    public FollowedItemsViewModel FollowedItems { get; }
    // The Friends page: what Battle.net friends shared, apart from your own characters.
    public FriendsViewModel Friends { get; }
    // The Stats page: leaderboards for today, the week, the month or all time, you against your friends.
    public StatsViewModel Stats { get; } = new();
    // Every day seen, yours and friends', for the Stats page's month and all time (the addon keeps 8).
    DayHistory DayHistory => dayHistory ??= new(Path.Combine(Config.Dir, $"days-{Flavor.Key}.json"));
    DayHistory? dayHistory;
    // The Professions page: your characters' professions by server and faction.
    public ProfessionsViewModel Professions { get; } = new() { EmptyText = "No professions yet. They show up once a character with one has been played with the addon." };
    public bool PostFollowed { get => Settings.PostFollowed; set { Settings.PostFollowed = value; InGameChanged(); } }
    public StreamViewModel Stream => main.Stream;

    // The addon needs the list (it only records rare and better otherwise), the overlay its marks.
    void FollowedChanged()
    {
        AddonSettingsChanged();
        main.Stream.Update(Flavor, allRows, Settings.MarkFollowed(ShownData.Events));
    }

    public void Refresh() => ShowData(lastData);

    // The realm (Retail) or ruleset (Forever) chips over the character list.
    public ChipFilter Groups { get; }
    string GroupOf(CharacterRow r) => Flavor.HasRulesets ? r.Ruleset : r.RealmText;
    // Alliance / Horde chips under them.
    public ChipFilter FactionChips { get; }

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

    // Home: rebuilt with the character list, and when the game starts or stops (playing now or not).
    // Every level time of your characters and the shown friends', so each chart bar can list the others.
    static void AddLevelBoard(IReadOnlyList<CharacterRow> rows)
    {
        var board = LevelRace.Board(rows.Select(r => (r.Guid, r.IsFriend ? $"{r.Name} ({r.FriendName})" : r.Name,
            (IReadOnlyList<LevelStats.Level>)LevelStats.Timed(r.Events))));
        foreach (var r in rows) r.Leveling.AddOthers(r.Guid, board);
    }

    public HomeViewModel Home { get; }
    IReadOnlyList<Entry> homeEvents = [];
    IReadOnlyList<DayTotals> homeDays = [];
    public void RefreshHome() =>
        Home.Show(allRows, homeEvents, homeDays, tracker?.SentIds ?? new HashSet<string>(), flavorDir is not null && Wow.IsRunning(flavorDir), Friends.Shown);

    // Every character of the tab, whatever the filter shows (posts look up the one an event is about).
    List<CharacterRow> allRows = [];
    public IReadOnlyList<CharacterRow> AllCharacters => allRows;
    public CharacterRow? RowFor(string guid) => allRows.FirstOrDefault(r => r.Guid == guid);

    void ShowData(TrackerData data)
    {
        lastData = data;
        CleanStressTestOnce(data);
        DropDoneCleanups(data.CleanedAt);
        data = ShownData = DataCleanup.Hide(data, Settings.Cleanups);
        var keepRow = SelectedCharacter;
        var keep = keepRow?.Guid;
        var removed = Settings.Removed;
        // The made-up characters, when switched on (Forever only). They're only shown, never posted.
        if (main.ShowTestCharacters && Flavor.HasRulesets)
        {
            var test = TestCharacters.Build();
            data = new TrackerData([.. data.Events, .. test.Events], [.. data.Characters, .. test.Characters])
            {
                Inventories = [.. data.Inventories, .. test.Inventories],
                XpTable = data.XpTable.Count > 0 ? data.XpTable : test.XpTable,
                ItemClasses = data.ItemClasses,
                Notes = data.Notes,
                Nicknames = data.Nicknames,
                FriendOrder = data.FriendOrder,
                Today = data.Today,
                Days = data.Days,
            };
        }
        main.TakeGameNotes(data.Notes); // notes edited in game, newer than the app's
        main.TakeGameNicknames(data.Nicknames); // and friends' nicknames
        main.TakeGameFriendOrder(data.FriendOrder); // and their order on the Friends page
        TakeInGameSettings(data.InGame); // and the same for the In game settings
        if (Settings.TakeShare(data.Share)) // and sharing with friends
        {
            main.Config.Save();
            WriteAddonSettings();
        }
        Lookup.SetItemClasses(data.ItemClasses);
        var keepOpen = SelectedCharacter; // the list refreshes on every sync
        Motion.Quiet(); // the rows are rebuilt: what's open stays still
        var built = CharacterRow.Build(data, Flavor, Lookup)
            .Where(r => !(removed.TryGetValue(r.Guid, out var at) && r.LastSeen <= at))
            .ToList();
        // Ignored ones only show under the footer's Ignored button, nowhere else.
        var ignored = Settings.Ignored;
        var rows = built.Where(r => !ignored.Contains(r.Guid)).ToList();
        IgnoredCharacters = [.. built.Where(r => ignored.Contains(r.Guid)).OrderBy(r => r.FullName, StringComparer.CurrentCultureIgnoreCase)];

        allRows = rows;
        if (keepRow is not null) rows.FirstOrDefault(r => r.Guid == keep)?.KeepGearFrom(keepRow);
        Groups.Fill(rows, GroupOf);
        FactionChips.Fill(rows, r => r.Faction);

        Characters.Clear();
        foreach (var r in Sort.Apply(rows.Where(r => Groups.Matches(GroupOf(r)) && FactionChips.Matches(r.Faction))))
        {
            r.TagOptions = main.TagOptionsFor(r.Guid);
            r.SetNote(main.NoteFor(r.Guid), text => main.SaveNote(r.Guid, text));
            r.SetPortraitChoice(main.Config.StandardPortraits.Contains(r.Guid), standard =>
            {
                main.Config.SetStandardPortrait(r.Guid, standard);
                main.Config.Save();
            });
            Characters.Add(r);
            _ = r.LoadPictureAsync(Flavor);
        }
        SelectedCharacter = Characters.FirstOrDefault(c => c.Guid == keep);
        SelectedCharacter?.KeepTabFrom(keepOpen);
        Items.Show(rows);
        Professions.Show(rows);
        Friends.Show(ShownData); // friends' rows stay on their own page (not the test characters either)
        QueueAddonPortraits();
        AddLevelBoard([.. rows, .. Friends.Shown]);
        Stats.Show(rows, data.Events, DayHistory.Add([.. data.Today, .. data.Days, .. ShownData.Friends.SelectMany(c => c.Days)]), Friends.Shown);
        homeEvents = data.Events;
        homeDays = data.Today;
        RefreshHome(); // after Friends: Home lists the friends seen lately
        main.Stream.Update(Flavor, rows, Settings.MarkFollowed(data.Events));
        SelectCheckedIn(); // a character that just logged in wins
        Raise(nameof(HasCharacters));
        Raise(nameof(IgnoredCharacters));
        Raise(nameof(HasIgnored));
        Raise(nameof(IgnoredText));
        Raise(nameof(ShowsFooter));
        Raise(nameof(TotalGoldText));
        Raise(nameof(TotalCoins));
        Raise(nameof(TotalPlayedText));
        UpdateStatus();
    }

    // Once per game tab with a pre-launch stress test (Forever): its characters go (StressTestCleanup).
    // The addon cleans the saved file on its side; until then the app keeps them hidden.
    void CleanStressTestOnce(TrackerData data)
    {
        if (Flavor.StressTestBefore <= 0 || Settings.StressTestCleaned || data.Characters.Count == 0) return;
        var old = StressTestCleanup.OldCharacters(data, Flavor.StressTestBefore);
        StressTestCleanup.Apply(main.Config, Settings, old, Flavor.StressTestBefore, guid => Files.TryDelete(CharacterArt.PortraitPath(guid)));
        main.Config.Save();
        WriteAddonSettings();
        if (old.Count > 0) Log.Info($"[{Name}] Removed {Format.Count(old.Count, "character")} left from the stress test (before November 4).");
    }

    // "Total gold on Tarren Mill (3 characters):" followed by the coins, for what the filter shows.
    public string TotalGoldText
    {
        get
        {
            var where = Groups.Key.Length > 0 ? $" on {Groups.Key}" : "";
            var side = FactionChips.Key.Length > 0 ? $" {FactionChips.Key}" : ""; // "Total Horde gold on Tarren Mill"
            var count = Characters.Count;
            return $"Total{side} gold{where} ({Format.Count(count, "character")}):";
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
        WriteAddonSettings(); // the addon stops sharing it with friends (Settings.lua removed)
        Log.Info($"[{Name}] Removed {row.Name}. Playing it again brings it back.");
        ShowData(lastData);
    }

    // A cleanup from the Data page: the app forgets its own copies now, the addon does the saved file the
    // next time the game loads it, and until then the app hides it (ShowData).
    public void Clean(CleanupOrder order)
    {
        var friendGuids = ShownData.Friends.SelectMany(f => f.Characters).Where(c => DataCleanup.RemovesFriend(order, c)).Select(c => c.Guid).ToHashSet();
        DataCleanup.Forget(main.Config, Settings, order.Chars, order.At, guid => Files.TryDelete(CharacterArt.PortraitPath(guid)));
        DayHistory.Remove(d => DataCleanup.Removes(order, d) || friendGuids.Contains(d.Guid));
        Settings.Cleanups = [.. Settings.Cleanups, order];
        main.Config.Save();
        WriteAddonSettings();
        ShowData(lastData);
    }

    // For the Data page: the days the app keeps for the Stats page, how each character is listed, and
    // the newest order (the next one must be newer).
    public IReadOnlyList<DayTotals> KeptDays => DayHistory.All;
    public bool IsIgnored(string guid) => Settings.Ignored.Contains(guid);
    public bool IsRemovedFromList(Snapshot c) => Settings.Removed.TryGetValue(c.Guid, out var at) && c.LastSeen <= at;
    public long LastCleanupAt => Settings.Cleanups.LastOrDefault()?.At ?? 0;

    // Orders every account's saved file has had go from Settings.lua.
    void DropDoneCleanups(long cleanedAt)
    {
        if (Settings.Cleanups.Count == 0) return;
        var pending = DataCleanup.StillPending(Settings.Cleanups, cleanedAt);
        if (pending.Count == Settings.Cleanups.Count) return;
        Settings.Cleanups = pending;
        main.Config.Save();
        WriteAddonSettings();
    }

    // Characters with Ignore tracking on, for the footer's Ignored button (hidden while there are none).
    public IReadOnlyList<CharacterRow> IgnoredCharacters { get; private set; } = [];
    public bool HasIgnored => IgnoredCharacters.Count > 0;
    public string IgnoredText => $"Ignored ({IgnoredCharacters.Count})";
    public bool ShowsFooter => HasCharacters || HasIgnored; // all ignored: still a way back to them
    bool showIgnored;
    public bool ShowIgnored { get => showIgnored; set => Set(ref showIgnored, value); }
    public ICommand TrackAgainCommand => trackAgain ??= new RelayCommand(p => { if (p is CharacterRow c) SetIgnored(c, false); });
    ICommand? trackAgain;

    // Ignore tracking on or off. Off doesn't bring back what happened meanwhile: the addon recorded
    // nothing for the character, so nothing old gets posted.
    public void SetIgnored(CharacterRow row, bool on)
    {
        var next = new HashSet<string>(Settings.Ignored);
        if (on) next.Add(row.Guid); else next.Remove(row.Guid);
        Settings.Ignored = next; // replaced, the tracker's thread reads it
        InGameChanged(nameof(IgnoredCharacters)); // saves; the addon's own toggle is the same setting
        Log.Info(on
            ? $"[{Name}] Stopped tracking {row.FullName}: nothing is posted, announced or shared for it. Ignored, under the list, tracks it again."
            : $"[{Name}] Tracking {row.FullName} again.");
        if (next.Count == 0) ShowIgnored = false;
        ShowData(lastData);
    }

    void UpdateStatus()
    {
        if (tracker is null) { StatusText = "Waiting for the game folder"; return; }
        var count = Characters.Count;
        var chars = count == 0 ? "no characters yet" : Format.Count(count, "character");
        StatusText = lastPost is null
            ? $"Watching {Name}, {chars}"
            : $"Watching {Name}, {chars}, last post: {Tracker.Describe(lastPost)}";
    }

    // A sample of that kind of post for the Test buttons, with the selected (or latest) character.
    public Entry SampleEntry(string kind = "level") => SampleEvents.For(kind, Flavor, SelectedCharacter ?? Characters.FirstOrDefault(), Characters);

    public void Shutdown() => tracker?.Dispose();
}
