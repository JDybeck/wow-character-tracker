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

    public static SolidColorBrush Hex(int rgb) => Brush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
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

// The two screenshot checkboxes for one kind of event.
sealed class ShotOption(FlavorSettings settings, string kind, Action saved) : Observable
{
    public bool Take
    {
        get => settings.Shoots(kind);
        set
        {
            settings.Screenshot[kind] = value;
            if (value && !settings.PostScreenshot.ContainsKey(kind)) settings.PostScreenshot[kind] = true;
            saved();
            Raise(nameof(Take));
            Raise(nameof(Post));
        }
    }

    public bool Post
    {
        get => settings.PostsShot(kind);
        set
        {
            settings.PostScreenshot[kind] = value;
            saved();
            Raise(nameof(Post));
        }
    }

    public void Refresh()
    {
        Raise(nameof(Take));
        Raise(nameof(Post));
    }
}

sealed record LevelRow(string LevelText, string Took, string Zone, string When);

// A boss kill, rare loot, new mount or death, shown in the character details.
sealed record HighlightRow(string Glyph, SolidColorBrush Brush, string Text, string When);

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
    public required bool Hardcore { get; init; }
    public required bool Dead { get; init; }
    public required IReadOnlyList<LevelRow> Levels { get; init; }
    public required IReadOnlyList<HighlightRow> Highlights { get; init; }

    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
    public string ItemLevelText => ItemLevel > 0 ? ItemLevel.ToString("0") : "-";
    public string PlayedText => Played > 0 ? Format.Played(Played) : "-";
    public string LastSeenText => Format.Ago(LastSeen) is { Length: > 0 } s ? s : "-";
    public string ProfessionsText => Professions is { Length: > 0 } and not "None" ? Professions : "None yet";
    public string TalentsText => Talents.Length > 0 ? Talents : "-";
    public string ZoneText => Zone.Length > 0 ? Zone : "-";
    public string Description => string.Join(" ", new[] { Race, Spec, ClassName }.Where(s => s.Length > 0));
    public bool HasLevels => Levels.Count > 0;
    public bool HasHighlights => Highlights.Count > 0;
    public string? Tag => Dead ? "DEAD" : Hardcore ? "HARDCORE" : null;

    public SolidColorBrush ClassBrush => ClassColors.Brush(ClassColors.For(ClassFile));
    public SolidColorBrush ClassSoftBrush => ClassColors.Brush(ClassColors.For(ClassFile), 0x26);

    // Segoe Fluent Icons glyphs: shield, heart, lightning bolt.
    public string RoleGlyph => Role switch { "TANK" => "", "HEALER" => "", "DAMAGER" => "", _ => "" };
    public SolidColorBrush RoleBrush => Role switch
    {
        "TANK" => ClassColors.Hex(0x94A3B8),
        "HEALER" => ClassColors.Hex(0x4ADE80),
        _ => ClassColors.Hex(0xF87171),
    };

    public string? PictureUrl { get => pictureUrl; set => Set(ref pictureUrl, value); }

    public async Task LoadPictureAsync()
    {
        PictureUrl = Discord.ClassIconUrl(ClassFile);
        if (await Discord.PictureUrlAsync(RaceFile, Sex, ClassFile) is { } url) PictureUrl = url;
    }

    // One row per character: the latest snapshot, filled in from events when there isn't one yet.
    public static List<CharacterRow> Build(TrackerData data)
    {
        var events = data.Events.GroupBy(e => e.Guid).ToDictionary(g => g.Key, g => g.OrderBy(e => e.At).ToList());
        var snaps = data.Characters.GroupBy(c => c.Guid).ToDictionary(g => g.Key, g => g.MaxBy(c => c.LastSeen)!);

        var rows = new List<CharacterRow>();
        foreach (var guid in snaps.Keys.Union(events.Keys))
        {
            snaps.TryGetValue(guid, out var s);
            var all = events.GetValueOrDefault(guid) ?? [];
            var levels = all.Where(e => e.Kind == "level").ToList();
            var last = all.LastOrDefault();
            if (s is null && last is null) continue;

            rows.Add(new CharacterRow
            {
                Guid = guid,
                Name = s?.Char is { Length: > 0 } n ? n : last?.Char ?? "?",
                Realm = s?.Realm ?? last!.Realm,
                Level = Math.Max(s?.Level ?? 0, all.Count > 0 ? all.Max(e => e.Level) : 0),
                ClassName = Pick(s?.Class, last?.Class),
                ClassFile = Pick(s?.ClassFile, last?.ClassFile),
                Race = Pick(s?.Race, last?.Race),
                RaceFile = Pick(s?.RaceFile, last?.RaceFile),
                Sex = Pick(s?.Sex, last?.Sex) is { Length: > 0 } sex ? sex : "male",
                Spec = Pick(s?.Spec, last?.Spec),
                Role = s?.Role ?? "",
                ItemLevel = s?.ItemLevel is > 0 ? s.ItemLevel : last?.ItemLevel ?? 0,
                Played = Math.Max(s?.Played ?? 0, all.Count > 0 ? all.Max(e => e.Played) : 0),
                Zone = Pick(s?.Zone, last?.Zone),
                Professions = Pick(s?.Professions, last?.Professions),
                Talents = Pick(s?.Talents, last?.Talents),
                LastSeen = Math.Max(s?.LastSeen ?? 0, last?.At ?? 0),
                Hardcore = (s?.Hardcore ?? false) || all.Any(e => e.Hardcore),
                Dead = all.Any(e => e.Kind == "death"),
                Levels = levels
                    .OrderByDescending(e => e.Level)
                    .Select(e => new LevelRow($"{e.Level - 1} to {e.Level}", Format.Duration(e.Seconds), e.Zone, When(e.At)))
                    .ToList(),
                Highlights = all
                    .Where(e => e.Kind is "boss" or "loot" or "mount" or "death")
                    .OrderByDescending(e => e.At)
                    .Select(Highlight)
                    .ToList(),
            });
        }

        return rows.OrderByDescending(r => r.LastSeen).ToList();
    }

    static HighlightRow Highlight(Entry e) => e.Kind switch
    {
        "boss" => new("", ClassColors.Hex(0xFBBF24), $"Killed {e.Boss}" + (e.Instance.Length > 0 ? $" ({e.Instance})" : ""), When(e.At)),
        "loot" => new("", ClassColors.Hex(e.Quality >= 5 ? 0xFF8000 : e.Quality == 4 ? 0xA335EE : 0x3B82F6), $"Looted {e.Item}", When(e.At)),
        "mount" => new("", ClassColors.Hex(0x4ADE80), (e.Bought ? "Bought " : "Got ") + e.Mount, When(e.At)),
        _ => new("", ClassColors.Hex(0xF87171), $"Died at level {e.Level}" + (e.Killer.Length > 0 ? $", killed by {e.Killer}" : ""), When(e.At)),
    };

    static string When(long at) => DateTimeOffset.FromUnixTimeSeconds(at).LocalDateTime.ToString("MMM d, HH:mm");
    static string Pick(string? a, string? b) => a is { Length: > 0 } ? a : b ?? "";
}

