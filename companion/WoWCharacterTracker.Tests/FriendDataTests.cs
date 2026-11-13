using MoonSharp.Interpreter;
using WoWCharacterTracker.Services;

namespace WoWCharacterTracker.Tests;

// What Battle.net friends shared (WoWCharacterTrackerDB.friends) and the sharing settings, read by the app.
public class FriendDataTests
{
    static DynValue Lua(string table) => new Script().DoString("return " + table);

    [Fact]
    public void A_friends_characters_become_snapshots_and_gear()
    {
        var friends = FriendData.ReadFriends(Lua("""
            { ['Zeb#2222'] = { seen = 2000, version = '0.33.0', chars = {
                ['Player-1-0000000B'] = { guid = 'Player-1-0000000B', char = 'Zeb', realm = 'Firemaw', level = 42, class = 'Warrior', classFile = 'WARRIOR',
                  race = 'Orc', raceFile = 'Orc', sex = 'female', faction = 'Horde', spec = 'Arms', ilvl = 38, at = 1900, zone = 'Duskwood', money = 5000,
                  gear = '1~19019,16~12345', enchants = '16~12345~Crusader~1900', talentCalc = '--505', surname = 'Stonehand', received = 1950,
                  professions = 'Mining 150/225, ' .. string.rep('x', 300) } } } }
            """));
        var zeb = Assert.Single(friends);
        Assert.Equal("Zeb#2222", zeb.Tag);
        Assert.Equal("0.33.0", zeb.Version);
        var s = Assert.Single(zeb.Characters);
        Assert.Equal(("Zeb", "Firemaw", 42, "WARRIOR", "Duskwood", 1900L, 5000L), (s.Char, s.Realm, s.Level, s.ClassFile, s.Zone, s.LastSeen, s.Money));
        Assert.Equal("--505", s.TalentCalc);
        Assert.StartsWith("Mining 150/225, ", s.Professions);
        Assert.Equal(256, s.Professions.Length); // the addon's cap
        Assert.Equal("Stonehand", s.Surname);
        Assert.Equal("female", s.Sex); // the right portrait
        var inv = Assert.Single(zeb.Inventories);
        Assert.Equal(2, inv.Gear.Count);
        Assert.Equal("Crusader", Assert.Single(inv.Enchants).Text);
    }

    // Every highlight kind the addon sends becomes an event the app can show, in the addon's field order.
    [Fact]
    public void Highlights_from_the_addon_become_the_characters_events()
    {
        var c = new FriendsLuaTests.Client("Player-1-0000000A", "{}", files: ["Shared.lua", "FriendData.lua"]);
        c.Lua("""
            events = {
              { kind = 'boss', at = 11, level = 60, boss = 'Ragnaros', instance = 'Molten Core', groupSize = 40 },
              { kind = 'keyrun', at = 10, level = 80, instance = 'Ara-Kara', keyLevel = 12, upgrades = 2 },
              { kind = 'loot', at = 9, level = 60, item = 'Thunderfury', itemId = 19019, quality = 5 },
              { kind = 'mount', at = 8, level = 40, mount = 'Horse', itemId = 2411, bought = true },
              { kind = 'duel', at = 7, level = 40, opponent = 'Grimtusk', lost = true, fled = true },
              { kind = 'makgora', at = 6, level = 40, opponent = 'Zug' },
              { kind = 'bg', at = 5, level = 40, instance = 'Warsong Gulch', lost = true },
              { kind = 'profession', at = 4, level = 40, profession = 'Mining', step = 150 },
              { kind = 'mplus', at = 3, level = 80, step = 2000, rating = 2034 },
              { kind = 'pvp', at = 2, level = 80, step = 1800, bracket = '3v3', rating = 1812 },
              { kind = 'death', at = 1, level = 42, killer = 'Hogger', played = 3600, gold = 5000, zone = 'Elwynn Forest' },
            }
            back = ns.decodeFriendChar(ns.encodeFriendChar('Player-1-0000000B', { char = 'Zeb', lastSeen = 20 }, nil, { highlights = true, gold = true, location = true }, events))
            """);
        Assert.All(EventKinds.Highlights, k => Assert.True(c.Lua($"return ns.isFriendHighlight({{ kind = '{k}', at = 1 }})").Boolean));
        c.Lua("friends = { ['Zeb#2222'] = { chars = { ['Player-1-0000000B'] = back } } }");
        var events = Assert.Single(FriendData.ReadFriends(c.Lua("return friends"))).Events;
        Assert.Equal(10, events.Count); // the newest 10: the death is the 11th
        Assert.All(events, e => Assert.Equal(("Player-1-0000000B", "Zeb"), (e.Guid, e.Char)));
        Assert.Equal(
            ["Killed Ragnaros (Molten Core)", "Timed +12 Ara-Kara (+2)", "Looted Thunderfury", "Bought Horse", "Ran from a duel with Grimtusk",
             "Won a Mak'gora against Zug", "Lost Warsong Gulch", "Reached 150 Mining", "Reached 2000 Mythic+ rating (2034)", "Reached 1800 in 3v3 (1812)"],
            events.Select(HighlightText.Text));
        Assert.Equal((19019, 5), (events[2].ItemId, events[2].Quality));
        Assert.Equal(40, events[0].GroupSize);
    }

