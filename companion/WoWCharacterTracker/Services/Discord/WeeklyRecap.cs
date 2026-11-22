using System.Globalization;

namespace WoWCharacterTracker.Services.Discord;

// The weekly recap: once a week, on the day and hour picked under Sharing (Sunday 20:00 by default,
// local time), one post to the tab's webhooks with what each of your characters did that week:
// "Levelled 41 to 48", the epics and mounts it got, bosses, quests, gold, item level, professions.
// Your own progress only, no comparing with friends (that's the Stats page). The app may be closed at
// that time, so a missed recap still posts when it opens, for the week up to the planned time,
// unless that was more than CatchUp ago (no recap days late).
static class WeeklyRecap
{
    public static readonly TimeSpan CatchUp = TimeSpan.FromDays(2);
    const int Color = 0xFFD100;
    const int MaxCharacters = 24; // Discord takes 25 fields; the last says how many more there were
    const int Named = 3;          // epics, mounts, bosses...: the first three by name, then "and 2 more"

    // The latest planned time at or before now (the hour on that weekday, in now's offset).
    public static DateTimeOffset LastSlot(DateTimeOffset now, DayOfWeek day, int hour)
    {
        var back = ((int)now.DayOfWeek - (int)day + 7) % 7;
        var slot = new DateTimeOffset(now.Date.AddDays(-back).AddHours(Math.Clamp(hour, 0, 23)), now.Offset);
        return slot > now ? slot.AddDays(-7) : slot;
    }

    // The planned time of the recap to post now, or null: the latest one, when it hasn't been sent
    // (sentAt: the planned time of the last one that went out, unix seconds) and isn't too old.
    public static DateTimeOffset? Due(DateTimeOffset now, DayOfWeek day, int hour, long sentAt)
    {
        var slot = LastSlot(now, day, hour);
        if (slot.ToUnixTimeSeconds() <= sentAt || now - slot > CatchUp) return null;
        return slot;
    }

    // "16 to 22 November", "28 November to 4 December": the seven days up to at.
    public static string WeekText(DateTimeOffset at)
    {
        var first = at.Date.AddDays(-6);
        var last = at.Date;
        string Day(DateTime d) => d.Day.ToString(CultureInfo.InvariantCulture);
        string Month(DateTime d) => d.ToString("MMMM", CultureInfo.InvariantCulture);
        return first.Month == last.Month
            ? $"{Day(first)} to {Day(last)} {Month(last)}"
            : $"{Day(first)} {Month(first)} to {Day(last)} {Month(last)}";
    }

    // One of your characters as the recap names it. Level: the level it's at now.
    public sealed record Character(string Guid, string Name, int Level);

    // A character's week: its heading and a line per thing it did. Score: how much happened, for the order.
    public sealed record Week(string Title, IReadOnlyList<string> Lines, int Score);

    // Every character that did something in the seven days up to at, the busiest first.
    // events and days: your own (not friends'); days count by the addon's local date.
    public static List<Week> Build(IEnumerable<Character> characters, IReadOnlyList<Entry> events, IReadOnlyList<DayTotals> days, DateTimeOffset at)
    {
        var from = at.ToUnixTimeSeconds() - 7 * 86400L;
        var until = at.ToUnixTimeSeconds();
        var firstDay = at.Date.AddDays(-6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var lastDay = at.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var inWeek = events.Where(e => e.At > from && e.At <= until).Select(e => e.Moment).ToLookup(e => e.Guid);
        var weekDays = DayHistory.OnePerDay(days.Where(d =>
            string.CompareOrdinal(d.Day, firstDay) >= 0 && string.CompareOrdinal(d.Day, lastDay) <= 0)).ToLookup(d => d.Guid);

        return [.. characters
            .Select(c => Of(c, [.. inWeek[c.Guid]], [.. weekDays[c.Guid].OrderBy(d => d.Day, StringComparer.Ordinal)]))
            .OfType<Week>()
            .OrderByDescending(w => w.Score)
            .ThenBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase)];
    }

