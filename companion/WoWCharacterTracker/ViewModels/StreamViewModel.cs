using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WoWCharacterTracker.ViewModels;

// The Stream overlay panel (between Discord and the console): on or off, the overlay's addresses
// (everything, or the card, highlights and alert as separate sources), how it looks and what it
// shows (Config.StreamOptions), and keeping the overlay and the text files up to date with the
// character played last, across the game tabs.
sealed class StreamViewModel : Observable
{
    readonly MainViewModel main;
    readonly Dictionary<Flavor, (StreamCharacter Character, IReadOnlyList<Entry> Events)> latest = [];
    OverlayServer? server;
    StreamSnapshot snapshot = StreamSnapshot.Empty;

    public StreamViewModel(MainViewModel main)
    {
        this.main = main;
        CopyCommand = new RelayCommand(p => Copy(Url + (p as string ?? "")));
        OpenFolderCommand = new RelayCommand(_ => { Directory.CreateDirectory(StreamFiles.Dir); Browser.Open(StreamFiles.Dir); });
        OpenOverlayCommand = new RelayCommand(_ => Browser.Open(Url + "?preview=1")); // a dark backdrop behind it
        TestAlertCommand = new RelayCommand(_ => TestAlert(), _ => server is not null);
        TogglePanelCommand = new RelayCommand(_ => PanelOpen = !PanelOpen);
        StartFromNowCommand = new RelayCommand(_ =>
        {
            Options.Range = "now";
            Options.Since = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            OptionsChanged();
            Log.Info("Stream overlay: counting from now. Everything before is still tracked in the app.");
        });
        TodayCommand = new RelayCommand(_ => { Options.Range = "today"; OptionsChanged(); });
        AllTimeCommand = new RelayCommand(_ => { Options.Range = "all"; OptionsChanged(); });
        PresetCommand = new RelayCommand(p =>
        {
            if (p is not ColorPreset preset) return;
            Options.Accent = preset.Accent;
            Options.Background = preset.Background;
            ColorsChanged();
        });
    }

    StreamOptions Options => main.Config.StreamOptions;

    public ICommand CopyCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand OpenOverlayCommand { get; }
    public ICommand TestAlertCommand { get; }
    public ICommand TogglePanelCommand { get; }
    public ICommand StartFromNowCommand { get; }
    public ICommand TodayCommand { get; }
    public ICommand AllTimeCommand { get; }

    public string Url => $"http://localhost:{main.Config.StreamPort}/";

    public bool Enabled
    {
        get => main.Config.StreamEnabled;
        set
        {
            if (main.Config.StreamEnabled == value) return;
            main.Config.StreamEnabled = value;
            main.Config.Save();
            Apply();
            Raise(nameof(Enabled));
        }
    }

    public bool PanelOpen
    {
        get => Options.PanelOpen;
        set { Options.PanelOpen = value; main.Config.Save(); Raise(nameof(PanelOpen)); }
    }

    // --- how it looks and what it shows ---

    public int CardOpacity { get => Options.CardOpacity; set { Options.CardOpacity = value; Raise(nameof(CardOpacity)); OptionsChanged(); } }
    public int HighlightsOpacity { get => Options.HighlightsOpacity; set { Options.HighlightsOpacity = value; Raise(nameof(HighlightsOpacity)); OptionsChanged(); } }
    public bool ShowStats { get => Options.ShowStats; set { Options.ShowStats = value; OptionsChanged(); } }
    public bool ShowLevels { get => Options.ShowLevels; set { Options.ShowLevels = value; OptionsChanged(); } }
    public bool ShowToday { get => Options.ShowToday; set { Options.ShowToday = value; OptionsChanged(); } }
    public bool ShowDuels { get => Options.ShowDuels; set { Options.ShowDuels = value; OptionsChanged(); } }
    public bool ShowBattlegrounds { get => Options.ShowBattlegrounds; set { Options.ShowBattlegrounds = value; OptionsChanged(); } }
    public bool ShowHighlights { get => Options.ShowHighlights; set { Options.ShowHighlights = value; OptionsChanged(); } }
    public bool ShowBorder { get => Options.ShowBorder; set { Options.ShowBorder = value; OptionsChanged(); } }

