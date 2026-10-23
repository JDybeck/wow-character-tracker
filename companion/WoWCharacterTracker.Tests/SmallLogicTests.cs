using WoWCharacterTracker.Services.Discord;

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

public class MountPostTests
{
    static readonly Entry Learned = new() { Kind = "mount", Char = "Snubbe", Mount = "Brown Horse", SpellId = 458, Level = 20 };

    // A mount from an item is known by the item, one learned straight into the journal by its spell.
    [Fact]
    public void Mount_is_known_by_its_item_else_its_spell()
    {
        Assert.Equal(("item", 5864), EventEmbeds.WowheadThing(Learned with { ItemId = 5864 }));
        Assert.Equal(("spell", 458), EventEmbeds.WowheadThing(Learned));
        Assert.Null(EventEmbeds.WowheadThing(Learned with { SpellId = 0 }));
        Assert.Null(EventEmbeds.WowheadThing(Learned with { Kind = "level" }));
    }

    [Fact]
    public void Learned_mount_links_its_spell_and_leaves_the_name_to_the_picture()
    {
        var embeds = new EventEmbeds(Flavors.Retail);
        Assert.Equal("https://www.wowhead.com/spell=458", embeds.Build(Learned, isTest: false)["url"]);
        Assert.Contains("[Brown Horse](https://www.wowhead.com/spell=458)", (string)embeds.Build(Learned, isTest: false)["description"]);
        Assert.DoesNotContain("Brown Horse", (string)embeds.Build(Learned, isTest: false, itemPicture: true)["description"]);
    }

    [Fact]
    public void Spell_id_comes_through_the_live_channel()
    {
        var e = WoWCharacterTracker.Services.LiveChannel.Payload.Parse("i=:mount:1;g=1403-0A1B2C3D;k=mount;w=Brown Horse;sp=458")!;
        Assert.Equal(458, e.SpellId);
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
    public void Release_notes_read_as_plain_text_in_the_banner() =>
        Assert.Equal("• Notes in game: type /wct note.\n      ◦ Lock it.\n\nComes with the addon.",
            Format.ReleaseNotes("- **Notes in game**: type `/wct note`.\r\n  - Lock it.\r\n\r\nComes with the addon.\r\n"));

    // A note edited in game replaces the app's only when it's newer; clearing counts as an edit.
    [Fact]
    public void The_newer_note_wins()
    {
        var config = new Config();
        config.SetNote("P-1", "From the app", 1000);
        Assert.False(config.TakeNewerNote(new GameNote("P-1", "Old game edit", 900)));
        Assert.True(config.TakeNewerNote(new GameNote("P-1", "", 1100)));
        Assert.Equal("", config.NoteOf("P-1"));
        Assert.Equal([new GameNote("P-1", "", 1100)], config.Notes().ToList()); // the clear still reaches the game
    }

    [Fact]
    public void Notes_edited_in_game_are_read_from_the_saved_file()
    {
        var file = Path.Combine(Path.GetTempPath(), "wct-notes-" + Guid.NewGuid().ToString("N") + ".lua");
        File.WriteAllText(file, "WoWCharacterTrackerDB = { notes = { [\"Player-1\"] = { text = \"a || b\\nc\", at = 1234 } } }");
        try { Assert.Equal([new GameNote("Player-1", "a | b\nc", 1234)], SavedVariables.Read(file).Notes); }
        finally { File.Delete(file); }
    }

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
            Enchants = Inventory.ParseEnchants("13~5~Enchant Ring - Nature's Fury~7346"),
        };
        var gear = CharacterGear.From(inv, new GameLookup(Flavors.Retail, () => { }), Flavors.Retail, "WARRIOR");
        Assert.Equal(["Equip: One.\nUse: Two.", "Equip: Three."], gear.Effects.Select(e => e.Text));
        Assert.False(gear.ShowsCatalystNote); // no sets
        // Enchants go under the item in its slot, not in a list of their own.
        var ring = gear.Right.Single(c => c.Item?.ItemId == 5);
        Assert.Equal("Nature's Fury", ring.Enchant);
        Assert.NotNull(ring.EnchantInfo);
        Assert.All(gear.Left, c => Assert.False(c.HasEnchant));
    }

    [Fact]
    public void Saved_enchant_lines_carry_their_id()
    {
        Assert.Equal(
            [new GearEnchant(5, 279869, "Stamina +2", 41), new GearEnchant(11, 240949, "Enchant Ring - Nature's Fury", 0)],
            Inventory.ParseEnchants("5~279869~Stamina +2~41\n11~240949~Enchant Ring - Nature's Fury")); // the second as saved before 0.24.1
    }

    [Fact]
    public async Task Classic_enchants_are_found_by_id_without_asking_Wowhead()
    {
        // Forever only shows the stats ("+4 All Stats"), so the id has to do it: 1891 is Enchant Chest - Greater Stats.
        Assert.Equal(20025, Enchants.SpellFor(1891));
        Assert.Equal(("spell", 20025), await Enchants.FindAsync(1891, "+4 All Stats", Flavors.Forever));
        Assert.Null(Enchants.SpellFor(0));
        Assert.Null(Enchants.SpellFor(999999));
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

public class DiscordCreditTests
{
    [Fact]
    public void Credit_goes_after_the_main_embeds_footer_without_changing_the_original()
    {
        var footer = new { text = "Forever" };
        var main = new Dictionary<string, object> { ["description"] = "Zeb just dinged level 40.", ["footer"] = footer };
        var shot = new Dictionary<string, object> { ["image"] = new { url = "attachment://shot.jpg" } };
        var sent = Services.Discord.DiscordWebhook.WithCredit([main, shot]);

        var sentFooter = Assert.IsType<Dictionary<string, string>>(sent[0]["footer"]);
        Assert.Equal("Forever • WoW Character Tracker", sentFooter["text"]);
        Assert.Equal(Services.Discord.DiscordWebhook.LogoUrl, sentFooter["icon_url"]);
        Assert.Equal("Zeb just dinged level 40.", sent[0]["description"]);
        Assert.Same(shot, sent[1]);
        Assert.Same(footer, main["footer"]); // the same embed goes to every webhook
    }

    [Fact]
    public void Credit_alone_when_there_is_no_footer()
    {
        var sent = Services.Discord.DiscordWebhook.WithCredit([new Dictionary<string, object>()]);
        var footer = Assert.IsType<Dictionary<string, string>>(sent[0]["footer"]);
        Assert.Equal(Services.Discord.DiscordWebhook.Credit, footer["text"]);
    }

    [Fact]
    public void Webhook_test_says_test_message_not_a_ding()
    {
        var e = ViewModels.SampleEvents.For("test", Flavors.Forever, null, []);
        var embed = new Services.Discord.EventEmbeds(Flavors.Forever).Build(e, isTest: true);
        Assert.Equal("Testy sent a test message.", embed["title"]);
    }
}