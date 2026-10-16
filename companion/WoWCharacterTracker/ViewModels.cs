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

// A filter chip above the character list: a realm (Retail) or ruleset (Forever), or "All".
sealed record GroupChip(string Key, string Label);

// A boss kill, rare loot, new mount or death, shown in the character details.
// A small icon with text, like a crest and its count or a profession and its skill. The icon and
// tooltip fill in once Wowhead has answered.
sealed class IconChip(string text, string? iconUrl = null, string tooltip = "") : Observable
{
    public string Text { get; } = text;

    string? iconUrl = iconUrl;
    public string? IconUrl { get => iconUrl; set => Set(ref iconUrl, value); }

    string tooltip = tooltip;
    public string Tooltip { get => tooltip; set => Set(ref tooltip, value); }

    // For crests: the currency id to look up.
    public int Tag { get; init; }
}

// One part of an amount of money: "73,101" with a gold coin, "77" with a silver one...
sealed record Coin(string Amount, SolidColorBrush Brush)
{
    static readonly SolidColorBrush Gold = ClassColors.Hex(0xF2C14E), Silver = ClassColors.Hex(0xC7CDD3), Copper = ClassColors.Hex(0xC27C4B);

    // Everything: gold, silver and copper (leading zero parts left out).
    public static IReadOnlyList<Coin> Full(long copper)
    {
        long g = copper / 10000, s = copper / 100 % 100, c = copper % 100;
        var parts = new List<Coin>();
        if (g > 0) parts.Add(new(g.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), Gold));
        if (g > 0 || s > 0) parts.Add(new(s.ToString(), Silver));
        parts.Add(new(c.ToString(), Copper));
        return parts;
    }

    // For the list: just gold from 100g up, gold and silver below that, silver and copper under 1g.
    public static IReadOnlyList<Coin> Short(long copper)
    {
        long g = copper / 10000, s = copper / 100 % 100, c = copper % 100;
        if (g >= 100) return [new(g.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), Gold)];
        if (g > 0) return [new(g.ToString(), Gold), new(s.ToString(), Silver)];
        return s > 0 ? [new(s.ToString(), Silver), new(c.ToString(), Copper)] : [new(c.ToString(), Copper)];
    }
}

// A boss kill, rare loot, new mount or death, shown in the character details. Loot and mounts
// get the item's real icon and tooltip from Wowhead.
// The verb ("Looted ") is plain text, the thing after it gets the color (item quality for loot).
sealed class HighlightRow(string glyph, SolidColorBrush brush, string verb, string subject, string when, int itemId = 0) : Observable
{
    public string Glyph { get; } = glyph;
    SolidColorBrush brush = brush;
    public SolidColorBrush Brush { get => brush; set => Set(ref brush, value); }
    public string Verb { get; } = verb;
    public string Subject { get; } = subject;
    public string Text => Verb + Subject;
    public string When { get; } = when;
    public int ItemId { get; } = itemId;

    // WoW's item quality colors: poor, common, uncommon, rare, epic, legendary, artifact, heirloom.
    static readonly int[] QualityColors = [0x9D9D9D, 0xFFFFFF, 0x1EFF00, 0x0070DD, 0xA335EE, 0xFF8000, 0xE6CC80, 0x00CCFF];
    public static SolidColorBrush? QualityBrush(int quality) =>
        quality >= 0 && quality < QualityColors.Length ? ClassColors.Hex(QualityColors[quality]) : null;

    string? iconUrl;
    public string? IconUrl { get => iconUrl; set { Set(ref iconUrl, value); Raise(nameof(HasIcon)); } }
    public bool HasIcon => iconUrl is not null;

