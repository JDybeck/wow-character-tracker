namespace WoWCharacterTracker.Models;

// The latest state of a character, saved by the addon on every /played, /reload and logout.
// Xp, XpMax and MaxLevel come from the saved file only (for the time-to-max estimate).
record Snapshot(string Guid, string Char, string Realm, int Level, string Class, string ClassFile,
    string Race, string RaceFile, string Sex, string Spec, string Role, double ItemLevel, long Played,
    string Zone, string Professions, string Talents, long LastSeen, bool Hardcore, string Ruleset,
    long Money = 0, string Crests = "", string? Guild = null, string Surname = "", string TalentPicks = "", string TalentExport = "",
    string Stats = "", string TalentCalc = "", string Region = "", long Xp = 0, long XpMax = 0, int MaxLevel = 0);

// Everything read for one game tab: events, character snapshots, what each character carries, and
// the XP each level needs on that client ([level] = XP).
record TrackerData(List<Entry> Events, List<Snapshot> Characters)
{
    public List<Inventory> Inventories { get; init; } = [];
    public Dictionary<int, long> XpTable { get; init; } = [];

    public static TrackerData Empty { get; } = new([], []);
}
