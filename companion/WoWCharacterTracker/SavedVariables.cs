using MoonSharp.Interpreter;

namespace WoWCharacterTracker;

// One finished level, as the addon recorded it.
record Entry(string Id, string Char, string Realm, int Level, long Seconds, long At,
    string Zone, string Professions, string Talents,
    string Spec = "", string Class = "", string ClassFile = "", string Race = "", string RaceFile = "",
    string Sex = "male", long Played = 0, double ItemLevel = 0)
{
    // Ids are "<guid>:<level>".
    public string Guid => Id.LastIndexOf(':') is var i and > 0 ? Id[..i] : Id;
}

// The latest state of a character, saved by the addon on every /reload and logout.
record Snapshot(string Guid, string Char, string Realm, int Level, string Class, string ClassFile,
    string Race, string RaceFile, string Sex, string Spec, string Role, double ItemLevel, long Played,
    string Zone, string Professions, string Talents, long LastSeen);

record TrackerData(List<Entry> Events, List<Snapshot> Characters)
{
    public static TrackerData Empty { get; } = new([], []);
}

static class SavedVariables
{
    public static TrackerData Read(string path)
    {
        string text;
        // The game may still have the file open, so share it.
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var sr = new StreamReader(fs))
        {
            text = sr.ReadToEnd();
        }

        var script = new Script(CoreModules.Preset_SoftSandbox);
        script.DoString(text);

        var db = script.Globals.Get("WoWCharacterTrackerDB");
        if (db.Type != DataType.Table) return TrackerData.Empty;

        return new TrackerData(ReadEvents(db.Table.Get("events")), ReadCharacters(db.Table.Get("chars")));
    }

    static List<Entry> ReadEvents(DynValue events)
    {
        var list = new List<Entry>();
        if (events.Type != DataType.Table) return list;

        foreach (var v in events.Table.Values)
        {
            if (v.Type != DataType.Table) continue;
            var t = v.Table;
            list.Add(new Entry(
                S(t, "id"), S(t, "char") is { Length: > 0 } c ? c : "?", S(t, "realm"),
                (int)N(t, "level"), (long)N(t, "seconds"), (long)N(t, "at"),
                S(t, "zone"), S(t, "professions"), S(t, "talents"),
                S(t, "spec"), S(t, "class"), S(t, "classFile"), S(t, "race"), S(t, "raceFile"),
                S(t, "sex") == "female" ? "female" : "male",
                (long)N(t, "played"), N(t, "ilvl")));
        }

        return list.Where(x => x.Id.Length > 0).ToList();
    }

    static List<Snapshot> ReadCharacters(DynValue chars)
    {
        var list = new List<Snapshot>();
        if (chars.Type != DataType.Table) return list;

        foreach (var pair in chars.Table.Pairs)
        {
            if (pair.Value.Type != DataType.Table || pair.Key.Type != DataType.String) continue;
            var t = pair.Value.Table;
            list.Add(new Snapshot(
                pair.Key.String, S(t, "char"), S(t, "realm"), (int)N(t, "level"),
                S(t, "class"), S(t, "classFile"), S(t, "race"), S(t, "raceFile"),
                S(t, "sex") == "female" ? "female" : "male",
                S(t, "spec"), S(t, "role"), N(t, "ilvl"), (long)N(t, "played"),
                S(t, "zone"), S(t, "professions"), S(t, "talents"), (long)N(t, "lastSeen")));
        }

        return list;
    }

    static string S(Table t, string key) => t.Get(key).CastToString() ?? "";
    static double N(Table t, string key) => t.Get(key).CastToNumber() ?? 0;
}

static class Format
{
    public static string Duration(long seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        if (ts.TotalMinutes >= 1) return $"{ts.Minutes}m {ts.Seconds}s";
        return $"{ts.Seconds}s";
    }

    // Longer form for total /played, e.g. "2d 3h 10m".
    public static string Played(long seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
        return Duration(seconds);
    }

    public static string Ago(long unixSeconds)
    {
        if (unixSeconds <= 0) return "";
        var span = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays}d ago";
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime.ToString("yyyy-MM-dd");
    }
}
