using WoWCharacterTracker.Services;

namespace WoWCharacterTracker.Tests;

// What a recipe needs and makes, from Wowhead's tooltip for its spell (the recipe window's right side).
public class RecipeDetailsTests
{
    // Wowhead's tooltip for Rough Bronze Shoulders (classic/tooltip/spell/3328), shortened.
    const string Shoulders = """
        <table><tr><td><a class="whtt-name q1" href="/classic/spell=3328/rough-bronze-shoulders"><b class="whtt-name q1">Rough Bronze Shoulders</b></a><br />12.5 sec cast</td></tr></table><table><tr><td>Tools:<br /><div class="indent q1"><a href="/classic/item=5956/blacksmith-hammer">Blacksmith Hammer</a></div>Reagents:<br /><div class="indent q1"><a href="/classic/item=2841/bronze-bar">Bronze Bar</a>&nbsp;(5), <a href="/classic/item=1210/shadowgem">Shadowgem</a>, <a href="/classic/item=3478/coarse-grinding-stone">Coarse Grinding Stone</a></div><br /><span class="q1"><a href="/classic/item=3480/rough-bronze-shoulders">Rough Bronze Shoulders</a></span><span class="q"><br>Item Level <!--ilvl-->22</span><br>Requires Level <!--rlvl-->17<br></td></tr></table>
        """;

    [Fact]
    public void Tools_reagents_with_counts_and_the_made_item_are_read()
    {
        var d = RecipeDetails.Parse(Shoulders);
        Assert.Equal(["Blacksmith Hammer"], d.Tools);
        Assert.Equal([new RecipeReagent(2841, "Bronze Bar", 5), new RecipeReagent(1210, "Shadowgem", 1), new RecipeReagent(3478, "Coarse Grinding Stone", 1)], d.Reagents);
        Assert.Equal(3480, d.MadeItemId);
    }

    [Fact]
    public void A_recipe_without_tools_or_a_made_item_reads_what_it_has()
    {
        var stone = RecipeDetails.Parse("""<table><tr><td>Reagents:<br /><div class="indent q1"><a href="/forever/item=2835/rough-stone">Rough Stone</a></div><br /><span class="q1"><a href="/forever/item=2862/rough-sharpening-stone">Rough Sharpening Stone</a></span></td></tr></table>""");
        Assert.Empty(stone.Tools);
        Assert.Equal(2835, Assert.Single(stone.Reagents).ItemId);
        Assert.Equal(2862, stone.MadeItemId);

        var enchant = RecipeDetails.Parse("""<table><tr><td>Reagents:<br /><div class="indent q1"><a href="/classic/item=10940/strange-dust">Strange Dust</a></div></td></tr></table><table><tr><td><span class="q">Permanently enchant boots to increase Agility by 1.</span></td></tr></table>""");
        Assert.Equal(0, enchant.MadeItemId);
        Assert.Same(RecipeDetails.Empty, RecipeDetails.Parse(null));
    }
}
