namespace WoWCharacterTracker.Models;

// How the stream overlay looks and what it shows (the Stream overlay panel), in config.json.
class StreamOptions
{
    public bool PanelOpen { get; set; } = true;

    // The card's background, 0 (see-through) to 100 (solid).
    public int CardOpacity { get; set; } = 78;

    public bool ShowLevels { get; set; } = true;     // the last level's time and the ones before
    public bool ShowToday { get; set; } = true;      // levels today and their average
    public bool ShowDuels { get; set; } = true;      // duel record, won and lost
    public bool ShowMakgora { get; set; } = true;    // Mak'gora record (only shown once there is one)
    public bool ShowHighlights { get; set; } = true;

    // Which highlights the card shows: loot, boss, mount, duel, makgora, death, mplus, pvp.
    public List<string> HighlightKinds { get; set; } = [.. AllKinds];
    public static readonly string[] AllKinds = ["loot", "boss", "mount", "duel", "makgora", "death", "mplus", "pvp"];

    // "Start from now": the overlay only counts what happened from this moment (unix seconds, 0 = all
    // time). Everything is still tracked and shown in the app as before.
    public long Since { get; set; }
}
