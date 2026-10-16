using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WoWCharacterTracker.ViewModels;

sealed class MainViewModel : Observable
{
    public MainViewModel(bool backfill, bool justUpdated)
    {
        Backfill = backfill;
        Updates = new UpdatesViewModel(justUpdated);
        Log.Written += line => OnUi(() =>
        {
            Logs.Insert(0, line);
            while (Logs.Count > 300) Logs.RemoveAt(Logs.Count - 1);
        });

        SyncCommand = new RelayCommand(_ => SelectedFlavor?.Tracker?.SyncAsync() ?? Task.CompletedTask,
            _ => SelectedFlavor?.Tracker is not null);
        ShowPageCommand = new RelayCommand(p => Page = (string)p!);
        ResetPostingCommand = new RelayCommand(_ => SelectedFlavor?.ResetPosting());
        CopyPostingCommand = new RelayCommand(_ =>
        {
            if (SelectedFlavor is not { } tab) return;
            Clipboard.SetText(tab.PostingCodeText);
            Log.Success($"[{tab.Name}] Posting settings copied. Share the code; others click Paste settings to use it.");
        });
        PastePostingCommand = new RelayCommand(_ =>
        {
            if (SelectedFlavor is not { } tab) return;
            tab.ApplyPostingCode(Clipboard.ContainsText() ? Clipboard.GetText() : "");
        });
        RemoveCharacterCommand = new RelayCommand(p => SelectedFlavor?.RemoveCharacter((CharacterRow)p!));
        PostCharacterCommand = new RelayCommand(p => PostCharacterAsync((CharacterRow)p!));
        PostHighlightCommand = new RelayCommand(p => PostHighlightAsync((HighlightRow)p!));
        AddTagCommand = new RelayCommand(p => AddTag((CharacterRow)p!));
        TestKindCommand = new RelayCommand(p => SendKindTestAsync((string)p!));
        DeleteTagCommand = new RelayCommand(p => DeleteTag((string)p!));
        ToggleAddWebhookCommand = new RelayCommand(_ => { ShowAddWebhook = !ShowAddWebhook; AddWebhookError = ""; });
        SaveWebhookCommand = new RelayCommand(_ => SaveWebhookAsync());
        SelectWebhookCommand = new RelayCommand(p => SelectWebhook((WebhookRow)p!));
        RemoveWebhookCommand = new RelayCommand(p => RemoveWebhook((WebhookRow)p!));
        RenameWebhookCommand = new RelayCommand(p =>
        {
            foreach (var w in Webhooks) w.IsEditing = false;
            var row = (WebhookRow)p!;
            row.EditName = row.Name;
            row.IsEditing = true;
        });
        SaveRenameCommand = new RelayCommand(p => SaveRename((WebhookRow)p!));
        ToggleWebhookOptionsCommand = new RelayCommand(p => ((WebhookRow)p!).IsExpanded = !((WebhookRow)p!).IsExpanded);
        CancelRenameCommand = new RelayCommand(p => ((WebhookRow)p!).IsEditing = false);
        TestWebhookCommand = new RelayCommand(_ => SendTestAsync(),
            _ => SelectedFlavor is not null && Config.ActiveWebhooks(SelectedFlavor.Flavor).Count > 0);
    }

    // Like Archon: a tray icon starts at login, the window opens when WoW starts and goes back to the
    // tray when WoW closes. Off means a normal app you open and close yourself.
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

    public bool Backfill { get; }
    public Config Config { get; private set; } = new();

    // Shows the made-up Forever characters (Zeb, Fågelmannen, Goggo) for testing. Not saved.
    bool showTestCharacters;
    public bool ShowTestCharacters
    {
        get => showTestCharacters;
        set
        {
            if (!Set(ref showTestCharacters, value)) return;
            foreach (var tab in FlavorTabs) tab.Refresh();
            if (value)
            {
                if (FlavorTabs.FirstOrDefault(t => t.Flavor == Flavors.Forever) is { } forever) SelectedFlavor = forever;
                Log.Info("Test characters on (Forever tab). They're never posted on their own, but Test buttons and Post to Discord work with them.");
            }
        }
    }

