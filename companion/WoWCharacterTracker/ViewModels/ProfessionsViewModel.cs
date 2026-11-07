using System.Windows.Media;

namespace WoWCharacterTracker.ViewModels;

// Professions by server, then faction, then profession, each with its characters (Views/ProfessionsView).
// One for your own characters (the Professions page) and one for friends' (the Friends page), where
// ownerOf puts each friend's characters under their name first.
sealed class ProfessionsViewModel : Observable
{
    public IReadOnlyList<ProfessionOwner> Owners { get; private set; } = [];
    public bool IsEmpty => Owners.Count == 0;
    public string EmptyText { get; init; } = "";

    public void Show(IEnumerable<CharacterRow> rows, Func<CharacterRow, string>? ownerOf = null)
    {
        // One line per character and profession it has.
        var lines = rows.SelectMany(r => ProfessionSkills.Parse(r.Professions).Select(s => (Row: r, Skill: s))).ToList();
        Owners = [.. lines.GroupBy(l => ownerOf?.Invoke(l.Row) ?? "")
            .OrderBy(g => g.Key, Order)
            .Select(g => new ProfessionOwner(g.Key, Realms(g)))];
        Raise(nameof(Owners));
        Raise(nameof(IsEmpty));
    }

    static readonly StringComparer Order = StringComparer.CurrentCultureIgnoreCase;

    // Servers by name, Alliance before Horde.
    static List<ProfessionRealm> Realms(IEnumerable<(CharacterRow Row, ProfessionSkill Skill)> lines) => [.. lines
        .GroupBy(l => (Realm: l.Row.RealmText, l.Row.Faction))
        .OrderBy(g => g.Key.Realm, Order).ThenBy(g => FactionOrder(g.Key.Faction))
        .Select(g => new ProfessionRealm(g.Key.Realm, g.Key.Faction, Professions(g)))];

    // Professions by name, each with its characters, highest skill first.
    static List<ProfessionGroup> Professions(IEnumerable<(CharacterRow Row, ProfessionSkill Skill)> lines) => [.. lines
        .GroupBy(l => l.Skill.Name)
        .OrderBy(g => g.Key, Order)
        .Select(g => new ProfessionGroup(g.Key, ProfessionSkills.IconUrl(g.Key), [.. g
            .OrderByDescending(l => l.Skill.Skill ?? -1).ThenBy(l => l.Row.Name, Order)
            .Select(l => ProfessionCharacter.Of(l.Row, l.Skill))]))];

    static int FactionOrder(string faction) => faction switch { "Alliance" => 0, "Horde" => 1, _ => 2 };
}

// A friend (Name) or yourself ("", no heading) and their servers.
sealed record ProfessionOwner(string Name, IReadOnlyList<ProfessionRealm> Realms)
{
    public bool HasName => Name.Length > 0;
}

// One server and faction: "Firemaw, Horde" with the faction's crest.
sealed record ProfessionRealm(string Realm, string Faction, IReadOnlyList<ProfessionGroup> Professions)
{
    public string Title => Faction.Length > 0 ? $"{Realm}, {Faction}" : Realm;
    public string? FactionCrestUrl => Factions.CrestIcon(Faction) is { } icon ? Wowhead.IconUrl(icon, "small") : null;
}

sealed record ProfessionGroup(string Name, string? IconUrl, IReadOnlyList<ProfessionCharacter> Characters);

// One character's line in a profession: name in its class color, "150/225" and a bar to the max.
sealed record ProfessionCharacter(string Name, int Level, string Profession, string SkillText, bool HasMax, double Fraction, SolidColorBrush ClassBrush)
{
    public static ProfessionCharacter Of(CharacterRow row, ProfessionSkill skill) => new(row.Name, row.Level, skill.Name,
        skill.Skill is { } s ? (skill.Max is { } m ? $"{s}/{m}" : s.ToString()) : "?", skill.Max is > 0, skill.Fraction, row.ClassBrush);
}