    // Size: the whole overlay's scale, and the card's width and smallest height (0 = fits its text).
    public int Scale { get => Options.Scale; set { Options.Scale = value; Raise(nameof(Scale)); OptionsChanged(); } }
    public int CardWidth { get => Options.CardWidth; set { Options.CardWidth = value; Raise(nameof(CardWidth)); Raise(nameof(CardWidthText)); OptionsChanged(); } }
    public int CardHeight { get => Options.CardHeight; set { Options.CardHeight = value; Raise(nameof(CardHeight)); Raise(nameof(CardHeightText)); OptionsChanged(); } }
    public string CardWidthText => CardWidth > 0 ? $"{Math.Max(CardWidth, 260)}" : "Auto"; // narrower than its text it can't go (StreamLook)
    public string CardHeightText => CardHeight > 0 ? $"{CardHeight}" : "Auto";
    public int HighlightsWidth { get => Options.HighlightsWidth; set { Options.HighlightsWidth = value; Raise(nameof(HighlightsWidth)); Raise(nameof(HighlightsWidthText)); OptionsChanged(); } }
    public int HighlightsHeight { get => Options.HighlightsHeight; set { Options.HighlightsHeight = value; Raise(nameof(HighlightsHeight)); Raise(nameof(HighlightsHeightText)); OptionsChanged(); } }
    public string HighlightsWidthText => HighlightsWidth > 0 ? $"{Math.Max(HighlightsWidth, 200)}" : "Auto"; // narrowest it goes (StreamLook)
    public string HighlightsHeightText => HighlightsHeight > 0 ? $"{HighlightsHeight}" : "Auto";

    // Colors: a few presets, or any "#rrggbb" typed in. Accent empty = the class color.
    public sealed record ColorPreset(string Name, string Accent, string Background)
    {
        public Brush Dot => Swatch(Accent, "#d97706");
    }
    public ColorPreset[] Presets { get; } =
    [
        new("Class", "", ""), new("Gold", "#ffd100", "#14100a"), new("Alliance", "#3c8cff", "#070d1c"), new("Horde", "#e03a2f", "#1a0606"),
        new("Arcane", "#b866ff", "#100820"), new("Fel", "#5fd13a", "#071207"), new("Frost", "#69ccf0", "#06121a"), new("Mono", "#d0d0d0", "#000000"),
    ];
    public ICommand PresetCommand { get; }

    public string Accent { get => Options.Accent; set { Options.Accent = value.Trim(); ColorsChanged(); } }
    public string Background { get => Options.Background; set { Options.Background = value.Trim(); ColorsChanged(); } }
    public Brush AccentBrush => Swatch(Options.Accent, "#d97706");
    public Brush BackgroundBrush => Swatch(Options.Background, "#0a0806");

    static Brush Swatch(string hex, string fallback) =>
        new SolidColorBrush((Color)ColorConverter.ConvertFromString(StreamLook.Hex(hex) is { Length: > 0 } h ? h : fallback));

    void ColorsChanged()
    {
        Raise(nameof(Accent)); Raise(nameof(Background)); Raise(nameof(AccentBrush)); Raise(nameof(BackgroundBrush));
        OptionsChanged();
    }

    // The on/off chips, each table in three parts: both games (the panel's Card, Highlights and Alerts
    // parts), Forever only and Retail only (the panel's game sections, GameSections). A new option goes
    // in the table for the games that can have it. The config keeps one list per chip type, so moving
    // a chip between tables never touches what's saved.