    string tooltip = "";
    public string Tooltip { get => tooltip; set => Set(ref tooltip, value); }
}

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
    public required string Ruleset { get; init; }
    public required bool Dead { get; init; }
    public required long Money { get; init; }   // copper
    public required string Crests { get; init; }
    public string Guild { get; init; } = "";
    public bool HasGuild => Guild.Length > 0;

    public string MoneyText => Money > 0 ? Format.Money(Money) : "-";
    public bool HasMoney => Money > 0;
    public IReadOnlyList<Coin> MoneyCoins => Coin.Full(Money);
    public IReadOnlyList<Coin> ShortCoins => Coin.Short(Money);
    public bool HasCrests => Crests.Length > 0;
    public IReadOnlyList<IconChip> CrestList { get; private set; } = [];
    public IReadOnlyList<IconChip> ProfessionList { get; private set; } = [];
    public bool HasProfessions => ProfessionList.Count > 0;

    // Fills in icons and tooltips: crests and items from Wowhead, professions from the built-in list.
    public async Task LoadIconsAsync(Flavor flavor)
    {
        CrestList = CrestChips(Crests);
        ProfessionList = ProfessionChips(Professions);
        Raise(nameof(CrestList));
        Raise(nameof(ProfessionList));
        Raise(nameof(HasProfessions));

        foreach (var chip in CrestList.Where(c => c.Tag > 0))
            if (await Wowhead.GetAsync("currency", chip.Tag, flavor) is { } info)
            {
                chip.IconUrl = Wowhead.IconUrl(info.Icon, "small");
                chip.Tooltip = info.Tooltip.Length > 0 ? info.Tooltip : info.Name;
            }
        foreach (var h in Highlights.Where(h => h.ItemId > 0))
            if (await Wowhead.GetAsync("item", h.ItemId, flavor) is { } info)
            {
                h.IconUrl = Wowhead.IconUrl(info.Icon, "small");
                h.Tooltip = info.Tooltip;
                if (HighlightRow.QualityBrush(info.Quality) is { } q) h.Brush = q;
            }
    }
    public required IReadOnlyList<LevelRow> Levels { get; init; }
    public required IReadOnlyList<HighlightRow> Highlights { get; init; }

    // The user's tags as on/off chips for this character, and the box for making a new one.
    IReadOnlyList<ToggleOption> tagOptions = [];
    public IReadOnlyList<ToggleOption> TagOptions { get => tagOptions; set { tagOptions = value; Raise(nameof(TagOptions)); } }
    string newTag = "";
    public string NewTag { get => newTag; set => Set(ref newTag, value); }

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
    public bool IsTest => TestCharacters.IsTest(Guid);
    public string? Tag => IsTest ? "TEST" : Dead ? "DEAD" : Hardcore ? "HARDCORE" : null;

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
        // Live check-ins leave the guild out when there's none, so take the newest snapshot that knows it.
        var guilds = data.Characters.Where(c => c.Guild is not null).GroupBy(c => c.Guid)
            .ToDictionary(g => g.Key, g => g.MaxBy(c => c.LastSeen)!.Guild!);

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
                Ruleset = Pick(s?.Ruleset, last?.Ruleset) is { Length: > 0 } rs ? rs
                    : (s?.Hardcore ?? false) || all.Any(e => e.Hardcore) ? "Hardcore" : "Normal",
                Dead = all.Any(e => e.Kind == "death"),
                // The newest of the snapshot and the last event says what the character has now.
                Money = last is not null && last.At > (s?.LastSeen ?? 0) && last.Money > 0 ? last.Money : s?.Money ?? last?.Money ?? 0,
                Crests = Pick(s?.Crests, last?.Crests),
                Guild = guilds.GetValueOrDefault(guid) ?? all.LastOrDefault(e => e.Guild is not null)?.Guild ?? "",
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
        "boss" => new("", ClassColors.Hex(0xFBBF24), "Killed ", e.Boss + (e.Instance.Length > 0 ? $" ({e.Instance})" : ""), When(e.At)),
        "loot" => new("", HighlightRow.QualityBrush(e.Quality) ?? ClassColors.Hex(0x0070DD), "Looted ", e.Item, When(e.At), e.ItemId),
        "mount" => new("", ClassColors.Hex(0x4ADE80), e.Bought ? "Bought " : "Got ", e.Mount, When(e.At), e.ItemId),
        _ => new("", ClassColors.Hex(0xF87171), "", $"Died at level {e.Level}" + (e.Killer.Length > 0 ? $", killed by {e.Killer}" : ""), When(e.At)),
    };

    // "id~qty~name,..." from newer addons; older ones wrote "Name 45, Name 120" (no icons then).
    static IReadOnlyList<IconChip> CrestChips(string crests)
    {
        if (crests.Length == 0) return [];
        if (!crests.Contains('~'))
            return crests.Split(", ").Select(c => new IconChip(c)).ToList();
        return crests.Split(',').Select(c => c.Split('~')).Where(p => p.Length >= 3)
            .Select(p => new IconChip(p[1], null, p[2]) { Tag = int.TryParse(p[0], out var id) ? id : 0 })
            .ToList();
    }

    // "Mining 104, Blacksmithing 96" with each profession's icon.
    static IReadOnlyList<IconChip> ProfessionChips(string professions)
    {
        if (professions is "" or "None") return [];
        return professions.Split(", ").Select(p =>
        {
            var space = p.LastIndexOf(' ');
            var name = space > 0 ? p[..space] : p;
            var icon = Wowhead.ProfessionIcons.TryGetValue(name, out var i) ? Wowhead.IconUrl(i, "small") : null;
            return new IconChip(p, icon, name);
        }).ToList();
    }

    static string When(long at) => DateTimeOffset.FromUnixTimeSeconds(at).LocalDateTime.ToString("MMM d, HH:mm");
    static string Pick(string? a, string? b) => a is { Length: > 0 } ? a : b ?? "";
}

