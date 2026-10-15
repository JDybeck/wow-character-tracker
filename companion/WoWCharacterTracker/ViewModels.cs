using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WoWCharacterTracker;

abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

sealed class RelayCommand(Func<object?, Task> run, Func<object?, bool>? canRun = null) : ICommand
{
    bool busy;

    public RelayCommand(Action<object?> run, Func<object?, bool>? canRun = null)
        : this(p => { run(p); return Task.CompletedTask; }, canRun) { }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !busy && (canRun?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        busy = true;
        CommandManager.InvalidateRequerySuggested();
        try { await run(parameter); }
        catch (Exception ex) { Log.Error(ex.Message); }
        finally
        {
            busy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}

static class ClassColors
{
    static readonly Color Neutral = Color.FromRgb(0x94, 0xA3, 0xB8);

    public static Color For(string classFile) =>
        Discord.ClassColors.TryGetValue(classFile.ToUpperInvariant(), out var c)
            ? Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c)
            : Neutral;

    public static SolidColorBrush Brush(Color c, byte alpha = 255)
    {
        var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }
}

// One of the three setup checks at the top of the window.
sealed class SetupItem(string title) : Observable
{
    public string Title { get; } = title;

    bool ok;
    public bool Ok { get => ok; set => Set(ref ok, value); }

    string detail = "";
    public string Detail { get => detail; set => Set(ref detail, value); }

    string? actionText;
    public string? ActionText { get => actionText; set => Set(ref actionText, value); }

    public ICommand? ActionCommand { get; init; }
}

sealed record LevelRow(string LevelText, string Took, string Zone, string When);

sealed class CharacterRow : Observable
{
    string? pictureUrl;

    public required string Guid { get; init; }
    public required string Name { get; init; }
    public required string Realm { get; init; }
    public required int Level { get; init; }
    public required string ClassName { get; init; }
    public required string ClassFile { get; init; }
    public required string Race { get; init; }
    public required string RaceFile { get; init; }
    public required string Sex { get; init; }
    public required string Spec { get; init; }
    public required string Role { get; init; }
    public required double ItemLevel { get; init; }
    public required long Played { get; init; }
    public required string Zone { get; init; }
    public required string Professions { get; init; }
    public required string Talents { get; init; }
    public required long LastSeen { get; init; }
    public required IReadOnlyList<LevelRow> Levels { get; init; }

    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
    public string ItemLevelText => ItemLevel > 0 ? ItemLevel.ToString("0") : "-";
    public string PlayedText => Played > 0 ? Format.Played(Played) : "-";
    public string LastSeenText => Format.Ago(LastSeen) is { Length: > 0 } s ? s : "-";
    public string ProfessionsText => Professions is { Length: > 0 } and not "None" ? Professions : "None yet";
    public string TalentsText => Talents.Length > 0 ? Talents : "-";
    public string ZoneText => Zone.Length > 0 ? Zone : "-";
    public string Description => string.Join(" ", new[] { Race, Spec, ClassName }.Where(s => s.Length > 0));
    public bool HasLevels => Levels.Count > 0;

    public SolidColorBrush ClassBrush => ClassColors.Brush(ClassColors.For(ClassFile));
    public SolidColorBrush ClassSoftBrush => ClassColors.Brush(ClassColors.For(ClassFile), 0x26);

    // Segoe Fluent Icons glyphs: shield, heart, lightning bolt.
    public string RoleGlyph => Role switch { "TANK" => "", "HEALER" => "", "DAMAGER" => "", _ => "" };
    public SolidColorBrush RoleBrush => Role switch
    {
        "TANK" => ClassColors.Brush(Color.FromRgb(0x94, 0xA3, 0xB8)),
        "HEALER" => ClassColors.Brush(Color.FromRgb(0x4A, 0xDE, 0x80)),
        _ => ClassColors.Brush(Color.FromRgb(0xF8, 0x71, 0x71)),
    };

    public string? PictureUrl { get => pictureUrl; set => Set(ref pictureUrl, value); }

    public async Task LoadPictureAsync()
    {
        PictureUrl = Discord.ClassIconUrl(ClassFile);
        if (await Discord.PictureUrlAsync(RaceFile, Sex, ClassFile) is { } url) PictureUrl = url;
    }

