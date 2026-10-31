using MoonSharp.Interpreter;
using WoWCharacterTracker.Services.Discord;
using WoWCharacterTracker.Services.Streaming;

namespace WoWCharacterTracker.Tests;

public class FollowedItemsTests
{
    const int Followed = 2589; // Linen Cloth, a common item

    // Wowhead's answer to forever/search/suggestions-template?q=thunderfury (saved 2026-10-31, the
    // descriptions cut): spells, a quest and news around the one item. Below it: an item without a
    // quality, one without a name.
    const string Sample = """
        {"search":"thunderfury","results":[
        {"type":6,"id":468157,"name":"Thunderfury","typeName":"Spell","icon":"spell_nature_cyclone","pinTitle":"Spell","pinBreadcrumb":["Uncategorized"],"popularity":0},
        {"type":3,"id":19019,"name":"Thunderfury, Blessed Blade of the Windseeker","typeName":"Item","icon":"inv_sword_39","quality":5,"pinTitle":"Item","pinFooterText":"One-Handed Sword","pinBreadcrumb":["Weapon","One-Handed Sword"],"popularity":2},
        {"type":5,"id":7787,"name":"Rise, Thunderfury!","typeName":"Quest","side":3,"pinTitle":"Quest","popularity":3},
        {"type":162,"id":375994,"name":"How to Complete Your Thunderfury in Phase 3 - WoW Classic Fresh","typeName":"News Post","pinTitle":"News","popularity":4},
        {"type":3,"id":7,"name":"No quality"},
        {"type":3,"id":8}
        ],"categories":{"decor":[],"guides":[],"news":[],"tools":[],"database":[]}}
        """;

    [Fact]
    public void Search_keeps_only_the_items()
    {
        Assert.Equal(
            [new Wowhead.ItemHit(19019, "Thunderfury, Blessed Blade of the Windseeker", "inv_sword_39", 5), new Wowhead.ItemHit(7, "No quality", "", -1)],
            Wowhead.ParseItemHits(Sample));
    }

    [Theory]
    [InlineData("""{"search":"zzz","results":[],"categories":{}}""")] // Wowhead's answer when nothing matches
    [InlineData("""{"search":"zzz"}""")]
    public void Search_without_items_is_empty(string json) => Assert.Empty(Wowhead.ParseItemHits(json));

    [Theory]
    [InlineData("19019", 19019)]
    [InlineData("  19019 ", 19019)]
    [InlineData("https://www.wowhead.com/forever/item=19019/thunderfury-blessed-blade", 19019)]
    [InlineData("https://www.wowhead.com/item=212/x?bonus=1", 212)]
    [InlineData("wowhead.com/classic/item/19019", 19019)]
    [InlineData("thunderfury", null)]
    [InlineData("https://www.wowhead.com/spell=458", null)]
    [InlineData("item 5", null)]
    [InlineData("0", null)]
    [InlineData("99999999999999", null)]
    [InlineData("", null)]
    public void A_pasted_address_or_id_gives_the_item(string text, int? id) => Assert.Equal(id, Wowhead.ItemIdFrom(text));

