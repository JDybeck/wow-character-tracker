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

    public static void Write(StreamSnapshot s)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            foreach (var (file, text) in Contents(s))
            {
                var path = Path.Combine(Dir, file);
                if (File.Exists(path) && File.ReadAllText(path) == text) continue;
                // UTF-8 with a BOM: OBS reads it right, names like Fågelmannen included.
                File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Couldn't write the stream text files: " + ex.Message);
        }
    }
}