    // One row per character: the latest snapshot, filled in from level-ups when there isn't one yet.
    public static List<CharacterRow> Build(TrackerData data)
    {
        var events = data.Events.GroupBy(e => e.Guid).ToDictionary(g => g.Key, g => g.OrderBy(e => e.Level).ToList());
        var snaps = data.Characters.GroupBy(c => c.Guid).ToDictionary(g => g.Key, g => g.MaxBy(c => c.LastSeen)!);

        var rows = new List<CharacterRow>();
        foreach (var guid in snaps.Keys.Union(events.Keys))
        {
            snaps.TryGetValue(guid, out var s);
            var levels = events.GetValueOrDefault(guid) ?? [];
            var last = levels.LastOrDefault();
            if (s is null && last is null) continue;

            rows.Add(new CharacterRow
            {
                Guid = guid,
                Name = s?.Char is { Length: > 0 } n ? n : last?.Char ?? "?",
                Realm = s?.Realm ?? last!.Realm,
                Level = Math.Max(s?.Level ?? 0, last?.Level ?? 0),
                ClassName = Pick(s?.Class, last?.Class),
                ClassFile = Pick(s?.ClassFile, last?.ClassFile),
                Race = Pick(s?.Race, last?.Race),
                RaceFile = Pick(s?.RaceFile, last?.RaceFile),
                Sex = Pick(s?.Sex, last?.Sex) is { Length: > 0 } sex ? sex : "male",
                Spec = Pick(s?.Spec, last?.Spec),
                Role = s?.Role ?? "",
                ItemLevel = s?.ItemLevel is > 0 ? s.ItemLevel : last?.ItemLevel ?? 0,
                Played = Math.Max(s?.Played ?? 0, last?.Played ?? 0),
                Zone = Pick(s?.Zone, last?.Zone),
                Professions = Pick(s?.Professions, last?.Professions),
                Talents = Pick(s?.Talents, last?.Talents),
                LastSeen = Math.Max(s?.LastSeen ?? 0, last?.At ?? 0),
                Levels = levels
                    .OrderByDescending(e => e.Level)
                    .Select(e => new LevelRow($"{e.Level - 1} to {e.Level}", Format.Duration(e.Seconds), e.Zone,
                        DateTimeOffset.FromUnixTimeSeconds(e.At).LocalDateTime.ToString("MMM d, HH:mm")))
                    .ToList(),
            });
        }

        return rows.OrderByDescending(r => r.LastSeen).ToList();
    }

    static string Pick(string? a, string? b) => a is { Length: > 0 } ? a : b ?? "";
}

sealed class WebhookRow(WebhookInfo info, bool isActive)
{
    public WebhookInfo Info { get; } = info;
    public string Name => Info.Name;
    public bool IsActive { get; } = isActive;

    // https://discord.com/.../1234/AbCdE...WxYz
    public string Masked
    {
        get
        {
            try
            {
                var uri = new Uri(Info.Url);
                var parts = uri.AbsolutePath.TrimEnd('/').Split('/');
                var token = parts[^1];
                var id = parts[^2];
                return token.Length > 9
                    ? $"{uri.Scheme}://{uri.Host}/.../{id}/{token[..5]}...{token[^4..]}"
                    : $"{uri.Scheme}://{uri.Host}/.../{id}/...";
            }
            catch (Exception) { return Info.Url.Length > 30 ? Info.Url[..30] + "..." : Info.Url; }
        }
    }
}

sealed class MainViewModel : Observable
{
    readonly bool backfill;
    Config config = new();
    Tracker? tracker;
    string? wowPath;
    Entry? lastPost;

    public MainViewModel(bool backfill)
    {
        this.backfill = backfill;
        Log.Written += line => OnUi(() =>
        {
            Logs.Insert(0, line);
            while (Logs.Count > 300) Logs.RemoveAt(Logs.Count - 1);
        });

        SyncCommand = new RelayCommand(_ => tracker?.SyncAsync() ?? Task.CompletedTask, _ => tracker is not null);
        WowStep = new SetupItem("World of Warcraft")
        {
            Detail = "Looking for the game...",
            ActionText = "Browse",
            ActionCommand = new RelayCommand(_ => BrowseWowAsync()),
        };
        AddonStep = new SetupItem("Addon")
        {
            Detail = "Waiting for the game folder",
            ActionCommand = new RelayCommand(_ => SetUpAddon(force: true)),
        };
        DiscordStep = new SetupItem("Discord") { Detail = "Add a webhook below" };
        ToggleAddWebhookCommand = new RelayCommand(_ => { ShowAddWebhook = !ShowAddWebhook; AddWebhookError = ""; });
        SaveWebhookCommand = new RelayCommand(_ => SaveWebhookAsync());
        SelectWebhookCommand = new RelayCommand(p => SelectWebhook((WebhookRow)p!));
        RemoveWebhookCommand = new RelayCommand(p => RemoveWebhook((WebhookRow)p!));
        TestWebhookCommand = new RelayCommand(_ => SendTestAsync(), _ => config.ActiveWebhook is not null);
    }

    // --- bindable state ---

    public ObservableCollection<LogLine> Logs { get; } = [];
    public ObservableCollection<CharacterRow> Characters { get; } = [];
    public ObservableCollection<WebhookRow> Webhooks { get; } = [];