    // Wowhead's Forever tooltips (saved 2026-10-31), the start of each.
    [Theory]
    [InlineData("""<table><tr><td><b class="q4">Teebu's Blazing Longsword</b><br>Binds when equipped<br>Unique-Equipped<table width="100%"><tr><td>Main Hand</td><th><span class="q1">Sword</span></th></tr></table>""", ItemCategories.Weapons)]
    [InlineData("""<table><tr><td><b class="q4">Cloak of Flames</b><br>Binds when equipped<table width="100%"><tr><td>Back</td></tr></table><span>50 Armor</span></td></tr></table>""", ItemCategories.Armor)]
    [InlineData("""<table><tr><td><b class="q4">The Eye of Shadow</b><br>Unique<table width="100%"><tr><td>Trinket</td><th><span class="q1"></span></th></tr></table></td></tr></table>""", ItemCategories.Jewelry)]
    [InlineData("""<table><tr><td><b class="q3">Deathcharger's Reins</b><br>Unique<br />Mount <span class="q0">(Account-wide)</span></td></tr></table>""", ItemCategories.Mounts)]
    [InlineData("""<table><tr><td><b class="q4">Plans: Lionheart Helm</b><br>Use: Teaches you how to make a Lionheart Helm.<br>Lionheart Helm<table width="100%"><tr><td>Head</td><th>Plate</th></tr></table></td></tr></table>""", ItemCategories.Other)]
    [InlineData("""<table><tr><td><b class="q4">Head of Onyxia</b><br>Unique<br>This Item Begins a Quest</td></tr></table>""", ItemCategories.Other)]
    [InlineData("""<table><tr><td><b class="q1">Chicken Egg</b><br>Use: Teaches you how to summon this companion.</td></tr></table>""", ItemCategories.Other)]
    [InlineData("""<table><tr><td><b class="q3">Bulwark</b><table width="100%"><tr><td>Off Hand</td><th>Shield</th></tr></table></td></tr></table>""", ItemCategories.Armor)]
    public void The_tooltip_gives_the_category(string html, string category) => Assert.Equal(category, ItemCategories.Of(TooltipHtml.ToText(html)));

    [Fact]
    public void An_item_without_a_tooltip_is_other() => Assert.Equal(ItemCategories.Other, ItemCategories.Of(null));

    [Fact]
    public void A_ready_made_list_only_counts_when_on_and_adds_to_your_own()
    {
        var rareAndFun = ReadyMadeList.RareAndFun;
        var deathcharger = new Entry { Kind = "mount", ItemId = 13335 };
        var cruelBarb = new Entry { Kind = "loot", ItemId = 5191 };
        var mine = Following(13335, Followed);
        Assert.Equal(2, mine.AllFollowed.Count);

        var off = new FlavorSettings();
        Assert.False(off.Follows(deathcharger));
        Assert.Empty(off.AllFollowed);

        var on = new FlavorSettings { ReadyMadeLists = [rareAndFun.Key] };
        Assert.True(on.Follows(deathcharger));
        Assert.False(on.Follows(cruelBarb)); // that's on the other list
        Assert.Equal(rareAndFun.Items.Count, on.AllFollowed.Count);
        Assert.Same(rareAndFun, on.ListWith(13335));

        mine.ReadyMadeLists = [rareAndFun.Key]; // one of them is on both: listed once, as your own
        Assert.Equal(rareAndFun.Items.Count + 1, mine.AllFollowed.Count);
        Assert.Single(mine.AllFollowed, i => i.Id == 13335);
        Assert.Null(mine.ListWith(13335));
    }

    [Fact]
    public void Ready_made_lists_have_no_repeats_and_only_forever_offers_them()
    {
        var items = ReadyMadeList.All.SelectMany(l => l.Items).ToList();
        Assert.Equal(items.Count, items.DistinctBy(i => i.Id).Count());
        Assert.Equal(ReadyMadeList.All.Count, ReadyMadeList.All.DistinctBy(l => l.Key).Count());

        // Both on: each item once.
        var both = new FlavorSettings { ReadyMadeLists = [.. ReadyMadeList.All.Select(l => l.Key)] };
        Assert.Equal(items.Count, both.AllFollowed.Count);
        Assert.Equal(both.AllFollowed.Count, both.AllFollowed.DistinctBy(i => i.Id).Count());
        Assert.Equal(ReadyMadeList.All, Flavors.Forever.ReadyMadeLists);
        Assert.Empty(Flavors.Retail.ReadyMadeLists);
    }

    [Fact]
    public void A_list_code_comes_back_as_the_same_items()
    {
        var code = FollowedListCode.Export([new(19019, "Thunderfury"), new(5191, "Cruel Barb")], Flavors.Forever);
        var (items, problem) = FollowedListCode.Import($"my list: {code} enjoy", Flavors.Forever);
        Assert.Null(problem);
        Assert.Equal([new FollowedItem(19019, "Thunderfury"), new FollowedItem(5191, "Cruel Barb")], items);
    }