    // Level times go out with Level times on (and only then), and the app reads them as level events,
    // with played time and the XP bar on the character.
    [Fact]
    public void Level_times_from_the_addon_become_level_events()
    {
        var c = new FriendsLuaTests.Client("Player-1-0000000A", "{}", files: ["Shared.lua", "FriendData.lua"]);
        c.Lua("""
            char = { char = 'Zeb', lastSeen = 20, level = 12, played = 9000, levelTime = 600, xp = 300, xpMax = 7600, maxLevel = 60 }
            levels = { { level = 11, seconds = 1500, at = 15 }, { level = 10, seconds = 1200, at = 10 }, { level = 12, seconds = 0, at = 16 } }
            on, onKey = ns.encodeFriendChar('Player-1-0000000B', char, nil, { leveling = true }, nil, levels)
            off = ns.encodeFriendChar('Player-1-0000000B', char, nil, {}, nil, levels)
            char.played, char.levelTime = 9500, 1100
            _, laterKey = ns.encodeFriendChar('Player-1-0000000B', char, nil, { leveling = true }, nil, levels)
            friends = { ['Zeb#2222'] = { chars = { ['Player-1-0000000B'] = ns.decodeFriendChar(on) } } }
            """);
        Assert.DoesNotContain("levels=", c.Lua("return off").String);
        Assert.DoesNotContain("played=", c.Lua("return off").String);
        Assert.Equal(c.Lua("return onKey").String, c.Lua("return laterKey").String); // played time alone doesn't resend
        Assert.Equal(2, c.Lua("return #ns.friendLevels(friends['Zeb#2222'].chars['Player-1-0000000B'])").Number);

        var zeb = Assert.Single(FriendData.ReadFriends(c.Lua("return friends")));
        Assert.Equal([(10, 1200L, 10L), (11, 1500L, 15L)], zeb.Events.Select(e => (e.Level, e.Seconds, e.At)).OrderBy(e => e.Level));
        Assert.All(zeb.Events, e => Assert.Equal(("level", "Player-1-0000000B"), (e.Kind, e.Guid)));
        var s = Assert.Single(zeb.Characters);
        Assert.Equal((9000L, 300L, 7600L, 60), (s.Played, s.Xp, s.XpMax, s.MaxLevel));
    }

    [Fact]
    public void Bad_level_lines_are_dropped()
    {
        var zeb = Assert.Single(FriendData.ReadFriends(Lua("""
            { ['Zeb#2222'] = { chars = { a = { guid = 'Player-1-0000000B', char = 'Zeb', at = 5, played = -5, xp = 1/0,
              levels = '10~1200~10\n10~999~11\n1~50~1\nx~1~1\n12~-3~1\n13~100\n14~200~5~9\n15~300~7' } } } }
            """)));
        Assert.Equal([10, 15], zeb.Events.Select(e => e.Level));
        Assert.Equal(1200, zeb.Events[0].Seconds); // the first line for a level counts
        var s = Assert.Single(zeb.Characters);
        Assert.Equal((0L, 0L), (s.Played, s.Xp));
    }

