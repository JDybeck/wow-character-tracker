using System.Text.RegularExpressions;

namespace WoWCharacterTracker.Services;

// How many pieces each set bonus needs. The game's tooltip leaves the count out once a bonus is
// active ("Set: ..." instead of "(2) Set: ..."), so the counts come from Wowhead's tooltip of a piece
// of the same set, which always lists them ("(2) Set Blood: ...", "(4) Set Blood: ..."). Both list the
// bonuses in the same order, so they're matched by position.
static partial class SetBonuses
{
    // Wowhead's counts for the set, in order, for the spec (tier bonuses name one; other specs' go).
    public static List<int> Counts(string html, string setName, string spec)
    {
        var lines = TooltipHtml.Parse(html).Select(l => l.Text).ToList();
        var start = lines.FindIndex(l => l.Contains(setName) && SetHeader().IsMatch(l));
        if (start < 0) return [];
        var counts = new List<int>();
        foreach (var line in lines.Skip(start + 1))
        {
            var m = Bonus().Match(line);
            if (!m.Success) continue;
            var label = m.Groups["label"].Value.Trim();
            if (label.Length == 0 || spec.Length == 0 || label.Equals(spec, StringComparison.OrdinalIgnoreCase))
                counts.Add(int.Parse(m.Groups["need"].Value));
        }
        return counts;
    }

    // The pieces each of the game's bonuses needs: Wowhead's counts when they line up with the game's
    // bonuses one for one, else what the game said (0 for an active one).
    public static List<int> Needs(IReadOnlyList<int> game, IReadOnlyList<int> wowhead) =>
        wowhead.Count == game.Count && game.Count > 0 ? [.. wowhead] : [.. game];

    [GeneratedRegex(@"\(\d+/\d+\)\s*$")]
    private static partial Regex SetHeader();

    [GeneratedRegex(@"^\((?<need>\d+)\)\s*Set(?<label>[^:]*):")]
    private static partial Regex Bonus();
}
