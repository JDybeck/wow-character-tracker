using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace WoWCharacterTracker.ViewModels;

// The Stream overlay panel (between Discord and the console): on or off, the overlay's addresses
// (everything, or the card, highlights and ding alert as separate sources), how it looks and what it
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
        TestDingCommand = new RelayCommand(_ => TestDing(), _ => server is not null);
        TogglePanelCommand = new RelayCommand(_ => PanelOpen = !PanelOpen);
        StartFromNowCommand = new RelayCommand(_ => { Options.Since = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); OptionsChanged(); Log.Info("Stream overlay: counting from now. Everything before is still tracked in the app."); });
        AllTimeCommand = new RelayCommand(_ => { Options.Since = 0; OptionsChanged(); });
    }

    StreamOptions Options => main.Config.StreamOptions;

    public ICommand CopyCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand OpenOverlayCommand { get; }
    public ICommand TestDingCommand { get; }
    public ICommand TogglePanelCommand { get; }
    public ICommand StartFromNowCommand { get; }
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
    public bool ShowLevels { get => Options.ShowLevels; set { Options.ShowLevels = value; OptionsChanged(); } }
    public bool ShowToday { get => Options.ShowToday; set { Options.ShowToday = value; OptionsChanged(); } }
    public bool ShowDuels { get => Options.ShowDuels; set { Options.ShowDuels = value; OptionsChanged(); } }
    public bool ShowMakgora { get => Options.ShowMakgora; set { Options.ShowMakgora = value; OptionsChanged(); } }
    public bool ShowHighlights { get => Options.ShowHighlights; set { Options.ShowHighlights = value; OptionsChanged(); } }

    // Which highlights count, as on/off chips.
    static readonly (string Kind, string Name)[] KindNames =
        [("loot", "Loot"), ("boss", "Boss kills"), ("mount", "Mounts"), ("duel", "Duels"), ("makgora", "Mak'gora"), ("death", "Deaths"), ("mplus", "Mythic+"), ("pvp", "PvP rating")];
    ObservableCollection<ToggleOption>? kinds;
    public ObservableCollection<ToggleOption> HighlightKinds => kinds ??= new(KindNames.Select(k => new ToggleOption(k.Name, Options.HighlightKinds.Contains(k.Kind), on =>
    {
        Options.HighlightKinds.Remove(k.Kind);
        if (on) Options.HighlightKinds.Add(k.Kind);
        OptionsChanged();
    })));

    public bool CountingFromNow => Options.Since > 0;
    public string SinceText => Options.Since > 0
        ? "Counting since " + DateTimeOffset.FromUnixTimeSeconds(Options.Since).LocalDateTime.ToString("MMM d, HH:mm", System.Globalization.CultureInfo.InvariantCulture)
        : "Counting everything (all time)";

    void OptionsChanged()
    {
        main.Config.Save();
        Raise(nameof(CountingFromNow));
        Raise(nameof(SinceText));
        Refresh();
    }

    // --- the overlay ---

    // Starts or stops the overlay to match the setting (at startup and when it's switched). The panel
    // is built before the config is loaded, so everything it shows is refreshed here too.
    public void Apply()
    {
        kinds = null;
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
        snapshot = StreamSnapshot.Latest(latest.Values, DateTimeOffset.Now, Options);
        if (server is null) return;
        server.Snapshot = snapshot;
        StreamFiles.Write(snapshot);
    }

    // Shows the ding alert on the overlay right now (OBS included), for setting it up without
    // leveling. Only the overlay sees it: nothing is posted or saved, the text files stay as they are.
    void TestDing()
    {
        if (server is null) return;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var level = snapshot.HasCharacter ? snapshot.Level + 1 : 40;
        var test = snapshot.HasCharacter ? snapshot : snapshot with { Character = "Zeb TESTCHAR", Level = 39, ClassName = "Warrior", ClassColor = "#C69B6D", Zone = "Scarlet Monastery" };
        server.Snapshot = test with { Ding = new StreamDing($"test-{now}", level, "23m 11s", now) };
        Log.Info("Test ding sent to the stream overlay (only the overlay shows it).");
        // Back to the real state a bit later, so the next real ding still counts as new.
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
        CharacterArt.ClassColors.TryGetValue(r.ClassFile.ToUpperInvariant(), out var color) ? $"#{color:X6}" : "#FFFFFF",
        r.Race, r.RealmText, r.Zone, r.Played > 0 ? Format.Played(r.Played) : "", r.ItemLevel > 0 ? r.ItemLevel.ToString("0") : "",
        r.LastSeen);
}
