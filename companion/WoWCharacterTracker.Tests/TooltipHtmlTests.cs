namespace WoWCharacterTracker.Tests;

public class TooltipHtmlTests
{
    // Wowhead's tooltip for Verigan's Fist (Forever), as the app caches it: rows split over raw line
    // breaks, two-column rows, a class link and coin spans.
    const string VerigansFist = """
<table><tr><td><!--nstart--><b class="q3">Verigan's Fist</b><!--nend--><!--ndstart--><!--ndend--><span class="q"><br>Item Level <!--ilvl-->31</span><!--bo--><br>Binds when picked up<!--ue--><!--ue--><table width="100%"><tr><td>Two-Hand</td><th><!--scstart2:5--><span class="q1">Mace</span><!--scend--></th></tr></table><!--rf--><table width="100%"><tr>
    <td><span><!--dmg-->65 - 99 Damage</span></td>
    <th>Speed <!--spd-->3.20</th>
</tr></table><!--dps-->(25.63 damage per second)<br><span><!--stat7-->+5 Stamina</span><br><span><!--stat5-->+6 Intellect</span><br><span><!--stat6-->+10 Spirit</span><!--ebstats--><!--egstats--><!--eistats--><!--nameDescStats--><!--rs--><!--e--><!--ps--><br>Durability 100 / 100<div class="wowhead-tooltip-item-classes">Classes: <a href="/forever/class=2/paladin" class="c2">Paladin</a></div></td></tr></table><table><tr><td><!--rr--><!--useText:0:1--><span id="useText1" class="q2">Chance on hit: <!--useEffect:0:1--><a href="/forever/spell=1322218/holy-forgefire" class="q2">Ignite the target in the flames of the Great Forge, dealing 39 to 59 Holyfire damage. Deals 3 times as much damage to Fire Elementals and Earth Elementals.</a><!--useEffect:1--></span><!--useText:1--><!--itemEffects:1--><br><!--pvpEquip--><!--pvpEquip--><div class="whtt-sellprice">Sell Price: <span class="moneysilver">74</span> <span class="moneycopper">59</span></div></td></tr></table>
""";

    [Fact]
    public void Lines_follow_the_tooltip_rows()
    {
        var text = TooltipHtml.ToText(VerigansFist).Split('\n');
        Assert.Equal("Verigan's Fist", text[0]);
        Assert.Equal("Item Level 31", text[1]);
        Assert.Equal("Binds when picked up", text[2]);
        Assert.Equal("Two-Hand   Mace", text[3]);
        Assert.Equal("65 - 99 Damage   Speed 3.20", text[4]);
        Assert.Contains("Classes: Paladin", text);
        Assert.Equal("Sell Price: 74s 59c", text[^1]);
    }

    [Fact]
    public void Two_column_rows_have_a_right_side()
    {
        var row = TooltipHtml.Parse(VerigansFist).Single(l => l.Text.StartsWith("Two-Hand"));
        Assert.Equal("Two-Hand", string.Concat(row.Left.Select(p => p.Text)));
        Assert.Equal("Mace", string.Concat(row.Right.Select(p => p.Text)));
    }

    [Fact]
    public void Parts_carry_wowheads_colors()
    {
        var lines = TooltipHtml.Parse(VerigansFist);
        Assert.Equal("q3", lines[0].Left[0].Color);                                  // rare item name
        Assert.Equal("q", lines[1].Left[0].Color);                                   // yellow item level
        Assert.Contains(lines.SelectMany(l => l.Left), p => p.Text == "Paladin" && p.Color == "c2");
        Assert.Contains(lines.SelectMany(l => l.Left), p => p.Text.StartsWith("Chance on hit") && p.Color == "q2");
        Assert.Contains(lines.SelectMany(l => l.Left), p => p.Text == "74s" && p.Color == "moneysilver");
    }

    [Fact]
    public void Wowheads_extended_info_is_left_out()
    {
        const string html = "<table><tr><td><b class=\"q1\">Oil</b><div class=\"whtt-extra whtt-maxstack\">Max Stack: 20</div><div class=\"whtt-sellprice\">Sell Price: <span class=\"moneygold\">1</span> <span class=\"moneysilver\">6</span></div></td></tr></table>";
        Assert.Equal("Oil\nSell Price: 1g 6s", TooltipHtml.ToText(html));
    }

    // A crest as Wowhead sends it: the description has its own line breaks, the cap is left out.
    const string Mistcrest = "<table><tr><td><b>Hero Mistcrest</b><br><div style=\"max-width: 300px\" class=\"q\">Used to upgrade Hero equipment.  \n\n"
        + "When used for crafting, sets the item level.\n\nEarned from the following activities:\n- Heroic The Venomous Abyss\n- Delves (Tier 11)</div>"
        + "<br><span class=\"q\">Total cap: </span>100<br></td></tr></table>";

    [Fact]
    public void Line_breaks_inside_text_are_kept_and_the_cap_goes()
    {
        var lines = TooltipHtml.Parse(Mistcrest);
        Assert.Equal(["Hero Mistcrest", "Used to upgrade Hero equipment.", "When used for crafting, sets the item level.",
            "Earned from the following activities:", "- Heroic The Venomous Abyss", "- Delves (Tier 11)"], lines.Select(l => l.Text));
        Assert.Equal([false, false, true, true, false, false], lines.Select(l => l.Gap));
        Assert.All(lines.Skip(1), l => Assert.Equal("q", l.Left[0].Color));
    }
}