sealed class WebhookRow(WebhookInfo info, bool isActive) : Observable
{
    public WebhookInfo Info { get; } = info;
    public string Name => Info.Name;
    public bool IsActive { get; } = isActive;

    bool isEditing;
    public bool IsEditing { get => isEditing; set => Set(ref isEditing, value); }

    string editName = info.Name;
    public string EditName { get => editName; set => Set(ref editName, value); }

    public void NameChanged() => Raise(nameof(Name));

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

// Everything for one game version tab: its folder, addon, characters and what it posts.
sealed class FlavorViewModel : Observable
{
    readonly MainViewModel main;
    Tracker? tracker;
    string? flavorDir;
    Entry? lastPost;

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
        Shots = Flavors.ShotKinds.ToDictionary(k => k, k => new ShotOption(Settings, k, ShotsChanged));
    }

    // Screenshot options per kind of event, bound as Shots[level].Take and so on.
    public Dictionary<string, ShotOption> Shots { get; }

    void ShotsChanged()
    {
        main.Config.Save();
        WriteAddonSettings();
        if (flavorDir is not null && Wow.IsRunning(flavorDir))
            Log.Info($"[{Name}] Screenshot settings reach the game on your next /reload or login.");
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
        tracker = new Tracker(Flavor, path, () => main.Config.ActiveWebhook(Flavor) is { } w ? new Discord(w.Url, Flavor) : null,
            () => Settings, main.Backfill);
        tracker.DataChanged += data => MainViewModel.OnUi(() => ShowData(data));
        tracker.Posted += e => MainViewModel.OnUi(() => { lastPost = e; UpdateStatus(); });
        tracker.LiveTest += _ => MainViewModel.OnUi(() => AddonStep.Detail = $"v{AddonInstaller.BundledVersion}, instant posting works");
        tracker.Start();
        UpdateStatus();
        CommandManager.InvalidateRequerySuggested();
    }

