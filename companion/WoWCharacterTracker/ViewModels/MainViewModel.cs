using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace WoWCharacterTracker.ViewModels;

// The window: game tabs, app settings, the console, tags, and posting from the character details.
// The Discord panel has its own view model (Discord), and each game tab one of its own.
sealed class MainViewModel : Observable
{
    public MainViewModel(bool backfill, bool justUpdated)
    {
        Backfill = backfill;
        Updates = new UpdatesViewModel(justUpdated);
        Discord = new DiscordViewModel(this);
        Uninstall = new UninstallViewModel(this);
        Appearance = new AppearanceViewModel(this);
        Stream = new StreamViewModel(this);
        Log.Written += line => OnUi(() =>
        {
            Logs.Insert(0, line);
            while (Logs.Count > 300) Logs.RemoveAt(Logs.Count - 1);
        });

        SyncCommand = new RelayCommand(SyncNowAsync, _ => SelectedFlavor?.Tracker is not null);
        ShowPageCommand = new RelayCommand(p =>
        {
            var target = (string)p!;
            if (target != Page)
            {
                Page = target;
                return;
            }
            // The open page's tab again: start that page over (MainWindow scrolls it to the top).
            SelectedFlavor?.ResetPage(target);
            PageReset?.Invoke();
        });
        ResetPostingCommand = new RelayCommand(_ => SelectedFlavor?.ResetPosting());
        CopyPostingCommand = new RelayCommand(_ =>
        {
            if (SelectedFlavor is not { } tab) return;
            Clipboard.SetText(tab.PostingCodeText);
            Log.Success($"[{tab.Name}] Posting settings copied. Share the code; others click Paste settings to use it.");
        });
        PastePostingCommand = new RelayCommand(_ => SelectedFlavor?.ApplyPostingCode(Clipboard.ContainsText() ? Clipboard.GetText() : ""));
        RemoveCharacterCommand = new RelayCommand(p => SelectedFlavor?.RemoveCharacter((CharacterRow)p!));
        AskRemoveCommand = new RelayCommand(p => ((CharacterRow)p!).ConfirmRemove = true);
        CancelRemoveCommand = new RelayCommand(p => ((CharacterRow)p!).ConfirmRemove = false);
        PostCharacterCommand = new RelayCommand(p => PostCharacterAsync((CharacterRow)p!));
        PostHighlightCommand = new RelayCommand(p => PostHighlightAsync((HighlightRow)p!));
        OpenTalentsCommand = new RelayCommand(p => OpenTalents(p as CharacterRow));
        PostTalentsCommand = new RelayCommand(p => { if (p is CharacterRow c) _ = PostTalentsAsync(c); });
        ToggleCardOptionsCommand = new RelayCommand(_ => ShowCardOptions = !ShowCardOptions);
        AddTagCommand = new RelayCommand(p => AddTag((CharacterRow)p!));
        DeleteTagCommand = new RelayCommand(p => DeleteTag((string)p!));
        TestKindCommand = new RelayCommand(p => SendKindTestAsync((string)p!));
        OpenUrlCommand = new RelayCommand(p => Browser.Open(p as string));
        ShowUninstallCommand = new RelayCommand(_ =>
        {
            ShowSettings = false;
            Uninstall.Refresh();
            Page = "uninstall";
        });
    }

    public bool Backfill { get; }
    public Config Config { get; private set; } = new();
    public UpdatesViewModel Updates { get; }
    public DiscordViewModel Discord { get; }
    public UninstallViewModel Uninstall { get; }
    public AppearanceViewModel Appearance { get; }
    public StreamViewModel Stream { get; }

    // Settings, Animations: slides, fades and the tab orb (Ui/Motion), or everything instant.
    public bool Animations
    {
        get => Config.Animations;
        set { Config.Animations = Motion.Enabled = value; Config.Save(); Raise(nameof(Animations)); }
    }

    public ObservableCollection<LogLine> Logs { get; } = [];
    public ObservableCollection<FlavorViewModel> FlavorTabs { get; } = [];

    FlavorViewModel? selectedFlavor;
    public FlavorViewModel? SelectedFlavor
    {
        get => selectedFlavor;
        set
        {
            var old = selectedFlavor;
            if (!Set(ref selectedFlavor, value) || value is null) return;
            if (old is not null) old.IsSelected = false;
            value.IsSelected = true;
            ShowItemsWhereOpen();
            Appearance.Apply();
            Config.SelectedFlavor = value.Flavor.Key;
            Config.Save();
            Discord.Refresh();
        }
    }