    // Which lines the stats column shows.
    internal static readonly (string Kind, string Name, string Tip)[] FieldNames =
    [
        ("ilvl", "Item level", "Average item level"), ("played", "Played", "Total time played"),
        ("levelPlayed", "This level", "Time played since the last ding"), ("gold", "Gold", "Gold on the character"),
        ("talents", "Talents", "Talent points per tree, numbers only (31/20/0)"), ("zone", "Zone", "Where the character is, under the name"),
    ];
    internal static readonly (string Kind, string Name, string Tip)[] RetailFieldNames =
    [
        ("keystone", "Keystone", "Your Mythic+ keystone"), ("ratings", "Ratings", "Mythic+ score and PvP ratings"),
    ];

    // Which highlights count.
    internal static readonly (string Kind, string Name, string Tip)[] KindNames =
    [
        ("loot", "Loot", "Rare, epic and legendary items looted"),
        ("mount", "Mounts", "New mounts, bought or looted"), ("duel", "Duels", "Duels won and lost"),
        ("bg", "Battlegrounds", "Battlegrounds won and lost"),
        ("profession", "Professions", "Profession milestones (the steps set for Discord)"),
    ];
    internal static readonly (string Kind, string Name, string Tip)[] ForeverKindNames =
    [
        ("boss", "Boss kills", "Dungeon and raid end bosses killed"), ("makgora", "Mak'gora", "Mak'goras won and lost"), ("death", "Deaths", "Hardcore deaths"),
    ];
    internal static readonly (string Kind, string Name, string Tip)[] RetailKindNames =
    [
        ("keyrun", "Mythic+ runs", "Keys finished, timed or over time"),
        ("mplus", "Mythic+", "Mythic+ rating milestones"), ("pvp", "PvP rating", "PvP rating milestones"),
    ];

    // Which moments pop up as the big alert.
    internal static readonly (string Kind, string Name, string Tip)[] AlertNames =
    [
        ("level", "Level-ups", "DING! with the level and how long it took"), ("maxlevel", "Max level", "MAX LEVEL! with light rays, when you reach the cap"),
        ("legendary", "Legendary loot", "LEGENDARY! in orange"),
        ("epic", "Epic loot", "EPIC LOOT! in purple"), ("rare", "Rare loot", "RARE LOOT! in blue (off by default, it happens a lot)"),
        ("mount", "Mounts", "NEW MOUNT!"), ("duel", "Duel wins", "VICTORY! with who you beat"),
        ("bg", "Battleground wins", "VICTORY! when your team wins a battleground"),
        ("profession", "Professions", "MILESTONE! when a profession passes a step"),
    ];
    internal static readonly (string Kind, string Name, string Tip)[] ForeverAlertNames =
    [
        ("dungeon", "Dungeon complete", "DUNGEON COMPLETE! when a dungeon's end boss dies"),
        ("boss", "Boss kills", "VICTORY! when an end boss dies. With Dungeon complete on too, dungeons get that alert and this one is for raids."),
        ("makgora", "Mak'gora wins", "MAK'GORA! in blood red"), ("death", "Deaths", "R.I.P. when a hardcore character dies"),
    ];
    internal static readonly (string Kind, string Name, string Tip)[] RetailAlertNames =
    [
        ("keyrun", "Mythic+ runs", "TIMED! when a key is beaten in time"),
        ("mplus", "Mythic+", "MILESTONE! for a Mythic+ rating step"), ("pvp", "PvP rating", "MILESTONE! for a PvP rating step"),
    ];

    bool HasRatings => Flavors.Named(snapshot.Game)?.HasRatings == true;

    ObservableCollection<ToggleOption>? fields, kinds, alertKinds;
    public ObservableCollection<ToggleOption> CardFields => fields ??= Chips(FieldNames, Options.CardFields);
    public ObservableCollection<ToggleOption> HighlightKinds => kinds ??= Chips(KindNames, Options.HighlightKinds);
    public ObservableCollection<ToggleOption> AlertKinds => alertKinds ??= Chips(AlertNames, Options.AlertKinds);

    ObservableCollection<ToggleOption> Chips((string Kind, string Name, string Tip)[] names, List<string> picked) =>
        ToggleOption.Kinds(names, picked, OptionsChanged);

