namespace WoWCharacterTracker.Models;

// The latest state of a character, saved by the addon on every /played, /reload and logout.
// Xp, XpMax and MaxLevel come from the saved file only (for the time-to-max estimate).
record Snapshot(string Guid, string Char, string Realm, int Level, string Class, string ClassFile,
    string Race, string RaceFile, string Sex, string Spec, string Role, double ItemLevel, long Played,
    string Zone, string Professions, string Talents, long LastSeen, bool Hardcore, string Ruleset,
    long Money = 0, string Crests = "", string? Guild = null, string Surname = "", string TalentPicks = "", string TalentExport = "",
    string Stats = "", string TalentCalc = "", string Region = "", long Xp = 0, long XpMax = 0, int MaxLevel = 0,
    string Keystone = "", string ZoneType = "", int MplusScore = 0, string PvpRatings = "")
{
    // The character as an event (or live check-in) describes it. Read with the same keys as events
    // (Entry.From), so a new character field is added there once; what only the saved file has
    // (talent picks, stats, XP) is filled in by SavedVariables.
    public static Snapshot Of(Entry e) => new(e.Guid, e.Char, e.Realm, e.Level, e.Class, e.ClassFile,
        e.Race, e.RaceFile, e.Sex, e.Spec, e.Role, e.ItemLevel, e.Played, e.Zone, e.Professions, e.Talents,
        e.At, e.Hardcore, e.Ruleset, e.Money, e.Crests, e.Guild, e.Surname, TalentExport: e.TalentExport, TalentCalc: e.TalentCalc,
        Region: e.Region, Keystone: e.Keystone, ZoneType: e.ZoneType, MplusScore: e.MplusScore, PvpRatings: e.PvpRatings);
}

// Everything read for one game tab: events, character snapshots, what each character carries, and
// the XP each level needs on that client ([level] = XP).
record TrackerData(List<Entry> Events, List<Snapshot> Characters)
{
    public List<Inventory> Inventories { get; init; } = [];
    public Dictionary<int, long> XpTable { get; init; } = [];
    public Dictionary<int, int> ItemClasses { get; init; } = []; // itemId -> the game's item class (Inventory.lua)
    public List<GameNote> Notes { get; init; } = []; // notes edited in game (NoteBox.lua)

    public static TrackerData Empty { get; } = new([], []);
}