    // The update check, banner and install.
    public UpdatesViewModel Updates { get; }

    public ObservableCollection<LogLine> Logs { get; } = [];
    public ObservableCollection<FlavorViewModel> FlavorTabs { get; } = [];
    public ObservableCollection<WebhookRow> Webhooks { get; } = [];

    FlavorViewModel? selectedFlavor;
    public FlavorViewModel? SelectedFlavor
    {
        get => selectedFlavor;
        set
        {
            if (!Set(ref selectedFlavor, value) || value is null) return;
            Theme.Apply(value.Flavor);
            Config.SelectedFlavor = value.Flavor.Key;
            Config.Save();
            RefreshWebhooks();
        }
    }

    // "characters" or "posting".
    string page = "characters";
    public string Page { get => page; set => Set(ref page, value); }

    bool showAddWebhook;
    public bool ShowAddWebhook { get => showAddWebhook; set => Set(ref showAddWebhook, value); }
    public bool HasWebhooks => Webhooks.Count > 0;
    string newWebhookName = "";
    public string NewWebhookName { get => newWebhookName; set => Set(ref newWebhookName, value); }
    string newWebhookUrl = "";
    public string NewWebhookUrl { get => newWebhookUrl; set => Set(ref newWebhookUrl, value); }
    string addWebhookError = "";
    public string AddWebhookError { get => addWebhookError; set => Set(ref addWebhookError, value); }

    public ICommand SyncCommand { get; }
    public ICommand ShowPageCommand { get; }
    public ICommand ResetPostingCommand { get; }
    public ICommand CopyPostingCommand { get; }
    public ICommand PastePostingCommand { get; }
    public ICommand RemoveCharacterCommand { get; }
    public ICommand PostCharacterCommand { get; }
    public ICommand PostHighlightCommand { get; }

    // Opens the character's build in Wowhead's talent calculator (retail loadout strings).
    public ICommand OpenTalentsCommand => new RelayCommand(p =>
    {
        if (p is not CharacterRow { HasTalentLink: true } c) return;
        // Forever: wowhead.com/forever/talent-calc/<class>/<points per talent, per tree>, like Wowhead's
        // classic calculators. Retail: the game's own loadout code.
        var url = SelectedFlavor?.Flavor == Flavors.Forever
            ? $"https://www.wowhead.com/forever/talent-calc/{c.ClassFile.ToLowerInvariant()}/{c.TalentCalc}".TrimEnd('/')
            : $"https://www.wowhead.com/talent-calc/blizzard/{Uri.EscapeDataString(c.TalentExport)}";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    });

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
    public ICommand ToggleCardOptionsCommand => new RelayCommand(_ => ShowCardOptions = !ShowCardOptions);
    public ICommand AddTagCommand { get; }
    public ICommand TestKindCommand { get; }
    public ICommand DeleteTagCommand { get; }
    public ICommand ToggleAddWebhookCommand { get; }
    public ICommand SaveWebhookCommand { get; }
    public ICommand SelectWebhookCommand { get; }
    public ICommand RemoveWebhookCommand { get; }
    public ICommand RenameWebhookCommand { get; }
    public ICommand SaveRenameCommand { get; }
    public ICommand ToggleWebhookOptionsCommand { get; }
    public ICommand CancelRenameCommand { get; }
    public ICommand TestWebhookCommand { get; }

    public async Task StartAsync()
    {
        Config = Config.Load();
        Log.Info("Character Tracker started.");
        ShowAddWebhook = Config.Webhooks.Count == 0;

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
        SelectedFlavor = FlavorTabs.FirstOrDefault(t => t.Flavor.Key == Config.SelectedFlavor) ?? FlavorTabs[0];

        foreach (var tab in FlavorTabs) tab.Start();
        if (Config.Webhooks.Count == 0)
            Log.Warn("No Discord webhook yet. Add one in the Discord panel so posts can go out.");
        await Task.WhenAll(FlavorTabs.Select(t => t.CheckDiscordAsync()));

        // Test runs stay off the network for this, unless they fake an old version to test it.
        if (Config.IsTest && Environment.GetEnvironmentVariable("CHARACTERTRACKER_FAKE_VERSION") is null) return;
        await Updates.StartAsync();
    }

