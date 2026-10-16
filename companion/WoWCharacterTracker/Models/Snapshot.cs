using MoonSharp.Interpreter;

namespace WoWCharacterTracker.Models;

// The latest state of a character, saved by the addon on every /played, /reload and logout.
record Snapshot(string Guid, string Char, string Realm, int Level, string Class, string ClassFile,
    string Race, string RaceFile, string Sex, string Spec, string Role, double ItemLevel, long Played,
    string Zone, string Professions, string Talents, long LastSeen, bool Hardcore, string Ruleset,
    long Money = 0, string Crests = "", string? Guild = null, string Surname = "", string TalentPicks = "", string TalentExport = "", string Stats = "", string TalentCalc = "", string Region = "");

record TrackerData(List<Entry> Events, List<Snapshot> Characters)
{
    public static TrackerData Empty { get; } = new([], []);
}
