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
        OpenOverlayCommand = new RelayCommand(_ => Browser.Open(Url));
    }

    public ICommand CopyUrlCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand OpenOverlayCommand { get; }
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

        snapshot = latest.Values.MaxBy(v => v.Character.LastSeen) is var (c, e) && c is not null
            ? StreamSnapshot.Build(c, e, DateTimeOffset.Now)
            : StreamSnapshot.Empty;
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
