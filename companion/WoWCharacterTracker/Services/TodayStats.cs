using System.Globalization;

namespace WoWCharacterTracker.Services;

// Home's TODAY rows, one character's or every character of the game (guid null), most important
// first: the addon's ns.todayStats (Today.lua) with the same order, plus what the app posted. Only
// stats above 0, padded to Shown with the main ones at 0; the rest folds behind "more".
static class TodayStats
{
    public const int Shown = 4;

    static readonly string[] Main = ["levels", "bosses", "epics", "mounts"];
    static readonly string[] Order = ["levels", "bosses", "epics", "mounts", "ilvl", "quests", "gold", "herbs", "ore", "skins", "fish", "posted"];
    static readonly Dictionary<string, string> Names = new()
    {
        ["levels"] = "levels", ["bosses"] = "bosses", ["epics"] = "epics", ["mounts"] = "mounts", ["ilvl"] = "item level",
        ["quests"] = "quests", ["gold"] = "gold", ["herbs"] = "herbs", ["ore"] = "veins", ["skins"] = "skins", ["fish"] = "fish", ["posted"] = "posted",
    };

    public static List<TodayCount> Build(IEnumerable<Entry> events, IEnumerable<DayTotals> days, IReadOnlySet<string> sent, bool keys, DateTime now,
        string? guid = null)
    {
        var v = Order.ToDictionary(k => k, _ => 0.0);

        foreach (var e in EventsOf(events, now).Where(e => guid is null || e.Guid == guid))
        {
            if (e.Kind == "level") v["levels"]++;
            else if (e.Kind is "boss" or "keyrun") v["bosses"]++;
            else if (e.Kind == "loot" && e.Quality >= 4) v["epics"]++;
            else if (e.Kind == "mount") v["mounts"]++;
            if (sent.Contains(e.Id)) v["posted"]++;
        }
        foreach (var d in DaysOf(days, now).Where(d => guid is null || d.Guid == guid))
        {
            v["quests"] += d.Quests;
            v["herbs"] += d.Herbs;
            v["ore"] += d.Ore;
            v["skins"] += d.Skins;
            v["fish"] += d.Fish;
            v["ilvl"] += Math.Max(0, d.Ilvl - d.IlvlStart);
            v["gold"] += Math.Max(0, d.Money - d.MoneyStart);
        }
        if (v["ilvl"] < 0.05) v["ilvl"] = 0; // rounding left over from averages
        if (v["gold"] < 100) v["gold"] = 0; // less than a silver isn't worth a spot

        var shown = Order.Where(k => v[k] > 0).ToList();
        shown.AddRange(Main.Where(k => v[k] == 0).Take(Math.Max(0, Shown - shown.Count)));
        return [.. shown.Select(k => new TodayCount(Value(k, v[k]), k == "bosses" && keys ? "keys" : Names[k]))];
    }

    // The characters played today: a moment since midnight or the addon's day counters.
    public static HashSet<string> Characters(IEnumerable<Entry> events, IEnumerable<DayTotals> days, DateTime now) =>
        [.. EventsOf(events, now).Select(e => e.Guid).Concat(DaysOf(days, now).Select(d => d.Guid)).Where(g => g.Length > 0)];

    static IEnumerable<Entry> EventsOf(IEnumerable<Entry> events, DateTime now)
    {
        var since = new DateTimeOffset(now.Date).ToUnixTimeSeconds();
        return events.Where(e => e.At >= since);
    }

    static IEnumerable<DayTotals> DaysOf(IEnumerable<DayTotals> days, DateTime now)
    {
        var day = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return days.Where(d => d.Day == day);
    }

    // "+2.5" item levels, "12g" or "40s", else the count (the addon's ns.todayValue).
    static string Value(string key, double value) => key switch
    {
        "ilvl" => "+" + value.ToString("0.#", CultureInfo.InvariantCulture),
        "gold" when value >= 10000 => Format.Gold((long)value),
        "gold" => $"{(long)(value / 100)}s",
        _ => ((long)value).ToString(CultureInfo.InvariantCulture),
    };
}

public sealed record TodayCount(string Value, string What);
