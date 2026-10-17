using System.Globalization;
using System.Text.RegularExpressions;

namespace WoWCharacterTracker.Services.LiveChannel;

// The addon's "key=value;key=value" format with %XX escapes. Newer addons use one-letter keys,
// leave the GUID off the front of the id, and write sex as f/m.
static class Payload
{
    // Same table as SHORT in the addon.
    static readonly Dictionary<string, string> ShortKeys = new()
    {
        ["i"] = "id", ["k"] = "kind", ["g"] = "guid", ["n"] = "char", ["r"] = "realm", ["l"] = "level", ["a"] = "at",
        ["z"] = "zone", ["p"] = "professions", ["t"] = "talents", ["s"] = "spec", ["o"] = "role", ["v"] = "ilvl",
        ["c"] = "class", ["C"] = "classFile", ["e"] = "race", ["E"] = "raceFile", ["x"] = "sex", ["h"] = "hardcore",
        ["u"] = "ruleset", ["d"] = "seconds", ["y"] = "played", ["b"] = "boss", ["I"] = "instance", ["m"] = "item",
        ["M"] = "itemId", ["q"] = "quality", ["G"] = "gold", ["K"] = "killer", ["w"] = "mount", ["B"] = "bought",
        ["S"] = "shot", ["P"] = "group", ["Z"] = "groupSize", ["N"] = "money", ["R"] = "crests", ["X"] = "maxLevel", ["U"] = "guild", ["L"] = "surname", ["O"] = "opponent", ["F"] = "fled", ["T"] = "rating", ["H"] = "step", ["D"] = "bracket", ["J"] = "talentExport", ["Q"] = "talentCalc", ["A"] = "region", ["Y"] = "lost", ["V"] = "opponentInfo",
    };

    public static Entry? Parse(string data)
    {
        var values = new Dictionary<string, string>();
        foreach (var pair in data.Split(';'))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            var key = pair[..eq];
            values[ShortKeys.GetValueOrDefault(key, key)] = Unescape(pair[(eq + 1)..]);
        }
        if (values.Count == 0) return null;
        if (values.TryGetValue("sex", out var sex) && sex.Length == 1) values["sex"] = sex == "f" ? "female" : "male";
        if (values.TryGetValue("id", out var id) && id.StartsWith(':') && values.TryGetValue("guid", out var guid))
            values["id"] = guid + id;

        var entry = Entry.From(
            k => values.GetValueOrDefault(k, ""),
            k => double.TryParse(values.GetValueOrDefault(k), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0);
        return entry.Id.Length > 0 ? entry : null;
    }

    static string Unescape(string s) =>
        s.Contains('%') ? Regex.Replace(s, "%([0-9A-Fa-f]{2})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString()) : s;
}