    static Week? Of(Character c, List<Entry> events, List<DayTotals> days)
    {
        var lines = new List<string>();
        var score = 0;
        void Add(string line, int weight = 1) { lines.Add(line); score += weight; }
        List<Entry> Kind(params string[] kinds) => [.. events.Where(e => kinds.Contains(e.Kind)).OrderBy(e => e.At)];

        // Levels: each once, from the level before the first ding to the last one.
        var levels = Kind("level").DistinctBy(e => e.Level).OrderBy(e => e.Level).ToList();
        if (levels.Count > 0)
        {
            var leveling = levels.Sum(e => e.Seconds);
            var line = levels.Count == 1
                ? $"Levelled from **{levels[0].Level - 1}** to **{levels[0].Level}**"
                : $"Levelled from **{levels[0].Level - 1}** to **{levels[^1].Level}** ({levels.Count} levels)";
            if (leveling > 0) line += $" in {Format.Played(leveling)}";
            if (levels.Any(e => e.MaxLevel)) line += ", reaching max level! 🎉";
            Add("⬆️ " + line, 10 * levels.Count);
        }

        var epics = Kind("loot").Where(e => e.Quality >= 4 && !e.Mat).ToList();
        if (epics.Count > 0)
            Add($"💜 {(epics.Count == 1 ? "New epic" : $"{epics.Count} new epics")}: {Names(epics.Select(e => e.Item))}", 5 * epics.Count);
        var mounts = Kind("mount");
        if (mounts.Count > 0)
            Add($"🐎 {(mounts.Count == 1 ? "New mount" : $"{mounts.Count} new mounts")}: {Names(mounts.Select(e => e.Mount))}", 5 * mounts.Count);

        var bosses = Kind("boss");
        if (bosses.Count > 0)
            Add($"💀 Killed {Names(bosses.Select(e => e.Boss).Distinct())}", 3 * bosses.Count);
        var keys = Kind("keyrun");
        if (keys.Count > 0)
        {
            var best = keys.MaxBy(e => (e.Timed, e.KeyLevel))!;
            Add($"🗝️ {Format.Count(keys.Count, "Mythic+ run")}, best {Format.KeyRun(best)}{(best.Timed ? " in time" : "")}", 3 * keys.Count);
        }

        // Professions: the highest skill reached in each.
        var professions = Kind("profession").Where(e => e.Profession.Length > 0)
            .GroupBy(e => e.Profession).Select(g => $"{g.Key} {g.Max(e => Math.Max(e.Rating, e.Step))}").ToList();
        if (professions.Count > 0) Add($"⚒️ Reached {Join(professions)}", 2 * professions.Count);

        var achievements = Kind("achievement").Where(e => e.Achievement.Length > 0).ToList();
        if (achievements.Count > 0) Add($"🏆 {Names(achievements.Select(e => e.Achievement))}", 3 * achievements.Count);

        var quests = days.Sum(d => d.Quests);
        if (quests > 0) Add($"📜 {Format.Count(quests, "quest")} done", quests / 10 + 1);

        var gathered = new[] { (days.Sum(d => d.Herbs), "herb"), (days.Sum(d => d.Ore), "vein"), (days.Sum(d => d.Skins), "skin"), (days.Sum(d => d.Fish), "fish") }
            .Where(x => x.Item1 > 0)
            .Select(x => x.Item2 == "fish" ? $"{x.Item1} fish" : Format.Count(x.Item1, x.Item2)).ToList();
        if (gathered.Count > 0) Add($"🌿 Gathered {Join(gathered)}");

        // Item level: from the first day's start to the last day's end.
        var ilvlFrom = days.Select(d => d.IlvlStart > 0 ? d.IlvlStart : d.Ilvl).FirstOrDefault(v => v > 0);
        var ilvlTo = days.Select(d => d.Ilvl).LastOrDefault(v => v > 0);
        if (ilvlFrom > 0 && Math.Round(ilvlTo - ilvlFrom, 1) >= 0.1)
            Add($"🛡️ Item level {Ilvl(ilvlFrom)} to **{Ilvl(ilvlTo)}**", 2);

        // Gold: what it had at the start of its first day against the end of its last.
        var moneyDays = days.Where(d => d.Money > 0 || d.MoneyStart > 0).ToList();
        if (moneyDays.Count > 0 && moneyDays[^1].Money - moneyDays[0].MoneyStart is var gold && Math.Abs(gold) >= 10000)
            Add(gold > 0 ? $"💰 **+{Format.Gold(gold)}**" : $"💸 Spent {Format.Gold(-gold)}");

        var duels = Kind("duel", "makgora").Count(e => !e.Lost);
        var bgs = Kind("bg").Count(e => !e.Lost);
        var wins = new[] { (duels, "duel"), (bgs, "battleground") }.Where(x => x.Item1 > 0).Select(x => Format.Count(x.Item1, x.Item2)).ToList();
        if (wins.Count > 0) Add($"⚔️ Won {Join(wins)}");

        var deaths = Kind("death").Count;
        if (deaths > 0) Add($"🪦 Died {(deaths == 1 ? "once" : $"{deaths} times")}", 0);

        return lines.Count == 0 ? null : new Week($"{c.Name}, level {c.Level}", lines, score);
    }

    // The post, or null when none of your characters did anything that week.
    public static Dictionary<string, object>? Embed(IReadOnlyList<Week> weeks, string footer, DateTimeOffset at)
    {
        if (weeks.Count == 0) return null;
        var fields = weeks.Take(MaxCharacters)
            .Select(w => (object)new { name = w.Title, value = Cut(string.Join("\n", w.Lines), 1024), inline = false })
            .ToList();
        if (weeks.Count > MaxCharacters)
            fields.Add(new { name = "​", value = $"And {Format.Count(weeks.Count - MaxCharacters, "more character")} played.", inline = false });

        return new Dictionary<string, object>
        {
            ["title"] = "Weekly recap",
            ["description"] = $"The week of {WeekText(at)}: {Format.Count(weeks.Count, "character")} played.",
            ["color"] = Color,
            ["fields"] = fields,
            ["timestamp"] = at.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            ["footer"] = new { text = footer }, // DiscordWebhook adds the app's name and logo
        };
    }

    // "**A**, **B** and 2 more"
    static string Names(IEnumerable<string> names)
    {
        var all = names.Where(n => n.Length > 0).ToList();
        if (all.Count == 0) return "";
        var shown = all.Take(Named).Select(n => $"**{n}**").ToList();
        return all.Count > Named ? $"{string.Join(", ", shown)} and {all.Count - Named} more" : Join(shown);
    }

    // "a", "a and b", "a, b and c"
    static string Join(IReadOnlyList<string> parts) =>
        parts.Count <= 1 ? string.Join("", parts) : $"{string.Join(", ", parts.Take(parts.Count - 1))} and {parts[^1]}";

    static string Ilvl(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 3)] + "...";
}
