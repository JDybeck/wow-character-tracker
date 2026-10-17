namespace WoWCharacterTracker.Services.Streaming;

// What the stream overlay and the stream text files show: the character played last, its latest
// level times, today's levels, its highlights, and the newest ding (for the alert). Built from plain
// data (StreamCharacter and the level events), so it doesn't depend on the app's view models.
sealed record StreamSnapshot(
    string Character, string Game, int Level, string ClassName, string ClassColor, string Race, string Realm,
    string Zone, string Played, string ItemLevel, IReadOnlyList<StreamLevel> Levels, string LastLevel, string Today,
    IReadOnlyList<string> Highlights, StreamDing? Ding, long Now)
{
    public static readonly StreamSnapshot Empty = new("", "", 0, "", "#FFFFFF", "", "", "", "", "", [], "", "", [], null, 0);

    public bool HasCharacter => Character.Length > 0;

    // "Ronnie Raket, level 21 Paladin"
    public string CharacterLine => HasCharacter ? $"{Character}, level {Level} {ClassName}".TrimEnd() : "";

    const int LevelsShown = 5;
    const int HighlightsShown = 3;

    // The character played last of each game's candidates (Empty when there are none: MaxBy on an
    // empty list of tuples would throw).
    public static StreamSnapshot Latest(IEnumerable<(StreamCharacter Character, IReadOnlyList<Entry> Events)> candidates, DateTimeOffset now)
    {
        var list = candidates.ToList();
        if (list.Count == 0) return Empty;
        var (character, events) = list.MaxBy(v => v.Character.LastSeen);
        return Build(character, events, now);
    }

    public static StreamSnapshot Build(StreamCharacter c, IEnumerable<Entry> events, DateTimeOffset now)
    {
        var levels = events.Where(e => e.Kind == "level" && e.Guid == c.Guid).OrderByDescending(e => e.At).ToList();
        var shown = levels.Take(LevelsShown).Select(e => new StreamLevel(e.Level, Took(e))).ToList();
        var newest = levels.FirstOrDefault();

        // Today: since local midnight.
        var midnight = new DateTimeOffset(now.LocalDateTime.Date, now.Offset).ToUnixTimeSeconds();
        var today = levels.Where(e => e.At >= midnight).ToList();
        var timed = today.Where(e => e.Seconds > 0).ToList();
        var todayText = today.Count == 0 ? "No levels yet today"
            : $"{today.Count} level{(today.Count == 1 ? "" : "s")} today" + (timed.Count > 0 ? $", {Format.Duration((long)timed.Average(e => e.Seconds))} average" : "");

        return new StreamSnapshot(
            c.Name, c.Game, c.Level, c.ClassName, c.ClassColor, c.Race, c.Realm, c.Zone, c.Played, c.ItemLevel,
            shown,
            newest is null ? "" : $"{newest.Level - 1} to {newest.Level} took {Took(newest)}",
            todayText,
            c.Highlights.Take(HighlightsShown).ToList(),
            newest is null ? null : new StreamDing(newest.Id, newest.Level, Took(newest), newest.At),
            now.ToUnixTimeSeconds());
    }

    static string Took(Entry e) => e.Seconds > 0 ? Format.Duration(e.Seconds) : "?";
}

// The character as the stream sees it (filled in from the character list).
sealed record StreamCharacter(string Guid, string Name, string Game, int Level, string ClassName, string ClassColor, string Race,
    string Realm, string Zone, string Played, string ItemLevel, long LastSeen, IReadOnlyList<string> Highlights);

sealed record StreamLevel(int Level, string Took);

// The newest level-up; the overlay shows its alert when it's new (Id) and recent (At).
sealed record StreamDing(string Id, int Level, string Took, long At);