    [Fact]
    public void A_list_code_only_goes_in_on_its_own_game()
    {
        var (items, problem) = FollowedListCode.Import(FollowedListCode.Export([new(1, "x")], Flavors.Forever), Flavors.Retail);
        Assert.Empty(items);
        Assert.Contains("Forever", problem);
    }

    [Theory]
    [InlineData("hello", "isn't a list code")]
    [InlineData("WCT1:abc", "isn't a list code")] // a settings code
    [InlineData("WCTL1:not base64!", "damaged")]
    [InlineData("WCTL1:bm90IGpzb24=", "damaged")]
    public void A_bad_list_code_says_why(string text, string why)
    {
        var (items, problem) = FollowedListCode.Import(text, Flavors.Forever);
        Assert.Empty(items);
        Assert.Contains(why, problem);
    }

    [Fact]
    public void A_list_code_is_cleaned_up_and_merged_without_losing_anything()
    {
        var code = FollowedListCode.Export([new(0, "bad"), new(5, new string('x', 500)), new(5, "again"), new(7, "seven")], Flavors.Forever);
        var (items, _) = FollowedListCode.Import(code, Flavors.Forever);
        Assert.Equal([5, 7], items.Select(i => i.Id));
        Assert.Equal(200, items[0].Name.Length);

        var s = Following(7, 9);
        Assert.Equal(1, FollowedListCode.Merge(s, items));
        Assert.Equal([5, 7, 9], s.FollowedItems.Select(i => i.Id));
        Assert.Equal(0, FollowedListCode.Merge(s, items));
    }

    [Fact]
    public void Classic_search_hits_only_add_names_forever_lacks_once_each()
    {
        const string thunderfury = "Thunderfury, Blessed Blade of the Windseeker";
        Wowhead.ItemHit Hit(int id, string name) => new(id, name, "", 4);

        // Forever has it: Classic's versions stay out, also the same id.
        var merged = Wowhead.WithClassicHits([Hit(19019, thunderfury)], [Hit(19019, thunderfury), Hit(230224, thunderfury)]);
        Assert.Equal([19019], merged.Select(h => h.Id));

        // Only Classic has it: its oldest id, listed after Forever's.
        merged = Wowhead.WithClassicHits([Hit(1, "Other")], [Hit(230224, thunderfury), Hit(19019, thunderfury), Hit(14551, "Edgemaster's Handguards")]);
        Assert.Equal([1, 19019, 14551], merged.Select(h => h.Id));
    }

    static FlavorSettings Following(params int[] ids) => new() { FollowedItems = [.. ids.Select(i => new FollowedItem(i, "x"))] };

    // Below rare, and with every other loot rule off: only the list makes it a post.
    [Fact]
    public void A_followed_item_posts_at_any_quality_while_others_do_not()
    {
        var s = Following(Followed);
        Assert.True(s.Wants(new Entry { Kind = "loot", ItemId = Followed, Quality = 1 }));
        Assert.False(s.Wants(new Entry { Kind = "loot", ItemId = 3, Quality = 1 }));
        Assert.True(s.Wants(new Entry { Kind = "mount", ItemId = Followed }) && s.Follows(new Entry { Kind = "mount", ItemId = Followed }));
        Assert.False(s.Follows(new Entry { Kind = "boss", ItemId = Followed }));
    }

    // The post switch off: the other rules decide, as if it weren't followed.
    [Fact]
    public void With_the_post_switched_off_the_other_rules_decide()
    {
        var s = Following(Followed);
        s.PostFollowed = false;
        Assert.False(s.Wants(new Entry { Kind = "loot", ItemId = Followed, Quality = 3 }));
        s.PostRare = true;
        s.RareBoeOnly = false;
        Assert.True(s.Wants(new Entry { Kind = "loot", ItemId = Followed, Quality = 3 }));
        Assert.False(s.PostsAsFollowed(new Entry { Kind = "loot", ItemId = Followed, Quality = 3 }));
    }