    // Installs or updates the bundled addon automatically. It's the whole point of the app,
    // so there's nothing to ask. Also keeps the interface number in step with game patches.
    void SetUpAddon(bool force)
    {
        if (flavorDir is null) return;
        var installed = AddonInstaller.InstalledVersion(flavorDir);
        var bundled = AddonInstaller.BundledVersion;
        var interfaceVersion = Wow.InterfaceVersion(flavorDir);

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
            else if (interfaceVersion is int v && AddonInstaller.InstalledInterface(flavorDir) != v.ToString())
            {
                AddonInstaller.SetInterface(flavorDir, v);
                Log.Info($"[{Name}] Updated the addon for game version {v}.");
            }

            WriteAddonSettings(); // installing puts the default Settings.lua back
            AddonStep.Ok = true;
            AddonStep.Detail = $"v{bundled} installed. Type /ct test in game to check instant posting";
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

    public async Task CheckDiscordAsync()
    {
        var active = main.Config.ActiveWebhook(Flavor);
        if (active is null)
        {
            DiscordStep.Ok = false;
            DiscordStep.Detail = main.Config.Webhooks.Count == 0 ? "Add a webhook" : "Pick a webhook for this tab";
            return;
        }

        var (name, problem) = await Discord.CheckAsync(active.Url);
        DiscordStep.Ok = name is not null || problem!.StartsWith("Couldn't reach") || problem.Contains("too long");
        DiscordStep.Detail = name is not null ? $"Posting to \"{active.Name}\"" : problem!;
        if (name is null) Log.Warn($"[{Name}] {problem}");
    }

    TrackerData lastData = TrackerData.Empty;

    void ShowData(TrackerData data)
    {
        lastData = data;
        var keep = SelectedCharacter?.Guid;
        Characters.Clear();
        var removed = Settings.Removed;
        foreach (var r in CharacterRow.Build(data)
                     .Where(r => !(removed.TryGetValue(r.Guid, out var at) && r.LastSeen <= at)))
        {
            Characters.Add(r);
            _ = r.LoadPictureAsync();
        }
        SelectedCharacter = Characters.FirstOrDefault(c => c.Guid == keep);
        Raise(nameof(HasCharacters));
        UpdateStatus();
    }

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

    // Uses the most recently played character for the test post so it looks familiar.
    public Entry SampleEntry()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var c = Characters.FirstOrDefault();
        if (c is null)
            return new Entry
            {
                Id = "test", Char = "Testy", Level = 12, Seconds = 18 * 60 + 4, At = now, Zone = "Elwynn Forest",
                Professions = "Mining 75, Herbalism 60", Talents = "Arms 5 / Fury 2 / Protection 0", Class = "Warrior",
                ClassFile = "WARRIOR", Race = "Human", RaceFile = "Human", Played = 4 * 3600 + 12 * 60,
            };

        return new Entry
        {
            Id = "test", Char = c.Name, Realm = c.Realm, Level = Math.Max(c.Level, 2), Seconds = 18 * 60 + 4, At = now,
            Zone = c.Zone, Professions = c.Professions, Talents = c.Talents, Spec = c.Spec, Class = c.ClassName,
            ClassFile = c.ClassFile, Race = c.Race, RaceFile = c.RaceFile, Sex = c.Sex, Played = c.Played,
        };
    }

    public void Shutdown() => tracker?.Dispose();
}

