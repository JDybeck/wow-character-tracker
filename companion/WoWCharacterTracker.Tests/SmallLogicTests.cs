namespace WoWCharacterTracker.Tests;

public class TalentLinkTests
{
    [Fact]
    public void Forever_link_has_wowheads_v2_prefix()
    {
        // The code Wowhead's own "Copy talents" gives for 16 points in Retribution.
        Assert.Equal("https://www.wowhead.com/forever/talent-calc/paladin/v2--05203021201",
            Wowhead.ForeverTalentUrl("PALADIN", "--05203021201"));
    }

    [Fact]
    public void No_code_opens_the_empty_calculator()
    {
        Assert.Equal("https://www.wowhead.com/forever/talent-calc/warrior", Wowhead.ForeverTalentUrl("WARRIOR", ""));
    }
}

public class OpponentTests
{
    [Theory]
    [InlineData("21~WARRIOR~Warrior~Orc", "level 21 Orc Warrior")]
    [InlineData("??~ROGUE~Rogue~Gnome", "level ?? Gnome Rogue")]
    [InlineData("~~~", "")]
    [InlineData("", "")]
    public void Opponent_text(string info, string expected)
    {
        Assert.Equal(expected, new Entry { OpponentInfo = info }.OpponentText);
    }

    [Fact]
    public void Opponent_info_comes_from_the_saved_field()
    {
        var fields = new Dictionary<string, string> { ["id"] = "x", ["kind"] = "duel", ["opponent"] = "Grimtusk", ["opponentInfo"] = "30~MAGE~Mage~Troll" };
        var e = Entry.From(k => fields.GetValueOrDefault(k, ""), _ => 0);
        Assert.Equal("level 30 Troll Mage", e.OpponentText);
        Assert.Equal("MAGE", e.OpponentClassFile);
    }
}

public class ItemKindTests
{
    [Theory]
    [InlineData(2, "gear")]
    [InlineData(4, "gear")]
    [InlineData(0, "consumable")]
    [InlineData(5, "trade")]
    [InlineData(7, "trade")]
    [InlineData(12, "quest")]
    [InlineData(15, "other")]
    [InlineData(-1, "other")] // not classed yet and no tooltip
    public void Item_class_picks_the_type_chip(int classId, string kind)
    {
        Assert.Equal(kind, new ItemInfo(1, "") { ClassId = classId }.Kind);
    }
}