    // Addon and app agree, through the Settings.lua the app writes.
    [Theory]
    [InlineData(true, 1, true)]
    [InlineData(true, 3, true)]
    [InlineData(false, 1, false)]
    public void Addon_agrees_on_followed_items(bool postFollowed, int quality, bool posted)
    {
        var s = Following(Followed);
        s.PostFollowed = postFollowed;
        var e = new Entry { Kind = "loot", Level = 30, Quality = quality, ItemId = Followed };
        Assert.Equal(posted, s.Wants(e));
        Assert.Equal(posted, SharedRulesTests.AddonWants(s, e));
    }

    [Fact]
    public void Only_the_followed_events_get_marked()
    {
        List<Entry> events = [new() { Id = "a", Kind = "loot", ItemId = Followed }, new() { Id = "b", Kind = "loot", ItemId = 3 }];
        Assert.Same(events, new FlavorSettings().MarkFollowed(events));
        Assert.Equal([true, false], Following(Followed).MarkFollowed(events).Select(e => e.Followed));
    }

    // --- the addon: recording ---

    (Script Script, Table Ns, List<Table> Recorded) Addon(string settings)
    {
        var script = new Script();
        script.DoString($$"""
            LOOT_ITEM_SELF = "You receive loot: %s."
            LOOT_ITEM_PUSHED_SELF = "You receive item: %s."
            LOOT_ITEM_CREATED_SELF = "You create: %s."
            function time() return 5000 end
            function GetItemInfo(link)
              if link:find("Hitem:9:") then return nil end -- the game hasn't loaded it
              return 'x', link, link:find("cff0070dd") and 3 or 1, 1, 1, 'Armor', 'Cloth', 1, 'INVTYPE_CHEST', 0, 0, 4, 1, 1
            end
            WoWCharacterTrackerSettings = {{settings}}
            """);
        var ns = new Table(script);
        var recorded = new List<Table>();
        ns["say"] = (Action<string>)(_ => { });
        foreach (var file in new[] { "Shared.lua", "Events.lua" })
            script.Call(script.LoadString(SharedRulesTests.AddonFile(file)), "WoWCharacterTracker", ns);
        ns["characterInfo"] = (Func<Table>)(() => script.DoString("return { guid = 'Player-1', level = 50 }").Table);
        ns["addEvent"] = (Action<Table>)recorded.Add;
        return (script, ns, recorded);
    }

    const string White = $"|cffffffff|Hitem:2589::::::::50:::::|h[Linen Cloth]|h|r";

    [Theory]
    [InlineData("You receive loot: " + White + ".")]
    [InlineData("You receive item: " + White + ".")] // bought or a quest reward
    [InlineData("You create: " + White + ".")]
    public void A_followed_common_item_is_recorded_however_it_comes(string line)
    {
        var (script, ns, recorded) = Addon("{ followed = { [2589] = true } }");
        script.Call(ns.Get("recordLoot"), line);
        Assert.Equal(1, Assert.Single(recorded).Get("quality").Number);
    }

    [Theory]
    [InlineData("{ followed = { [7] = true } }")]
    [InlineData("{ }")]
    public void Other_common_items_are_not_recorded(string settings)
    {
        var (script, ns, recorded) = Addon(settings);
        script.Call(ns.Get("recordLoot"), "You receive loot: " + White + ".");
        script.Call(ns.Get("recordLoot"), "You create: " + White + ".");
        Assert.Empty(recorded);
    }

    // A rare that's also followed: one event, so one post.
    [Fact]
    public void A_followed_rare_is_one_event()
    {
        var (script, ns, recorded) = Addon("{ followed = { [17705] = true } }");
        script.Call(ns.Get("recordLoot"), "You receive loot: |cff0070dd|Hitem:17705::::::::50:::::|h[Thrash Blade]|h|r.");
        Assert.Equal(3, Assert.Single(recorded).Get("quality").Number);
    }

