using WoWCharacterTracker.Ui;

namespace WoWCharacterTracker.Tests;

// The Professions view: your characters (or a friend's) by server, then faction, then profession.
public class ProfessionsViewModelTests
{
    static int next;

    static Snapshot Char(string name, string realm, string raceFile, string classFile, string professions, int level = 60, string region = "") =>
        new($"Player-1-{++next:X8}", name, realm, level, classFile, classFile, raceFile, raceFile, "male", "", "", 0, 0, "", professions, "", 5, false, "",
            Region: region);

    static List<CharacterRow> Rows(params Snapshot[] chars) =>
        CharacterRow.Build(new TrackerData([], [.. chars]), Flavors.Forever, new GameLookup(Flavors.Forever, () => { }));

    static string Describe(ProfessionsViewModel vm) => string.Join(" | ", vm.Owners.SelectMany(o => o.Realms.Select(r =>
        $"{(o.Name.Length > 0 ? o.Name + ": " : "")}{r.Title}: " + string.Join("; ", r.Professions.Select(p =>
            $"{p.Name} " + string.Join(",", p.Characters.Select(c => $"{c.Name} {c.SkillText}")))))));

    [Fact]
    public void Characters_group_by_server_then_faction_then_profession()
    {
        var vm = new ProfessionsViewModel();
        vm.Show(Rows(
            Char("Zeb", "Firemaw", "Orc", "WARRIOR", "Mining 150/225, Blacksmithing 140/225"),
            Char("Kalle", "Firemaw", "Orc", "ROGUE", "Mining 300/300"),
            Char("Lumi", "Firemaw", "Human", "PRIEST", "Tailoring 75"),
            Char("Brandur", "Ashfall", "Gnome", "MAGE", "Tailoring 200/225, Enchanting 190/225"),
            Char("Nobody", "Ashfall", "Gnome", "MAGE", "")));
        Assert.Equal("Ashfall, Alliance: Enchanting Brandur 190/225; Tailoring Brandur 200/225"
            + " | Firemaw, Alliance: Tailoring Lumi 75"
            + " | Firemaw, Horde: Blacksmithing Zeb 140/225; Mining Kalle 300/300,Zeb 150/225", Describe(vm));
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public void A_line_has_the_class_color_and_a_bar_when_the_max_is_known()
    {
        var vm = new ProfessionsViewModel();
        vm.Show(Rows(Char("Zeb", "Firemaw", "Orc", "WARRIOR", "Mining 150/300, Herbalism 75")));
        var lines = vm.Owners.Single().Realms.Single().Professions.SelectMany(p => p.Characters).ToList();
        var mining = lines.Single(l => l.Profession == "Mining");
        Assert.True(mining.HasMax);
        Assert.Equal(0.5, mining.Fraction, 3);
        Assert.Equal(ClassColors.Brush(ClassColors.For("WARRIOR")).Color, mining.ClassBrush.Color);
        Assert.False(lines.Single(l => l.Profession == "Herbalism").HasMax);
        Assert.NotNull(vm.Owners.Single().Realms.Single().Professions.Single(p => p.Name == "Mining").IconUrl);
    }

    // Friends: grouped by friend first (their nickname), each with their own servers.
    [Fact]
    public void Friends_characters_group_by_friend_first()
    {
        var vm = new ProfessionsViewModel();
        var rows = Rows(Char("Zeb", "Firemaw", "Orc", "WARRIOR", "Mining 150/225"), Char("Ann", "Firemaw", "Orc", "MAGE", "Tailoring 10/75"));
        var owners = new Dictionary<string, string> { ["Zeb"] = "Zebbe", ["Ann"] = "Annie" };
        vm.Show(rows, r => owners[r.Name]);
        Assert.Equal("Annie: Firemaw, Horde: Tailoring Ann 10/75 | Zebbe: Firemaw, Horde: Mining Zeb 150/225", Describe(vm));
    }

    // The Professions page: yours first under "You", then each friend's; Who shows only one side.
    [Fact]
    public void Yours_and_friends_show_together_or_one_side_at_a_time()
    {
        var vm = new ProfessionsViewModel { YoursEmptyText = "none of yours", FriendsEmptyText = "none of theirs" };
        var mine = Rows(Char("Zeb", "Firemaw", "Orc", "WARRIOR", "Mining 150/225"));
        var theirs = Rows(Char("Ann", "Firemaw", "Orc", "MAGE", "Tailoring 10/75"));
        foreach (var r in theirs) { r.IsFriend = true; r.FriendName = "Annie"; }
        vm.Show([.. theirs, .. mine], r => r.IsFriend ? r.FriendName : "");
        Assert.True(vm.HasBoth);
        Assert.Equal("You: Firemaw, Horde: Mining Zeb 150/225 | Annie: Firemaw, Horde: Tailoring Ann 10/75", Describe(vm));

        vm.Who = "friends";
        Assert.Equal("Annie: Firemaw, Horde: Tailoring Ann 10/75", Describe(vm));
        vm.Who = "you";
        Assert.Equal("Firemaw, Horde: Mining Zeb 150/225", Describe(vm)); // no heading on its own

        vm.Who = "friends";
        vm.Show(mine, r => ""); // no friends' left: the switch hides, so back to everyone's
        Assert.False(vm.HasBoth);
        Assert.Equal("all", vm.Who);
        Assert.Equal("Firemaw, Horde: Mining Zeb 150/225", Describe(vm));

        vm.Show(theirs, r => r.FriendName);
        vm.Who = "you";
        Assert.True(vm.IsEmpty);
        Assert.Equal("none of yours", vm.ShownEmptyText);
    }

    // Two friends with the same nickname keep their own headings (grouped by BattleTag).
    [Fact]
    public void Friends_with_the_same_nickname_stay_apart()
    {
        var vm = new ProfessionsViewModel();
        var rows = Rows(Char("Ann", "Firemaw", "Orc", "MAGE", "Tailoring 10/75"), Char("Bob", "Firemaw", "Orc", "ROGUE", "Mining 20/75"));
        rows[0].FriendTag = "Sam#1111"; rows[1].FriendTag = "Sam#2222";
        foreach (var r in rows) { r.IsFriend = true; r.FriendName = "Sam"; }
        vm.Show(rows, r => r.FriendName);
        Assert.Equal(["Sam", "Sam"], vm.Owners.Select(o => o.Name));
    }

    // Recipes show as a count on the character's line; only a line with recipes opens its window,
    // which lists them under their categories in the game's order and picks the first.
    [Fact]
    public void A_line_with_recipes_opens_them_by_category()
    {
        var vm = new ProfessionsViewModel();
        vm.Show(Rows(Char("Zeb", "Firemaw", "Orc", "WARRIOR", "Alchemy 80/150, Herbalism 75")
            with { Recipes = "Alchemy~Potions=2330,2330/Elixirs=2329,3170\nCooking~Meat=2538" }));
        var lines = vm.Owners.Single().Realms.Single().Professions.SelectMany(p => p.Characters).ToList();
        var alchemy = lines.Single(l => l.Profession == "Alchemy");
        Assert.Equal((3, "3 recipes"), (alchemy.RecipeCount, alchemy.RecipeCountText));
        Assert.True(vm.HasRecipes);

        vm.Open(lines.Single(l => l.Profession == "Herbalism"));
        Assert.False(vm.IsBookOpen);
        vm.Open(alchemy, 3170);
        var book = Assert.IsType<RecipeBook>(vm.Book);
        Assert.Equal(["Potions", "Elixirs"], book.Categories.Select(c => c.Name));
        Assert.Equal([2330], book.Categories[0].Recipes.Select(r => r.Info.ItemId));
        Assert.Equal(3170, book.Selected?.Info.ItemId);
        Assert.True(book.Selected!.IsSelected);
        book.SelectCommand.Execute(book.Categories[0].Recipes[0]);
        Assert.False(book.Categories[1].Recipes[1].IsSelected);

        vm.Query = "potion"; // a search closes the window
        Assert.False(vm.IsBookOpen);
        Assert.True(vm.ShowsHits);
    }

    [Fact]
    public void Same_named_servers_in_other_regions_stay_apart_and_nothing_is_empty()
    {
        var vm = new ProfessionsViewModel();
        vm.Show(Rows(Char("Zeb", "Firemaw", "Orc", "WARRIOR", "Mining 1", region: "eu"), Char("Bob", "Firemaw", "Orc", "WARRIOR", "Mining 2", region: "us")));
        Assert.Equal(["EU · Firemaw, Horde", "US · Firemaw, Horde"], vm.Owners.Single().Realms.Select(r => r.Title));
        vm.Show(Rows(Char("Zeb", "Firemaw", "Orc", "WARRIOR", "")));
        Assert.True(vm.IsEmpty);
    }

    // The character details' Recipes tab and the overview's profession chips.
    static CharacterRow WithRecipes(string professions, string recipes) => Rows(
        Char("Zeb", "Firemaw", "Orc", "WARRIOR", professions) with { Recipes = recipes }).Single();

    [Fact]
    public void A_profession_chip_says_how_many_recipes_and_opens_them_on_the_recipes_tab()
    {
        var row = WithRecipes("Blacksmithing 140/225, Mining 150/225", "Blacksmithing~Weapon Stones=2660,3115/Armor=3328");
        var smithing = row.RecipeProfessions.Single(p => p.Profession == "Blacksmithing");
        var mining = row.RecipeProfessions.Single(p => p.Profession == "Mining");
        Assert.Equal("Blacksmithing 140", smithing.Text);
        Assert.Equal("3 recipes", smithing.RecipeCountText);
        Assert.Contains("Click to see its 3 recipes", smithing.ChipTooltip);
        Assert.False(mining.HasRecipes);
        Assert.Contains("Open Mining once in game", mining.ChipTooltip);
        Assert.True(row.HasRecipes);

        row.OpenRecipesCommand.Execute(smithing);
        Assert.Equal("recipes", row.DetailTab);
        Assert.True(row.IsRecipesTab);
        Assert.Equal("Blacksmithing", row.RecipeBook!.Line.Profession);
        Assert.True(smithing.IsOpen);

        // A profession without recipes doesn't open (its chip can't be clicked anyway).
        row.DetailTab = "overview";
        row.OpenRecipesCommand.Execute(mining);
        Assert.Equal("overview", row.DetailTab);
        Assert.Equal("Blacksmithing", row.OpenRecipeProfession);
    }

    [Fact]
    public void The_recipes_tab_opens_on_the_first_profession_with_recipes_and_keeps_it_over_a_sync()
    {
        const string professions = "Alchemy 100/150, Herbalism 120/150, Mining 90/150";
        const string recipes = "Alchemy~Potions=2330/Elixirs=2329\nMining~Smelting=2657";
        var row = WithRecipes(professions, recipes);
        row.IsRecipesTab = true;
        Assert.Equal("Alchemy", row.OpenRecipeProfession);
        row.OpenRecipes("Mining");
        Assert.Equal("Mining", row.OpenRecipeProfession);

        var synced = WithRecipes(professions, recipes);
        synced.KeepTabFrom(row);
        Assert.Equal("recipes", synced.DetailTab);
        Assert.Equal("Mining", synced.OpenRecipeProfession);
        Assert.True(synced.RecipeProfessions.Single(p => p.Profession == "Mining").IsOpen);
        Assert.False(synced.RecipeProfessions.Single(p => p.Profession == "Alchemy").IsOpen);
    }

    [Fact]
    public void Without_recipes_the_tab_says_how_to_get_them()
    {
        var own = WithRecipes("Mining 90/150", "");
        own.IsRecipesTab = true;
        Assert.Null(own.RecipeBook);
        Assert.False(own.HasRecipes);
        Assert.Contains("Open a profession once in game", own.NoRecipesText);

        var friend = WithRecipes("Mining 90/150", "");
        friend.IsFriend = true;
        friend.FriendName = "Rasmus";
        Assert.Contains("once Rasmus opens a profession", friend.NoRecipesText);
        Assert.Contains("once Rasmus opens Mining", friend.RecipeProfessions.Single().ChipTooltip);
    }
}
