using System.Globalization;

namespace WoWCharacterTracker.Services;

// One of a profession's category headers in game ("Weapon Stones") and its recipe (spell) ids, in order.
sealed record RecipeGroup(string Category, IReadOnlyList<int> Ids);

// One profession from the addon's text: its skill and max, null when not known.
sealed record ProfessionSkill(string Name, int? Skill, int? Max)
{
    // What other people see (posts, the card): "Mining 150", as before the max was saved.
    public string Text => Skill is { } s ? $"{Name} {s}" : Name;
    // "Mining 150/225" when the max is known.
    public string FullText => Max is { } m && Skill is { } s ? $"{Name} {s}/{m}" : Text;
    // How far to the max, 0 to 1 (0 without a max).
    public double Fraction => Max is > 0 && Skill is { } s ? Math.Clamp((double)s / Max.Value, 0, 1) : 0;
}

// The one reader of the addon's professions text: "Mining 150/225, Herbalism 75" (older addons
// left out the max, "None" or "" means none).
static class ProfessionSkills
{
    public static IReadOnlyList<ProfessionSkill> Parse(string text)
    {
        if (text is "" or "None") return [];
        return [.. text.Split(", ", StringSplitOptions.RemoveEmptyEntries).Select(One)];
    }

    // The addon's recipes text (Recipes.lua), one profession per line under the game's category headers:
    // "Blacksmithing~Camping=2660/Reagents=3115,3116". The known recipe (spell) ids per profession, in
    // the game's order; ids without a category ("Alchemy~2329,2330") get "". Bad parts are skipped,
    // each id once.
    public static IReadOnlyDictionary<string, IReadOnlyList<RecipeGroup>> Recipes(string text)
    {
        var recipes = new Dictionary<string, IReadOnlyList<RecipeGroup>>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var tilde = line.IndexOf('~');
            if (tilde <= 0 || line[..tilde].Trim().Length == 0) continue;
            var seen = new HashSet<int>();
            List<RecipeGroup> groups = [];
            foreach (var part in line[(tilde + 1)..].Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = part.IndexOf('=');
                var category = eq >= 0 ? part[..eq].Trim() : "";
                List<int> ids = [.. part[(eq + 1)..].Split(',')
                    .Select(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0)
                    .Where(id => id > 0 && seen.Add(id))];
                if (ids.Count > 0) groups.Add(new(category, ids));
            }
            if (groups.Count > 0) recipes[line[..tilde].Trim()] = groups;
        }
        return recipes;
    }

    // The profession's small Wowhead icon, null for one we don't know.
    public static string? IconUrl(string name) => Wowhead.ProfessionIcons.TryGetValue(name, out var icon) ? Wowhead.IconUrl(icon, "small") : null;

    static ProfessionSkill One(string part)
    {
        var space = part.LastIndexOf(' ');
        if (space <= 0) return new(part, null, null);
        var numbers = part[(space + 1)..].Split('/');
        if (numbers.Length > 2 || !int.TryParse(numbers[0], out var skill)) return new(part, null, null);
        int? max = null;
        if (numbers.Length == 2)
        {
            if (!int.TryParse(numbers[1], out var m)) return new(part, null, null);
            if (m > 0) max = m; // 0: the game gave none
        }
        return new(part[..space], skill, max);
    }
}