    // Rares not on the list keep the old rule: looted only.
    [Fact]
    public void A_bought_rare_that_is_not_followed_is_not_recorded()
    {
        var (script, ns, recorded) = Addon("{ followed = { [7] = true } }");
        script.Call(ns.Get("recordLoot"), "You receive item: |cff0070dd|Hitem:17705::::::::50:::::|h[Thrash Blade]|h|r.");
        Assert.Empty(recorded);
    }

    // The game hasn't loaded it and its color says nothing: still recorded, Wowhead knows the rest.
    [Fact]
    public void A_followed_item_the_game_has_not_loaded_is_still_recorded()
    {
        var (script, ns, recorded) = Addon("{ followed = { [9] = true } }");
        script.Call(ns.Get("recordLoot"), "You receive loot: |cnIQ2:|Hitem:9::::::::50:::::|h[Unknown]|h|r.");
        Assert.Equal("Unknown", Assert.Single(recorded).Get("item").String);
    }

    // Guild and group chat: the followed line replaces the usual one, and only when picked.
    [Theory]
    [InlineData("followed = true", 2589, 1, "Got [Linen Cloth] from my followed items!")]
    [InlineData("followed = true, loot = true", 2589, 4, "Got [Linen Cloth] from my followed items!")]
    [InlineData("loot = true", 2589, 1, null)]
    [InlineData("loot = true", 2589, 4, "Looted [Linen Cloth]!")]
    [InlineData("followed = true", 7, 4, null)]
    public void Chat_tells_about_followed_items_once(string kinds, int itemId, int quality, string? said)
    {
        var script = new Script();
        script.DoString($$"""
            sent = {}
            function IsInGuild() return true end
            function SendChatMessage(msg) table.insert(sent, msg) end
            function GetTime() return 0 end
            C_Timer = { After = function(_, fn) fn() end }
            WoWCharacterTrackerSettings = { followed = { [2589] = true }, announce = { guild = true, kinds = { {{kinds}} } } }
            """);
        var ns = new Table(script);
        foreach (var file in new[] { "Shared.lua", "Announce.lua" })
            script.Call(script.LoadString(SharedRulesTests.AddonFile(file)), "WoWCharacterTracker", ns);
        script.Call(ns.Get("announce"), script.DoString($"return {{ kind = 'loot', item = 'Linen Cloth', itemId = {itemId}, quality = {quality} }}"));
        var sent = script.Globals.Get("sent").Table.Values.Select(v => v.String).ToList();
        Assert.Equal(said is null ? [] : [said], sent);
    }

    // --- the overlay ---

    static readonly DateTimeOffset Now = DateTimeOffset.Now;
    static readonly StreamCharacter Me = new("g", "Me", "Forever", 30, "Mage", "#fff", "Human", "Realm", "", "", "", 0);
    static Entry Loot(int quality, bool boe = false, bool followed = false) =>
        new() { Id = $"{quality}", Guid = "g", Kind = "loot", Item = "Blade", ItemId = 5, Quality = quality, Boe = boe, Followed = followed, At = Now.ToUnixTimeSeconds() };

    [Theory]
    [InlineData(1, "gold")]
    [InlineData(3, "rare")]
    [InlineData(4, "epic")]
    [InlineData(5, "legendary")]
    public void A_followed_item_alerts_as_itself_in_its_quality(int quality, string tone)
    {
        var a = StreamSnapshot.Build(Me, [Loot(quality, followed: true)], Now, new StreamOptions { AlertKinds = ["followed", "rare", "epic", "legendary"] }).Alert!;
        Assert.Equal(("followed", "ITEM FOUND!", "Blade", tone), (a.Kind, a.Title, a.Line, a.Tone));
    }

    // Its alert off: an ordinary loot alert when the kind is on, and nothing below rare.
    [Fact]
    public void With_the_followed_alert_off_it_is_ordinary_loot()
    {
        var o = new StreamOptions { AlertKinds = ["epic"] };
        Assert.Equal("epic", StreamSnapshot.Build(Me, [Loot(4, followed: true)], Now, o).Alert!.Kind);
        Assert.Null(StreamSnapshot.Build(Me, [Loot(1, followed: true)], Now, o).Alert);
    }

