namespace WoWCharacterTracker.Services.Streaming;

// What the stream overlay and the stream text files show, for the character played last. The card is
// the character as it is now (item level, played, gold, keystone, ratings, duel and Mak'gora records) next to
// its leveling (latest level times, today); the highlights box is what happened (loot, bosses, duel
// wins...); the alert is the newest big moment (a level-up, boss kill, epic loot...). Plus how the
// overlay should look (StreamOptions). Built from plain data (StreamCharacter and the events), so it
// doesn't depend on the app's view models. StreamOptions.From decides what counts (all time, today,
// or since "Start counting now").
sealed record StreamSnapshot(
    string Character, string Game, int Level, string ClassName, string ClassColor, string Race, string Realm,
    string Zone, string Played, string ItemLevel, IReadOnlyList<StreamLevel> Levels, string LastLevel, string Today,
    IReadOnlyList<string> Highlights, StreamAlert? Alert, long Now,
    StreamRecord? Duels = null, StreamRecord? Makgora = null, StreamLook? Look = null, string SinceText = "",
    string Spec = "", string Gold = "", string Keystone = "", IReadOnlyList<StreamRating>? Ratings = null, StreamRecord? Battlegrounds = null)
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
        var from = o.From(now);
        var mine = events.Where(e => e.Guid == c.Guid && e.At >= from).OrderByDescending(e => e.At).ToList();
        var levels = mine.Where(e => e.Kind == "level").ToList();
        var shown = levels.Take(LevelsShown).Select(e => new StreamLevel(e.Level, Took(e))).ToList();
        var newest = levels.FirstOrDefault();

        // Today: since local midnight (and since "Start counting now", when that's later).
        var midnight = new DateTimeOffset(now.LocalDateTime.Date, now.Offset).ToUnixTimeSeconds();
        var today = levels.Where(e => e.At >= midnight).ToList();
        var timed = today.Where(e => e.Seconds > 0).ToList();
        var todayText = today.Count == 0 ? "No levels yet today"
            : $"{Format.Count(today.Count, "level")} today" + (timed.Count > 0 ? $", {Format.Duration((long)timed.Average(e => e.Seconds))} average" : "");

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
            mine.Select(AlertFor).FirstOrDefault(a => a is not null && o.AlertKinds.Contains(a.Kind)),
            now.ToUnixTimeSeconds(),
            Record("duel"), Record("makgora"), StreamLook.From(o),
            RangeText(o), c.Spec, c.Gold, c.Keystone, c.Ratings ?? [], Record("bg"));
    }

    // Under the card: what it counts, when it's not all time.
    public static string RangeText(StreamOptions o) => o.RangeMode switch
    {
        "today" => "Today",
        "now" => "Since " + DateTimeOffset.FromUnixTimeSeconds(o.Since).LocalDateTime.ToString("MMM d, HH:mm", System.Globalization.CultureInfo.InvariantCulture),
        _ => "",
    };

    // One line per highlight, the same as the app's (Services.HighlightText): "Looted Thunderfury...",
    // "Won a duel against Grimtusk", "Lost Warsong Gulch". Wins and losses both.
    public static string HighlightText(Entry e) => Services.HighlightText.Text(e);

    // The overlay's alert for an event, the way Discord would post it, or null when it's not alert
    // material (a duel or Mak'gora lost, rare loot is its own kind). Tone picks the colors on the page.
    public static StreamAlert? AlertFor(Entry e) => e.Kind switch
    {
        "level" when e.MaxLevel => new(e.Id, "maxlevel", "MAX LEVEL!", $"Level {e.Level}", e.Played > 0 ? $"Total played {Format.Played(e.Played)}" : "", "legendary", e.At),
        "level" => new(e.Id, "level", "DING!", $"Level {e.Level}", e.Seconds > 0 ? $"{e.Level - 1} to {e.Level} took {Took(e)}" : "", "gold", e.At),
        "boss" when e.Boss.Length > 0 => new(e.Id, "boss", "VICTORY!", $"{e.Boss} defeated", e.Instance, "victory", e.At),
        "loot" when e.Quality >= 5 => new(e.Id, "legendary", "LEGENDARY!", e.Item, "", "legendary", e.At),
        "loot" when e.Quality == 4 => new(e.Id, "epic", "EPIC LOOT!", e.Item, "", "epic", e.At),
        "loot" when e.Quality == 3 => new(e.Id, "rare", "RARE LOOT!", e.Item, "", "rare", e.At),
        "mount" when e.Mount.Length > 0 => new(e.Id, "mount", "NEW MOUNT!", e.Mount, e.Bought ? "Bought" : "", "gold", e.At),
        "duel" when !e.Lost => new(e.Id, "duel", "VICTORY!", e.Fled ? $"{e.Opponent} ran from the duel" : $"Won a duel against {e.Opponent}", Capital(e.OpponentText), "duel", e.At),
        "makgora" when !e.Lost => new(e.Id, "makgora", "MAK'GORA!", $"{e.Opponent} fell to the death", Capital(e.OpponentText), "blood", e.At),
        "bg" when !e.Lost => new(e.Id, "bg", "VICTORY!", e.Instance.Length > 0 ? $"Won {e.Instance}" : "Battleground won", "", "victory", e.At),
        "death" => new(e.Id, "death", "R.I.P.", $"{e.Char} fell at level {e.Level}", e.Played > 0 ? $"After {Format.Played(e.Played)} played" : "", "death", e.At),
        "mplus" => new(e.Id, "mplus", "MILESTONE!", $"{e.Step} Mythic+ rating", e.Rating > 0 ? $"Rating {e.Rating}" : "", "rating", e.At),
        "pvp" => new(e.Id, "pvp", "MILESTONE!", $"{e.Step} in {e.Bracket}", e.Rating > 0 ? $"Rating {e.Rating}" : "", "rating", e.At),
        _ => null,
    };

    static string Capital(string s) => s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;

    static string Took(Entry e) => e.Seconds > 0 ? Format.Duration(e.Seconds) : "?";
}

// The character as the stream sees it (filled in from the character list).
sealed record StreamCharacter(string Guid, string Name, string Game, int Level, string ClassName, string ClassColor, string Race,
    string Realm, string Zone, string Played, string ItemLevel, long LastSeen, string Spec = "", string Gold = "", string Keystone = "",
    IReadOnlyList<StreamRating>? Ratings = null);

sealed record StreamLevel(int Level, string Took);

// A Retail rating on the card: "Mythic+" 2850, "3v3" 1850 (Format.Ratings).
sealed record StreamRating(string Name, int Rating);

// Duels or Mak'goras won and lost.
sealed record StreamRecord(int Won, int Lost)
{
    public override string ToString() => $"{Won} won, {Lost} lost";
}

// The newest alert-worthy moment; the overlay pops it up when it's new (Id) and recent (At).
// Kind: level, maxlevel, boss, legendary, epic, rare, mount, duel, makgora, bg, death, mplus, pvp.
sealed record StreamAlert(string Id, string Kind, string Title, string Line, string Sub, string Tone, long At);

// How the overlay looks: the card's background (0-100) and which parts it shows.
sealed record StreamLook(int CardOpacity, bool Levels, bool Today, bool Duels, bool Makgora, bool Highlights, bool Stats = true, bool Battlegrounds = true)
{
    public static StreamLook From(StreamOptions o) =>
        new(Math.Clamp(o.CardOpacity, 0, 100), o.ShowLevels, o.ShowToday, o.ShowDuels, o.ShowMakgora, o.ShowHighlights, o.ShowStats, o.ShowBattlegrounds);
}
