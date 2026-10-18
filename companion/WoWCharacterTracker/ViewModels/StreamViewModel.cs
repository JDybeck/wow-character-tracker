using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

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
    public bool ShowStats { get => Options.ShowStats; set { Options.ShowStats = value; OptionsChanged(); } }
    public bool ShowLevels { get => Options.ShowLevels; set { Options.ShowLevels = value; OptionsChanged(); } }
    public bool ShowToday { get => Options.ShowToday; set { Options.ShowToday = value; OptionsChanged(); } }
    public bool ShowDuels { get => Options.ShowDuels; set { Options.ShowDuels = value; OptionsChanged(); } }
    public bool ShowMakgora { get => Options.ShowMakgora; set { Options.ShowMakgora = value; OptionsChanged(); } }
    public bool ShowBattlegrounds { get => Options.ShowBattlegrounds; set { Options.ShowBattlegrounds = value; OptionsChanged(); } }
    public bool ShowHighlights { get => Options.ShowHighlights; set { Options.ShowHighlights = value; OptionsChanged(); } }

    // Which highlights count, as on/off chips. Mythic+ and PvP rating are Retail only, so they're
    // only offered while the overlay shows a Retail character.
    static readonly (string Kind, string Name, string Tip)[] KindNames =
    [
        ("loot", "Loot", "Rare, epic and legendary items looted"), ("boss", "Boss kills", "Dungeon and raid end bosses killed"),
        ("mount", "Mounts", "New mounts, bought or looted"), ("duel", "Duels", "Duels won and lost"),
        ("makgora", "Mak'gora", "Mak'goras won and lost"), ("bg", "Battlegrounds", "Battlegrounds won and lost"), ("death", "Deaths", "Hardcore deaths"),
        ("mplus", "Mythic+", "Mythic+ rating milestones (Retail)"), ("pvp", "PvP rating", "PvP rating milestones (Retail)"),
    ];

    bool HasRatings => Flavors.Named(snapshot.Game)?.HasRatings == true;

    ObservableCollection<ToggleOption>? kinds;
    public ObservableCollection<ToggleOption> HighlightKinds => kinds ??= Chips(KindNames, Options.HighlightKinds);

    // Which moments pop up as the big alert, as on/off chips.
    static readonly (string Kind, string Name, string Tip)[] AlertNames =
    [
        ("level", "Level-ups", "DING! with the level and how long it took"), ("maxlevel", "Max level", "MAX LEVEL! with light rays, when you reach the cap"),
        ("boss", "Boss kills", "VICTORY! when an end boss dies"), ("legendary", "Legendary loot", "LEGENDARY! in orange"),
        ("epic", "Epic loot", "EPIC LOOT! in purple"), ("rare", "Rare loot", "RARE LOOT! in blue (off by default, it happens a lot)"),
        ("mount", "Mounts", "NEW MOUNT!"), ("duel", "Duel wins", "VICTORY! with who you beat"), ("makgora", "Mak'gora wins", "MAK'GORA! in blood red"),
        ("bg", "Battleground wins", "VICTORY! when your team wins a battleground"),
        ("death", "Deaths", "R.I.P. when a hardcore character dies"),
        ("mplus", "Mythic+", "MILESTONE! for a Mythic+ rating step (Retail)"), ("pvp", "PvP rating", "MILESTONE! for a PvP rating step (Retail)"),
    ];
    ObservableCollection<ToggleOption>? alertKinds;
    public ObservableCollection<ToggleOption> AlertKinds => alertKinds ??= Chips(AlertNames, Options.AlertKinds);

    // One on/off chip per kind this game offers.
    ObservableCollection<ToggleOption> Chips((string Kind, string Name, string Tip)[] names, List<string> picked) =>
        ToggleOption.Kinds(EventKinds.Offered(names, HasRatings), picked, OptionsChanged);

    // What the overlay counts: three choices, the one in use highlighted.
    public bool RangeAll => Options.RangeMode == "all";
    public bool RangeToday => Options.RangeMode == "today";
    public bool RangeNow => Options.RangeMode == "now";
    public string NowText => RangeNow
        ? "Counting since " + DateTimeOffset.FromUnixTimeSeconds(Options.Since).LocalDateTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)
        : "Start counting now";

    void OptionsChanged()
    {
        main.Config.Save();
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
        Raise(string.Empty);
        if (Enabled && server is null)
        {
            server = new OverlayServer(main.Config.StreamPort) { Snapshot = snapshot };
            if (server.Start())
            {
                Log.Success($"Stream overlay on: add {Url} as a Browser source in OBS. Text files: {StreamFiles.Dir}");
                StreamFiles.Write(snapshot);
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
        var game = snapshot.Game;
        snapshot = StreamSnapshot.Latest(latest.Values, DateTimeOffset.Now, Options);
        if (snapshot.Game != game)
        {
            // Another game's character: the Retail-only chips come or go.
            kinds = null;
            alertKinds = null;
            Raise(nameof(HighlightKinds));
            Raise(nameof(AlertKinds));
        }
        if (server is null) return;
        server.Snapshot = snapshot;
        StreamFiles.Write(snapshot);
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
        string[] kinds = ["level", "max", "boss", "legendary", "epic", "duel", "makgora", "mount", "death"];
        var e = SampleEvents.For(kinds[testIndex++ % kinds.Length], Flavors.Forever, null, []);
        e = e with { Id = $"test-{now}-{testIndex}", Char = name, At = now, Level = e.MaxLevel ? e.Level : level };
        if (StreamSnapshot.AlertFor(e) is not { } sample) return;
        var highlight = HighlightText.Text(e);
        var test = snapshot.HasCharacter ? snapshot : snapshot with { Character = name, Level = level - 1, ClassName = "Warrior", ClassColor = "#C69B6D", Zone = "Scarlet Monastery" };
        var highlights = highlight.Length > 0 ? [highlight, .. test.Highlights.Take(2)] : test.Highlights;
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
        CharacterArt.ClassColor(r.ClassFile) is int color ? $"#{color:X6}" : "#FFFFFF",
        r.Race, r.RealmText, r.Zone, r.Played > 0 ? Format.Played(r.Played) : "", r.ItemLevel > 0 ? r.ItemLevel.ToString("0") : "",
        r.LastSeen, r.Spec, r.Money >= 10000 ? $"{r.Money / 10000:N0}g" : "", r.KeystoneText,
        Format.Ratings(r.MplusScore, r.PvpRatings).Select(x => new StreamRating(x.Name, x.Rating)).ToList());
}
