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
    public bool ShowHighlights { get; set; } = true;

    // Which highlights the highlights box shows: loot, boss, mount, duel, makgora, death, mplus, pvp.
    public List<string> HighlightKinds { get; set; } = [.. AllKinds];
    public static readonly string[] AllKinds = ["loot", "boss", "mount", "duel", "makgora", "death", "mplus", "pvp"];

    // Which moments pop up as the big alert (StreamAlert kinds). Rare loot is off by default (it's often).
    public List<string> AlertKinds { get; set; } = ["level", "maxlevel", "boss", "legendary", "epic", "mount", "duel", "makgora", "death", "mplus", "pvp"];
    public static readonly string[] AllAlertKinds = ["level", "maxlevel", "boss", "legendary", "epic", "rare", "mount", "duel", "makgora", "death", "mplus", "pvp"];

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
