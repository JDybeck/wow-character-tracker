namespace WoWCharacterTracker.Services.Streaming;

// What the stream overlay and the stream text files show: the character played last, its latest
// level times, today's levels, its duel and Mak'gora records, its highlights, and the newest ding
// (for the alert), plus how the overlay should look (StreamOptions). Built from plain data
// (StreamCharacter and the events), so it doesn't depend on the app's view models. With
// StreamOptions.Since set, only what happened from then on counts ("Start from now").
sealed record StreamSnapshot(
    string Character, string Game, int Level, string ClassName, string ClassColor, string Race, string Realm,
    string Zone, string Played, string ItemLevel, IReadOnlyList<StreamLevel> Levels, string LastLevel, string Today,
    IReadOnlyList<string> Highlights, StreamDing? Ding, long Now,
    StreamRecord? Duels = null, StreamRecord? Makgora = null, StreamLook? Look = null, string SinceText = "")
{
    public static readonly StreamSnapshot Empty = new("", "", 0, "", "#FFFFFF", "", "", "", "", "", [], "", "", [], null, 0, Look: StreamLook.From(new()));

    public bool HasCharacter => Character.Length > 0;

    // "Ronnie Raket, level 21 Paladin"
    public string CharacterLine => HasCharacter ? $"{Character}, level {Level} {ClassName}".TrimEnd() : "";

    const int LevelsShown = 5;
    const int HighlightsShown = 3;

    // The character played last of each game's candidates (Empty when there are none: MaxBy on an
    // empty list of tuples would throw).
    public static StreamSnapshot Latest(IEnumerable<(StreamCharacter Character, IReadOnlyList<Entry> Events)> candidates, DateTimeOffset now, StreamOptions? options = null)
    {
        var list = candidates.ToList();
        if (list.Count == 0) return Empty with { Look = StreamLook.From(options ?? new()) };
        var (character, events) = list.MaxBy(v => v.Character.LastSeen);
        return Build(character, events, now, options);
    }

    public static StreamSnapshot Build(StreamCharacter c, IEnumerable<Entry> events, DateTimeOffset now, StreamOptions? options = null)
    {
        var o = options ?? new();
        var mine = events.Where(e => e.Guid == c.Guid && e.At >= o.Since).OrderByDescending(e => e.At).ToList();
        var levels = mine.Where(e => e.Kind == "level").ToList();
        var shown = levels.Take(LevelsShown).Select(e => new StreamLevel(e.Level, Took(e))).ToList();
        var newest = levels.FirstOrDefault();

        // Today: since local midnight (and since "Start from now", when that's later).
        var midnight = new DateTimeOffset(now.LocalDateTime.Date, now.Offset).ToUnixTimeSeconds();
        var today = levels.Where(e => e.At >= midnight).ToList();
        var timed = today.Where(e => e.Seconds > 0).ToList();
        var todayText = today.Count == 0 ? "No levels yet today"
            : $"{today.Count} level{(today.Count == 1 ? "" : "s")} today" + (timed.Count > 0 ? $", {Format.Duration((long)timed.Average(e => e.Seconds))} average" : "");

        StreamRecord? Record(string kind)
        {
            var fights = mine.Where(e => e.Kind == kind).ToList();
            return fights.Count == 0 ? null : new StreamRecord(fights.Count(e => !e.Lost), fights.Count(e => e.Lost));
        }

        var kinds = o.HighlightKinds.ToHashSet();
        var highlights = mine.Where(e => kinds.Contains(e.Kind)).Select(HighlightText).Where(t => t.Length > 0).Take(HighlightsShown).ToList();

        return new StreamSnapshot(
            c.Name, c.Game, c.Level, c.ClassName, c.ClassColor, c.Race, c.Realm, c.Zone, c.Played, c.ItemLevel,
            shown,
            newest is null ? "" : $"{newest.Level - 1} to {newest.Level} took {Took(newest)}",
            todayText,
            highlights,
            newest is null ? null : new StreamDing(newest.Id, newest.Level, Took(newest), newest.At),
            now.ToUnixTimeSeconds(),
            Record("duel"), Record("makgora"), StreamLook.From(o),
            o.Since > 0 ? "Since " + DateTimeOffset.FromUnixTimeSeconds(o.Since).LocalDateTime.ToString("MMM d, HH:mm", System.Globalization.CultureInfo.InvariantCulture) : "");
    }

    // One line per highlight, like the app's: "Looted Thunderfury...", "Won a duel against Grimtusk".
    // Losses aren't highlights (they still count in the records).
    public static string HighlightText(Entry e) => e.Kind switch
    {
        "loot" when e.Item.Length > 0 => $"Looted {e.Item}",
        "boss" when e.Boss.Length > 0 => $"Killed {e.Boss}",
        "mount" when e.Mount.Length > 0 => (e.Bought ? "Bought " : "Got ") + e.Mount,
        "duel" when !e.Lost => e.Fled ? $"{e.Opponent} ran from a duel" : $"Won a duel against {e.Opponent}",
        "makgora" when !e.Lost => $"Won a Mak'gora against {e.Opponent}",
        "death" => $"Died at level {e.Level}",
        "mplus" => $"Reached {e.Step} Mythic+ rating",
        "pvp" => $"Reached {e.Step} in {e.Bracket}",
        _ => "",
    };

    static string Took(Entry e) => e.Seconds > 0 ? Format.Duration(e.Seconds) : "?";
}

// The character as the stream sees it (filled in from the character list).
sealed record StreamCharacter(string Guid, string Name, string Game, int Level, string ClassName, string ClassColor, string Race,
    string Realm, string Zone, string Played, string ItemLevel, long LastSeen);

sealed record StreamLevel(int Level, string Took);

// Duels or Mak'goras won and lost.
sealed record StreamRecord(int Won, int Lost)
{
    public override string ToString() => $"{Won} won, {Lost} lost";
}

// The newest level-up; the overlay shows its alert when it's new (Id) and recent (At).
sealed record StreamDing(string Id, int Level, string Took, long At);

// How the overlay looks: the card's background (0-100) and which parts it shows.
sealed record StreamLook(int CardOpacity, bool Levels, bool Today, bool Duels, bool Makgora, bool Highlights)
{
    public static StreamLook From(StreamOptions o) =>
        new(Math.Clamp(o.CardOpacity, 0, 100), o.ShowLevels, o.ShowToday, o.ShowDuels, o.ShowMakgora, o.ShowHighlights);
}