    void RefreshWebhooks()
    {
        var expanded = Webhooks.Where(w => w.IsExpanded).Select(w => w.Info.Id).ToHashSet();
        Webhooks.Clear();
        foreach (var w in Config.Webhooks)
        {
            var games = FlavorTabs.Select(t => new ToggleOption(t.Name, Config.IsActive(t.Flavor, w), on =>
            {
                Config.SetActive(t.Flavor, w, on);
                WebhookOptionsChanged();
            })).ToList();
            var rulesets = WebhookInfo.AllRulesets.Select(r => new ToggleOption(r, w.ForeverRulesets?.Contains(r) ?? true, on =>
            {
                w.ForeverRulesets ??= [.. WebhookInfo.AllRulesets];
                w.ForeverRulesets.Remove(r);
                if (on) w.ForeverRulesets.Add(r);
                if (w.ForeverRulesets.Count == WebhookInfo.AllRulesets.Length) w.ForeverRulesets = null; // all again
                WebhookOptionsChanged();
            })).ToList();
            var tags = Config.Tags.Select(tag => new ToggleOption(tag, w.Tags?.Contains(tag) ?? false, on =>
            {
                w.Tags ??= [];
                w.Tags.Remove(tag);
                if (on) w.Tags.Add(tag);
                if (w.Tags.Count == 0) w.Tags = null; // back to every character
                WebhookOptionsChanged();
            })).ToList();
            var active = SelectedFlavor is not null && Config.IsActive(SelectedFlavor.Flavor, w);
            Webhooks.Add(new WebhookRow(w, active, expanded.Contains(w.Id), games, rulesets, tags));
        }
        Raise(nameof(HasWebhooks));
        CommandManager.InvalidateRequerySuggested();
    }