// A clickable on/off chip, used for a webhook's games and rulesets.
sealed class ToggleOption(string name, bool on, Action<bool> changed) : Observable
{
    bool isOn = on;
    public string Name { get; } = name;
    public bool IsOn
    {
        get => isOn;
        set
        {
            if (!Set(ref isOn, value)) return;
            changed(value);
        }
    }
}

sealed class WebhookRow(WebhookInfo info, bool isActive, bool isExpanded,
    IReadOnlyList<ToggleOption> games, IReadOnlyList<ToggleOption> rulesets, IReadOnlyList<ToggleOption> tags) : Observable
{
    public WebhookInfo Info { get; } = info;
    public string Name => Info.Name;
    public bool IsActive { get; } = isActive;

    // The options panel: which games this webhook posts for, which Forever rulesets, and which
    // character tags (none picked = every character).
    public IReadOnlyList<ToggleOption> Games { get; } = games;
    public IReadOnlyList<ToggleOption> Rulesets { get; } = rulesets;
    public IReadOnlyList<ToggleOption> Tags { get; } = tags;
    public bool HasTags => Tags.Count > 0;

    bool isExpanded = isExpanded;
    public bool IsExpanded { get => isExpanded; set => Set(ref isExpanded, value); }

    // "Retail, Forever (Hardcore)" when collapsed, so the options show without opening them.
    public string Summary
    {
        get
        {
            var on = Games.Where(g => g.IsOn).Select(g => g.Name).ToList();
            if (on.Count == 0) return "Not posting anywhere";
            var rules = Rulesets.Where(r => r.IsOn).Select(r => r.Name).ToList();
            var forever = Flavors.Forever.Name;
            if (on.Contains(forever) && rules.Count < Rulesets.Count)
                on[on.IndexOf(forever)] = $"{forever} ({(rules.Count == 0 ? "no rulesets" : string.Join(", ", rules))})";
            var tagged = Tags.Where(t => t.IsOn).Select(t => t.Name).ToList();
            return "Posts for " + string.Join(", ", on) + (tagged.Count > 0 ? $", only {string.Join(", ", tagged)}" : "");
        }
    }

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

    public bool HideChatInShots { get => Settings.HideChatInShots; set { Settings.HideChatInShots = value; Changed(); } }

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
            var (name, problem) = await Discord.CheckAsync(w.Url);
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
            _ = r.LoadPictureAsync();
            _ = r.LoadIconsAsync(Flavor);
        }
        SelectedCharacter = Characters.FirstOrDefault(c => c.Guid == keep);
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

    // A sample of any kind of post, using the selected (or most recently played) character so it
    // looks familiar. Item and boss details are made up.
    public Entry SampleEntry(string kind = "level")
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var c = SelectedCharacter ?? Characters.FirstOrDefault();
        var e = c is null
            ? new Entry
            {
                Id = "test", Char = "Testy", Level = 12, At = now, Zone = "Elwynn Forest",
                Professions = "Mining 75, Herbalism 60", Talents = "Arms 5 / Fury 2 / Protection 0", Class = "Warrior",
                ClassFile = "WARRIOR", Race = "Human", RaceFile = "Human", Played = 4 * 3600 + 12 * 60,
            }
            : new Entry
            {
                Id = "test", Char = c.Name, Realm = c.Realm, Level = Math.Max(c.Level, 2), At = now,
                Zone = c.Zone, Professions = c.Professions, Talents = c.Talents, Spec = c.Spec, Class = c.ClassName,
                ClassFile = c.ClassFile, Race = c.Race, RaceFile = c.RaceFile, Sex = c.Sex, Played = c.Played,
                Hardcore = c.Hardcore, Ruleset = c.Ruleset,
            };

        // Other characters in the list stand in as the group for a boss kill.
        var group = string.Join(",", Characters.Where(o => o != c).Take(4)
            .Select(o => $"{o.Name}~{o.Level}~{o.ClassFile}~{o.ClassName}~{o.Race}~"));

        return kind switch
        {
            "rare" => e with { Kind = "loot", Item = "Cruel Barb", ItemId = 5191, Quality = 3, Zone = "The Deadmines" },
            "epic" => e with { Kind = "loot", Item = "Arcanite Reaper", ItemId = 12784, Quality = 4 },
            "legendary" => e with { Kind = "loot", Item = "Thunderfury, Blessed Blade of the Windseeker", ItemId = 19019, Quality = 5 },
            "max" => e with { Kind = "level", Level = Flavor == Flavors.Forever ? 60 : 90, Seconds = 2 * 3600 + 14 * 60, MaxLevel = true, Group = group, GroupSize = 5 },
            "mount" => e with { Kind = "mount", Mount = "Pinto Bridle", ItemId = 2414, Bought = true },
            "boss" => e with { Kind = "boss", Boss = "Edwin VanCleef", Instance = "The Deadmines", Group = group, GroupSize = 5 },
            "death" => e with { Kind = "death", Killer = "Defias Pillager", Gold = 123456, Hardcore = true },
            _ => e with { Kind = "level", Seconds = 18 * 60 + 4 },
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
        PostCharacterCommand = new RelayCommand(p => PostCharacterAsync((CharacterRow)p!));
        AddTagCommand = new RelayCommand(p => AddTag((CharacterRow)p!));
        TestKindCommand = new RelayCommand(p => SendKindTestAsync((string)p!));
        CheckUpdatesCommand = new RelayCommand(_ => CheckForUpdatesAsync(manual: true));
        InstallUpdateCommand = new RelayCommand(_ => InstallUpdateAsync(), _ => update is not null);
        ToggleNotesCommand = new RelayCommand(_ => ShowNotes = !ShowNotes);
        CloseBannerCommand = new RelayCommand(_ => ShowBanner = false);
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

    // --- updates ---

    public bool JustUpdated { get; init; }
    public event Action? UpdateStarted;
    public string VersionText => $"v{Updater.Current}";

    Updater.Release? update;
    string bannerTitle = "";
    public string BannerTitle { get => bannerTitle; set => Set(ref bannerTitle, value); }
    string bannerNotes = "";
    public string BannerNotes { get => bannerNotes; set => Set(ref bannerNotes, value); }
    bool showBanner;
    public bool ShowBanner { get => showBanner; set => Set(ref showBanner, value); }
    bool showNotes;
    public bool ShowNotes { get => showNotes; set => Set(ref showNotes, value); }
    public bool HasUpdate => update is not null;

    // Quietly on start; the button says "up to date" too.
    async Task CheckForUpdatesAsync(bool manual)
    {
        var latest = await Updater.LatestAsync();
        if (latest is null)
        {
            if (manual) Log.Warn("Couldn't reach GitHub to check for updates.");
            return;
        }
        if (latest.Version <= Updater.Current)
        {
            if (manual) Log.Info($"You have the latest version ({VersionText}).");
            return;
        }
        update = latest;
        BannerTitle = $"Version {latest.Version} is available (you have {Updater.Current}).";
        BannerNotes = latest.Notes.Trim();
        ShowNotes = false;
        ShowBanner = true;
        Raise(nameof(HasUpdate));
        Log.Info($"Update available: {latest.Version}.");
    }

    async Task InstallUpdateAsync()
    {
        if (update is null) return;
        Log.Info($"Downloading version {update.Version}...");
        var problem = await Updater.InstallAsync(update);
        if (problem is null)
        {
            UpdateStarted?.Invoke(); // the new version has started and waits for this one to exit
            return;
        }
        Log.Error(problem);
        if (update.PageUrl.Length > 0)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(update.PageUrl) { UseShellExecute = true });
    }

    // After an update: what's new in this version.
    async Task ShowWhatsNewAsync()
    {
        Log.Success($"Updated to {VersionText}.");
        if (await Updater.ForVersionAsync(Updater.Current) is not { } release) return;
        BannerTitle = $"Updated to {VersionText}. Here's what's new:";
        BannerNotes = release.Notes.Trim();
        ShowNotes = true;
        ShowBanner = true;
    }

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
    public ICommand RemoveCharacterCommand { get; }
    public ICommand PostCharacterCommand { get; }

    // What the character card includes (the gear next to Post to Discord). Same for every character.
    public static readonly (string Key, string Label)[] CardParts =
    [
        ("ilvl", "Item level"), ("played", "Total played"), ("gold", "Gold"), ("crests", "Crests"),
        ("professions", "Professions"), ("talents", "Talents"), ("guild", "Guild"), ("zone", "Last seen"),
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
    public ICommand CheckUpdatesCommand { get; }
    public ICommand InstallUpdateCommand { get; }
    public ICommand ToggleNotesCommand { get; }
    public ICommand CloseBannerCommand { get; }
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
        if (JustUpdated) await ShowWhatsNewAsync();
        else await CheckForUpdatesAsync(manual: false);
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
        var asEntry = new Entry { Ruleset = c.Ruleset, Hardcore = c.Hardcore };
        var hooks = Config.ActiveWebhooks(tab.Flavor)
            .Where(w => tab.Flavor != Flavors.Forever || w.AcceptsRuleset(asEntry))
            .Where(w => w.AcceptsTags(Config.TagsOf(c.Guid)))
            .ToList();
        if (hooks.Count == 0)
        {
            Log.Warn($"[{tab.Name}] No webhook to post {c.Name} to. Turn one on in the Discord panel" +
                     (tab.Flavor == Flavors.Forever ? $" (and check it takes {c.Ruleset} characters)." : "."));
            return;
        }
        var hidden = Config.CardHidden.ToList();
        var card = await CardImage.RenderAsync(c, tab.Flavor, hidden);
        foreach (var w in hooks)
            if (await new Discord(w.Url, tab.Flavor).PostCharacterAsync(c, hidden, card))
                Log.Success($"[{tab.Name}] Posted {c.Name}'s character card to \"{w.Name}\".");
    }

    async Task SendTestAsync()
    {
        if (SelectedFlavor is not { } tab) return;
        foreach (var w in Config.ActiveWebhooks(tab.Flavor))
            if (await new Discord(w.Url, tab.Flavor).PostAsync(tab.SampleEntry(), isTest: true))
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
            if (await new Discord(w.Url, tab.Flavor).PostAsync(sample, isTest: true))
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