    // "characters" or "posting".
    string page = "characters";
    public string Page { get => page; set { if (Set(ref page, value)) ShowItemsWhereOpen(); } }

    // Only the open tab's Items page builds its list (ItemsViewModel.IsShown).
    void ShowItemsWhereOpen()
    {
        foreach (var tab in FlavorTabs) tab.Items.IsShown = tab.IsSelected && page == "items";
    }

    public ICommand SyncCommand { get; }
    public ICommand ShowPageCommand { get; }
    public event Action? PageReset;
    public ICommand ResetPostingCommand { get; }
    public ICommand CopyPostingCommand { get; }
    public ICommand PastePostingCommand { get; }
    public ICommand RemoveCharacterCommand { get; }
    public ICommand AskRemoveCommand { get; }
    public ICommand CancelRemoveCommand { get; }
    public ICommand PostCharacterCommand { get; }
    public ICommand PostHighlightCommand { get; }
    public ICommand OpenTalentsCommand { get; }
    public ICommand PostTalentsCommand { get; }
    public ICommand ToggleCardOptionsCommand { get; }
    public ICommand AddTagCommand { get; }
    public ICommand DeleteTagCommand { get; }
    public ICommand TestKindCommand { get; }
    public ICommand OpenUrlCommand { get; }
    public ICommand ShowUninstallCommand { get; }

    public async Task StartAsync()
    {
        Config = Config.Load();
        Motion.Enabled = Config.Animations;
        Log.Info("Character Tracker started.");
        Discord.ShowAddWebhook = Config.Webhooks.Count == 0;

        // On by default, so tracking is running before you start the game. Re-applied every start
        // so the startup entry follows the exe if it was moved.
        if (Config.OpenWithWow is null)
        {
            Config.OpenWithWow = true;
            Config.Save();
        }
        Autostart.Apply(Config.OpenWithWow.Value);
        Raise(nameof(OpenWithWow));

        foreach (var f in Flavors.All) FlavorTabs.Add(new FlavorViewModel(this, f));
        // Opens on the last tab, unless that game is turned off and another one isn't.
        var last = FlavorTabs.FirstOrDefault(t => t.Flavor.Key == Config.SelectedFlavor) ?? FlavorTabs[0];
        SelectedFlavor = last.IsOn ? last : FlavorTabs.FirstOrDefault(t => t.IsOn) ?? last;

        Stream.Apply();
        foreach (var tab in FlavorTabs) tab.Start();
        if (Config.Webhooks.Count == 0)
            Log.Warn("No Discord webhook yet. Add one in the Discord panel so posts can go out.");
        await Task.WhenAll(FlavorTabs.Where(t => t.IsOn).Select(t => t.CheckDiscordAsync()));

        // Test runs stay off the network for this, unless they fake an old version to test it.
        if (Config.IsTest && Environment.GetEnvironmentVariable("CHARACTERTRACKER_FAKE_VERSION") is null) return;
        await Updates.StartAsync();
    }

    public void Shutdown()
    {
        foreach (var tab in FlavorTabs) tab.Shutdown();
        Stream.Shutdown();
    }

