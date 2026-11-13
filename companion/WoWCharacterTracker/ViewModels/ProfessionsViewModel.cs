using System.Windows.Media;

namespace WoWCharacterTracker.ViewModels;

// Professions by server, then faction, then profession, each with its characters (Views/ProfessionsView).
// One for your own characters (the Professions page) and one for friends' (the Friends page), where
// ownerOf puts each friend's characters under their name first. A character's recipes open in a
// window like the game's (Book), and the search finds who knows a recipe.
sealed class ProfessionsViewModel : Observable
{
    public IReadOnlyList<ProfessionOwner> Owners { get; private set; } = [];
    public bool IsEmpty => Owners.Count == 0;
    public string EmptyText { get; init; } = "";
    List<ProfessionCharacter> lines = [];

    public ProfessionsViewModel()
    {
        CloseBookCommand = new RelayCommand(_ => Book = null);
        OpenCommand = new RelayCommand(p => { if (p is ProfessionCharacter line) Open(line); });
        OpenHitCommand = new RelayCommand(p => { if (p is RecipeHitKnower k) Open(k.Line, k.RecipeId); });
        ClearSearchCommand = new RelayCommand(_ => Query = "");
    }

    public void Show(IEnumerable<CharacterRow> rows, Func<CharacterRow, string>? ownerOf = null)
    {
        // One line per character and profession it has.
        lines = [.. rows.SelectMany(r => ProfessionSkills.Parse(r.Professions).Select(s => ProfessionCharacter.Of(r, s, ownerOf?.Invoke(r) ?? "")))];
        Owners = [.. lines.GroupBy(l => l.Owner)
            .OrderBy(g => g.Key, Order)
            .Select(g => new ProfessionOwner(g.Key, Realms(g)))];
        Raise(nameof(Owners));
        Raise(nameof(IsEmpty));
        Raise(nameof(HasRecipes));
        if (IsSearching) _ = SearchAsync();
    }

    static readonly StringComparer Order = StringComparer.CurrentCultureIgnoreCase;

    // Servers by name, Alliance before Horde.
    static List<ProfessionRealm> Realms(IEnumerable<ProfessionCharacter> lines) => [.. lines
        .GroupBy(l => (Realm: l.Row.RealmText, l.Row.Faction))
        .OrderBy(g => g.Key.Realm, Order).ThenBy(g => FactionOrder(g.Key.Faction))
        .Select(g => new ProfessionRealm(g.Key.Realm, g.Key.Faction, Professions(g)))];

    // Professions by name, each with its characters, highest skill first.
    static List<ProfessionGroup> Professions(IEnumerable<ProfessionCharacter> lines) => [.. lines
        .GroupBy(l => l.Profession)
        .OrderBy(g => g.Key, Order)
        .Select(g => new ProfessionGroup(g.Key, ProfessionSkills.IconUrl(g.Key), [.. g
            .OrderByDescending(l => l.Skill ?? -1).ThenBy(l => l.Name, Order)]))];

    static int FactionOrder(string faction) => faction switch { "Alliance" => 0, "Horde" => 1, _ => 2 };

    // The recipe window for one character's profession, null when the cards show.
    RecipeBook? book;
    public RecipeBook? Book
    {
        get => book;
        private set
        {
            if (!Set(ref book, value)) return;
            Raise(nameof(IsBookOpen));
            Raise(nameof(ShowsCards));
            Raise(nameof(ShowsHits));
        }
    }
    public bool IsBookOpen => Book is not null;
    public RelayCommand OpenCommand { get; }
    public RelayCommand CloseBookCommand { get; }

    public void Open(ProfessionCharacter line, int? recipeId = null)
    {
        if (line.HasRecipes) Book = new RecipeBook(line, recipeId);
    }

    // Finding who knows a recipe: every known recipe across the characters shown, by name. Names come
    // from Wowhead (kept on disk), so a first search looks them all up and fills in as they arrive.
    string query = "";
    public string Query
    {
        get => query;
        set
        {
            if (!Set(ref query, value)) return;
            if (IsSearching) Book = null;
            Raise(nameof(IsSearching));
            Raise(nameof(ShowsCards));
            Raise(nameof(ShowsHits));
            _ = SearchAsync();
        }
    }
    public bool IsSearching => Query.Trim().Length > 0;
    public bool ShowsCards => !IsSearching && Book is null;
    public bool ShowsHits => IsSearching && Book is null;
    public bool HasRecipes => lines.Any(l => l.HasRecipes);
    public RelayCommand ClearSearchCommand { get; }
    public RelayCommand OpenHitCommand { get; }

