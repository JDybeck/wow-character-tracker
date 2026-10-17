using System.Windows;
using System.Windows.Input;

namespace WoWCharacterTracker.ViewModels;

// Stream overlay and text files (settings menu): on or off, the overlay's address, and keeping both
// up to date with the character played last, across the game tabs.
sealed class StreamViewModel : Observable
{
    readonly MainViewModel main;
    readonly Dictionary<Flavor, (StreamCharacter Character, IReadOnlyList<Entry> Events)> latest = [];
    OverlayServer? server;
    StreamSnapshot snapshot = StreamSnapshot.Empty;

    public StreamViewModel(MainViewModel main)
    {
        this.main = main;
        CopyUrlCommand = new RelayCommand(_ => { try { Clipboard.SetText(Url); Log.Info("Copied the stream overlay address: " + Url); } catch { } });
        OpenFolderCommand = new RelayCommand(_ => { Directory.CreateDirectory(StreamFiles.Dir); Browser.Open(StreamFiles.Dir); });
        OpenOverlayCommand = new RelayCommand(_ => Browser.Open(Url + "?preview=1")); // a dark backdrop behind it
        TestDingCommand = new RelayCommand(_ => TestDing(), _ => server is not null);
    }

    public ICommand CopyUrlCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand OpenOverlayCommand { get; }
    public ICommand TestDingCommand { get; }

    // Shows the ding alert on the overlay right now (OBS included), for setting it up without
    // leveling. Only the overlay sees it: nothing is posted or saved, the text files stay as they are.
    void TestDing()
    {
        if (server is null) return;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var level = snapshot.HasCharacter ? snapshot.Level + 1 : 40;
        var test = snapshot.HasCharacter ? snapshot : StreamSnapshot.Empty with { Character = "Zeb TESTCHAR", Level = 39, ClassName = "Warrior", ClassColor = "#C69B6D", Zone = "Scarlet Monastery" };
        server.Snapshot = test with { Ding = new StreamDing($"test-{now}", level, "23m 11s", now) };
        Log.Info("Test ding sent to the stream overlay (only the overlay shows it).");
        // Back to the real state a bit later, so the next real ding still counts as new.
        _ = Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(_ => { if (server is not null) server.Snapshot = snapshot; });
    }
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

    // Starts or stops the overlay to match the setting (at startup and when it's switched).
    public void Apply()
    {
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
    }

    // A game tab's characters changed: the overlay follows the character played last (test
    // characters don't count, they'd always look like they were just played).
    public void Update(Flavor flavor, IEnumerable<CharacterRow> rows, IReadOnlyList<Entry> events)
    {
        if (rows.Where(r => !r.IsTest).MaxBy(r => r.LastSeen) is { } row)
            latest[flavor] = (Character(row, flavor), events);
        else
            latest.Remove(flavor);

        snapshot = StreamSnapshot.Latest(latest.Values, DateTimeOffset.Now);
        if (server is null) return;
        server.Snapshot = snapshot;
        StreamFiles.Write(snapshot);
    }

    public void Shutdown() => server?.Dispose();

    static StreamCharacter Character(CharacterRow r, Flavor flavor) => new(
        r.Guid, r.FullName, flavor.Name, r.Level, r.ClassName,
        CharacterArt.ClassColors.TryGetValue(r.ClassFile.ToUpperInvariant(), out var color) ? $"#{color:X6}" : "#FFFFFF",
        r.Race, r.RealmText, r.Zone, r.Played > 0 ? Format.Played(r.Played) : "", r.ItemLevel > 0 ? r.ItemLevel.ToString("0") : "",
        r.LastSeen, r.Highlights.Select(h => h.Text).ToList());
}
