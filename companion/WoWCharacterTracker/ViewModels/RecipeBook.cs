namespace WoWCharacterTracker.ViewModels;

// One character's recipes in a profession, laid out like the game's profession window
// (Views/RecipeBookView): the category headers and recipes on the left in the game's order, with a
// search, and the picked recipe on the right with its reagents and the item it makes.
sealed class RecipeBook : Observable
{
    public RecipeBook(ProfessionCharacter line, int? select = null)
    {
        Line = line;
        Categories = [.. line.Groups.Select(g => new RecipeCategory(g.Category,
            [.. g.Ids.Select(id => line.Row.Find("spell", id)).OfType<ItemInfo>().Select(i => new RecipeEntry(i, line.Row))]))];
        SelectCommand = new RelayCommand(p => { if (p is RecipeEntry e) Selected = e; });
        var all = Categories.SelectMany(c => c.Recipes).ToList();
        Selected = all.FirstOrDefault(e => e.Info.ItemId == select) ?? all.FirstOrDefault();
    }

    public ProfessionCharacter Line { get; }
    public IReadOnlyList<RecipeCategory> Categories { get; }
    public RelayCommand SelectCommand { get; }

    RecipeEntry? selected;
    public RecipeEntry? Selected
    {
        get => selected;
        set
        {
            if (selected == value) return;
            if (selected is not null) selected.IsSelected = false;
            selected = value;
            if (value is not null)
            {
                value.IsSelected = true;
                _ = value.LoadDetailsAsync();
            }
            Raise(nameof(Selected));
        }
    }

    // The window's own search: by recipe name, on the names Wowhead has given so far.
    string search = "";
    public string Search
    {
        get => search;
        set
        {
            if (!Set(ref search, value)) return;
            foreach (var c in Categories) c.Filter(value.Trim());
        }
    }
}

// A category header ("Weapon Stones") with its recipes; a click folds it, like the minus in game.
sealed class RecipeCategory : Observable
{
    public RecipeCategory(string name, IReadOnlyList<RecipeEntry> recipes)
    {
        Name = name.Length > 0 ? name : "Recipes";
        Recipes = recipes;
        shown = recipes;
        ToggleCommand = new RelayCommand(_ => IsOpen = !IsOpen);
    }

    public string Name { get; }
    public IReadOnlyList<RecipeEntry> Recipes { get; }
    IReadOnlyList<RecipeEntry> shown;
    public IReadOnlyList<RecipeEntry> Shown { get => shown; private set { Set(ref shown, value); Raise(nameof(IsVisible)); } }
    public bool IsVisible => Shown.Count > 0;
    bool isOpen = true;
    public bool IsOpen { get => isOpen; set => Set(ref isOpen, value); }
    public RelayCommand ToggleCommand { get; }

    public void Filter(string text) => Shown = text.Length == 0 ? Recipes
        : [.. Recipes.Where(r => r.Info.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase))];
}

// One recipe in the window. Its details (reagents, tools, what it makes) come from Wowhead's tooltip
// for the recipe (Services/RecipeDetails) the first time it's picked.
sealed class RecipeEntry(ItemInfo info, CharacterRow row) : Observable
{
    public ItemInfo Info { get; } = info;
    bool isSelected;
    public bool IsSelected { get => isSelected; set => Set(ref isSelected, value); }

    public IReadOnlyList<ReagentLine> Reagents { get; private set; } = [];
    public bool HasReagents => Reagents.Count > 0;
    public string ToolsText { get; private set; } = "";
    public bool HasTools => ToolsText.Length > 0;
    public ItemInfo? Made { get; private set; }
    public bool HasMade => Made is not null;
    // No details from Wowhead (offline, or a recipe it doesn't know): the recipe's own tooltip text.
    public bool ShowsTooltip => loaded && !HasReagents && !HasMade;
    bool loaded;

    Task? details;
    public Task LoadDetailsAsync() => details ??= Load();

    async Task Load()
    {
        await Info.WhenLoaded; // started by the tab's lookup (GameLookup.For)
        loaded = true;
        var parsed = RecipeDetails.Parse(Info.Html);
        Reagents = [.. parsed.Reagents.Select(r => new ReagentLine(row.Find("item", r.ItemId), r.Name, r.Count))];
        ToolsText = string.Join(", ", parsed.Tools);
        Made = parsed.MadeItemId > 0 ? row.Find("item", parsed.MadeItemId) : null;
        Raise(nameof(Reagents));
        Raise(nameof(HasReagents));
        Raise(nameof(ToolsText));
        Raise(nameof(HasTools));
        Raise(nameof(Made));
        Raise(nameof(HasMade));
        Raise(nameof(ShowsTooltip));
    }
}

// A reagent line: the item (its icon and tooltip), its name, and how many (in the icon's corner, as in bags).
sealed record ReagentLine(ItemInfo? Info, string Name, int Count)
{
    public string CountText => Count > 1 ? Count.ToString() : "";
}