    // A labelled box of the options only one game has, a chip group per part of the overlay. Both
    // boxes always show: the options are shared across games, so they can be set from either, whichever
    // character the overlay is following right now.
    public sealed record ChipGroup(string Title, IReadOnlyList<ToggleOption> Chips);
    public sealed record GameSection(string Title, string Tip, IReadOnlyList<ChipGroup> Groups);

    IReadOnlyList<GameSection>? gameSections;
    public IReadOnlyList<GameSection> GameSections => gameSections ??=
    [
        new("FOREVER ONLY", "Moments only WoW Forever has: end boss kills and hardcore. Card and Highlights chips need those parts on.",
        [
            new("Card", [new ToggleOption("Mak'gora record", Options.ShowMakgora, on => { Options.ShowMakgora = on; OptionsChanged(); }, "Mak'goras won and lost (shown once there is one)")]),
            new("Highlights", Chips(ForeverKindNames, Options.HighlightKinds)),
            new("Alerts", Chips(ForeverAlertNames, Options.AlertKinds)),
        ]),
        new("RETAIL ONLY", "What only Retail has: keystones, Mythic+ and PvP ratings. Card and Highlights chips need those parts on.",
        [
            new("Card", Chips(RetailFieldNames, Options.CardFields)),
            new("Highlights", Chips(RetailKindNames, Options.HighlightKinds)),
            new("Alerts", Chips(RetailAlertNames, Options.AlertKinds)),
        ]),
    ];

