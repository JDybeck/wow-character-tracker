using System.Globalization;

namespace WoWCharacterTracker.Services;

// One faction from the addon's reputations text (Reputation.lua). Standing is the game's 1 (Hated) to
// 8 (Exalted); Retail renown factions have Renown (and RenownMax when known), friendships a Label
// ("Best Friend") with Rank and RankMax. Cur and Max: how far through the standing, rank or renown
// level, null when not known.
sealed record Reputation(int Id, string Name, int Standing, int? Cur, int? Max,
    int? Renown = null, int? RenownMax = null, int? Rank = null, int? RankMax = null, string? Label = null)
{
    // "Revered", "Renown 12" or "Best Friend".
    public string StandingText => Renown is { } r ? $"Renown {r}" : Label is { Length: > 0 } l ? l : Reputations.StandingName(Standing);

    // At the top: Exalted, the top renown. Nothing left to fill there.
    public bool AtTop => Renown is { } r ? RenownMax is { } m && r >= m : Label is null && Standing >= Reputations.Exalted;

    // How far through, 0 to 1 (1 at the top).
    public double Fraction => AtTop ? 1 : Max is > 0 && Cur is { } c ? Math.Clamp((double)c / Max.Value, 0, 1) : 0;

    // "2400 / 21000", "" at the top or when not known.
    public string ProgressText => AtTop || Max is not > 0 || Cur is null ? "" : $"{Cur} / {Max}";
}

// The one reader of the addon's reputations text: a line per faction, "id~standing~cur~max~extra~name",
// extra "R12/25" (renown), "F3/6:Best Friend" (a friendship) or empty. Bad lines are skipped.
static class Reputations
{
    public const int Exalted = 8;
    public static readonly string[] Standings = ["Hated", "Hostile", "Unfriendly", "Neutral", "Friendly", "Honored", "Revered", "Exalted"];

    // The milestone choices on the Sharing page: Friendly (5) to Exalted (8).
    public const int LowestStep = 5;

    public static string StandingName(int standing) => standing is >= 1 and <= 8 ? Standings[standing - 1] : "Neutral";

    public static IReadOnlyList<Reputation> Parse(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var list = new List<Reputation>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Split('~', 6);
            if (p.Length < 6 || !int.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0 || p[5].Trim().Length == 0) continue;
            var r = new Reputation(id, p[5].Trim(), Int(p[1]) ?? 4, Int(p[2]), Int(p[3]));
            var extra = p[4];
            if (extra.StartsWith('R'))
            {
                var (level, top) = Pair(extra[1..]);
                if (level is not null) r = r with { Renown = level, RenownMax = top };
            }
            else if (extra.StartsWith('F') && extra.IndexOf(':') is var colon and > 0)
            {
                var (rank, top) = Pair(extra[1..colon]);
                r = r with { Rank = rank, RankMax = top, Label = extra[(colon + 1)..] };
            }
            list.Add(r);
        }
        return list;
    }

    // Renown first, then the highest standing, then by name: what the character has worked on most.
    public static IReadOnlyList<Reputation> Sorted(IEnumerable<Reputation> list) =>
        [.. list.OrderByDescending(r => r.Renown is not null).ThenByDescending(r => r.Renown ?? 0)
            .ThenByDescending(r => r.Standing).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];

    static int? Int(string s) => int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) ? n : null;

    static (int?, int?) Pair(string s)
    {
        var slash = s.IndexOf('/');
        return slash < 0 ? (Int(s), null) : (Int(s[..slash]), Int(s[(slash + 1)..]));
    }
}