    void WebhookOptionsChanged()
    {
        Config.Save();
        // Rebuild after the click has finished, so the chip being clicked isn't replaced mid-click.
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            RefreshWebhooks();
            foreach (var tab in FlavorTabs) _ = tab.CheckDiscordAsync();
        });
    }

    async Task SaveWebhookAsync()
    {
        AddWebhookError = "";
        var url = NewWebhookUrl.Trim();
        if (!DiscordWebhook.LooksLikeWebhook(url))
        {
            AddWebhookError = "That doesn't look like a webhook link. It starts with https://discord.com/api/webhooks/";
            return;
        }

        var (discordName, problem) = await DiscordWebhook.CheckAsync(url);
        if (discordName is null)
        {
            AddWebhookError = problem!;
            return;
        }

        var info = new WebhookInfo { Name = NewWebhookName.Trim() is { Length: > 0 } n ? n : discordName, Url = url };
        Config.Webhooks.Add(info);
        // On for this tab, and for any tab that has nothing to post to yet.
        foreach (var tab in FlavorTabs)
            if (tab == SelectedFlavor || Config.ActiveWebhooks(tab.Flavor).Count == 0)
                Config.SetActive(tab.Flavor, info, true);
        Config.Save();
        RefreshWebhooks();

        NewWebhookName = "";
        NewWebhookUrl = "";
        ShowAddWebhook = false;
        Log.Success($"Added webhook \"{info.Name}\". Use Send test to see a post.");
        await Task.WhenAll(FlavorTabs.Select(t => t.CheckDiscordAsync()));
        foreach (var tab in FlavorTabs)
            if (tab.Tracker is not null) await tab.Tracker.SyncAsync(); // post anything that was waiting
    }

    // Clicking a webhook turns it on or off for the tab you're on.
    void SelectWebhook(WebhookRow row)
    {
        if (SelectedFlavor is null || row.IsEditing) return;
        var on = !row.IsActive;
        Config.SetActive(SelectedFlavor.Flavor, row.Info, on);
        Config.Save();
        RefreshWebhooks();
        Log.Info($"[{SelectedFlavor.Name}] " + (on ? $"Now also posting to \"{row.Name}\"." : $"Stopped posting to \"{row.Name}\"."));
        _ = SelectedFlavor.CheckDiscordAsync();
    }

    void SaveRename(WebhookRow row)
    {
        var name = row.EditName.Trim();
        row.IsEditing = false;
        if (name.Length == 0 || name == row.Info.Name) return;
        var old = row.Info.Name;
        row.Info.Name = name;
        Config.Save();
        row.NameChanged();
        Log.Info($"Renamed webhook \"{old}\" to \"{name}\".");
        foreach (var tab in FlavorTabs) _ = tab.CheckDiscordAsync(); // the Discord check shows the name
    }

    void RemoveWebhook(WebhookRow row)
    {
        var answer = MessageBox.Show($"Remove the webhook \"{row.Name}\"?", "Character Tracker",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        Config.Webhooks.Remove(row.Info);
        foreach (var tab in FlavorTabs) Config.For(tab.Flavor).ActiveWebhookIds?.Remove(row.Info.Id);
        Config.Save();
        RefreshWebhooks();
        Log.Info($"Removed webhook \"{row.Name}\".");
        foreach (var tab in FlavorTabs) _ = tab.CheckDiscordAsync();
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

    // A tag nobody uses anymore can be deleted from the webhook options.
    void DeleteTag(string tag)
    {
        var answer = MessageBox.Show($"Delete the tag \"{tag}\" from all characters and webhooks?", "Character Tracker",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        Config.Tags.Remove(tag);
        foreach (var list in Config.CharacterTags.Values) list.Remove(tag);
        foreach (var w in Config.Webhooks) w.Tags?.Remove(tag);
        Config.Save();
        RefreshTags();
    }

    void RefreshTags()
    {
        foreach (var tab in FlavorTabs)
            foreach (var c in tab.Characters) c.TagOptions = TagOptionsFor(c.Guid);
        RefreshWebhooks();
    }

    // Sends the character's card to this tab's webhooks (on Forever, only those that take its
    // ruleset, and only webhooks that post for its tags).
    async Task PostCharacterAsync(CharacterRow c)
    {
        if (SelectedFlavor is not { } tab) return;
        var hooks = WebhooksFor(tab, c);
        if (hooks.Count == 0) return;
        var hidden = Config.CardHidden.ToList();
        var card = await CardImage.RenderAsync(c, tab.Flavor, hidden, Config.For(tab.Flavor).NameStyle);
        foreach (var w in hooks)
            if (await new DiscordPoster(w.Url, tab.Flavor, Config.For(tab.Flavor).NameStyle).PostCharacterAsync(c, hidden, card))
                Log.Success($"[{tab.Name}] Posted {c.Name}'s character card to \"{w.Name}\".");
    }

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

    // Posts one highlight (a loot, boss kill, mount...) from the character details, to the same
    // webhooks a character card would go to.
    async Task PostHighlightAsync(HighlightRow h)
    {
        if (SelectedFlavor is not { } tab || h.Source is not { } e || tab.SelectedCharacter is not { } c) return;
        var hooks = WebhooksFor(tab, c);
        if (hooks.Count == 0) return;
        foreach (var w in hooks)
            if (await new DiscordPoster(w.Url, tab.Flavor, Config.For(tab.Flavor).NameStyle).PostAsync(e))
                Log.Success($"[{tab.Name}] Posted {h.Text.Trim()} to \"{w.Name}\".");
    }

    async Task SendTestAsync()
    {
        if (SelectedFlavor is not { } tab) return;
        foreach (var w in Config.ActiveWebhooks(tab.Flavor))
            if (await new DiscordPoster(w.Url, tab.Flavor, Config.For(tab.Flavor).NameStyle).PostAsync(tab.SampleEntry(), isTest: true))
                Log.Success($"[{tab.Name}] Test post sent to \"{w.Name}\". Check your Discord channel.");
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
            if (await new DiscordPoster(w.Url, tab.Flavor, Config.For(tab.Flavor).NameStyle).PostAsync(sample, isTest: true))
                Log.Success($"[{tab.Name}] Test {kind} post sent to \"{w.Name}\".");
    }

    public static void OnUi(Action a)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null) return;
        if (d.CheckAccess()) a(); else d.BeginInvoke(a);
    }

    public void Shutdown()
    {
        foreach (var tab in FlavorTabs) tab.Shutdown();
    }
}