    // The addon window's estimate (Leveling.lua) is the app's (LevelStats), the same numbers in.
    [Fact]
    public void The_addon_estimates_time_to_max_like_the_app()
    {
        var c = new FriendsLuaTests.Client("Player-1-0000000A", "{}", files: ["Shared.lua", "Leveling.lua"]);
        var timed = new[] { (20, 2400L), (21, 2600L), (22, 2500L), (23, 3100L), (24, 2900L), (25, 3300L) };
        c.Lua("levels = { " + string.Join(", ", timed.Select(l => $"{{ level = {l.Item1}, seconds = {l.Item2} }}")) + " }");
        var lua = (long)c.Lua("return ns.timeToMax(levels, { [24] = 34000, [25] = 36000 }, 25, 5000, 36000, 60)").Number;
        var app = LevelStats.TimeToMax([.. timed.Select(l => new LevelStats.Level(l.Item1, l.Item2))],
            new Dictionary<int, long> { [24] = 34000, [25] = 36000 }, 25, 5000, 36000, 60);
        Assert.NotNull(app);
        Assert.InRange(lua, app.Value - 1, app.Value + 1); // rounding
        Assert.True(c.Lua("return ns.timeToMax({ levels[1] }, nil, 25, 0, 0, 60) == nil").Boolean); // one level is too little
        c.Lua("summary = ns.levelSummary(levels)");
        Assert.Equal((6, 20, 25), ((int)c.Lua("return summary.count").Number, (int)c.Lua("return summary.fastest.level").Number, (int)c.Lua("return summary.slowest.level").Number));
    }

    [Fact]
    public void A_death_highlight_keeps_what_it_cost_and_bad_lines_are_dropped()
    {
        var zeb = Assert.Single(FriendData.ReadFriends(Lua("""
            { ['Zeb#2222'] = { chars = { a = { guid = 'Player-1-0000000B', char = 'Zeb', at = 5, highlights =
              'death~300~42~Hogger~3600~5000~Elwynn Forest\nnope~1~1~x\nloot~-5~1~X~1~3\nloot~abc~1~X~1~3\nboss\nloot~7~60~Big~99999999999999999~4' } } } }
            """)));
        Assert.Equal(2, zeb.Events.Count);
        var death = zeb.Events[0];
        Assert.Equal(("death", 300L, 42, "Hogger", 3600L, 5000L, "Elwynn Forest"), (death.Kind, death.At, death.Level, death.Killer, death.Played, death.Gold, death.Zone));
        Assert.Equal(0, zeb.Events[1].ItemId); // too big a number reads as none
    }

    [Fact]
    public void Hostile_friend_data_is_cleaned_or_dropped()
    {
        var friends = FriendData.ReadFriends(Lua("""
            { ['Zeb#2222'] = { version = string.rep('x', 40) .. '\0', chars = {
                ['Player-1-0000000B'] = { guid = 'Player-1-0000000B', char = string.rep('Z', 100), level = 'abc', ilvl = 1/0, spec = 'A|cff00ff00B' },
                ['bad'] = { guid = 'Creature-1', char = 'X' },
                ['Player-1-0000000C'] = 'not a table' } },
              [5] = { chars = {} } }
            """));
        var zeb = Assert.Single(friends);
        Assert.Equal("", zeb.Version);
        var s = Assert.Single(zeb.Characters);
        Assert.Equal(24, s.Char.Length);
        Assert.Equal(0, s.Level);
        Assert.Equal(0, s.ItemLevel);
        Assert.Equal("Acff00ff00B", s.Spec);
    }

    [Fact]
    public void At_most_50_characters_per_friend_are_read()
    {
        var chars = string.Concat(Enumerable.Range(1, 60).Select(i => $"['Player-1-{i:X8}'] = {{ guid = 'Player-1-{i:X8}', char = 'A{i}', at = {i} }},"));
        var zeb = Assert.Single(FriendData.ReadFriends(Lua($"{{ ['Zeb#2222'] = {{ chars = {{ {chars} }} }} }}")));
        Assert.Equal(50, zeb.Characters.Count);
        Assert.DoesNotContain(zeb.Characters, c => c.Char == "A1"); // the ones seen longest ago go
    }

