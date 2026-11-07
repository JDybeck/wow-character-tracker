namespace WoWCharacterTracker.Services;

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