    public static void OnUi(Action a)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null) return;
        if (d.CheckAccess()) a(); else d.BeginInvoke(a);
    }

    // --- app settings (the gear in the header) ---

    bool showSettings;
    public bool ShowSettings { get => showSettings; set => Set(ref showSettings, value); }

    // A tray icon starts at login, the window opens when WoW starts and goes back to the tray when
    // WoW closes. Off means a normal app you open and close yourself.
    public bool OpenWithWow
    {
        get => Config.OpenWithWow ?? false;
        set
        {
            Config.OpenWithWow = value;
            Config.Save();
            Autostart.Apply(value);
            Raise(nameof(OpenWithWow));
            Log.Info(value
                ? "The app now opens when World of Warcraft starts (it waits in the tray)."
                : "The app no longer opens with World of Warcraft. Closing the window quits it.");
        }
    }

    public bool HideWhenGameCloses
    {
        get => Config.HideWhenGameCloses;
        set
        {
            Config.HideWhenGameCloses = value;
            Config.Save();
            Raise(nameof(HideWhenGameCloses));
        }
    }

    // Shows the made-up Forever characters (Zeb, Fågelmannen, Goggo) for testing. Not saved.
    bool showTestCharacters;
    public bool ShowTestCharacters
    {
        get => showTestCharacters;
        set
        {
            if (!Set(ref showTestCharacters, value)) return;
            foreach (var tab in FlavorTabs) tab.Refresh();
            if (!value) return;
            if (FlavorTabs.FirstOrDefault(t => t.Flavor == Flavors.Forever) is { IsOn: true } forever) SelectedFlavor = forever;
            Log.Info("Test characters on (Forever tab). They're never posted on their own, but Test buttons and Post to Discord work with them.");
        }
    }

    // --- Sync now: reads the game's saved data again and posts what's new; the button spins meanwhile ---

    bool isSyncing;
    public bool IsSyncing { get => isSyncing; set => Set(ref isSyncing, value); }

    async Task SyncNowAsync(object? _)
    {
        if (SelectedFlavor?.Tracker is not { } tracker) return;
        IsSyncing = true;
        Log.Info($"[{SelectedFlavor.Name}] Syncing...");
        var started = DateTime.Now;
        try { await tracker.SyncAsync(report: true); }
        finally
        {
            // Long enough to see that something happened, even when the sync is instant.
            var left = TimeSpan.FromMilliseconds(600) - (DateTime.Now - started);
            if (left > TimeSpan.Zero) await Task.Delay(left);
            IsSyncing = false;
        }
    }

    public void CheckDiscordAll()
    {
        foreach (var tab in FlavorTabs) _ = tab.CheckDiscordAsync();
    }

    // --- the character card (Post to Discord) ---

    // What the character card includes (the gear next to Post to Discord). Same for every character.
    public static readonly (string Key, string Label)[] CardParts =
    [
        ("ilvl", "Item level"), ("played", "Total played"), ("gold", "Gold"), ("crests", "Crests"),
        ("professions", "Professions"), ("talents", "Talents"), ("stats", "Stats"), ("guild", "Guild"), ("zone", "Last seen"),
        ("levels", "Recent levels"), ("highlights", "Highlights"),
    ];

    IReadOnlyList<ToggleOption>? cardOptions;
    public IReadOnlyList<ToggleOption> CardOptions => cardOptions ??= CardParts
        .Select(p => new ToggleOption(p.Label, !Config.CardHidden.Contains(p.Key), on =>
        {
            Config.CardHidden.Remove(p.Key);
            if (!on) Config.CardHidden.Add(p.Key);
            Config.Save();
        }))
        .ToList();

    bool showCardOptions;
    public bool ShowCardOptions { get => showCardOptions; set => Set(ref showCardOptions, value); }

    // Opens the character's build in Wowhead's talent calculator.
    void OpenTalents(CharacterRow? c)
    {
        if (c is null || SelectedFlavor is not { } tab) return;
        if (Wowhead.TalentCalcUrl(tab.Flavor, c.ClassFile, c.TalentCalc, c.TalentExport) is { } url) Browser.Open(url);
    }

    // Posts the character's talents (level, race, class, the build and its Wowhead link) to the
    // webhooks a character card would go to.
    async Task PostTalentsAsync(CharacterRow c)
    {
        if (SelectedFlavor is not { } tab) return;
        foreach (var w in WebhooksFor(tab, c))
            if (await PosterFor(tab, w).PostTalentsAsync(c))
                Log.Success($"[{tab.Name}] Posted {c.Name}'s talents to \"{w.Name}\".");
    }

    // --- tags ---

    public IReadOnlyList<ToggleOption> TagOptionsFor(string guid) =>
        Config.Tags.Select(tag => new ToggleOption(tag, Config.TagsOf(guid).Contains(tag), on => SetTag(guid, tag, on))).ToList();

    void SetTag(string guid, string tag, bool on)
    {
        if (!Config.CharacterTags.TryGetValue(guid, out var list)) Config.CharacterTags[guid] = list = [];
        list.Remove(tag);
        if (on) list.Add(tag);
        if (list.Count == 0) Config.CharacterTags.Remove(guid);
        Config.Save();
    }

    // Makes a new tag (if it's new) and puts it on the character.
    void AddTag(CharacterRow c)
    {
        var tag = c.NewTag.Trim();
        if (tag.Length == 0) return;
        if (!Config.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) Config.Tags.Add(tag);
        tag = Config.Tags.First(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));
        SetTag(c.Guid, tag, true);
        c.NewTag = "";
        RefreshTags();
        Log.Info($"Tagged {c.Name} as \"{tag}\". Webhooks can be set to post only for tags, under the gear.");
    }

    // "Delete this tag everywhere" in a tag chip's right-click menu (webhook options).
    void DeleteTag(string tag)
    {
        Config.Tags.Remove(tag);
        foreach (var list in Config.CharacterTags.Values) list.Remove(tag);
        foreach (var w in Config.Webhooks) w.Tags?.Remove(tag);
        Config.Save();
        RefreshTags();
        Log.Info($"Deleted the tag \"{tag}\" from all characters and webhooks.");
    }

    void RefreshTags()
    {
        foreach (var tab in FlavorTabs)
            foreach (var c in tab.Characters) c.TagOptions = TagOptionsFor(c.Guid);
        Discord.Refresh();
    }

    // --- posting from the app ---

    public DiscordPoster PosterFor(FlavorViewModel tab, WebhookInfo w) =>
        new(w.Url, tab.Flavor, Config.For(tab.Flavor).NameStyle, guid => Config.StandardPortraits.Contains(guid), guid => tab.RowFor(guid)?.Picture);

    // The tab's webhooks that take this character: on Forever only those for its ruleset, and only
    // webhooks that post for its tags. Says so in the console when there are none.
    List<WebhookInfo> WebhooksFor(FlavorViewModel tab, CharacterRow c)
    {
        var asEntry = new Entry { Ruleset = c.Ruleset, Hardcore = c.Hardcore };
        var hooks = Config.ActiveWebhooks(tab.Flavor)
            .Where(w => tab.Flavor != Flavors.Forever || w.AcceptsRuleset(asEntry))
            .Where(w => w.AcceptsTags(Config.TagsOf(c.Guid)))
            .ToList();
        if (hooks.Count == 0)
            Log.Warn($"[{tab.Name}] No webhook to post {c.Name} to. Turn one on in the Discord panel" +
                     (tab.Flavor == Flavors.Forever ? $" (and check it takes {c.Ruleset} characters)." : "."));
        return hooks;
    }

    // Sends the character's card to the webhooks that take this character.
    async Task PostCharacterAsync(CharacterRow c)
    {
        if (SelectedFlavor is not { } tab) return;
        var hooks = WebhooksFor(tab, c);
        if (hooks.Count == 0) return;
        var hidden = Config.CardHidden.ToList();
        var card = await CardImage.RenderAsync(c, tab.Flavor, hidden, Config.For(tab.Flavor).NameStyle);
        foreach (var w in hooks)
            if (await PosterFor(tab, w).PostCharacterAsync(c, hidden, card))
                Log.Success($"[{tab.Name}] Posted {c.Name}'s character card to \"{w.Name}\".");
    }

    // Posts one highlight (a loot, boss kill, mount...) from the character details, to the same
    // webhooks a character card would go to.
    async Task PostHighlightAsync(HighlightRow h)
    {
        if (SelectedFlavor is not { } tab || h.Source is not { } e || tab.SelectedCharacter is not { } c) return;
        foreach (var w in WebhooksFor(tab, c))
            if (await PosterFor(tab, w).PostAsync(e))
                Log.Success($"[{tab.Name}] Posted {h.Text.Trim()} to \"{w.Name}\".");
    }

    // The Test buttons on the "What gets posted" page: a sample of that kind of post.
    async Task SendKindTestAsync(string kind)
    {
        if (SelectedFlavor is not { } tab) return;
        var hooks = Config.ActiveWebhooks(tab.Flavor);
        if (hooks.Count == 0)
        {
            Log.Warn($"[{tab.Name}] Turn on a webhook in the Discord panel first.");
            return;
        }
        var sample = tab.SampleEntry(kind);
        foreach (var w in hooks)
            if (await PosterFor(tab, w).PostAsync(sample, isTest: true))
                Log.Success($"[{tab.Name}] Test {kind} post sent to \"{w.Name}\".");
    }
}