    [Fact]
    public void Friend_characters_are_not_your_characters()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wct-friends-{Guid.NewGuid():N}.lua");
        File.WriteAllText(path, """
            WoWCharacterTrackerDB = { chars = { ['Player-1-0000000A'] = { char = 'Deadbeck', realm = 'Firemaw', level = 10, lastSeen = 5 } },
              friends = { ['Zeb#2222'] = { chars = { ['Player-1-0000000B'] = { guid = 'Player-1-0000000B', char = 'Zeb' } } } } }
            """);
        try
        {
            var data = SavedVariables.Read(path);
            Assert.Equal(["Player-1-0000000A"], data.Characters.Select(c => c.Guid));
            Assert.Equal("Zeb", Assert.Single(Assert.Single(data.Friends).Characters).Char);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void The_newest_share_wins_both_ways()
    {
        var s = new FlavorSettings();
        var game = FriendData.ReadShare(Lua("{ enabled = true, at = 200 }"));
        Assert.True(s.TakeShare(game));
        Assert.True(s.Share!.Enabled);
        s.EditShare(x => x with { NewChars = false });
        Assert.True(s.Share.At > 200);
        Assert.False(s.TakeShare(game)); // older than the app's edit now
        Assert.False(s.Share.NewChars);
    }

    [Fact]
    public void Share_settings_survive_the_config_file()
    {
        var s = new FlavorSettings();
        s.TakeShare(FriendData.ReadShare(Lua("{ enabled = true, at = 200, defaults = { gold = true }, chars = { ['Player-1-0000000D'] = false }, contacts = { ['Zeb#2222'] = { share = true, gold = false } }, blocked = { ['Bad#1'] = true } }")));
        var back = System.Text.Json.JsonSerializer.Deserialize<FlavorSettings>(System.Text.Json.JsonSerializer.Serialize(s))!;
        Assert.Equal(200, back.Share!.At);
        Assert.True(back.Share.Defaults.Gold);
        Assert.False(back.Share.Chars["Player-1-0000000D"]);
        Assert.False(back.Share.Contacts["Zeb#2222"].Gold);
        Assert.Null(back.Share.Contacts["Zeb#2222"].Gear);
        Assert.Contains("Bad#1", back.Share.Blocked);
    }

    // The addon gets the characters removed in the app (guid and when), so it never shares them.
    [Fact]
    public void Settings_lua_lists_the_removed_characters()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wct-removed-" + Guid.NewGuid().ToString("N"));
        try
        {
            var s = new FlavorSettings();
            s.Removed["Player-1-0000000D"] = 1234;
            var script = new Script();
            script.DoString(File.ReadAllText(WriteSettings(dir, s)));
            Assert.Equal(1234, script.DoString("return WoWCharacterTrackerSettings.removed['Player-1-0000000D']").Number);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // Settings.lua in a fake game folder; returns its path.
    static string WriteSettings(string dir, FlavorSettings s, Dictionary<string, string>? names = null)
    {
        Directory.CreateDirectory(Path.Combine(dir, "Interface", "AddOns", "WoWCharacterTracker"));
        AddonInstaller.WriteSettings(dir, s, friendNames: names);
        return Path.Combine(dir, "Interface", "AddOns", "WoWCharacterTracker", "Settings.lua");
    }

    // Nicknames go both ways, like notes: the game's newer one is taken, an older one isn't, and
    // Settings.lua carries when each was set in the app (a cleared one too).
    [Fact]
    public void Nicknames_set_in_game_are_taken_when_newer_and_written_back_with_their_time()
    {
        var read = SavedVariables.ReadNicknames(Lua("{ ['Zeb#2222'] = { name = 'Big||Zeb', at = 900 }, ['Kalle#3333'] = { name = '', at = 300 }, ['Bad#1'] = { name = 'x' } }"));
        Assert.Equal(["Kalle#3333", "Zeb#2222"], read.Select(n => n.Tag).Order()); // no time: skipped
        Assert.Equal("Big|Zeb", read.Single(n => n.Tag == "Zeb#2222").Name);

        var config = new Config();
        config.SetNickname("Zeb#2222", "Zebbe", 800);
        config.SetNickname("Kalle#3333", "Kalle", 400);
        Assert.Equal([true, false], read.OrderByDescending(n => n.Tag).Select(config.TakeNewerNickname));
        Assert.Equal("Big|Zeb", config.FriendNicknames["Zeb#2222"]);
        Assert.Equal("Kalle", config.FriendNicknames["Kalle#3333"]); // the app's is newer
        config.SetNickname("Kalle#3333", " ", 950); // cleared in the app
        Assert.False(config.FriendNicknames.ContainsKey("Kalle#3333"));

        var dir = Path.Combine(Path.GetTempPath(), "wct-names-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "Interface", "AddOns", "WoWCharacterTracker"));
            AddonInstaller.WriteSettings(dir, new FlavorSettings(), friendNames: config.FriendNicknames, friendNamesAt: config.FriendNicknamesAt);
            var script = new Script();
            script.DoString(File.ReadAllText(Path.Combine(dir, "Interface", "AddOns", "WoWCharacterTracker", "Settings.lua")));
            Assert.Equal(900, script.DoString("return WoWCharacterTrackerSettings.friendNameAt['Zeb#2222']").Number);
            Assert.Equal(950, script.DoString("return WoWCharacterTrackerSettings.friendNameAt['Kalle#3333']").Number);
            Assert.True(script.DoString("return WoWCharacterTrackerSettings.friendNames['Kalle#3333']").IsNil());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // The Friends page's order: dragged in game it's read in order and taken when newer, and the
    // app's goes to Settings.lua with its time.
    [Fact]
    public void The_friend_order_goes_both_ways_and_the_newer_wins()
    {
        var read = SavedVariables.ReadFriendOrder(Lua("{ tags = { 'Zeb#2222', 'Kalle#3333', 'Zeb#2222' }, at = 900 }"))!;
        Assert.Equal(["Zeb#2222", "Kalle#3333"], read.Tags);
        Assert.Null(SavedVariables.ReadFriendOrder(Lua("{ tags = { 'Zeb#2222' } }"))); // no time
        var config = new Config { FriendOrder = ["Kalle#3333"], FriendOrderAt = 950 };
        Assert.False(config.TakeNewerFriendOrder(read));
        config.FriendOrderAt = 800;
        Assert.True(config.TakeNewerFriendOrder(read));
        Assert.Equal(["Zeb#2222", "Kalle#3333"], config.FriendOrder);

        var dir = Path.Combine(Path.GetTempPath(), "wct-order-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "Interface", "AddOns", "WoWCharacterTracker"));
            AddonInstaller.WriteSettings(dir, new FlavorSettings(), friendOrder: new GameFriendOrder(config.FriendOrder, config.FriendOrderAt));
            var script = new Script();
            script.DoString(File.ReadAllText(Path.Combine(dir, "Interface", "AddOns", "WoWCharacterTracker", "Settings.lua")));
            Assert.Equal("Kalle#3333", script.DoString("return WoWCharacterTrackerSettings.friendOrder[2]").String);
            Assert.Equal(900, script.DoString("return WoWCharacterTrackerSettings.friendOrderAt").Number);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // A drag only knows the friends that page shows: they swap among their own places, everyone
    // else keeps theirs, and new ones go at the end (Friends.lua's mergeFriendOrder is the same).
    [Theory]
    [InlineData("A,B,C,D,E", "E,D", "A,B,C,E,D")]   // Retail's two, Forever's three kept
    [InlineData("A,B,C,D", "C,A", "C,B,A,D")]       // shown ones swap places, B and D stay
    [InlineData("A,B", "X,B,A", "X,B,A")]           // a new one dragged first takes the first shown place
    [InlineData("", "B,A", "B,A")]
    public void A_drag_merges_into_the_whole_friend_order(string saved, string shown, string merged)
    {
        static List<string> L(string s) => [.. s.Split(',', StringSplitOptions.RemoveEmptyEntries)];
        Assert.Equal(L(merged), FriendOrder.Merge(L(saved), L(shown)));
    }

    [Fact]
    public void Settings_lua_has_no_share_until_the_app_has_one()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wct-share-" + Guid.NewGuid().ToString("N"));
        try
        {
            var s = new FlavorSettings();
            Assert.DoesNotContain("share =", File.ReadAllText(WriteSettings(dir, s)));
            s.TakeShare(FriendData.ReadShare(Lua("{ enabled = true, at = 200, contacts = { ['Zeb#2222'] = { share = true, gold = true } } }")));
            var file = WriteSettings(dir, s, new() { ["Zeb#2222"] = "Zebbe \"Z\"" });
            var script = new Script();
            script.DoString(File.ReadAllText(file));
            var share = FriendData.ReadShare(script.Globals.Get("WoWCharacterTrackerSettings").Table.Get("share"))!;
            Assert.True(share.Enabled);
            Assert.Equal(200, share.At);
            Assert.True(share.Contacts["Zeb#2222"].Gold);
            Assert.Null(share.Contacts["Zeb#2222"].Talents);
            Assert.Equal("Zebbe \"Z\"", script.DoString("return WoWCharacterTrackerSettings.friendNames['Zeb#2222']").String);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void The_addon_reads_the_share_the_app_wrote()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wct-share-" + Guid.NewGuid().ToString("N"));
        try
        {
            var s = new FlavorSettings();
            s.TakeShare(FriendData.ReadShare(Lua("{ enabled = true, at = 200, defaults = { gear = false, professions = false }, contacts = { ['Zeb#2222'] = { share = true, gold = true }, ['Kalle#3333'] = { share = true, professions = true } } }")));
            var file = WriteSettings(dir, s, new() { ["Zeb#2222"] = "Zebbe" });
            var c = new FriendsLuaTests.Client("Player-1-0000000A", "{}", db: "{ share = { enabled = false, at = 100 } }");
            c.Lua(File.ReadAllText(file));
            Assert.True(c.Lua("return ns.shareSettings().enabled").Boolean); // the app's is newer
            Assert.True(c.Lua("local s = ns.shareSettings(); return ns.allowedFor(s, s.contacts['Zeb#2222']).gold").Boolean);
            Assert.False(c.Lua("local s = ns.shareSettings(); return ns.allowedFor(s, s.contacts['Zeb#2222']).gear").Boolean);
            Assert.False(c.Lua("local s = ns.shareSettings(); return ns.allowedFor(s, s.contacts['Zeb#2222']).professions").Boolean);
            Assert.True(c.Lua("local s = ns.shareSettings(); return ns.allowedFor(s, s.contacts['Kalle#3333']).professions").Boolean);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // Two WoW accounts on one PC each keep their own friends list: the newest news per friend wins.
    [Fact]
    public void Friends_from_several_accounts_merge_with_the_newest_per_friend()
    {
        var older = FriendData.ReadFriends(Lua("{ ['Zeb#2222'] = { seen = 100, chars = { a = { guid = 'Player-1-0000000B', char = 'Old' } } } }"));
        var newer = FriendData.ReadFriends(Lua("{ ['Zeb#2222'] = { seen = 200, chars = { a = { guid = 'Player-1-0000000B', char = 'New' } } }, ['Kalle#3333'] = { seen = 50 } }"));
        var merged = FriendData.Merge([older, newer]);
        Assert.Equal(["Kalle#3333", "Zeb#2222"], merged.Select(f => f.Tag).Order());
        Assert.Equal("New", merged.Single(f => f.Tag == "Zeb#2222").Characters.Single().Char);
    }

    // Like the addon: on unless turned off (nothing is shared without a contact anyway).
    [Fact]
    public void Sharing_is_on_unless_turned_off()
    {
        Assert.True(FriendData.ReadShare(Lua("{ at = 5 }"))!.Enabled);
        Assert.False(FriendData.ReadShare(Lua("{ at = 5, enabled = false }"))!.Enabled);
        Assert.True(ShareSettings.Empty.Enabled);
    }

    [Fact]
    public void Share_settings_are_read_with_defaults_for_what_is_missing()
    {
        var share = FriendData.ReadShare(Lua("""
            { enabled = true, at = 300, defaults = { gold = true }, chars = { ['Player-1-0000000D'] = false },
              contacts = { ['Zeb#2222'] = { share = true, receive = true, location = false, professions = false } }, blocked = { ['Bad#1'] = true } }
            """))!;
        Assert.True(share.Enabled);
        Assert.Equal(300, share.At);
        Assert.True(share.Defaults.Gold);
        Assert.True(share.Defaults.Gear);       // missing: the addon's default
        Assert.True(share.Defaults.Location);   // on by default since 0.33.3
        Assert.True(share.Defaults.Professions); // on by default
        Assert.False(share.Chars["Player-1-0000000D"]);
        Assert.False(share.Contacts["Zeb#2222"].Location);
        Assert.False(share.Contacts["Zeb#2222"].Professions);
        Assert.Null(share.Contacts["Zeb#2222"].Gold); // follows the general choice
        Assert.Contains("Bad#1", share.Blocked);
        Assert.Null(FriendData.ReadShare(Lua("nil")));
    }

    [Fact]
    public void Map_settings_are_off_and_live_unless_set()
    {
        var empty = FriendData.ReadShare(Lua("{ at = 5 }"))!;
        Assert.False(empty.Defaults.Map);
        Assert.False(empty.Defaults.OtherFaction);
        Assert.Equal(5, empty.MapRate);
        Assert.Equal(5, FriendData.ReadShare(Lua("{ at = 5, mapRate = 7 }"))!.MapRate); // not a step
        var share = FriendData.ReadShare(Lua("{ at = 5, mapRate = 300, defaults = { map = true }, contacts = { ['Zeb#2222'] = { share = true, map = false, otherFaction = true } } }"))!;
        Assert.True(share.Defaults.Map);
        Assert.Equal(300, share.MapRate);
        Assert.False(share.Contacts["Zeb#2222"].Map);
        Assert.True(share.Contacts["Zeb#2222"].OtherFaction);
    }

    [Fact]
    public void The_addon_reads_the_map_settings_the_app_wrote()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wct-share-" + Guid.NewGuid().ToString("N"));
        try
        {
            var s = new FlavorSettings();
            s.TakeShare(FriendData.ReadShare(Lua("{ enabled = true, at = 200, mapRate = 60, defaults = { otherFaction = true }, contacts = { ['Zeb#2222'] = { share = true, map = true }, ['Kalle#3333'] = { share = true, otherFaction = false } } }")));
            var file = WriteSettings(dir, s);
            var c = new FriendsLuaTests.Client("Player-1-0000000A", "{}", db: "{ share = { enabled = false, at = 100 } }");
            c.Lua(File.ReadAllText(file));
            Assert.Equal(60, c.Lua("return ns.shareSettings().mapRate").Number);
            Assert.True(c.Lua("local s = ns.shareSettings(); return ns.allowedFor(s, s.contacts['Zeb#2222']).map").Boolean);
            Assert.True(c.Lua("local s = ns.shareSettings(); return ns.allowedFor(s, s.contacts['Zeb#2222']).otherFaction").Boolean);
            Assert.False(c.Lua("local s = ns.shareSettings(); return ns.allowedFor(s, s.contacts['Kalle#3333']).otherFaction").Boolean);
            Assert.False(c.Lua("local s = ns.shareSettings(); return ns.allowedFor(s, s.contacts['Kalle#3333']).map").Boolean);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // Config.json saved before map sharing has none of it: live, and both off.
    [Fact]
    public void An_old_config_without_map_settings_reads_as_live_and_off()
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<ShareSettings>(
            """{"Enabled":true,"NewChars":true,"Defaults":{"Gear":true},"Chars":{},"Contacts":{"Zeb#2222":{"Share":true,"Receive":true}},"Blocked":[],"At":5}""")!;
        Assert.Equal(5, old.MapRate);
        Assert.False(old.Defaults.Map);
        Assert.Null(old.Contacts["Zeb#2222"].Map);
        Assert.True(old.Defaults.Highlights); // on, like the addon's default
        Assert.Null(old.Contacts["Zeb#2222"].Highlights);
    }

    [Fact]
    public void The_addon_reads_the_highlights_choice_the_app_wrote()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wct-share-" + Guid.NewGuid().ToString("N"));
        try
        {
            var s = new FlavorSettings();
            s.TakeShare(FriendData.ReadShare(Lua("{ enabled = true, at = 200, defaults = { highlights = false }, contacts = { ['Zeb#2222'] = { share = true, highlights = true }, ['Kalle#3333'] = { share = true } } }")));
            Assert.False(s.Share!.Defaults.Highlights);
            Assert.True(s.Share.Contacts["Zeb#2222"].Highlights);
            var c = new FriendsLuaTests.Client("Player-1-0000000A", "{}", db: "{ share = { enabled = false, at = 100 } }");
            c.Lua(File.ReadAllText(WriteSettings(dir, s)));
            Assert.True(c.Lua("local s = ns.shareSettings(); return ns.allowedFor(s, s.contacts['Zeb#2222']).highlights").Boolean);
            Assert.False(c.Lua("local s = ns.shareSettings(); return ns.allowedFor(s, s.contacts['Kalle#3333']).highlights").Boolean);
            var fresh = new FriendsLuaTests.Client("Player-1-0000000A", "{}", db: "{ share = { enabled = true, at = 100 } }");
            Assert.True(fresh.Lua("return ns.allowedFor(ns.shareSettings(), nil).highlights").Boolean); // on unless turned off
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