    CharacterRow? selectedCharacter;
    public CharacterRow? SelectedCharacter { get => selectedCharacter; set => Set(ref selectedCharacter, value); }
    public bool HasCharacters => Characters.Count > 0;

    bool isSyncing;
    public bool IsSyncing { get => isSyncing; set => Set(ref isSyncing, value); }

    string statusText = "Starting up...";
    public string StatusText { get => statusText; set => Set(ref statusText, value); }

    public SetupItem WowStep { get; }
    public SetupItem AddonStep { get; }
    public SetupItem DiscordStep { get; }
    public IReadOnlyList<SetupItem> Steps => [WowStep, AddonStep, DiscordStep];

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
    public ICommand ToggleAddWebhookCommand { get; }
    public ICommand SaveWebhookCommand { get; }
    public ICommand SelectWebhookCommand { get; }
    public ICommand RemoveWebhookCommand { get; }
    public ICommand TestWebhookCommand { get; }

    // --- startup ---

    public async Task StartAsync()
    {
        config = Config.Load();
        RefreshWebhooks();
        ShowAddWebhook = config.Webhooks.Count == 0;
        Log.Info("Character Tracker started.");

        var path = Wow.IsValid(config.WowPath) ? config.WowPath : Wow.Detect();
        if (path is null)
        {
            WowStep.Ok = false;
            WowStep.Detail = $"Not found. Click Browse and pick the folder with {Wow.Flavor} in it.";
            Log.Warn("Couldn't find World of Warcraft. In Battle.net, click the gear next to Play, then Show in Explorer, and pick that folder with Browse.");
        }
        else
        {
            UseWowPath(path);
        }

        await CheckDiscordAsync();
    }

    void UseWowPath(string path)
    {
        wowPath = path;
        WowStep.Ok = true;
        WowStep.Detail = path;
        WowStep.ActionText = "Change";
        if (config.WowPath != path)
        {
            config.WowPath = path;
            config.Save();
        }
        Log.Success($"World of Warcraft found: {path}");

        SetUpAddon(force: false);

        tracker?.Dispose();
        tracker = new Tracker(path, () => config.ActiveWebhook is { } w ? new Discord(w.Url) : null, backfill);
        tracker.DataChanged += data => OnUi(() => ShowData(data));
        tracker.Posted += e => OnUi(() => { lastPost = e; UpdateStatus(); });
        tracker.Start();
        UpdateStatus();
        CommandManager.InvalidateRequerySuggested();
    }

    async Task BrowseWowAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Pick your World of Warcraft folder" };
        if (dialog.ShowDialog() != true) return;
        var path = Wow.Normalize(dialog.FolderName);
        if (path is null)
        {
            Log.Warn($"That folder doesn't have a {Wow.Flavor} folder inside it. Pick the main World of Warcraft folder.");
            return;
        }
        UseWowPath(path);
        await Task.CompletedTask;
    }

    // Installs or updates the bundled addon automatically. It's the whole point of the app,
    // so there's nothing to ask. Also keeps the interface number in step with game patches.
    void SetUpAddon(bool force)
    {
        if (wowPath is null) return;
        var installed = AddonInstaller.InstalledVersion(wowPath);
        var bundled = AddonInstaller.BundledVersion;
        var interfaceVersion = Wow.InterfaceVersion(wowPath);

        try
        {
            if (installed is null || installed != bundled || force)
            {
                AddonInstaller.Install(wowPath, interfaceVersion);
                Log.Success(installed is null ? $"Installed the addon (v{bundled})." : $"Updated the addon to v{bundled}.");
                if (Wow.IsRunning())
                    Log.Warn(installed is null
                        ? "WoW is running: exit the game fully and start it again so it finds the new addon."
                        : "WoW is running: type /reload in game to load the new addon version.");
            }
            else if (interfaceVersion is int v && AddonInstaller.InstalledInterface(wowPath) != v.ToString())
            {
                AddonInstaller.SetInterface(wowPath, v);
                Log.Info($"Updated the addon for game version {v}.");
            }

            AddonStep.Ok = true;
            AddonStep.Detail = $"v{bundled} installed";
            AddonStep.ActionText = null;
        }
        catch (UnauthorizedAccessException)
        {
            AddonStep.Ok = false;
            AddonStep.Detail = "Windows blocked writing to the AddOns folder";
            AddonStep.ActionText = "Retry";
            Log.Error($"Couldn't write to {Wow.AddOnsDir(wowPath)}. Right-click the app and pick Run as administrator once, then it works normally.");
        }
    }

