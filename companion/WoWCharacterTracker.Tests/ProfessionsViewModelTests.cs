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

    [Fact]
    public void Same_named_servers_in_other_regions_stay_apart_and_nothing_is_empty()
    {
        var vm = new ProfessionsViewModel();
        vm.Show(Rows(Char("Zeb", "Firemaw", "Orc", "WARRIOR", "Mining 1", region: "eu"), Char("Bob", "Firemaw", "Orc", "WARRIOR", "Mining 2", region: "us")));
        Assert.Equal(["EU · Firemaw, Horde", "US · Firemaw, Horde"], vm.Owners.Single().Realms.Select(r => r.Title));
        vm.Show(Rows(Char("Zeb", "Firemaw", "Orc", "WARRIOR", "")));
        Assert.True(vm.IsEmpty);
    }
}
