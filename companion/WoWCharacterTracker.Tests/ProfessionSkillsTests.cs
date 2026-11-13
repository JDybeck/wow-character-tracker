using WoWCharacterTracker.Services;

namespace WoWCharacterTracker.Tests;

// The addon's professions text ("Mining 150/225, Herbalism 75") and everything that reads it.
public class ProfessionSkillsTests
{
    [Fact]
    public void Skill_and_max_are_read_and_older_text_still_parses()
    {
        Assert.Equal([new ProfessionSkill("Mining", 150, 225), new ProfessionSkill("Herbalism", 75, null), new ProfessionSkill("First Aid", null, null)],
            ProfessionSkills.Parse("Mining 150/225, Herbalism 75, First Aid"));
        Assert.Empty(ProfessionSkills.Parse(""));
        Assert.Empty(ProfessionSkills.Parse("None"));
        Assert.Equal([new ProfessionSkill("Mining", 0, null)], ProfessionSkills.Parse("Mining 0/0")); // the game hid it
        Assert.Equal([new ProfessionSkill("Mining x/y", null, null)], ProfessionSkills.Parse("Mining x/y"));
    }

    [Fact]
    public void Recipes_are_read_per_profession_and_bad_parts_skipped()
    {
        var recipes = ProfessionSkills.Recipes("Alchemy~Potions=2330,2329,x,-5,0/Elixirs=2329,3170/Empty=\nbroken line\nCooking~\nTailoring~3915");
        Assert.Equal(["Alchemy", "Tailoring"], recipes.Keys.Order());
        Assert.Equal([("Potions", new[] { 2330, 2329 }), ("Elixirs", [3170])], recipes["Alchemy"].Select(g => (g.Category, g.Ids.ToArray())));
        var plain = Assert.Single(recipes["Tailoring"]); // the first format, without categories
        Assert.Equal(("", 3915), (plain.Category, plain.Ids.Single()));
        Assert.Empty(ProfessionSkills.Recipes(""));
    }

    [Fact]
    public void Text_for_other_people_keeps_the_old_wording()
    {
        var mining = new ProfessionSkill("Mining", 150, 225);
        Assert.Equal("Mining 150", mining.Text);
        Assert.Equal("Mining 150/225", mining.FullText);
        Assert.Equal("Herbalism", new ProfessionSkill("Herbalism", null, null).FullText);
        Assert.Equal(0.5, new ProfessionSkill("Mining", 150, 300).Fraction, 3);
        Assert.Equal(0, new ProfessionSkill("Mining", 150, null).Fraction);
    }

    [Fact]
    public void Discord_shows_skill_without_max_with_emoji()
    {
        Assert.Equal("⛏️ Mining 150, 🌿 Herbalism 75, Weird 3", Format.ProfessionsWithEmoji("Mining 150/225, Herbalism 75, Weird 3/9"));
        Assert.Equal("⛏️ Mining", Format.ProfessionsWithEmoji("Mining")); // a milestone's profession name
        Assert.Equal("", Format.ProfessionsWithEmoji("None"));
    }

    [Fact]
    public void The_leveling_tab_reads_skill_from_the_new_text()
    {
        var events = new List<Entry>
        {
            new() { Kind = "level", Guid = "Player-1-0000000A", Char = "Zeb", Realm = "Firemaw", Level = 20, At = 100, Professions = "Mining 75/150" },
            new() { Kind = "level", Guid = "Player-1-0000000A", Char = "Zeb", Realm = "Firemaw", Level = 30, At = 200, Professions = "Mining 210/225" },
        };
        var leveling = CharacterLeveling.From(events, 30, null, new Dictionary<int, long>(), estimate: false);
        Assert.Contains("Mining 75 to 210", leveling.Progress);
    }

    [Fact]
    public void Profession_chips_show_the_skill_and_tell_the_max_on_hover()
    {
        var snap = new Snapshot("Player-1-0000000A", "Zeb", "Firemaw", 30, "Warrior", "WARRIOR", "Orc", "Orc", "male", "", "", 0, 0, "",
            "Mining 150/225, Herbalism 75", "", 5, false, "");
        var row = CharacterRow.Build(new TrackerData([], [snap]), Flavors.Forever, new GameLookup(Flavors.Forever, () => { })).Single();
        _ = row.LoadIconsAsync(Flavors.Forever); // the chips are made before its first await
        Assert.Equal(["Mining 150", "Herbalism 75"], row.ProfessionList.Select(c => c.Text));
        Assert.Equal("Mining 150/225", row.ProfessionList[0].Tooltip);
    }
}