    IReadOnlyList<RecipeHit> hits = [];
    public IReadOnlyList<RecipeHit> Hits { get => hits; private set { Set(ref hits, value); Raise(nameof(HitsText)); } }
    bool lookingUp;
    public string HitsText => Hits.Count switch
    {
        0 when lookingUp => "Looking up recipe names...",
        0 => HasRecipes ? "Nobody knows a recipe like that." : "No recipes yet. Open a profession in game once and its recipes show up here.",
        1 => "1 recipe",
        _ => Hits.Count >= MaxHits ? $"The first {MaxHits} recipes" : $"{Hits.Count} recipes",
    };
    const int MaxHits = 100;
    int searchRun;

    async Task SearchAsync()
    {
        var run = ++searchRun;
        var text = Query.Trim();
        if (text.Length == 0) { Hits = []; return; }
        var known = lines.SelectMany(l => l.RecipeIds.Select(id => (Line: l, Info: l.Row.Find("spell", id))))
            .Where(k => k.Info is not null).Select(k => (k.Line, Info: k.Info!)).ToList();
        void Update() => Hits = [.. known.Where(k => k.Info.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase))
            .GroupBy(k => k.Info)
            .OrderBy(g => g.Key.Name, Order)
            .Take(MaxHits)
            .Select(g => new RecipeHit(g.Key, [.. g.Select(k => new RecipeHitKnower(k.Line, g.Key.ItemId))
                .OrderByDescending(k => k.Line.Skill ?? -1).ThenBy(k => k.Line.Name, Order)]))];
        var waiting = known.Where(k => k.Info.Name.Length == 0).Select(k => k.Info.WhenLoaded).ToList();
        lookingUp = waiting.Count > 0;
        Update();
        if (!lookingUp) return;
        await Task.WhenAll(waiting);
        if (run != searchRun) return;
        lookingUp = false;
        Update();
    }
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

// One character's line in a profession: name in its class color, "150/225", a bar to the max, and how
// many recipes it knows (once its profession window has been opened in game). Owner: the friend's
// name on the Friends page, "" for your own.
sealed class ProfessionCharacter
{
    ProfessionCharacter(CharacterRow row, ProfessionSkill skill, string owner)
    {
        Row = row;
        Owner = owner;
        Profession = skill.Name;
        Skill = skill.Skill;
        SkillText = skill.Skill is { } s ? (skill.Max is { } m ? $"{s}/{m}" : s.ToString()) : "?";
        HasMax = skill.Max is > 0;
        Fraction = skill.Fraction;
        Groups = row.RecipesOf(skill.Name);
        RecipeIds = [.. Groups.SelectMany(g => g.Ids)];
    }

    public static ProfessionCharacter Of(CharacterRow row, ProfessionSkill skill, string owner = "") => new(row, skill, owner);

    public CharacterRow Row { get; }
    public string Owner { get; }
    public string Name => Row.Name;
    public int Level => Row.Level;
    public SolidColorBrush ClassBrush => Row.ClassBrush;
    public string Profession { get; }
    public int? Skill { get; }
    public string SkillText { get; }
    public bool HasMax { get; }
    public double Fraction { get; }
    public string? ProfessionIconUrl => ProfessionSkills.IconUrl(Profession);

    public IReadOnlyList<RecipeGroup> Groups { get; }
    public IReadOnlyList<int> RecipeIds { get; }
    public int RecipeCount => RecipeIds.Count;
    public bool HasRecipes => RecipeIds.Count > 0;
    public string RecipeCountText => RecipeIds.Count == 1 ? "1 recipe" : $"{RecipeIds.Count} recipes";
}

// A search hit: a recipe and the characters who know it.
sealed record RecipeHit(ItemInfo Recipe, IReadOnlyList<RecipeHitKnower> Knowers);

// One character who knows a hit's recipe; a click opens their recipe window on it.
sealed record RecipeHitKnower(ProfessionCharacter Line, int RecipeId)
{
    public string OwnerText => Line.Owner.Length > 0 ? $"({Line.Owner})" : "";
}
