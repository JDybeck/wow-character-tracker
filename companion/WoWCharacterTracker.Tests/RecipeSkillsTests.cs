using MoonSharp.Interpreter;

namespace WoWCharacterTracker.Tests;

// The skill a recipe needs: the addon's RecipeSkills.lua, read by the addon and by the app.
public class RecipeSkillsTests
{
    static string Lua => File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, "RecipeSkills.lua"));

    [Fact]
    public void The_list_is_read_by_profession_and_bad_entries_are_skipped()
    {
        var skills = RecipeSkills.Parse("""
            ns.RECIPE_SKILLS = {
              ["Blacksmithing"] = {
                [9993]=185, [8768]=125, [12]=0,
              },
              ["First Aid"] = { [3276]=40 },
            }
            """);
        Assert.Equal(("Blacksmithing", 185), skills[9993]);
        Assert.Equal(("Blacksmithing", 125), skills[8768]);
        Assert.Equal(("First Aid", 40), skills[3276]);
        Assert.False(skills.ContainsKey(12)); // 0 says nothing
        Assert.Empty(RecipeSkills.Parse(""));
    }

    // The app reads the addon it ships: Forever has the list, Retail (other skill lines) doesn't.
    [Fact]
    public void Forever_recipes_have_their_skill_and_retail_has_none()
    {
        Assert.Equal(("Blacksmithing", 185), RecipeSkills.For(Flavors.Forever, 9993)); // Heavy Mithril Axe
        Assert.Equal(("Blacksmithing", 125), RecipeSkills.For(Flavors.Forever, 8768)); // Iron Buckle
        Assert.Null(RecipeSkills.For(Flavors.Forever, 1));
        Assert.Null(RecipeSkills.For(Flavors.Retail, 9993));
        Assert.True(RecipeSkills.Parse(Lua).Count > 1500);
    }

    // The addon's lookup: the skill and profession, nothing when unknown or on Retail.
    [Fact]
    public void The_addon_finds_the_skill_on_classic_clients_only()
    {
        var script = new Script();
        var ns = new Table(script);
        script.Call(script.LoadString(Lua, codeFriendlyName: "RecipeSkills.lua"), "WoWCharacterTracker", ns);
        script.Globals["ns"] = ns;
        var found = script.DoString("return ns.recipeSkill(9993)");
        Assert.Equal(185, found.Tuple[0].Number);
        Assert.Equal("Blacksmithing", found.Tuple[1].String);
        Assert.True(script.DoString("return ns.recipeSkill(1)").IsNil());
        script.DoString("WOW_PROJECT_MAINLINE = 1 WOW_PROJECT_ID = 1");
        Assert.True(script.DoString("return ns.recipeSkill(9993)").IsNil());
    }
}
