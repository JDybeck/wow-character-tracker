using MoonSharp.Interpreter;

namespace WoWCharacterTracker;

record Entry(string Id, string Char, string Realm, int Level, long Seconds, long At,
    string Zone, string Professions, string Talents,
    string Spec = "", string Class = "", string ClassFile = "", string Race = "", string RaceFile = "",
    string Sex = "male");

static class SavedVariables
{
    // Reads the level-up entries from the addon's SavedVariables file.
    public static List<Entry> Read(string path)
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

        var list = new List<Entry>();
        var db = script.Globals.Get("WoWCharacterTrackerDB");
        if (db.Type != DataType.Table) return list;

        var events = db.Table.Get("events");
        if (events.Type != DataType.Table) return list;

        foreach (var v in events.Table.Values)
        {
            if (v.Type != DataType.Table) continue;
            var t = v.Table;
            string S(string key) => t.Get(key).CastToString() ?? "";
            long N(string key) => (long)(t.Get(key).CastToNumber() ?? 0);

            list.Add(new Entry(
                S("id"), S("char") is { Length: > 0 } c ? c : "?", S("realm"),
                (int)N("level"), N("seconds"), N("at"),
                S("zone"), S("professions"), S("talents"),
                S("spec"), S("class"), S("classFile"), S("race"), S("raceFile"),
                S("sex") == "female" ? "female" : "male"));
        }

        return list.Where(x => x.Id.Length > 0).ToList();
    }
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
}
