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

public class RatingTests
{
    [Theory]
    [InlineData(2850, "2~1850,7~2100", "Mythic+ 2850, 3v3 1850, Solo Shuffle 2100")]
    [InlineData(0, "9~1650,1~1720,4~1900", "2v2 1720, RBG 1900, Blitz 1650")] // the game's bracket order
    [InlineData(3100, "", "Mythic+ 3100")]
    [InlineData(0, "2~0,3~1500,x~5,7", "")] // zeros, unknown brackets and broken parts
    [InlineData(0, "", "")]
    public void Ratings_text(int score, string pvp, string expected) => Assert.Equal(expected, Format.RatingsText(score, pvp));

    [Fact]
    public void Every_rating_has_an_icon()
    {
        var all = Format.Ratings(1, "1~1,2~1,4~1,7~1,9~1");
        Assert.Equal(6, all.Count);
        Assert.All(all, r => Assert.True(Wowhead.RatingIcons.ContainsKey(r.Name), r.Name));
    }

    [Fact]
    public void Live_check_ins_carry_the_ratings()
    {
        var e = WoWCharacterTracker.Services.LiveChannel.Payload.Parse("i=:snapshot:1;g=1403-0A1B2C3D;k=snapshot;j=2850;pr=2~1850,7~2100")!;
        Assert.Equal((2850, "2~1850,7~2100"), (e.MplusScore, e.PvpRatings));
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

    [Theory]
    [InlineData("DEATHKNIGHT", "Frost", "spell_deathknight_frostpresence")]
    [InlineData("MAGE", "Frost", "spell_frost_frostbolt02")]
    [InlineData("DRUID", "Feral Combat", "ability_druid_catform")] // Forever's tree name
    [InlineData("DEMONHUNTER", "Devourer", "classicon_demonhunter_void")]
    [InlineData("WARRIOR", "", null)]
    [InlineData("WARRIOR", "Gladiator", null)] // unknown: the row falls back to the role icon
    public void Spec_icons_go_by_class_and_spec(string classFile, string spec, string? icon) =>
        Assert.Equal(icon, SpecIcons.Of(classFile, spec));

    // The set part of Wowhead's tooltip for a Retail tier piece and a weapon set piece, as Wowhead sends it.
    const string TierHtml = "<br /><br /><span class=\"q\"><a href=\"/item-set=2055/x\" class=\"q\">Baleful Grave-Knight's Crucible</a> (0/5)</span>"
        + "<div class=\"q0 indent\"><span><a href=\"/item=271477/x\">Baleful Grave-Knight's Breastplate</a></span><br /></div><br />"
        + "<span class=\"q0\"><span>(2) Set Blood: <a href=\"/spell=1\">When you Death Strike...</a></span><br />"
        + "<span>(2) Set Frost: <a href=\"/spell=2\">Each time Remorseless Winter...</a></span><br />"
        + "<span>(4) Set Blood: <a href=\"/spell=3\">When consuming Blood Debt...</a></span><br />"
        + "<span>(4) Set Frost: <a href=\"/spell=4\">Remorseless Winter deals...</a></span><br /></span>";
    const string WeaponSetHtml = "<span class=\"q\"><a href=\"/item-set=2070/x\" class=\"q\">Bite of Zul'jan</a> (0/3)</span>"
        + "<div class=\"q0 indent\"><span><a href=\"/item=270173/x\">Zul'jin's Guillotine Technique</a></span><br /></div><br />"
        + "<span class=\"q0\"><span>(2) Set : <a href=\"/spell=5\">Perfect your Guillotine technique...</a></span></span>";

    [Fact]
    public void Wowhead_gives_the_pieces_each_bonus_needs_for_the_spec()
    {
        Assert.Equal([2, 4], SetBonuses.Counts(TierHtml, "Baleful Grave-Knight's Crucible", "Blood"));
        Assert.Equal([2], SetBonuses.Counts(WeaponSetHtml, "Bite of Zul'jan", "Blood"));
        Assert.Empty(SetBonuses.Counts(WeaponSetHtml, "Baleful Grave-Knight's Crucible", "Blood")); // another set
    }

    [Fact]
    public void Active_bonuses_get_their_counts_by_position()
    {
        // The game: both active, so no counts shown (0), or an older scan's wrong guess (5, 5).
        Assert.Equal([2, 4], SetBonuses.Needs([0, 0], [2, 4]));
        Assert.Equal([2, 4], SetBonuses.Needs([5, 5], [2, 4]));
        Assert.Equal([2], SetBonuses.Needs([3], [2]));
        Assert.Equal([0, 4], SetBonuses.Needs([0, 4], [2])); // they don't line up: keep the game's
    }

    [Fact]
    public void Saved_set_lines_group_into_sets_with_active_bonuses()
    {
        var set = Assert.Single(Inventory.ParseSets("Battlegear~3~5~4~Four.\nBattlegear~3~5~2~Two, with a ~ in it.\nbroken line"));
        Assert.Equal(("Battlegear", 3, 5), (set.Name, set.Have, set.Max));
        Assert.Equal([4, 2], set.Bonuses.Select(b => b.Need)); // as saved: the game's order
        Assert.Equal("Two, with a ~ in it.", set.Bonuses[1].Text);
        Assert.Equal([false, true], set.Bonuses.Select(b => b.IsActive(set.Have)));
        Assert.True(new SetBonus(0, "active, count not shown").IsActive(1));
        Assert.Equal(new GearEffect(7, 271473, "Equip: Something."), Assert.Single(Inventory.ParseEffects("7~271473~Equip: Something.\nx~y~z")));
    }

    [Fact]
    public void Tooltip_lines_lose_grammar_codes_and_get_their_line_breaks_back()
    {
        Assert.Equal("Use: Bite for 20 sec.\nWhile the hunger persists. (1 Min 30 Sec Cooldown)",
            Format.TooltipLine("Use: Bite for 20 sec.    While the hunger persists. (1 |4Min:Min; 30 |4Sec:Sec; Cooldown)"));
        Assert.Equal("2 charges, 1 charge", Format.TooltipLine("2 |4charge:charges;, 1 |4charge:charges;"));
    }

    [Fact]
    public void Effects_of_one_item_share_a_row()
    {
        var inv = new Inventory("g", [new ItemSlot(Inventory.Equipped, 13, 5, 1)], [], new Dictionary<int, int>(), [], new Dictionary<int, int>(), 1, 0)
        {
            Effects = Inventory.ParseEffects("13~5~Equip: One.\n13~5~Use: Two.\n14~6~Equip: Three."),
            Enchants = Inventory.ParseEffects("13~5~Enchant Ring - Nature's Fury"),
        };
        var gear = CharacterGear.From(inv, (type, id) => new ItemInfo(id, "", type), Flavors.Retail, "WARRIOR");
        Assert.Equal(["Equip: One.\nUse: Two.", "Equip: Three."], gear.Effects.Select(e => e.Text));
        Assert.False(gear.ShowsCatalystNote); // no sets
        // Enchants go under the item in its slot, not in a list of their own.
        Assert.Equal("Nature's Fury", gear.Right.Single(c => c.Item?.ItemId == 5).Enchant?.Text);
        Assert.All(gear.Left, c => Assert.False(c.HasEnchant));
    }

    [Fact]
    public void Enchants_are_found_on_Wowhead_by_their_exact_name()
    {
        const string json = """
            {"results":[
              {"type":3,"id":256752,"name":"Formula: Enchant Ring - Nature's Fury"},
              {"type":6,"id":1236080,"name":"Enchant Ring - Nature's Fury"},
              {"type":3,"id":243987,"name":"Enchant Ring - Nature's Fury"}]}
            """;
        Assert.Equal(("item", 243987), Wowhead.PickByName(json, "Enchant Ring - Nature's Fury")); // the scroll beats the spell
        Assert.Equal(("spell", 326805), Wowhead.PickByName("""{"results":[{"type":6,"id":326805,"name":"Rune of Sanguination"}]}""", "Rune of Sanguination"));
        Assert.Null(Wowhead.PickByName("""{"results":[{"type":3,"id":1,"name":"Something else"}]}""", "+4 All Stats"));
        Assert.Null(Wowhead.PickByName("""{"search":"x"}""", "x"));
    }

    [Theory]
    [InlineData("Enchant Helm - Empowered Blessing of Speed", "Empowered Blessing of Speed")]
    [InlineData("Enchant Weapon - Rune of Sanguination - extra", "Rune of Sanguination - extra")]
    [InlineData("+41 Agility/Strength & +115 Stamina", "+41 Agility/Strength & +115 Stamina")]
    [InlineData("Rune of Sanguination", "Rune of Sanguination")]
    public void Enchant_names_lose_the_slot_prefix(string saved, string shown) => Assert.Equal(shown, Format.EnchantName(saved));
}

public class PlaceTests
{
    [Theory]
    [InlineData("The Deadmines", "dungeon", "Dungeon")]
    [InlineData("Molten Core", "raid", "Raid")]
    [InlineData("Warsong Gulch", "pvp", "Battleground")]
    [InlineData("Ironforge", "rest", "City")]
    [InlineData("Orgrimmar", "", "City")]          // from before zone types: capitals are still known
    [InlineData("Goldshire", "rest", "Town")]
    [InlineData("Elwynn Forest", "zone", "Zone")]
    public void Zone_kind(string zone, string type, string expected)
    {
        Assert.Equal(expected, Places.Of(zone, type)?.Name);
    }

    [Fact]
    public void Unknown_type_has_no_kind()
    {
        Assert.Null(Places.Of("Elwynn Forest", ""));
    }

    [Fact]
    public void Every_stat_the_addon_sends_has_an_icon()
    {
        foreach (var stat in new[] { "Strength", "Agility", "Stamina", "Intellect", "Spirit", "Armor", "Attack Power", "Spell Power", "Crit", "Haste", "Mastery", "Versatility" })
            Assert.True(Wowhead.StatIcons.ContainsKey(stat), stat);
    }
}
