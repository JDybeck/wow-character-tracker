using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace WoWCharacterTracker.ViewModels;

// The Discord panel: the webhook list (on or off per game tab), each webhook's options, renaming,
// removing, adding a new one, and Send test.
sealed class DiscordViewModel : Observable
{
    readonly MainViewModel main;
    Config Config => main.Config;

    public DiscordViewModel(MainViewModel main)
    {
        this.main = main;
        TestWebhookCommand = new RelayCommand(_ => SendTestAsync(),
            _ => main.SelectedFlavor is { } tab && Config.ActiveWebhooks(tab.Flavor).Count > 0);
        ToggleAddWebhookCommand = new RelayCommand(_ => { ShowAddWebhook = !ShowAddWebhook; AddWebhookError = ""; });
        SaveWebhookCommand = new RelayCommand(_ => SaveWebhookAsync());
        SelectWebhookCommand = new RelayCommand(p => SelectWebhook((WebhookRow)p!));
        AskRemoveWebhookCommand = new RelayCommand(p => ((WebhookRow)p!).ConfirmRemove = true);
        CancelRemoveWebhookCommand = new RelayCommand(p => ((WebhookRow)p!).ConfirmRemove = false);
        RemoveWebhookCommand = new RelayCommand(p => RemoveWebhook((WebhookRow)p!));
        RenameWebhookCommand = new RelayCommand(p => StartRename((WebhookRow)p!));
        SaveRenameCommand = new RelayCommand(p => SaveRename((WebhookRow)p!));
        CancelRenameCommand = new RelayCommand(p => ((WebhookRow)p!).IsEditing = false);
        ToggleWebhookOptionsCommand = new RelayCommand(p => ((WebhookRow)p!).IsExpanded = !((WebhookRow)p!).IsExpanded);
    }

    public ObservableCollection<WebhookRow> Webhooks { get; } = [];
    public bool HasWebhooks => Webhooks.Count > 0;
    public string TabName => main.SelectedFlavor?.Name ?? "";

    bool showAddWebhook;
    public bool ShowAddWebhook { get => showAddWebhook; set => Set(ref showAddWebhook, value); }
    string newWebhookName = "";
    public string NewWebhookName { get => newWebhookName; set => Set(ref newWebhookName, value); }
    string newWebhookUrl = "";
    public string NewWebhookUrl { get => newWebhookUrl; set => Set(ref newWebhookUrl, value); }
    string addWebhookError = "";
    public string AddWebhookError { get => addWebhookError; set => Set(ref addWebhookError, value); }

    public ICommand TestWebhookCommand { get; }
    public ICommand ToggleAddWebhookCommand { get; }
    public ICommand SaveWebhookCommand { get; }
    public ICommand SelectWebhookCommand { get; }
    public ICommand AskRemoveWebhookCommand { get; }
    public ICommand CancelRemoveWebhookCommand { get; }
    public ICommand RemoveWebhookCommand { get; }
    public ICommand RenameWebhookCommand { get; }
    public ICommand SaveRenameCommand { get; }
    public ICommand CancelRenameCommand { get; }
    public ICommand ToggleWebhookOptionsCommand { get; }

    // Rebuilds the rows from the config, keeping open option panels open.
    public void Refresh()
    {
        var expanded = Webhooks.Where(w => w.IsExpanded).Select(w => w.Info.Id).ToHashSet();
        Webhooks.Clear();
        foreach (var w in Config.Webhooks)
        {
            var games = main.FlavorTabs.Select(t => new ToggleOption(t.Name, Config.IsActive(t.Flavor, w), on =>
            {
                Config.SetActive(t.Flavor, w, on);
                OptionsChanged();
            })).ToList();
            var rulesets = WebhookInfo.AllRulesets.Select(r => new ToggleOption(r, w.ForeverRulesets?.Contains(r) ?? true, on =>
            {
                w.ForeverRulesets ??= [.. WebhookInfo.AllRulesets];
                w.ForeverRulesets.Remove(r);
                if (on) w.ForeverRulesets.Add(r);
                if (w.ForeverRulesets.Count == WebhookInfo.AllRulesets.Length) w.ForeverRulesets = null; // all again
                OptionsChanged();
            })).ToList();
            var tags = Config.Tags.Select(tag => new ToggleOption(tag, w.Tags?.Contains(tag) ?? false, on =>
            {
                w.Tags ??= [];
                w.Tags.Remove(tag);
                if (on) w.Tags.Add(tag);
                if (w.Tags.Count == 0) w.Tags = null; // back to every character
                OptionsChanged();
            })).ToList();
            var active = main.SelectedFlavor is { } tab && Config.IsActive(tab.Flavor, w);
            Webhooks.Add(new WebhookRow(w, active, expanded.Contains(w.Id), games, rulesets, tags));
        }
        Raise(nameof(HasWebhooks));
        Raise(nameof(TabName));
        CommandManager.InvalidateRequerySuggested();
    }

    void OptionsChanged()
    {
        Config.Save();
        // Rebuild after the click has finished, so the chip being clicked isn't replaced mid-click.
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            Refresh();
            main.CheckDiscordAll();
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
        foreach (var tab in main.FlavorTabs)
            if (tab == main.SelectedFlavor || Config.ActiveWebhooks(tab.Flavor).Count == 0)
                Config.SetActive(tab.Flavor, info, true);
        Config.Save();
        Refresh();

        NewWebhookName = "";
        NewWebhookUrl = "";
        ShowAddWebhook = false;
        Log.Success($"Added webhook \"{info.Name}\". Use Send test to see a post.");
        await Task.WhenAll(main.FlavorTabs.Select(t => t.CheckDiscordAsync()));
        foreach (var tab in main.FlavorTabs)
            if (tab.Tracker is not null) await tab.Tracker.SyncAsync(); // post anything that was waiting
    }

    // Clicking a webhook turns it on or off for the tab you're on.
    void SelectWebhook(WebhookRow row)
    {
        if (main.SelectedFlavor is not { } tab || row.IsEditing) return;
        var on = !row.IsActive;
        Config.SetActive(tab.Flavor, row.Info, on);
        Config.Save();
        Refresh();
        Log.Info($"[{tab.Name}] " + (on ? $"Now also posting to \"{row.Name}\"." : $"Stopped posting to \"{row.Name}\"."));
        _ = tab.CheckDiscordAsync();
    }

    void StartRename(WebhookRow row)
    {
        foreach (var w in Webhooks) w.IsEditing = false;
        row.EditName = row.Name;
        row.IsEditing = true;
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
        main.CheckDiscordAll(); // the Discord card shows the name
    }

    void RemoveWebhook(WebhookRow row)
    {
        Config.Webhooks.Remove(row.Info);
        foreach (var tab in main.FlavorTabs) Config.For(tab.Flavor).ActiveWebhookIds?.Remove(row.Info.Id);
        Config.Save();
        Refresh();
        Log.Info($"Removed webhook \"{row.Name}\".");
        main.CheckDiscordAll();
    }

    async Task SendTestAsync()
    {
        if (main.SelectedFlavor is not { } tab) return;
        foreach (var w in Config.ActiveWebhooks(tab.Flavor))
            if (await main.PosterFor(tab, w).PostAsync(tab.SampleEntry(), isTest: true))
                Log.Success($"[{tab.Name}] Test post sent to \"{w.Name}\". Check your Discord channel.");
    }
}