    // Part of the shared rule: the alert and the highlights use Discord's Entry.PassesRareRule.
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    public void Rare_alerts_can_be_bind_on_equip_only(bool boeOnly, bool boe, bool alerts)
    {
        var o = new StreamOptions { AlertKinds = ["rare"], RareAlertBoeOnly = boeOnly };
        Assert.Equal(alerts, StreamSnapshot.Build(Me, [Loot(3, boe)], Now, o).Alert is not null);
        Assert.Equal(alerts, Loot(3, boe).PassesRareRule(boeOnly));
    }

    [Fact]
    public void Epic_alerts_and_followed_rares_do_not_care_about_bind_on_equip()
    {
        var o = new StreamOptions { AlertKinds = ["epic", "followed"], RareAlertBoeOnly = true };
        Assert.NotNull(StreamSnapshot.Build(Me, [Loot(4)], Now, o).Alert);
        Assert.NotNull(StreamSnapshot.Build(Me, [Loot(3, followed: true)], Now, o).Alert);
    }

    [Fact]
    public void Rare_highlights_can_be_bind_on_equip_only()
    {
        List<Entry> events = [Loot(3), Loot(3, followed: true) with { Id = "f" }, Loot(4)];
        Assert.Equal(3, StreamSnapshot.Build(Me, events, Now, new StreamOptions()).Highlights.Count); // every rare by default
        Assert.Equal(2, StreamSnapshot.Build(Me, events, Now, new StreamOptions { RareHighlightBoeOnly = true }).Highlights.Count);
    }

    // --- the Discord post ---

    [Fact]
    public void The_post_says_it_is_followed_and_copes_with_any_quality()
    {
        var embeds = new EventEmbeds(Flavors.Forever);
        var post = embeds.Build(new Entry { Kind = "loot", Char = "Me", Item = "Linen Cloth", ItemId = Followed, Quality = 1, Zone = "Duskwood", At = 1, Followed = true }, isTest: false);
        Assert.Equal("⭐ Me got [Linen Cloth]", post["title"]);
        Assert.Contains("On their followed items list!", (string)post["description"]);
        Assert.Contains("Item, got in **Duskwood**.", (string)post["description"]);
        var plain = embeds.Build(new Entry { Kind = "loot", Char = "Me", Item = "Blade", ItemId = 5, Quality = 3, At = 1 }, isTest: false);
        Assert.Equal("Me looted [Blade]", plain["title"]);
    }

    // The app writes the list for the addon, and keeps it per game.
    [Fact]
    public void Settings_lua_carries_the_list()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wct-followed-" + Guid.NewGuid().ToString("N"));
        var addon = Path.Combine(dir, "Interface", "AddOns", "WoWCharacterTracker");
        Directory.CreateDirectory(addon);
        try
        {
            AddonInstaller.WriteSettings(dir, Following(19019, 2589));
            var lua = File.ReadAllText(Path.Combine(addon, "Settings.lua"));
            Assert.Contains("followed = { [19019] = true, [2589] = true, }", lua);
            Assert.Contains("followed = true", lua);
            new Script().DoString(lua); // it's valid Lua, with an empty list too
            AddonInstaller.WriteSettings(dir, new FlavorSettings());
            new Script().DoString(File.ReadAllText(Path.Combine(addon, "Settings.lua")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // Reset and shared settings codes leave the list alone: it's the user's own.
    [Fact]
    public void The_list_stays_through_a_reset_and_a_settings_code()
    {
        var s = Following(Followed);
        s.CopyPostingFrom(Flavors.Forever.Defaults());
        Assert.Single(s.FollowedItems);

        var from = Following(1, 2);
        from.PostFollowed = false;
        Assert.Null(PostingCode.Import(PostingCode.Export(from, Flavors.Forever), s).Problem);
        Assert.False(s.PostFollowed);
        Assert.Equal(Followed, Assert.Single(s.FollowedItems).Id);
    }
}