sealed class MainViewModel : Observable
{
    public MainViewModel(bool backfill)
    {
        Backfill = backfill;
        Log.Written += line => OnUi(() =>
        {
            Logs.Insert(0, line);
            while (Logs.Count > 300) Logs.RemoveAt(Logs.Count - 1);
        });

        SyncCommand = new RelayCommand(_ => SelectedFlavor?.Tracker?.SyncAsync() ?? Task.CompletedTask,
            _ => SelectedFlavor?.Tracker is not null);
        ShowPageCommand = new RelayCommand(p => Page = (string)p!);
        ResetPostingCommand = new RelayCommand(_ => SelectedFlavor?.ResetPosting());
        RemoveCharacterCommand = new RelayCommand(p => SelectedFlavor?.RemoveCharacter((CharacterRow)p!));
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
        CancelRenameCommand = new RelayCommand(p => ((WebhookRow)p!).IsEditing = false);
        TestWebhookCommand = new RelayCommand(_ => SendTestAsync(),
            _ => SelectedFlavor is not null && Config.ActiveWebhook(SelectedFlavor.Flavor) is not null);
    }

    public bool Backfill { get; }
    public Config Config { get; private set; } = new();

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
    public ICommand RemoveCharacterCommand { get; }
    public ICommand ToggleAddWebhookCommand { get; }
    public ICommand SaveWebhookCommand { get; }
    public ICommand SelectWebhookCommand { get; }
    public ICommand RemoveWebhookCommand { get; }
    public ICommand RenameWebhookCommand { get; }
    public ICommand SaveRenameCommand { get; }
    public ICommand CancelRenameCommand { get; }
    public ICommand TestWebhookCommand { get; }

    public async Task StartAsync()
    {
        Config = Config.Load();
        Log.Info("Character Tracker started.");
        ShowAddWebhook = Config.Webhooks.Count == 0;

        foreach (var f in Flavors.All) FlavorTabs.Add(new FlavorViewModel(this, f));
        SelectedFlavor = FlavorTabs.FirstOrDefault(t => t.Flavor.Key == Config.SelectedFlavor) ?? FlavorTabs[0];

        foreach (var tab in FlavorTabs) tab.Start();
        if (Config.Webhooks.Count == 0)
            Log.Warn("No Discord webhook yet. Add one in the Discord panel so posts can go out.");
        await Task.WhenAll(FlavorTabs.Select(t => t.CheckDiscordAsync()));
    }

    void RefreshWebhooks()
    {
        Webhooks.Clear();
        var active = SelectedFlavor is null ? null : Config.For(SelectedFlavor.Flavor).ActiveWebhookId;
        foreach (var w in Config.Webhooks) Webhooks.Add(new WebhookRow(w, w.Id == active));
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
        Config.Webhooks.Add(info);
        // Active for this tab, and for any tab that has nothing to post to yet.
        foreach (var tab in FlavorTabs)
            if (tab == SelectedFlavor || Config.ActiveWebhook(tab.Flavor) is null)
                Config.For(tab.Flavor).ActiveWebhookId = info.Id;
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

    void SelectWebhook(WebhookRow row)
    {
        if (SelectedFlavor is null) return;
        var s = Config.For(SelectedFlavor.Flavor);
        if (s.ActiveWebhookId == row.Info.Id) return;
        s.ActiveWebhookId = row.Info.Id;
        Config.Save();
        RefreshWebhooks();
        Log.Info($"[{SelectedFlavor.Name}] Now posting to \"{row.Name}\".");
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
        foreach (var tab in FlavorTabs)
        {
            var s = Config.For(tab.Flavor);
            if (s.ActiveWebhookId == row.Info.Id) s.ActiveWebhookId = Config.Webhooks.FirstOrDefault()?.Id;
        }
        Config.Save();
        RefreshWebhooks();
        Log.Info($"Removed webhook \"{row.Name}\".");
        foreach (var tab in FlavorTabs) _ = tab.CheckDiscordAsync();
    }

    async Task SendTestAsync()
    {
        if (SelectedFlavor is not { } tab || Config.ActiveWebhook(tab.Flavor) is not { } active) return;
        if (await new Discord(active.Url, tab.Flavor).PostAsync(tab.SampleEntry(), isTest: true))
            Log.Success($"[{tab.Name}] Test post sent to \"{active.Name}\". Check your Discord channel.");
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