    async Task CheckDiscordAsync()
    {
        var active = config.ActiveWebhook;
        if (active is null)
        {
            DiscordStep.Ok = false;
            DiscordStep.Detail = "Add a webhook below";
            Log.Warn("No Discord webhook yet. Add one in the Discord panel so level-ups get posted.");
            return;
        }

        var (name, problem) = await Discord.CheckAsync(active.Url);
        DiscordStep.Ok = name is not null || problem!.StartsWith("Couldn't reach") || problem.Contains("too long");
        DiscordStep.Detail = name is not null ? $"Posting to \"{active.Name}\"" : problem!;
        if (name is not null) Log.Success($"Discord webhook \"{active.Name}\" works.");
        else Log.Warn(problem!);
    }

    // --- data ---

    void ShowData(TrackerData data)
    {
        var keep = SelectedCharacter?.Guid;
        var rows = CharacterRow.Build(data);
        Characters.Clear();
        foreach (var r in rows)
        {
            Characters.Add(r);
            _ = r.LoadPictureAsync();
        }
        SelectedCharacter = Characters.FirstOrDefault(c => c.Guid == keep);
        Raise(nameof(HasCharacters));
        UpdateStatus();
    }

    void UpdateStatus()
    {
        if (tracker is null) { StatusText = "Waiting for the game folder"; return; }
        var count = Characters.Count;
        var chars = count == 0 ? "no characters yet" : $"{count} character{(count == 1 ? "" : "s")}";
        StatusText = lastPost is null
            ? $"Watching for level-ups, {chars}"
            : $"Watching for level-ups, {chars}, last post: {lastPost.Char} hit {lastPost.Level}";
    }

    // --- webhooks ---

    void RefreshWebhooks()
    {
        Webhooks.Clear();
        foreach (var w in config.Webhooks) Webhooks.Add(new WebhookRow(w, w.Id == config.ActiveWebhookId));
        Raise(nameof(HasWebhooks));
        CommandManager.InvalidateRequerySuggested();
    }

    async Task SaveWebhookAsync()
    {
        AddWebhookError = "";
        var url = NewWebhookUrl.Trim();
        if (!Discord.LooksLikeWebhook(url))
        {
            AddWebhookError = "That doesn't look like a webhook link. It starts with https://discord.com/api/webhooks/";
            return;
        }

        var (discordName, problem) = await Discord.CheckAsync(url);
        if (discordName is null)
        {
            AddWebhookError = problem!;
            return;
        }

        var info = new WebhookInfo { Name = NewWebhookName.Trim() is { Length: > 0 } n ? n : discordName, Url = url };
        config.Webhooks.Add(info);
        config.ActiveWebhookId = info.Id;
        config.Save();
        RefreshWebhooks();

        NewWebhookName = "";
        NewWebhookUrl = "";
        ShowAddWebhook = false;
        Log.Success($"Added webhook \"{info.Name}\" and made it active. Use Send test to see a post.");
        await CheckDiscordAsync();
        if (tracker is not null) await tracker.SyncAsync(); // post anything that was waiting
    }

    void SelectWebhook(WebhookRow row)
    {
        if (config.ActiveWebhookId == row.Info.Id) return;
        config.ActiveWebhookId = row.Info.Id;
        config.Save();
        RefreshWebhooks();
        Log.Info($"Now posting to \"{row.Name}\".");
        _ = CheckDiscordAsync();
    }

    void RemoveWebhook(WebhookRow row)
    {
        var answer = MessageBox.Show($"Remove the webhook \"{row.Name}\"?", "Character Tracker",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        config.Webhooks.Remove(row.Info);
        if (config.ActiveWebhookId == row.Info.Id) config.ActiveWebhookId = config.Webhooks.FirstOrDefault()?.Id;
        config.Save();
        RefreshWebhooks();
        Log.Info($"Removed webhook \"{row.Name}\".");
        _ = CheckDiscordAsync();
    }

    async Task SendTestAsync()
    {
        if (config.ActiveWebhook is not { } active) return;
        var sample = SampleEntry();
        if (await new Discord(active.Url).PostAsync(sample, isTest: true))
            Log.Success($"Test post sent to \"{active.Name}\". Check your Discord channel.");
    }

    // Uses your most recently played character for the test post so you see something familiar.
    Entry SampleEntry()
    {
        var c = Characters.FirstOrDefault();
        if (c is null)
            return new Entry("test", "Testy", "", 12, 18 * 60 + 4, DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                "Elwynn Forest", "Mining 75, Herbalism 60", "Class 4 / Arms 2",
                "Arms", "Warrior", "WARRIOR", "Human", "Human", "male", 4 * 3600 + 12 * 60);

        return new Entry("test", c.Name, c.Realm, Math.Max(c.Level, 2), 18 * 60 + 4,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(), c.Zone, c.Professions, c.Talents,
            c.Spec, c.ClassName, c.ClassFile, c.Race, c.RaceFile, c.Sex, c.Played);
    }

    static void OnUi(Action a)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null) return;
        if (d.CheckAccess()) a(); else d.BeginInvoke(a);
    }

    public void Shutdown() => tracker?.Dispose();
}
