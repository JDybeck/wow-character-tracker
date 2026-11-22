using System.Globalization;

namespace WoWCharacterTracker.Services.Discord;

// The weekly recap: once a week, on the day and hour picked under Sharing (Sunday 20:00 by default,
// local time), the Stats page's week goes to the tab's webhooks as one post: the MVP podium and each
// board's top three. The app may be closed at that time, so a missed recap still posts when it opens,
// for the week up to the planned time, unless that was more than CatchUp ago (no recap days late).
static class WeeklyRecap
{
    public static readonly TimeSpan CatchUp = TimeSpan.FromDays(2);
    const int Top = 3;
    const int Gold = 0xFFD100;

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

    static readonly string[] Medals = ["🥇", "🥈", "🥉"];

    // The post: null when the week has nothing on any board. youName: what the post calls you
    // (the Stats page says "You", which reads wrong in a channel).
    public static Dictionary<string, object>? Embed(IReadOnlyList<Leaderboards.Board> boards, string youName, string footer, DateTimeOffset at)
    {
        if (boards.Count == 0) return null;
        string Name(Leaderboards.Player p) => p.IsYou ? youName : p.Name;

        var lines = new List<string> { $"The week of {WeekText(at)}." };
        var mvps = Leaderboards.Mvps(boards);
        if (mvps.Count > 0)
        {
            lines.Add("");
            lines.Add("**MVP**");
            foreach (var m in mvps.Take(Top))
            {
                var medal = m.Rank <= Medals.Length ? Medals[m.Rank - 1] : $"{m.Rank}.";
                var line = $"{medal} **{Name(m.Who)}**, {Format.Count(m.Points, "point")}";
                if (m.Won.Count > 0) line += $" (won {Won(m.Won)})";
                lines.Add(line);
            }
        }

        // One field per board, three side by side, like the Stats page's columns. Discord takes up to
        // 25 fields of up to 1024 characters each.
        var fields = boards.Take(25).Select(b => new
        {
            name = b.Title,
            value = Cut(string.Join("\n", b.Scores.Where(s => s.Rank is >= 1 and <= Top).Select(s =>
                $"{s.Rank}. {Name(s.Who)}: **{s.Text}**" + (s.Detail.Length > 0 ? $" ({s.Detail})" : ""))), 1024),
            inline = true,
        }).Where(f => f.value.Length > 0).ToList<object>();

        var embed = new Dictionary<string, object>
        {
            ["title"] = "Weekly recap",
            ["description"] = string.Join("\n", lines),
            ["color"] = Gold,
            ["timestamp"] = at.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            ["footer"] = new { text = footer }, // DiscordWebhook adds the app's name and logo
        };
        if (fields.Count > 0) embed["fields"] = fields;
        return embed;
    }

    // "Most quests, Most epics and 3 more", like the podium on the Stats page.
    static string Won(IReadOnlyList<string> won) => won.Count switch
    {
        1 => won[0],
        2 => $"{won[0]} and {won[1]}",
        _ => $"{won[0]}, {won[1]} and {won.Count - 2} more",
    };

    static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 3)] + "...";
}
