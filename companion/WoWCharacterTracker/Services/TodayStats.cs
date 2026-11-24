using System.Globalization;

namespace WoWCharacterTracker.Services;

// Home's TODAY numbers, one character's or every character of the game (guid null), most important
// first: the addon's ns.todayStats (Today.lua) with the same order, plus what the app posted. Only
// stats above 0 (a 0 says nothing; a quiet day has none); the rest folds
// behind "more".
static class TodayStats
{
    public const int Shown = 4;

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
        return [.. shown.Select(k => new TodayCount(Value(k, v[k]), Name(k, v[k], keys)))];
    }

    // Gold under one gold is silver ("98 silver"), bosses are keys on Retail (the addon's ns.todayName).
    internal static string Name(string key, double value, bool keys) => key switch
    {
        "gold" when value < 10000 => "silver",
        "bosses" when keys => "keys",
        _ => Names[key],
    };

    // The characters that did something today (a moment, a quest, gold, gathering...), each with
    // what, unpadded. Only logging in doesn't count: that alone starts the addon's day counters.
    public static Dictionary<string, List<TodayCount>> ByCharacter(IEnumerable<Entry> events, IEnumerable<DayTotals> days, IReadOnlySet<string> sent,
        bool keys, DateTime now)
    {
        var list = events as IReadOnlyCollection<Entry> ?? [.. events];
        var dayList = days as IReadOnlyCollection<DayTotals> ?? [.. days];
        var guids = EventsOf(list, now).Select(e => e.Guid).Concat(DaysOf(dayList, now).Select(d => d.Guid)).Where(g => g.Length > 0).Distinct();
        return guids
            .Select(g => (g, counts: Build(list, dayList, sent, keys, now, g)))
            .Where(x => x.counts.Count > 0)
            .ToDictionary(x => x.g, x => x.counts);
    }

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

    // "+2.5" item levels, gold as "12" or "1,234" (silver under one gold: "98"), else the count; the
    // name after it says which (the addon's ns.todayValue).
    internal static string Value(string key, double value) => key switch
    {
        "ilvl" => "+" + value.ToString("0.#", CultureInfo.InvariantCulture),
        "gold" when value >= 10000 => Format.Thousands((long)value / 10000),
        "gold" => ((long)(value / 100)).ToString(CultureInfo.InvariantCulture),
        _ => ((long)value).ToString(CultureInfo.InvariantCulture),
    };
}

public sealed record TodayCount(string Value, string What);
