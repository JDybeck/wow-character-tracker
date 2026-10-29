namespace WoWCharacterTracker.Models;

// How the stream overlay looks and what it shows (the Stream overlay panel), in config.json.
class StreamOptions
{
    public bool PanelOpen { get; set; } = true;

    // The card's background, 0 (see-through) to 100 (solid).
    public int CardOpacity { get; set; } = 78;

    public bool ShowStats { get; set; } = true;      // the card's character column: item level, played, gold, records
    public bool ShowLevels { get; set; } = true;     // the card's leveling column: the last level's time and the ones before
    public bool ShowToday { get; set; } = true;      // levels today and their average
    public bool ShowDuels { get; set; } = true;      // duel record, won and lost
    public bool ShowMakgora { get; set; } = true;    // Mak'gora record (only shown once there is one)
    public bool ShowBattlegrounds { get; set; } = true; // battleground record, won and lost (once there is one)
    public bool ShowHighlights { get; set; } = true;

    // Which lines the stats column shows (the Stats chips): item level, played, played at this level,
    // gold, talents (numbers only, 31/20/0), zone, and on Retail the keystone and ratings.
    public List<string> CardFields { get; set; } = [.. AllCardFields];
    public static readonly string[] AllCardFields = ["ilvl", "played", "levelPlayed", "gold", "talents", "zone", "keystone", "ratings"];

    // Size: the whole overlay in one source (percent, ?scale wins), and the card's own width and
    // smallest height in px (0 = fits its text), so it can cover a chat box. A single part in its
    // own source is still fitted to the source, these then set its shape.
    public int Scale { get; set; } = 100;
    public int CardWidth { get; set; }
    public int CardHeight { get; set; }

    // The highlights box's width and height in px (0 = fits its lines). With a height the box keeps
    // that size and shows the newest lines that fit, so a source fitted to it never rescales.
    public int HighlightsWidth { get; set; }
    public int HighlightsHeight { get; set; }

    // Colors as "#rrggbb": the boxes' edge and titles (empty = the character's class color) and
    // their background (empty = the dark default). Presets in StreamViewModel.
    public string Accent { get; set; } = "";
    public string Background { get; set; } = "";

    public bool ShowBorder { get; set; } = true;  // the thin colored edge around the card and highlights

    // Which highlights the highlights box shows (Highlights.Kinds): loot, boss, keyrun, mount, duel, makgora, bg, profession, death, mplus, pvp.
    public List<string> HighlightKinds { get; set; } = [.. AllKinds];
    public static readonly string[] AllKinds = ["loot", "boss", "keyrun", "mount", "duel", "makgora", "bg", "profession", "death", "mplus", "pvp"];

    // Which moments pop up as the big alert (StreamAlert kinds). Rare loot is off by default (it's often).
    public List<string> AlertKinds { get; set; } = ["level", "maxlevel", "boss", "keyrun", "legendary", "epic", "mount", "duel", "makgora", "bg", "profession", "death", "mplus", "pvp"];
    public static readonly string[] AllAlertKinds = ["level", "maxlevel", "boss", "keyrun", "legendary", "epic", "rare", "mount", "duel", "makgora", "bg", "profession", "death", "mplus", "pvp"];

    // Battlegrounds came in 0.21.2, Mythic+ runs in 0.28.0, profession milestones in 0.29.0: older
    // configs get them switched on once (Config.Load).
    public bool HasBattlegrounds { get; set; }
    public bool HasKeyRuns { get; set; }
    public bool HasProfessions { get; set; }

    // What the overlay counts (records, highlights, level times): "all" (all time), "today" (since
    // local midnight) or "now" (since Since, the moment "Start counting now" was clicked). Empty in
    // configs from before 0.20.0: then Since decides, as it did. Everything is still tracked in the app.
    public string Range { get; set; } = "";
    public long Since { get; set; }

    public string RangeMode => Range is "all" or "today" or "now" ? Range : Since > 0 ? "now" : "all";

    // The first moment that counts (unix seconds, 0 = all time).
    public long From(DateTimeOffset now) => RangeMode switch
    {
        "today" => new DateTimeOffset(now.LocalDateTime.Date, now.Offset).ToUnixTimeSeconds(),
        "now" => Since,
        _ => 0,
    };
}