    // What the overlay counts: three choices, the one in use highlighted.
    public bool RangeAll => Options.RangeMode == "all";
    public bool RangeToday => Options.RangeMode == "today";
    public bool RangeNow => Options.RangeMode == "now";
    public string NowText => RangeNow
        ? "Counting since " + DateTimeOffset.FromUnixTimeSeconds(Options.Since).LocalDateTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)
        : "Start counting now";

    void OptionsChanged()
    {
        main.SaveSoon(); // sliders change many times a second
        Raise(nameof(RangeAll));
        Raise(nameof(RangeToday));
        Raise(nameof(RangeNow));
        Raise(nameof(NowText));
        Refresh();
    }

    // --- the overlay ---

    // Starts or stops the overlay to match the setting (at startup and when it's switched). The panel
    // is built before the config is loaded, so everything it shows is refreshed here too.
    public void Apply()
    {
        kinds = null;
        alertKinds = null;
        fields = null;
        gameSections = null;
        Raise(string.Empty);
        if (Enabled && server is null)
        {
            server = new OverlayServer(main.Config.StreamPort);
            if (server.Start())
            {
                Log.Success($"Stream overlay on: add {Url} as a Browser source in OBS. Text files: {StreamFiles.Dir}");
                Refresh(); // built now: while off, only the game was kept
            }
            else { server.Dispose(); server = null; }
        }
        else if (!Enabled && server is not null)
        {
            server.Dispose();
            server = null;
            Log.Info("Stream overlay off.");
        }
        CommandManager.InvalidateRequerySuggested();
    }

    // A game tab's characters changed: the overlay follows the character played last (test
    // characters don't count, they'd always look like they were just played).
    public void Update(Flavor flavor, IEnumerable<CharacterRow> rows, IReadOnlyList<Entry> events)
    {
        if (rows.Where(r => !r.IsTest).MaxBy(r => r.LastSeen) is { } row)
            latest[flavor] = (Character(row, flavor), events);
        else
            latest.Remove(flavor);
        Refresh();
    }

    void Refresh()
    {
        if (server is null) return; // off: no building, files or Wowhead lookups
        snapshot = StreamSnapshot.Latest(latest.Values, DateTimeOffset.Now, Options, Item);
        LookUpItems();
        server.Snapshot = snapshot;
        StreamFiles.Write(snapshot);
    }

    // The highlights' items (loot, mounts) as Wowhead knows them, for their icons and quality: each
    // looked up once (Wowhead caches it on disk too), the overlay refreshed when new ones come in. A
    // lookup that fails is forgotten, so a later refresh tries again; until then the kind's icon shows.
    readonly Dictionary<int, Wowhead.Info?> items = [];
    Wowhead.Info? Item(int id) => items.GetValueOrDefault(id);

    async void LookUpItems()
    {
        if (Flavors.Named(snapshot.Game) is not { } flavor) return;
        var missing = snapshot.Highlights.Select(h => h.ItemId).Where(id => id > 0 && !items.ContainsKey(id)).Distinct().ToList();
        if (missing.Count == 0) return;
        foreach (var id in missing) items[id] = null; // asked, so the next refresh doesn't ask again
        var found = false;
        foreach (var id in missing)
        {
            if (await Wowhead.GetAsync("item", id, flavor) is { } info) { items[id] = info; found = true; }
            else items.Remove(id);
        }
        if (found) Refresh();
    }

    // Shows an alert on the overlay right now (OBS included), for setting it up without playing. Each
    // click shows the next kind (DING!, MAX LEVEL!, VICTORY!, EPIC LOOT!...). Only the overlay sees it:
    // nothing is posted or saved, the text files stay as they are.
    int testIndex;
    void TestAlert()
    {
        if (server is null) return;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var level = snapshot.HasCharacter ? snapshot.Level + 1 : 40;
        var name = snapshot.HasCharacter ? snapshot.Character : "Zeb TESTCHAR";
        // The same sample events as the Test post buttons (SampleEvents), shown the way a real one
        // would be: its alert and the highlight line it adds (level-ups aren't highlights), so the
        // highlights source can be set up too.
        string[] kinds = HasRatings
            ? ["level", "max", "keyrun", "legendary", "epic", "duel", "mount"]
            : ["level", "max", "boss", "dungeon", "legendary", "epic", "duel", "makgora", "mount", "death"];
        var e = SampleEvents.For(kinds[testIndex++ % kinds.Length], Flavors.Forever, null, []);
        e = e with { Id = $"test-{now}-{testIndex}", Char = name, At = now, Level = e.MaxLevel ? e.Level : level };
        if (StreamSnapshot.AlertFor(e) is not { } sample) return;
        var highlight = StreamSnapshot.Highlight(e, Item);
        var test = snapshot.HasCharacter ? snapshot : snapshot with { Character = name, Level = level - 1, ClassName = "Warrior", ClassColor = "#C69B6D", Zone = "Scarlet Monastery" };
        var highlights = highlight.Text.Length > 0 ? [highlight, .. test.Highlights.Take(2)] : test.Highlights;
        server.Snapshot = test with { Alert = sample, Highlights = highlights };
        Log.Info($"Test alert sent to the stream overlay: {sample.Title} (click again for the next kind; only the overlay shows it).");
        // Back to the real state a bit later, so the next real alert still counts as new.
        _ = Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(_ => { if (server is not null) server.Snapshot = snapshot; });
    }

    static void Copy(string url)
    {
        try { Clipboard.SetText(url); Log.Info("Copied the stream overlay address: " + url); }
        catch (System.Runtime.InteropServices.ExternalException) { } // the clipboard was busy
    }

    public void Shutdown() => server?.Dispose();

    static StreamCharacter Character(CharacterRow r, Flavor flavor) => new(
        r.Guid, r.FullName, flavor.Name, r.Level, r.ClassName,
        GameColors.Class(r.ClassFile) is int color ? GameColors.Hex(color) : "#FFFFFF",
        r.Race, r.RealmText, r.Zone, r.Played > 0 ? Format.Played(r.Played) : "", r.ItemLevel > 0 ? r.ItemLevel.ToString("0") : "",
        r.LastSeen, r.Spec, r.Money >= 10000 ? $"{r.Money / 10000:N0}g" : "", r.KeystoneText,
        Format.Ratings(r.MplusScore, r.PvpRatings).Select(x => new StreamRating(x.Name, x.Rating)).ToList(), r.Talents, r.Played);
}
