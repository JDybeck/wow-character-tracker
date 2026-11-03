using System.Text;

namespace WoWCharacterTracker.Services.Streaming;

// Text files for OBS / Streamlabs Text sources ("Read from file"), one value per file, in
// <data dir>\stream (inside the app's folder, so Uninstall removes them). A file is only rewritten
// when its text changes, so the stream software doesn't redraw for nothing.
static class StreamFiles
{
    public static string Dir => Path.Combine(Config.Dir, "stream");

    // File name -> what it holds, for the snapshot.
    public static IReadOnlyList<(string File, string Text)> Contents(StreamSnapshot s) =>
    [
        ("character.txt", s.Character),
        ("character_line.txt", s.CharacterLine),
        ("level.txt", s.HasCharacter ? s.Level.ToString() : ""),
        ("last_level.txt", s.LastLevel),
        ("levels.txt", string.Join(Environment.NewLine, s.Levels.Select(l => $"Level {l.Level}: {l.Took}"))),
        ("today.txt", s.HasCharacter ? s.Today : ""),
        ("highlights.txt", string.Join(Environment.NewLine, s.Highlights.Take(StreamSnapshot.HighlightsShown).Select(h => h.Text))),
        ("zone.txt", s.Zone),
        ("played.txt", s.Played),
        ("item_level.txt", s.ItemLevel),
        ("duels.txt", s.Duels?.ToString() ?? ""),
        ("makgora.txt", s.Makgora?.ToString() ?? ""),
        ("battlegrounds.txt", s.Battlegrounds?.ToString() ?? ""),
    ];

    // What each file was last written with, so an unchanged one isn't read back from disk every refresh.
    static readonly Dictionary<string, string> Written = [];
    // UTF-8 with a BOM: OBS reads it right, names like Fågelmannen included.
    static readonly UTF8Encoding WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public static void Write(StreamSnapshot s)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            foreach (var (file, text) in Contents(s))
            {
                var path = Path.Combine(Dir, file);
                if (Written.GetValueOrDefault(file) == text && File.Exists(path)) continue;
                // Whole or not at all, so OBS never reads a half-written file.
                Files.WriteAtomic(path, [.. WithBom.GetPreamble(), .. WithBom.GetBytes(text)]);
                Written[file] = text;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Couldn't write the stream text files: " + ex.Message);
        }
    }
}
