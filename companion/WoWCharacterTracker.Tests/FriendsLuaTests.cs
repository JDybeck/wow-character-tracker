using System.Globalization;
using MoonSharp.Interpreter;

namespace WoWCharacterTracker.Tests;

// Sharing with Battle.net friends (FriendData.lua, Comms.lua, Friends.lua) in MoonSharp, with
// Battle.net stubbed. Two clients can talk: what one sends with SendGameData is handed to the
// other's BN_CHAT_MSG_ADDON handler.
public class FriendsLuaTests
{
    internal const string Stubs = """
        function time() return math.floor(clock) end
        function strtrim(s) return (s:gsub('^%s+', ''):gsub('%s+$', '')) end
        inCombat, lockdown, groupGuids, sendResult = false, false, {}, 0
        function InCombatLockdown() return inCombat end
        function IsEncounterInProgress() return false end
        C_ChatInfo = { RegisterAddonMessagePrefix = function() end, InChatMessagingLockdown = function() return lockdown end }
        BNET_CLIENT_WOW, WOW_PROJECT_ID = 'WoW', 5
        sent, shown, said, whispers, hidden = {}, {}, {}, {}, {}
        function BNGetNumFriends() return #friends end
        C_BattleNet = {
          GetFriendAccountInfo = function(i) return friends[i] and { battleTag = friends[i].tag, bnetAccountID = friends[i].bnet or i * 100 } end,
          GetFriendNumGameAccounts = function(i) return friends[i] and 1 or 0 end,
          GetFriendGameAccountInfo = function(i)
            local f = friends[i]
            return f and { isOnline = f.online ~= false, clientProgram = 'WoW', wowProjectID = 5, gameAccountID = f.account, playerGuid = f.guid,
              characterName = f.char, className = f.className, classFilename = f.classFile, characterLevel = f.level, areaName = f.zone,
              factionName = f.faction, realmName = f.realm }
          end,
          SendGameData = function(to, prefix, text) table.insert(sent, { to = to, prefix = prefix, text = text }) return sendResult end,
          SendWhisper = function(to, text) table.insert(whispers, { to = to, text = text }) end,
        }
        function StaticPopup_Hide(name) table.insert(hidden, name) end
        function IsInGroup() return #groupGuids > 0 end
        function IsInRaid() return false end
        function GetNumGroupMembers() return #groupGuids end
        function UnitGUID(u)
          if u == 'player' then return MY_GUID end
          local i = tonumber(u:match('^party(%d+)$') or '')
          return i and groupGuids[i] or nil
        end
        StaticPopupDialogs = {}
        function StaticPopup_Show(name, arg, _, data) table.insert(shown, { name = name, arg = arg, data = data }) return {} end
        function GetAddOnMetadata() return '0.33.0' end
        """;

    internal static readonly string[] AllFiles = ["Shared.lua", "Leveling.lua", "FriendData.lua", "Comms.lua", "Friends.lua"];

    // One game client. me: this player's GUID; friendsLua: the Battle.net friends list as a Lua
    // table of { tag, account, guid, online }; db and settings: the saved file and Settings.lua.
    internal sealed class Client
    {
        public Script Script { get; } = new();
        public Table Ns { get; }
        public Table Comms => Ns.Get("comms").Table;

        public Client(string me, string friendsLua, string db = "{ chars = {}, items = {} }", string settings = "{}", string[]? files = null)
        {
            Script.DoString(AddonLuaTests.FakeTimers);
            Script.DoString(Stubs);
            Script.DoString($"MY_GUID = '{me}'; friends = {friendsLua}; WoWCharacterTrackerDB = {db}; WoWCharacterTrackerSettings = {settings}");
            Ns = new Table(Script);
            Script.Globals["ns"] = Ns;
            Ns["say"] = (Action<string>)(s => Script.Globals.Get("said").Table.Append(DynValue.NewString(s)));
            foreach (var file in files ?? AllFiles)
                Script.Call(Script.LoadString(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, file)), codeFriendlyName: file), "WoWCharacterTracker", Ns);
        }

        public DynValue Lua(string code) => Script.DoString(code);
        public DynValue Call(string name, params object[] args) => Script.Call(Ns.Get(name), args);
        public void Run(double seconds) => Script.DoString($"runTimers(clock + {seconds.ToString(CultureInfo.InvariantCulture)})");
        public List<Table> Sent() => [.. Script.Globals.Get("sent").Table.Values.Select(v => v.Table)];
        public List<string> Said() => [.. Script.Globals.Get("said").Table.Values.Select(v => v.String)];
        public void Receive(string text, int fromAccount, string prefix = "WCTF") =>
            Script.Call(Comms.Get("receive"), prefix, text, "WHISPER", fromAccount);
    }

    static string Str(Client c, string expr) => c.Lua("return " + expr).String;

    [Fact]
    public void A_popup_is_added_to_the_games_list_once_and_gets_its_data()
    {
        var c = new Client("Player-1-0000000A", "{}", files: ["Shared.lua"]);
        c.Lua("""
            ns.popup('WCT_TEST', { text = 'first %s', button1 = 'OK' }, 'x', { n = 1 })
            ns.popup('WCT_TEST', { text = 'second %s', button1 = 'OK' }, 'y', { n = 2 })
            """);
        Assert.Equal("first %s", Str(c, "StaticPopupDialogs.WCT_TEST.text"));
        Assert.True(c.Lua("return StaticPopupDialogs.WCT_TEST.hideOnEscape").Boolean);
        Assert.Equal(2, c.Lua("return #shown").Number);
        Assert.Equal("y", Str(c, "shown[2].arg"));
        Assert.Equal(2, c.Lua("return shown[2].data.n").Number);
    }

    // Ignore tracking (Settings.lua ignored, an In game setting): nothing recorded, never shared.
    [Fact]
    public void An_ignored_character_records_nothing_and_is_never_shared()
    {
        var c = new Client("Player-1-0000000A", "{}", "{ chars = { ['Player-1-0000000A'] = { lastSeen = 1 } }, items = {}, events = {} }",
            "{ ignored = { ['Player-1-0000000A'] = true } }");
        Assert.True(c.Lua("return ns.isIgnored('Player-1-0000000A')").Boolean);
        Assert.False(c.Lua("return ns.isIgnored('Player-1-0000000B')").Boolean);
        Assert.True(c.Lua("return ns.record('level', { level = 5 })").IsNil());
        Assert.Equal(0, c.Lua("return #WoWCharacterTrackerDB.events").Number);
        Assert.False(c.Lua("return ns.isCharShared(ns.shareSettings(), 'Player-1-0000000A')").Boolean);

        c.Lua("ns.editSettings(function(s) s.ignored['Player-1-0000000A'] = nil end)"); // turned off in game
        Assert.False(c.Lua("return ns.isIgnored('Player-1-0000000A')").Boolean);
        Assert.True(c.Lua("return ns.isCharShared(ns.shareSettings(), 'Player-1-0000000A')").Boolean);
    }

    static readonly string[] DataFiles = ["Shared.lua", "FriendData.lua"];

    const string Deadbeck = "{ char = 'Deadbeck', realm = 'Firemaw', level = 42, class = 'Warrior', classFile = 'WARRIOR', race = 'Orc', raceFile = 'Orc', faction = 'Horde', ilvl = 38, spec = 'Arms', zone = 'Stranglethorn Vale', money = 123456, talents = 'a;b\\\\c', guild = 'Secret', lastSeen = 900 }";

    [Fact]
    public void A_character_goes_as_one_line_with_only_the_allowed_categories_and_comes_back_whole()
    {
        var c = new Client("Player-1-0000000A", "{}", files: DataFiles);
        c.Lua($$"""
            char = {{Deadbeck}}
            items = { gear = '1~19019,16~12345', enchants = '16~12345~Crusader~1900\n5~3000~+4 Stats~1891' }
            text = ns.encodeFriendChar('Player-1-0000000A', char, items, { gear = true, talents = true })
            back = ns.decodeFriendChar(text)
            """);
        Assert.DoesNotContain("\n", Str(c, "text"));
        Assert.Equal("Player-1-0000000A", Str(c, "back.guid"));
        Assert.Equal("Deadbeck", Str(c, "back.char"));
        Assert.Equal(42, c.Lua("return back.level").Number);
        Assert.Equal(900, c.Lua("return back.at").Number);
        Assert.Equal("a;b\\c", Str(c, "back.talents"));
        Assert.Equal("1~19019,16~12345", Str(c, "back.gear"));
        Assert.Equal("16~12345~Crusader~1900\n5~3000~+4 Stats~1891", Str(c, "back.enchants"));
        Assert.True(c.Lua("return back.zone").IsNil());  // location not allowed
        Assert.True(c.Lua("return back.money").IsNil()); // gold not allowed
        Assert.True(c.Lua("return back.guild").IsNil()); // never sent
    }

    // Professions are their own category, on unless turned off, and capped at 256 characters.
    [Fact]
    public void Professions_go_only_when_allowed_and_are_cut_to_length()
    {
        var c = new Client("Player-1-0000000A", "{}", files: DataFiles);
        c.Lua($$"""
            char = {{Deadbeck}}
            char.professions = 'Mining 150/225, Herbalism 75/150'
            with = ns.decodeFriendChar(ns.encodeFriendChar('Player-1-0000000A', char, nil, { professions = true }))
            without = ns.decodeFriendChar(ns.encodeFriendChar('Player-1-0000000A', char, nil, {}))
            long = ns.decodeFriendChar('guid=Player-1-0000000B;char=Zeb;professions=' .. string.rep('x', 300))
            """);
        Assert.Equal("Mining 150/225, Herbalism 75/150", Str(c, "with.professions"));
        Assert.True(c.Lua("return without.professions").IsNil());
        Assert.Equal(256, c.Lua("return #long.professions").Number);
    }

    // Highlights: on unless turned off, the newest 10 as lines, gold and zone of a death only with
    // their own categories, group members never.
    [Fact]
    public void Highlights_go_newest_first_with_only_what_the_friend_may_see()
    {
        var c = new Client("Player-1-0000000A", "{}", files: DataFiles);
        c.Lua($$"""
            char = {{Deadbeck}}
            events = {
              { kind = 'boss', at = 100, level = 60, boss = 'Ragnaros', instance = 'Molten~Core', groupSize = 40, group = 'Zeb~60~WARRIOR~Warrior~Orc~Firemaw' },
              { kind = 'death', at = 300, level = 42, killer = 'Hogger', played = 3600, gold = 5000, zone = 'Elwynn Forest' },
              { kind = 'level', at = 400, level = 43 },
              { kind = 'duel', at = 200, level = 42, opponent = 'Grim|tusk', lost = true },
            }
            with = ns.decodeFriendChar(ns.encodeFriendChar('Player-1-0000000A', char, nil, { highlights = true }, events))
            all = ns.decodeFriendChar(ns.encodeFriendChar('Player-1-0000000A', char, nil, { highlights = true, gold = true, location = true }, events))
            without = ns.decodeFriendChar(ns.encodeFriendChar('Player-1-0000000A', char, nil, { gold = true }, events))
            many = {}
            for i = 1, 30 do many[i] = { kind = 'loot', at = i, level = 42, item = 'Item ' .. i, itemId = i, quality = 3 } end
            capped = ns.decodeFriendChar(ns.encodeFriendChar('Player-1-0000000A', char, nil, { highlights = true }, many))
            """);
        Assert.Equal("death~300~42~Hogger~3600~~\nduel~200~42~Grimtusk~1~\nboss~100~60~Ragnaros~MoltenCore~40", Str(c, "with.highlights"));
        Assert.StartsWith("death~300~42~Hogger~3600~5000~Elwynn Forest\n", Str(c, "all.highlights"));
        Assert.True(c.Lua("return without.highlights").IsNil());
        Assert.Equal(10, c.Lua("local n = 0; for _ in capped.highlights:gmatch('[^\\n]+') do n = n + 1 end; return n").Number);
        Assert.StartsWith("loot~30~42~Item 30~30~3\n", Str(c, "capped.highlights"));
    }

    // Incognito with friends paused: highlights from that stretch never reach friends, later ones do.
    [Fact]
    public void Highlights_from_an_incognito_stretch_never_reach_friends()
    {
        var a = new Client(A, AFriends, ADb(AShares).Replace("items = {", """
            incognito = { { from = 700, ['until'] = 900, friends = true } },
            events = { { kind = 'loot', guid = 'Player-1-0000000A', at = 800, level = 42, item = 'Thunderfury', itemId = 19019, quality = 5 },
                       { kind = 'boss', guid = 'Player-1-0000000A', at = 950, level = 42, boss = 'Onyxia', groupSize = 40 } },
            items = {
            """));
        var b = new Client(B, BFriends, BDb(BReceives));
        a.Call("friendsTick");
        b.Call("friendsTick");
        Talk(a, b);
        Assert.Equal("boss~950~42~Onyxia~~40", Str(b, GotDeadbeck + ".highlights"));
    }

    // Incognito: one stretch at a time, extended when started again, ended early by off; what
    // happened inside one is covered also after it's over.
    [Fact]
    public void Incognito_stretches_start_extend_and_end()
    {
        var c = new Client(A, "{}", files: ["Shared.lua"]);
        c.Lua("WoWCharacterTrackerDB = {}; ns.startIncognito(3600, false)");
        Assert.True(c.Lua("return ns.incognitoActive() ~= nil").Boolean);
        c.Run(600);
        c.Lua("ns.startIncognito(7200, true)"); // extended, now with friends
        Assert.Equal(1, c.Lua("return #WoWCharacterTrackerDB.incognito").Number);
        Assert.True(c.Lua("return ns.inIncognito(clock - 300, true)").Boolean);
        c.Lua("ns.endIncognito()");
        Assert.True(c.Lua("return ns.incognitoActive() == nil").Boolean);
        Assert.True(c.Lua("return ns.inIncognito(clock - 300)").Boolean); // still covered afterwards
        c.Run(60);
        Assert.False(c.Lua("return ns.inIncognito(clock)").Boolean);
    }

    // Each character's newest highlights reach a contact, kept up to date as new events come in.
    [Fact]
    public void A_contact_gets_the_newest_highlights_and_new_ones_after_them()
    {
        var a = new Client(A, AFriends, ADb(AShares).Replace("items = {", """
            events = { { kind = 'loot', guid = 'Player-1-0000000A', at = 800, level = 42, item = 'Thunderfury', itemId = 19019, quality = 5 },
                       { kind = 'mount', guid = 'Player-1-0000000C', at = 450, level = 12, mount = 'Horse', itemId = 2411 } },
            items = {
            """));
        var b = new Client(B, BFriends, BDb(BReceives));
        a.Call("friendsTick");
        b.Call("friendsTick");
        Talk(a, b);
        Assert.Equal("loot~800~42~Thunderfury~19019~5", Str(b, GotDeadbeck + ".highlights")); // only Deadbeck's own
        a.Lua("table.insert(WoWCharacterTrackerDB.events, { kind = 'boss', guid = 'Player-1-0000000A', at = 950, level = 42, boss = 'Onyxia', groupSize = 40 })");
        a.Run(700); // past ONLINE_EVERY
        a.Call("friendsTick");
        Talk(a, b);
        Assert.StartsWith("boss~950~42~Onyxia~~40\n", Str(b, GotDeadbeck + ".highlights"));
    }

    [Fact]
    public void What_a_friend_sends_is_cleaned_field_by_field()
    {
        var c = new Client("Player-1-0000000A", "{}", files: DataFiles);
        c.Lua("""
            back = ns.decodeFriendChar('guid=Player-1-0000000B;char=Ze|cffff0000b|r;level=abc;ilvl=1e999;spec=' .. string.rep('x', 100)
              .. ';evil=1;zone=Elwynn\\nForest;enchants=1~2~A\\n3~4~B')
            """);
        Assert.Equal("Zecffff0000br", Str(c, "back.char")); // no | escapes
        Assert.True(c.Lua("return back.level").IsNil());    // not a number
        Assert.True(c.Lua("return back.ilvl").IsNil());     // not a finite number
        Assert.Equal(32, c.Lua("return #back.spec").Number);
        Assert.True(c.Lua("return back.evil").IsNil());
        Assert.Equal("ElwynnForest", Str(c, "back.zone"));  // line breaks only in enchants
        Assert.Equal("1~2~A\n3~4~B", Str(c, "back.enchants"));
    }

    [Theory]
    [InlineData("char=Zeb")]                  // no guid
    [InlineData("guid=Creature-0-1;char=Zeb")] // not a player
    [InlineData("guid=Player-1-0000000B")]     // no name
    public void Text_that_isnt_a_character_is_dropped(string text)
    {
        var c = new Client("Player-1-0000000A", "{}", files: DataFiles);
        Assert.True(c.Lua($"return ns.decodeFriendChar('{text}')").IsNil());
    }

    // Forever's surnames go along with the name, like the name itself (no category needed).
    [Fact]
    public void A_surname_goes_with_the_character()
    {
        var c = new Client("Player-1-0000000A", "{}", files: DataFiles);
        c.Lua("""
            text = ns.encodeFriendChar('Player-1-0000000A', { char = 'Zeb', surname = 'Stonehand', lastSeen = 1 }, nil, {})
            back = ns.decodeFriendChar(text)
            """);
        Assert.Equal("Stonehand", Str(c, "back.surname"));
    }

    // The character's sex goes along too (for portraits and the details), only as male or female.
    [Fact]
    public void Sex_goes_with_the_character_as_male_or_female_only()
    {
        var c = new Client("Player-1-0000000A", "{}", files: DataFiles);
        c.Lua("""
            text = ns.encodeFriendChar('Player-1-0000000A', { char = 'Zeb', sex = 'female', lastSeen = 1 }, nil, {})
            back = ns.decodeFriendChar(text)
            odd = ns.decodeFriendChar('guid=Player-1-0000000A;char=Zeb;sex=robot')
            """);
        Assert.Equal("female", Str(c, "back.sex"));
        Assert.True(c.Lua("return odd.sex").IsNil());
    }

    [Fact]
    public void Text_over_8_kb_is_dropped()
    {
        var c = new Client("Player-1-0000000A", "{}", files: DataFiles);
        Assert.True(c.Lua("return ns.decodeFriendChar('guid=Player-1-0000000B;char=Zeb;talents=' .. string.rep('x', 8200))").IsNil());
    }

    [Fact]
    public void The_change_key_ignores_when_the_snapshot_was_taken()
    {
        var c = new Client("Player-1-0000000A", "{}", files: DataFiles);
        c.Lua($$"""
            char = {{Deadbeck}}
            t1, k1 = ns.encodeFriendChar('Player-1-0000000A', char, nil, {})
            char.lastSeen = 1900
            t2, k2 = ns.encodeFriendChar('Player-1-0000000A', char, nil, {})
            """);
        Assert.NotEqual(Str(c, "t1"), Str(c, "t2"));
        Assert.Equal(Str(c, "k1"), Str(c, "k2"));
    }

    static readonly string[] CommsFiles = ["Shared.lua", "Comms.lua"];

    // A client with only Comms.lua, whose whole messages land in the Lua table `got`.
    static Client CommsClient()
    {
        var c = new Client("Player-1-0000000A", "{}", files: CommsFiles);
        c.Lua("got = {}; ns.comms.onReceive = function(from, kind, body) table.insert(got, { from = from, kind = kind, body = body }) end");
        return c;
    }

    [Fact]
    public void A_long_text_goes_in_parts_and_comes_back_whole_in_any_order()
    {
        var c = CommsClient();
        c.Lua("long = string.rep('abcdefghij', 70); ns.comms.send(2, 'DATA', long)"); // 700 bytes: 3 parts
        c.Run(5);
        var sent = c.Sent();
        Assert.Equal(3, sent.Count);
        Assert.All(sent, m =>
        {
            Assert.Equal("WCTF", m.Get("prefix").String);
            Assert.Equal(2, m.Get("to").Number);
            Assert.StartsWith("1:DATA:", m.Get("text").String);
        });
        foreach (var m in Enumerable.Reverse(sent)) c.Receive(m.Get("text").String, 7);
        Assert.Equal(1, c.Lua("return #got").Number);
        Assert.Equal(7, c.Lua("return got[1].from").Number);
        Assert.Equal("DATA", Str(c, "got[1].kind"));
        Assert.Equal(Str(c, "long"), Str(c, "got[1].body"));
    }

    [Fact]
    public void Pipes_are_dropped_both_ways_and_strangers_prefixes_ignored()
    {
        var c = CommsClient();
        c.Lua("ns.comms.send(2, 'HI', 'a|b')");
        Assert.Equal("1:HI:1:1:1:ab", c.Sent()[0].Get("text").String);
        c.Receive("1:HI:1:1:1:x", 7, prefix: "OTHER");
        c.Receive("garbage", 7);
        c.Receive("1:HI:5:1:1:a|b", 7);
        Assert.Equal(1, c.Lua("return #got").Number);
        Assert.Equal("ab", Str(c, "got[1].body"));
    }

    [Fact]
    public void Another_protocol_is_reported_as_its_version()
    {
        var c = CommsClient();
        c.Receive("2:HI:1:1:1:0.40.0", 7);
        Assert.Equal("VERSION", Str(c, "got[1].kind"));
        Assert.Equal("2", Str(c, "got[1].body"));
    }

    [Fact]
    public void Half_a_message_is_dropped_after_30_seconds()
    {
        var c = CommsClient();
        c.Receive("1:DATA:9:1:2:abc", 7);
        c.Run(31);
        c.Receive("1:DATA:9:2:2:def", 7);
        Assert.Equal(0, c.Lua("return #got").Number);
        c.Receive("1:DATA:10:1:2:abc", 7);
        c.Receive("1:DATA:10:2:2:def", 7);
        Assert.Equal("abcdef", Str(c, "got[1].body"));
    }

    [Fact]
    public void At_most_eight_half_messages_wait_at_once()
    {
        var c = CommsClient();
        for (var id = 1; id <= 9; id++) c.Receive($"1:DATA:{id}:1:2:x", 7);
        c.Receive("1:DATA:9:2:2:y", 7); // the ninth was never kept
        Assert.Equal(0, c.Lua("return #got").Number);
        c.Receive("1:DATA:1:2:2:y", 7);
        Assert.Equal("xy", Str(c, "got[1].body"));
    }

    [Theory]
    [InlineData("inCombat")]
    [InlineData("lockdown")]
    public void Nothing_goes_out_in_combat_or_chat_lockdown_and_it_goes_after(string flag)
    {
        var c = CommsClient();
        c.Lua($"{flag} = true; ns.comms.send(2, 'HI', 'x')");
        c.Run(20);
        Assert.Empty(c.Sent());
        c.Lua($"{flag} = false");
        c.Run(6);
        Assert.Single(c.Sent());
    }

    // Map messages go in combat and pass what waits for it to end; the chat lockdown holds them too.
    [Fact]
    public void Map_messages_go_in_combat_past_what_waits()
    {
        var c = CommsClient();
        c.Lua("inCombat = true; ns.comms.send(2, 'HI', 'x'); ns.comms.send(2, 'POS', 'p'); ns.comms.send(2, 'WATCH', '')");
        c.Run(20);
        Assert.Equal(["POS", "WATCH"], c.Sent().Select(m => m.Get("text").String.Split(':')[1]));
        c.Lua("inCombat = false");
        c.Run(6);
        Assert.Equal("HI", c.Sent().Last().Get("text").String.Split(':')[1]);

        c.Lua("lockdown = true; ns.comms.send(2, 'POS', 'q')");
        c.Run(20);
        Assert.Equal(3, c.Sent().Count);
    }

    [Fact]
    public void A_throttled_send_is_tried_three_times_then_dropped()
    {
        var c = CommsClient();
        c.Lua("sendResult = 3; ns.comms.send(2, 'HI', 'x')");
        c.Run(60);
        Assert.Equal(3, c.Sent().Count);
        c.Lua("sendResult = 0; ns.comms.send(2, 'HI', 'y')");
        c.Run(1);
        Assert.Equal("1:HI:2:1:1:y", c.Sent()[3].Get("text").String);
    }

    [Fact]
    public void A_text_over_40_parts_is_refused()
    {
        var c = CommsClient();
        Assert.False(c.Lua("return ns.comms.send(2, 'DATA', string.rep('x', 240 * 40 + 1))").Boolean);
        Assert.Empty(c.Sent());
    }

    [Fact]
    public void A_handler_error_does_not_escape_the_receive()
    {
        var c = CommsClient();
        c.Lua("ns.comms.onReceive = function() error('boom') end");
        c.Receive("1:HI:1:1:1:x", 7); // would throw without the guard
        Assert.Contains("boom", Str(c, "ns.lastCommsError"));
    }

    const string A = "Player-1-0000000A", B = "Player-1-0000000B";
    const int AAccount = 1, BAccount = 2; // each one's game account as the other's friends list has it
    const string AFriends = "{ { tag = 'Zeb#2222', account = 2, guid = 'Player-1-0000000B' } }";
    const string BFriends = "{ { tag = 'JDybeck#1111', account = 1, guid = 'Player-1-0000000A' } }";
    const string AShares = "{ enabled = true, contacts = { ['Zeb#2222'] = { share = true, receive = true, location = true } }, chars = { ['Player-1-0000000D'] = false }, at = 100 }";
    const string BReceives = "{ enabled = true, contacts = { ['JDybeck#1111'] = { receive = true } }, at = 100 }";
    const string GotDeadbeck = "WoWCharacterTrackerDB.friends['JDybeck#1111'].chars['Player-1-0000000A']";

    // A's saved file: the character being played (A), an alt (C) and a hidden alt (D).
    static string ADb(string share) => $$"""
        { share = {{share}}, chars = {
            ['Player-1-0000000A'] = {{Deadbeck}},
            ['Player-1-0000000C'] = { char = 'Drygbeck', realm = 'Firemaw', level = 12, class = 'Mage', classFile = 'MAGE', lastSeen = 500 },
            ['Player-1-0000000D'] = { char = 'Hiddenbeck', realm = 'Firemaw', level = 5, class = 'Rogue', classFile = 'ROGUE', lastSeen = 400 } },
          items = { ['Player-1-0000000A'] = { gear = '1~19019,16~12345' } } }
        """;

    static string BDb(string share) => $"{{ share = {share}, chars = {{}}, items = {{}} }}";

    // Hands what from sent to to, as fromAccount (how to's friends list knows from).
    internal static void Deliver(Client from, Client to, int fromAccount)
    {
        var messages = from.Sent();
        from.Lua("sent = {}");
        foreach (var m in messages) to.Receive(m.Get("text").String, fromAccount, m.Get("prefix").String);
    }

    // Both clients send what they queued and answer each other, a few rounds.
    internal static void Talk(Client a, Client b, int rounds = 4)
    {
        for (var i = 0; i < rounds; i++)
        {
            a.Run(1);
            b.Run(1);
            Deliver(a, b, AAccount);
            Deliver(b, a, BAccount);
        }
    }

    static int Count(Client c, string kind) => c.Sent().Count(m => m.Get("text").String.Contains($":{kind}:"));

    // Map position (off) and the other faction rule (off) work like the categories: general, or per friend.
    [Fact]
    public void Map_position_and_the_other_faction_are_off_unless_turned_on_and_can_be_set_per_friend()
    {
        var a = new Client(A, AFriends, ADb(AShares));
        Assert.False(a.Lua("return ns.allowedFor(ns.shareSettings(), nil).map").Boolean);
        Assert.False(a.Lua("return ns.allowedFor(ns.shareSettings(), nil).otherFaction").Boolean);
        a.Call("friendsCommand", "set Zeb#2222 map on");
        a.Call("friendsCommand", "set zeb#2222 OtherFaction on"); // any case
        Assert.True(a.Lua("local s = ns.shareSettings(); return ns.allowedFor(s, s.contacts['Zeb#2222']).map").Boolean);
        Assert.True(a.Lua("return ns.shareSettings().contacts['Zeb#2222'].otherFaction").Boolean);
        a.Call("friendsCommand", "set all map on");
        Assert.True(a.Lua("return ns.shareSettings().defaults.map").Boolean);
        Assert.Contains("Friends now get map: on.", a.Said());
        Assert.Equal("gear, talents, location, professions, highlights, level times, map position", Str(a, "ns.shareSummary(ns.allowedFor(ns.shareSettings(), nil))"));
    }

    [Fact]
    public void Contact_sharing_lists_the_other_faction_rule_after_the_categories()
    {
        var a = new Client(A, AFriends, ADb(AShares));
        a.Lua("o = ns.contactSharing('Zeb#2222')");
        Assert.Equal(9, a.Lua("return #o.categories").Number);
        Assert.Equal("map", Str(a, "o.categories[8].key"));
        Assert.Equal("otherFaction", Str(a, "o.categories[9].key"));
        Assert.True(a.Lua("return o.categories[9].rule").Boolean);
        Assert.True(a.Lua("return o.categories[8].rule").IsNil());
        a.Call("cycleContactCategory", "Zeb#2222", "otherFaction");
        Assert.True(a.Lua("return ns.shareSettings().contacts['Zeb#2222'].otherFaction").Boolean);
    }

    [Theory]
    [InlineData("live", 5)]
    [InlineData("1m", 60)]
    [InlineData("5M", 300)]
    public void The_map_rate_is_live_unless_set_to_one_of_the_steps(string word, int rate)
    {
        var a = new Client(A, AFriends, ADb("{ enabled = true, mapRate = 7, at = 100 }"));
        Assert.Equal(5, a.Lua("return ns.shareSettings().mapRate").Number); // 7 isn't a step
        a.Call("friendsCommand", "maprate " + word);
        Assert.Equal(rate, a.Lua("return ns.shareSettings().mapRate").Number);
        a.Call("friendsCommand", "maprate 2m");
        Assert.Equal(rate, a.Lua("return ns.shareSettings().mapRate").Number); // unchanged
        Assert.Contains(a.Said(), s => s.StartsWith("/wct friends maprate", StringComparison.Ordinal));
    }

    [Fact]
    public void Online_friends_carry_their_faction_and_realm()
    {
        var a = new Client(A, "{ { tag = 'Zeb#2222', account = 2, guid = 'Player-1-0000000B', faction = 'Alliance', realm = 'Firemaw' } }");
        Assert.Equal("Alliance", Str(a, "ns.friendsOnline()[1].faction"));
        Assert.Equal("Firemaw", Str(a, "ns.onlineFriends()[1].realm"));
        Assert.Equal("Alliance", Str(a, "ns.onlineFriends()[1].faction"));
    }

    // FriendMap.lua adds its own kinds through this; unknown kinds are still dropped quietly.
    [Fact]
    public void Another_file_can_handle_a_kind_and_unknown_kinds_are_dropped()
    {
        var a = new Client(A, AFriends, ADb(AShares));
        a.Lua("got = nil; ns.onFriendKind('ZZ', function(f, contact, body) got = f.tag .. ':' .. body end)");
        a.Receive("1:ZZ:4:1:1:hello", BAccount);
        Assert.Equal("Zeb#2222:hello", Str(a, "got"));
        a.Receive("1:QQ:5:1:1:x", BAccount);
        Assert.True(a.Lua("return ns.lastCommsError").IsNil());
    }

    // On by default, but nothing goes to anyone until a request is allowed (or you share yourself).
    [Fact]
    public void Sharing_is_on_by_default_but_only_a_request_starts_anything()
    {
        var a = new Client(A, AFriends, ADb("nil"));
        Assert.True(a.Lua("return ns.shareSettings().enabled").Boolean);
        Assert.True(a.Lua("return ns.allowedFor(ns.shareSettings(), {}).location").Boolean); // location on by default
        Assert.True(a.Lua("return ns.allowedFor(ns.shareSettings(), {}).professions").Boolean); // and professions
        a.Call("friendsTick");
        a.Receive("1:DATA:2:1:1:guid=Player-1-0000000B;char=Zeb", BAccount);
        a.Run(5);
        Assert.Empty(a.Sent());
        Assert.True(a.Lua("return WoWCharacterTrackerDB.friends").IsNil());
        a.Receive("1:REQ:3:1:1:", BAccount);
        Assert.Equal(1, a.Lua("return #shown").Number); // the popup asks first
        Assert.Empty(a.Sent());
    }

    // One line a minute after a real login (never a /reload) while sharing is on but you share with
    // nobody; nothing while you share, or with sharing off.
    [Fact]
    public void The_sharing_reminder_comes_once_a_minute_after_login_while_you_share_with_nobody()
    {
        static bool Reminder(string s) => s.StartsWith("You're not sharing with any friends", StringComparison.Ordinal);
        var a = new Client(A, AFriends, ADb("nil"));
        a.Call("remindSharing", false); // a /reload
        a.Run(120);
        Assert.DoesNotContain(a.Said(), Reminder);
        a.Call("remindSharing", true);
        a.Run(30);
        Assert.DoesNotContain(a.Said(), Reminder); // not yet
        a.Run(40);
        Assert.Single(a.Said(), Reminder);
        a.Run(3600);
        Assert.Single(a.Said(), Reminder); // and not again

        var sharing = new Client(A, AFriends, ADb(AShares));
        sharing.Call("maybeShareTip");
        Assert.DoesNotContain(sharing.Said(), Reminder);
        var off = new Client(A, AFriends, ADb("{ enabled = false, at = 100 }"));
        off.Call("maybeShareTip");
        Assert.Empty(off.Said());
    }

    // In WoW chat | starts a code (|t is a texture): "gear|talents" showed as "gearalents".
    [Fact]
    public void Friends_help_lines_have_no_raw_pipes()
    {
        var a = new Client(A, AFriends, ADb("nil"));
        a.Call("friendsCommand", "help");
        a.Call("friendsCommand", "set all nonsense on");
        Assert.NotEmpty(a.Said());
        Assert.All(a.Said(), s => Assert.DoesNotContain("|", s));
        Assert.Contains(a.Said(), s => s.Contains("gear/talents/location/professions/highlights/leveling/gold", StringComparison.Ordinal));
    }

    [Fact]
    public void Sharing_turned_off_sends_and_keeps_nothing()
    {
        var a = new Client(A, AFriends, ADb("{ enabled = false, at = 100 }"));
        a.Call("friendsTick");
        a.Run(5);
        Assert.Empty(a.Sent());
        a.Receive("1:HI:1:1:1:0.33.0", BAccount);
        a.Receive("1:DATA:2:1:1:guid=Player-1-0000000B;char=Zeb", BAccount);
        a.Run(5);
        Assert.Empty(a.Sent());
        Assert.True(a.Lua("return WoWCharacterTrackerDB.friends").IsNil());
    }

    [Fact]
    public void A_contact_gets_the_character_being_played_with_only_what_they_may_see()
    {
        var a = new Client(A, AFriends, ADb(AShares));
        var b = new Client(B, BFriends, BDb(BReceives));
        a.Call("friendsTick");
        b.Call("friendsTick");
        Talk(a, b);
        Assert.Equal("Stranglethorn Vale", Str(b, GotDeadbeck + ".zone")); // location: Zeb's own setting
        Assert.Equal("1~19019,16~12345", Str(b, GotDeadbeck + ".gear"));   // gear: the general one
        Assert.True(b.Lua($"return {GotDeadbeck}.money").IsNil());          // gold: off by default
        Assert.Equal("Drygbeck", Str(b, "WoWCharacterTrackerDB.friends['JDybeck#1111'].chars['Player-1-0000000C'].char")); // the alt B had no copy of (HAVE, WANT)
        Assert.True(b.Lua("return WoWCharacterTrackerDB.friends['JDybeck#1111'].chars['Player-1-0000000D']").IsNil()); // never a hidden one
        Assert.Equal("0.33.0", Str(b, "WoWCharacterTrackerDB.friends['JDybeck#1111'].version"));
        Assert.True(a.Lua("local f = WoWCharacterTrackerDB.friends; return not (f and f['Zeb#2222'] and next(f['Zeb#2222'].chars) ~= nil)").Boolean); // B shares nothing
    }

    [Fact]
    public void What_a_friend_gets_is_the_general_choice_unless_set_for_them()
    {
        var a = new Client(A, AFriends, ADb("{ enabled = true, defaults = { gold = true, gear = false }, contacts = { ['Zeb#2222'] = { share = true, gear = true, talents = false } }, at = 100 }"));
        a.Lua("s = ns.shareSettings(); allowed = ns.allowedFor(s, s.contacts['Zeb#2222']); general = ns.allowedFor(s, {})");
        Assert.True(a.Lua("return allowed.gold").Boolean);     // general
        Assert.True(a.Lua("return allowed.gear").Boolean);     // their own beats the general off
        Assert.False(a.Lua("return allowed.talents").Boolean); // their own beats the general on
        Assert.True(a.Lua("return allowed.location").Boolean);  // the general default, on
        Assert.False(a.Lua("return general.gear").Boolean);
        var fresh = new Client(A, AFriends, ADb("{ enabled = true, at = 100 }"));
        Assert.False(fresh.Lua("return ns.allowedFor(ns.shareSettings(), {}).gold").Boolean); // gold is off until turned on
        Assert.True(fresh.Lua("return ns.allowedFor(ns.shareSettings(), {}).gear").Boolean);
    }

    [Fact]
    public void Two_clients_go_quiet_after_saying_hello()
    {
        const string BShares = "{ enabled = true, contacts = { ['JDybeck#1111'] = { share = true, receive = true } }, at = 100 }";
        var a = new Client(A, AFriends, ADb(AShares));
        var b = new Client(B, BFriends, BDb(BShares).Replace("chars = {}", "chars = { ['Player-1-0000000B'] = { char = 'Zeb', lastSeen = 1 } }"));
        a.Call("friendsTick");
        b.Call("friendsTick");
        Talk(a, b);
        a.Run(1);
        b.Run(1);
        Assert.Empty(a.Sent());
        Assert.Empty(b.Sent());
        Assert.Equal("Zeb", Str(a, "WoWCharacterTrackerDB.friends['Zeb#2222'].chars['Player-1-0000000B'].char"));
    }

    [Fact]
    public void Data_from_someone_we_dont_keep_data_from_is_dropped()
    {
        var b = new Client(B, BFriends, BDb("{ enabled = true, contacts = { ['JDybeck#1111'] = { receive = false } }, at = 100 }"));
        b.Receive("1:DATA:1:1:1:guid=Player-1-0000000A;char=Deadbeck", AAccount);
        b.Receive("1:DATA:2:1:1:guid=Player-1-0000000E;char=Stranger", 99); // not a Battle.net friend
        Assert.True(b.Lua("return WoWCharacterTrackerDB.friends").IsNil());
    }

    // A after saying hello to B and getting B's answer (so A knows B has the addon).
    static Client HelloDone(string groupGuids = "{}")
    {
        var a = new Client(A, AFriends, ADb(AShares));
        a.Lua($"groupGuids = {groupGuids}");
        a.Call("friendsTick");
        a.Receive("1:HEY:1:1:1:0.33.0", BAccount);
        a.Run(1);
        Assert.Equal(1, Count(a, "DATA"));
        a.Lua("sent = {}");
        return a;
    }

    [Fact]
    public void The_character_being_played_goes_when_due_and_changed_or_as_a_heartbeat()
    {
        var a = HelloDone();
        a.Lua("WoWCharacterTrackerDB.chars['Player-1-0000000A'].zone = 'Ratchet'");
        a.Run(200);
        a.Call("friendsTick");
        a.Run(1);
        Assert.Equal(0, Count(a, "DATA")); // changed, but 5 minutes not up
        a.Lua("WoWCharacterTrackerDB.chars['Player-1-0000000A'].zone = 'Stranglethorn Vale'");
        a.Run(300);
        a.Call("friendsTick");
        a.Run(1);
        Assert.Equal(0, Count(a, "DATA")); // up, but nothing changed
        a.Lua("WoWCharacterTrackerDB.chars['Player-1-0000000A'].zone = 'Booty Bay'");
        a.Call("friendsTick");
        a.Run(1);
        Assert.Equal(1, Count(a, "DATA"));
        a.Lua("sent = {}");
        a.Run(1800);
        a.Call("friendsTick");
        a.Run(1);
        Assert.Equal(1, Count(a, "DATA")); // the heartbeat
    }

    [Theory]
    [InlineData("{ 'Player-1-0000000B' }", 1)]
    [InlineData("{}", 0)]
    public void A_contact_in_your_group_gets_it_every_2_minutes(string group, int expected)
    {
        var a = HelloDone(group);
        a.Lua("WoWCharacterTrackerDB.chars['Player-1-0000000A'].zone = 'Booty Bay'");
        a.Run(121);
        a.Call("friendsTick");
        a.Run(1);
        Assert.Equal(expected, Count(a, "DATA"));
    }

    [Fact]
    public void At_most_50_characters_are_kept_per_contact()
    {
        var b = new Client(B, BFriends, BDb(BReceives));
        for (var i = 1; i <= 55; i++)
            b.Receive($"1:DATA:{i}:1:1:guid=Player-1-{i:X8};char=Alt{i};at={i}", AAccount);
        Assert.Equal(50, b.Lua("local n = 0; for _ in pairs(WoWCharacterTrackerDB.friends['JDybeck#1111'].chars) do n = n + 1 end; return n").Number);
        Assert.True(b.Lua("return WoWCharacterTrackerDB.friends['JDybeck#1111'].chars['Player-1-00000001']").IsNil()); // seen longest ago
        Assert.False(b.Lua("return WoWCharacterTrackerDB.friends['JDybeck#1111'].chars['Player-1-00000037']").IsNil()); // the 55th
    }

    [Fact]
    public void The_newest_sharing_settings_win_and_an_edit_in_game_is_newer_than_the_apps()
    {
        var a = new Client(A, AFriends, ADb("{ enabled = false, at = 100 }"), settings: "{ share = { enabled = true, at = 200 } }");
        Assert.True(a.Lua("return ns.shareSettings().enabled").Boolean);
        a.Lua("clock = 150; ns.editShare(function(s) s.newChars = false end)");
        Assert.Equal(201, a.Lua("return WoWCharacterTrackerDB.share.at").Number);
        Assert.False(a.Lua("return ns.shareSettings().newChars").Boolean);
        Assert.True(a.Lua("return ns.shareSettings().enabled").Boolean); // the app's copy was the base
    }

    [Fact]
    public void Sharing_settings_changed_in_game_survive_an_app_that_doesnt_know_them()
    {
        var a = new Client(A, AFriends, ADb(AShares), settings: "{ inGameAt = 99999, announce = {} }");
        Assert.True(a.Lua("return ns.shareSettings().enabled").Boolean);
        Assert.True(a.Lua("return ns.shareSettings().contacts['Zeb#2222'].share").Boolean);
    }

    // A and B after saying hello; B keeps A's data, A shares with B.
    static (Client A, Client B) Friends()
    {
        var a = new Client(A, AFriends, ADb(AShares));
        var b = new Client(B, BFriends, BDb(BReceives));
        a.Call("friendsTick");
        b.Call("friendsTick");
        Talk(a, b);
        return (a, b);
    }

    [Fact]
    public void Asking_gets_the_shared_alts_but_never_a_hidden_one()
    {
        var (a, b) = Friends();
        b.Call("friendsCommand", "ask jdybeck#1111"); // any case
        Talk(a, b);
        Assert.Equal("Drygbeck", Str(b, "WoWCharacterTrackerDB.friends['JDybeck#1111'].chars['Player-1-0000000C'].char"));
        Assert.True(b.Lua("return WoWCharacterTrackerDB.friends['JDybeck#1111'].chars['Player-1-0000000D']").IsNil());
        Assert.Contains(b.Said(), s => s.StartsWith("Asked JDybeck#1111"));
    }

    // Characters removed in the app (Settings.lua removed) are never shared, until played again.
    [Fact]
    public void Characters_removed_in_the_app_are_never_shared()
    {
        var a = new Client(A, AFriends, ADb(AShares), settings: "{ removed = { ['Player-1-0000000C'] = 600 } }"); // Drygbeck, last seen 500
        var b = new Client(B, BFriends, BDb(BReceives));
        a.Call("friendsTick");
        b.Call("friendsTick");
        Talk(a, b);
        b.Call("friendsCommand", "ask JDybeck#1111");
        Talk(a, b);
        Assert.True(b.Lua("return WoWCharacterTrackerDB.friends['JDybeck#1111'].chars['Player-1-0000000C']").IsNil());
        Assert.False(a.Lua("return ns.isCharShared(ns.shareSettings(), 'Player-1-0000000C')").Boolean);
        a.Lua("WoWCharacterTrackerDB.chars['Player-1-0000000C'].lastSeen = 700"); // played again
        Assert.True(a.Lua("return ns.isCharShared(ns.shareSettings(), 'Player-1-0000000C')").Boolean);
    }

    // A friend's character they no longer share (hidden, removed, deleted) disappears on your side.
    [Fact]
    public void Characters_a_friend_no_longer_shares_disappear()
    {
        var (a, b) = Friends();
        b.Call("friendsCommand", "ask JDybeck#1111");
        Talk(a, b);
        Assert.False(b.Lua("return WoWCharacterTrackerDB.friends['JDybeck#1111'].chars['Player-1-0000000C']").IsNil());
        a.Lua("WoWCharacterTrackerDB.share.chars['Player-1-0000000C'] = false"); // Drygbeck hidden
        a.Run(31);
        b.Run(31);
        b.Call("friendsCommand", "ask JDybeck#1111");
        Talk(a, b);
        Assert.True(b.Lua("return WoWCharacterTrackerDB.friends['JDybeck#1111'].chars['Player-1-0000000C']").IsNil());
        Assert.False(b.Lua("return WoWCharacterTrackerDB.friends['JDybeck#1111'].chars['Player-1-0000000A']").IsNil()); // still shared
    }

    [Fact]
    public void Alts_go_once_a_session_and_asking_again_too_soon_waits()
    {
        var (a, b) = Friends();
        b.Call("friendsCommand", "ask JDybeck#1111");
        Talk(a, b);
        b.Call("friendsCommand", "ask JDybeck#1111");
        Assert.Contains(b.Said(), s => s.Contains("a moment ago"));
        a.Run(31);
        b.Run(31);
        b.Call("friendsCommand", "ask JDybeck#1111");
        b.Run(1);
        Deliver(b, a, BAccount);
        a.Run(1);
        Assert.Contains(a.Sent(), m => m.Get("text").String.Contains("char=Deadbeck"));
        Assert.DoesNotContain(a.Sent(), m => m.Get("text").String.Contains("char=Drygbeck"));
    }

    // A, who doesn't share with Zeb yet, gets a request from Zeb's game account.
    static Client Asked(string settings = "{}")
    {
        var a = new Client(A, AFriends, ADb("{ enabled = true, at = 100 }"), settings);
        a.Receive("1:REQ:1:1:1:", BAccount);
        return a;
    }

    static void Answer(Client a, string choice) =>
        a.Script.Call(a.Ns.Get("answerFriendRequest"), a.Lua("return shown[#shown].data"), choice);

    [Fact]
    public void A_request_shows_a_popup_and_allow_shares_what_friends_get_in_general()
    {
        var a = Asked("{ friendNames = { ['Zeb#2222'] = 'Zebbe' } }");
        Assert.Equal("CHARACTERTRACKER_FRIEND_REQUEST", Str(a, "shown[1].name"));
        Assert.Equal("Zebbe (Zeb#2222) wants to see your characters.", Str(a, "shown[1].arg"));
        Answer(a, "allow");
        a.Run(1);
        Assert.True(a.Lua("return ns.shareSettings().contacts['Zeb#2222'].share").Boolean);
        Assert.True(a.Lua("return ns.shareSettings().contacts['Zeb#2222'].gold").IsNil()); // follows the general choice
        Assert.Contains(a.Sent(), m => m.Get("text").String.Contains("char=Deadbeck"));
        Assert.Contains(a.Sent(), m => m.Get("text").String.Contains("char=Drygbeck"));
        Assert.DoesNotContain(a.Sent(), m => m.Get("text").String.Contains("money=")); // gold is off by default
    }

    [Fact]
    public void Asking_for_categories_sends_the_known_ones_in_the_request()
    {
        var b = new Client(B, BFriends, BDb("{ enabled = true, at = 100 }"));
        b.Call("friendsCommand", "ask JDybeck#1111 map Gold");
        b.Run(1);
        Assert.Contains(b.Sent(), m => m.Get("text").String.Contains(":REQ:") && m.Get("text").String.EndsWith(":gold,map", StringComparison.Ordinal));
        Assert.Contains(b.Said(), s => s.Contains("for gold, map position"));
        b.Run(31);
        b.Call("friendsCommand", "ask JDybeck#1111 wings");
        Assert.Contains(b.Said(), s => s.StartsWith("/wct friends ask", StringComparison.Ordinal));
    }

    [Fact]
    public void A_request_for_more_asks_a_friend_you_share_with_and_allow_turns_on_only_that()
    {
        var a = new Client(A, AFriends, ADb(AShares));
        a.Receive("1:REQ:1:1:1:map", BAccount);
        a.Run(1);
        Assert.Contains(a.Sent(), m => m.Get("text").String.Contains("char=Deadbeck")); // what they get already goes out
        Assert.Equal("Zeb#2222 asks to also see your map position.", Str(a, "shown[1].arg"));
        Assert.True(a.Lua("return ns.pendingRequests()[1].more").Boolean);
        a.Lua("ns.answerFriendRequest(ns.pendingRequests()[1], 'allow')"); // from the list: no choices given
        Assert.True(a.Lua("return ns.shareSettings().contacts['Zeb#2222'].map").Boolean);
        Assert.True(a.Lua("return ns.shareSettings().contacts['Zeb#2222'].gold").IsNil());
        Assert.False(a.Lua("return ns.allowedFor(ns.shareSettings(), nil).map").Boolean); // only for them
    }

    [Fact]
    public void A_request_for_what_they_already_get_is_just_answered()
    {
        var a = new Client(A, AFriends, ADb(AShares));
        a.Receive("1:REQ:1:1:1:location,wings", BAccount);
        a.Run(1);
        Assert.Equal(0, a.Lua("return #shown").Number);
        Assert.Contains(a.Sent(), m => m.Get("text").String.Contains("char=Deadbeck"));
    }

    [Fact]
    public void A_first_request_with_categories_ticks_them_in_the_box()
    {
        var a = new Client(A, AFriends, ADb("{ enabled = true, at = 100 }"));
        a.Receive("1:REQ:1:1:1:map", BAccount);
        Assert.Equal("Zeb#2222 wants to see your characters, with your map position.", Str(a, "shown[1].arg"));
        Assert.True(a.Lua("return ns.requestInfo(shown[1].data).choices.map").Boolean);
        Assert.False(a.Lua("return ns.requestInfo(shown[1].data).more").Boolean);
        Answer(a, "allow");
        Assert.True(a.Lua("return ns.shareSettings().contacts['Zeb#2222'].map").Boolean);
    }

    [Fact]
    public void Not_now_sends_nothing_and_waits_10_minutes_before_asking_again()
    {
        var a = Asked();
        Answer(a, "notnow");
        a.Run(31);
        a.Receive("1:REQ:2:1:1:", BAccount);
        a.Run(1);
        Assert.Equal(1, a.Lua("return #shown").Number);
        Assert.Empty(a.Sent());
        a.Run(600);
        a.Receive("1:REQ:3:1:1:", BAccount);
        Assert.Equal(2, a.Lua("return #shown").Number); // asked again later: shown again
    }

    // B relogs (or switches character) right after saying hello: A answers again, so B's addon
    // knows A has it and sends A the new character.
    [Fact]
    public void A_hello_right_after_a_relog_is_answered_and_the_new_character_goes()
    {
        const string BShares = "{ enabled = true, contacts = { ['JDybeck#1111'] = { share = true, receive = true } }, at = 100 }";
        var a = new Client(A, AFriends, ADb(AShares));
        var b = new Client(B, BFriends, BDb(BShares).Replace("chars = {}", "chars = { ['Player-1-0000000B'] = { char = 'Zeb', lastSeen = 1 } }"));
        a.Call("friendsTick");
        b.Call("friendsTick");
        Talk(a, b);
        a.Run(20);
        b.Run(20);

        // B relogs onto another character: a fresh addon session on the same game account.
        var b2 = new Client("Player-1-0000000E", BFriends, BDb(BShares).Replace("chars = {}", "chars = { ['Player-1-0000000E'] = { char = 'Zebalt', lastSeen = 2 } }"));
        b2.Lua("clock = " + b.Lua("return clock").Number);
        b2.Call("friendsTick");
        Talk(a, b2);
        Assert.Equal("Zebalt", Str(a, "WoWCharacterTrackerDB.friends['Zeb#2222'].chars['Player-1-0000000E'].char"));
    }

    // An alt played while the friend was offline: its newer snapshot reaches them on the next hello,
    // one they already have the same copy of doesn't go again.
    [Fact]
    public void An_alt_played_while_a_friend_was_away_catches_up_on_the_next_hello()
    {
        var (_, b) = Friends();
        Assert.Equal(500, b.Lua("return WoWCharacterTrackerDB.friends['JDybeck#1111'].chars['Player-1-0000000C'].at").Number);
        b.Run(20);
        // A logs in again later, having played the alt in between; B never left.
        var a2 = new Client(A, AFriends, ADb(AShares));
        a2.Lua("WoWCharacterTrackerDB.chars['Player-1-0000000C'].level = 13; WoWCharacterTrackerDB.chars['Player-1-0000000C'].lastSeen = 1500");
        a2.Lua("clock = " + b.Lua("return clock").Number);
        a2.Call("friendsTick");
        Talk(a2, b);
        Assert.Equal(13, b.Lua("return WoWCharacterTrackerDB.friends['JDybeck#1111'].chars['Player-1-0000000C'].level").Number);
        Assert.Equal(1500, b.Lua("return WoWCharacterTrackerDB.friends['JDybeck#1111'].chars['Player-1-0000000C'].at").Number);

        a2.Lua("sent = {}");
        b.Lua("sent = {}");
        b.Receive("1:HAVE:60:1:1:Player-1-0000000C~1500", AAccount);
        Assert.Equal(0, Count(b, "WANT")); // the same copy: nothing asked
    }

    // Asking with Send them mine too: the request says so, the asker's characters wait with it, and
    // Allow keeps them; Not now drops them.
    [Fact]
    public void Asking_with_yours_sends_them_along_and_allow_keeps_them()
    {
        var a = new Client(A, AFriends, ADb("{ enabled = true, at = 100 }"));
        var b = new Client(B, BFriends, BDb("{ enabled = true, at = 100 }").Replace("chars = {}", "chars = { ['Player-1-0000000B'] = { char = 'Zeb', level = 30, lastSeen = 1 } }"));
        b.Call("askFriend", "JDybeck#1111", b.Lua("return { 'map' }"), b.Lua("""
            return { gear = true, talents = true, location = false, professions = false, highlights = false, leveling = false, gold = false, map = true }
            """));
        Assert.Contains(b.Said(), s => s.StartsWith("Asked JDybeck#1111 for map position, and sent them yours (gear, talents, map position)", StringComparison.Ordinal));
        Assert.True(b.Lua("return ns.shareSettings().contacts['JDybeck#1111'].share").Boolean);
        Talk(a, b);
        Assert.Equal("Zeb#2222 wants to see your characters, with your map position, and shares theirs with you.", Str(a, "shown[1].arg"));
        Assert.Equal(1, a.Lua("return ns.requestInfo(shown[1].data).gifts").Number);
        Assert.True(a.Lua("return WoWCharacterTrackerDB.friends").IsNil()); // nothing kept before an answer
        Answer(a, "allow");
        Assert.Equal("Zeb", Str(a, "WoWCharacterTrackerDB.friends['Zeb#2222'].chars['Player-1-0000000B'].char"));
        Talk(a, b);
        Assert.Equal("Deadbeck", Str(b, GotDeadbeck + ".char"));
        Assert.Contains(b.Said(), s => s.StartsWith("JDybeck#1111 shared Deadbeck with you.", StringComparison.Ordinal));
    }

    [Fact]
    public void Characters_sent_with_a_request_go_when_it_isnt_allowed()
    {
        var a = new Client(A, AFriends, ADb("{ enabled = true, at = 100 }"));
        a.Receive("1:REQ:1:1:1:+shares", BAccount);
        a.Receive("1:DATA:2:1:1:guid=Player-1-0000000B;char=Zeb", BAccount);
        Assert.Equal(1, a.Lua("return ns.requestInfo(shown[1].data).gifts").Number);
        Answer(a, "notnow");
        Assert.True(a.Lua("return WoWCharacterTrackerDB.friends").IsNil());
        a.Receive("1:DATA:3:1:1:guid=Player-1-0000000B;char=Zeb", BAccount); // no request waits: dropped
        Assert.True(a.Lua("return WoWCharacterTrackerDB.friends").IsNil());
    }

    // A nickname set in game wins over the app's when newer, and the app's newer one wins back.
    [Fact]
    public void A_nickname_set_in_game_or_in_the_app_the_newer_wins()
    {
        var a = new Client(A, AFriends, ADb(AShares), settings: "{ friendNames = { ['Zeb#2222'] = 'Zebbe' }, friendNameAt = { ['Zeb#2222'] = 500 } }");
        Assert.Equal("Zebbe", Str(a, "ns.friendNickname('Zeb#2222')"));
        a.Lua("clock = 400");
        a.Call("friendsCommand", "name zeb#2222 Big Zeb");
        Assert.Equal(501, a.Lua("return WoWCharacterTrackerDB.friendNames['Zeb#2222'].at").Number); // newer than the app's
        Assert.Equal("Big Zeb (Zeb#2222)", Str(a, "ns.friendLabel('Zeb#2222')"));
        Assert.Contains("Zeb#2222 is now called Big Zeb here and in the app.", a.Said());
        a.Lua("WoWCharacterTrackerSettings.friendNames['Zeb#2222'] = 'Z'; WoWCharacterTrackerSettings.friendNameAt['Zeb#2222'] = 600");
        Assert.Equal("Z", Str(a, "ns.friendNickname('Zeb#2222')")); // set in the app later
        a.Call("friendsCommand", "name Zeb#2222");
        Assert.Equal("", Str(a, "ns.friendNickname('Zeb#2222')")); // cleared in game
        Assert.Equal("Zeb#2222", Str(a, "ns.friendLabel('Zeb#2222')"));

        var old = new Client(A, AFriends, ADb(AShares), settings: "{ friendNames = { ['Zeb#2222'] = 'Zebbe' } }"); // an app without times
        old.Lua("ns.setFriendNickname('Zeb#2222', 'New|cff name')");
        Assert.Equal("Newcff name", Str(old, "ns.friendNickname('Zeb#2222')"));
    }

    // The Friends tab's order: dragged in game it beats the app's older one, a newer app one wins back.
    [Fact]
    public void The_friend_order_from_the_game_or_the_app_the_newer_wins()
    {
        var a = new Client(A, AFriends, ADb(AShares), settings: "{ friendOrder = { 'Kalle#3333', 'Zeb#2222' }, friendOrderAt = 500 }");
        Assert.Equal("Kalle#3333,Zeb#2222", Str(a, "table.concat(ns.friendOrder(), ',')"));
        a.Lua("clock = 400; ns.setFriendOrder({ 'Zeb#2222', 'Kalle#3333' })");
        Assert.Equal(501, a.Lua("return WoWCharacterTrackerDB.friendOrder.at").Number);
        Assert.Equal("Zeb#2222,Kalle#3333", Str(a, "table.concat(ns.friendOrder(), ',')"));
        a.Lua("WoWCharacterTrackerSettings.friendOrderAt = 600");
        Assert.Equal("Kalle#3333,Zeb#2222", Str(a, "table.concat(ns.friendOrder(), ',')"));
    }

    // In game a drag only knows this game's friends: it merges into the whole order like the app.
    [Fact]
    public void A_drag_in_game_keeps_the_places_of_friends_it_doesnt_show()
    {
        var a = new Client(A, AFriends, ADb(AShares), settings: "{ friendOrder = { 'A', 'B', 'C', 'D', 'E' }, friendOrderAt = 500 }");
        a.Lua("clock = 600; ns.setFriendOrder({ 'E', 'D' })");
        Assert.Equal("A,B,C,E,D", Str(a, "table.concat(ns.friendOrder(), ',')"));
        Assert.Equal("X,B,A", Str(a, "table.concat(ns.mergeFriendOrder({ 'A', 'B' }, { 'X', 'B', 'A' }), ',')"));
    }

    // A flood of HAVEs gets one WANT per 10 seconds, so it can't fill the send queue.
    [Fact]
    public void Have_is_answered_at_most_every_10_seconds()
    {
        var b = new Client(B, BFriends, BDb(BReceives));
        for (var i = 0; i < 20; i++) b.Receive($"1:HAVE:{i + 1}:1:1:Player-1-000000{i:X2}~5", AAccount);
        b.Run(1);
        Assert.Equal(1, Count(b, "WANT"));
        b.Run(10);
        b.Receive("1:HAVE:99:1:1:Player-1-000000FF~5", AAccount);
        b.Run(1);
        Assert.Equal(2, Count(b, "WANT"));
    }

    // Asked with mine, they said Not now, and asked again later: my alts still reach them once they
    // allow (the ones sent along weren't counted as sent, and their hello gets my list).
    [Fact]
    public void Alts_dropped_with_a_not_now_still_arrive_after_a_later_allow()
    {
        var a = new Client(A, AFriends, ADb("{ enabled = true, at = 100 }"));
        var b = new Client(B, BFriends, BDb("{ enabled = true, at = 100 }"));
        const string give = "{ gear = true, talents = true, location = true, professions = true, highlights = true, leveling = true, gold = false, map = false }";
        a.Call("askFriend", "Zeb#2222", a.Lua("return {}"), a.Lua("return " + give));
        Talk(a, b);
        Answer(b, "notnow");
        a.Run(700);
        b.Run(700);
        a.Call("askFriend", "Zeb#2222", a.Lua("return {}"), a.Lua("return " + give));
        Talk(a, b);
        Answer(b, "allow");
        Talk(a, b, rounds: 6);
        Assert.Equal("Drygbeck", Str(b, GotDeadbeck.Replace("0000000A", "0000000C") + ".char"));
    }

    // Nicknames are cut by letters, never inside one (å is two bytes in the game).
    [Fact]
    public void Nicknames_are_cut_by_letters()
    {
        var a = new Client(A, AFriends, ADb(AShares));
        Assert.Equal("Åsa Ö", Str(a, "ns.cutLetters('Åsa Örn', 5)"));
        Assert.Equal("abc", Str(a, "ns.cutLetters('abc', 32)"));
        a.Lua("ns.setFriendNickname('Zeb#2222', string.rep('å', 40))");
        Assert.Equal(32, a.Lua("local n = 0; for _ in ns.friendNickname('Zeb#2222'):gmatch('[^\\128-\\191][\\128-\\191]*') do n = n + 1 end; return n").Number);
    }

    // After their Never you can't ask for a day; they may have unblocked you since.
    [Fact]
    public void A_no_stops_asking_for_a_day()
    {
        var b = new Client(B, BFriends, BDb("{ enabled = true, at = 100 }"));
        b.Call("friendsCommand", "ask JDybeck#1111");
        b.Receive("1:NO:1:1:1:", AAccount);
        b.Run(31);
        b.Call("friendsCommand", "ask JDybeck#1111");
        Assert.Contains(b.Said(), s => s.Contains("said no"));
        b.Lua("sent = {}");
        b.Run(86401);
        b.Call("friendsCommand", "ask JDybeck#1111");
        b.Run(1);
        Assert.Equal(1, Count(b, "REQ"));
    }

    [Fact]
    public void Never_blocks_them_and_their_addon_stops_asking()
    {
        var a = new Client(A, AFriends, ADb("{ enabled = true, at = 100 }"));
        var b = new Client(B, BFriends, BDb("{ enabled = true, at = 100 }"));
        b.Call("friendsCommand", "ask JDybeck#1111");
        Talk(a, b);
        Answer(a, "never");
        Talk(a, b);
        Assert.True(a.Lua("return ns.shareSettings().blocked['Zeb#2222']").Boolean);
        Assert.False(b.Lua("return WoWCharacterTrackerDB.friends['JDybeck#1111'].refused").IsNil());
        b.Run(31);
        b.Call("friendsCommand", "ask JDybeck#1111");
        Assert.Contains(b.Said(), s => s.Contains("said no"));
        a.Run(31);
        a.Receive("1:REQ:9:1:1:", BAccount); // a blocked friend asking again: no popup, no second NO
        a.Run(1);
        Assert.Equal(1, a.Lua("return #shown").Number);
        Assert.Equal(0, Count(a, "NO"));
    }

    [Fact]
    public void Two_requests_within_30_seconds_get_one_answer()
    {
        var (a, _) = Friends();
        a.Lua("sent = {}");
        a.Receive("1:REQ:1:1:1:", BAccount);
        a.Run(10);
        a.Receive("1:REQ:2:1:1:", BAccount);
        a.Run(1);
        Assert.Equal(1, a.Sent().Count(m => m.Get("text").String.Contains("char=Deadbeck")));
    }

    [Fact]
    public void A_request_in_combat_waits_for_the_fight_to_end()
    {
        var a = new Client(A, AFriends, ADb("{ enabled = true, at = 100 }"));
        a.Lua("inCombat = true");
        a.Receive("1:REQ:1:1:1:", BAccount);
        Assert.Equal(0, a.Lua("return #shown").Number);
        a.Lua("inCombat = false");
        a.Call("showNextFriendRequest");
        Assert.Equal(1, a.Lua("return #shown").Number);
    }

    [Fact]
    public void Commands_find_the_friend_and_change_what_they_get()
    {
        var a = new Client(A, AFriends, ADb("nil"));
        a.Call("friendsCommand", "ask nobody#1");
        Assert.Contains(a.Said(), s => s.Contains("isn't on your Battle.net friends list"));
        a.Call("friendsCommand", "share zeb#2222");
        Assert.True(a.Lua("return ns.shareSettings().enabled").Boolean);
        Assert.True(a.Lua("return ns.shareSettings().contacts['Zeb#2222'].share").Boolean);
        a.Call("friendsCommand", "set all gold on");
        Assert.True(a.Lua("return ns.shareSettings().defaults.gold").Boolean);
        a.Call("friendsCommand", "set Zeb#2222 gold off");
        Assert.False(a.Lua("return ns.allowedFor(ns.shareSettings(), ns.shareSettings().contacts['Zeb#2222']).gold").Boolean);
        a.Call("friendsCommand", "set Zeb#2222 gold default");
        Assert.True(a.Lua("return ns.allowedFor(ns.shareSettings(), ns.shareSettings().contacts['Zeb#2222']).gold").Boolean);
        a.Call("friendsCommand", "hide");
        Assert.False(a.Lua("return ns.isCharShared(ns.shareSettings(), 'Player-1-0000000A')").Boolean);
        a.Lua("sent = {}");
        a.Receive("1:HI:1:1:1:0.33.0", BAccount);
        a.Run(1);
        Assert.Equal(0, Count(a, "DATA")); // hidden: not sent
        a.Lua("WoWCharacterTrackerDB.friends = { ['Zeb#2222'] = { chars = {} } }");
        a.Call("friendsCommand", "remove Zeb#2222");
        Assert.True(a.Lua("return ns.shareSettings().contacts['Zeb#2222']").IsNil());
        Assert.True(a.Lua("return WoWCharacterTrackerDB.friends['Zeb#2222']").IsNil());
        a.Call("friendsCommand", "off");
        Assert.False(a.Lua("return ns.shareSettings().enabled").Boolean);
    }

    [Fact]
    public void The_toc_has_every_file_and_loads_the_friend_files_after_what_they_use()
    {
        var lines = File.ReadAllLines(Path.Combine(AddonLuaTests.AddonDir, "WoWCharacterTracker.toc")).Where(l => l.EndsWith(".lua")).ToList();
        Assert.Equal(Directory.GetFiles(AddonLuaTests.AddonDir, "*.lua").Select(f => Path.GetFileName(f)).Order(), lines.Order());
        Assert.True(lines.IndexOf("Shared.lua") < lines.IndexOf("FriendData.lua"));
        Assert.True(lines.IndexOf("FriendData.lua") < lines.IndexOf("Comms.lua"));
        Assert.True(lines.IndexOf("Comms.lua") < lines.IndexOf("Friends.lua"));
        Assert.True(lines.IndexOf("Friends.lua") < lines.IndexOf("Options.lua"));
    }

    [Fact]
    public void The_friends_command_gets_what_was_typed_with_its_case()
    {
        var c = new Client(A, "{}", files: ["Shared.lua"]);
        c.Lua("""
            SlashCmdList = {}
            got = nil
            ns.friendsCommand = function(args) got = args end
            """);
        c.Script.Call(c.Script.LoadString(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, "Commands.lua"))), "WoWCharacterTracker", c.Ns);
        c.Lua("SlashCmdList.CHARACTERTRACKER('Friends ask Zeb#2222')");
        Assert.Equal(" ask Zeb#2222", Str(c, "got"));
    }

    // Review fixes ---------------------------------------------------------------------------

    [Fact]
    public void Debug_saves_the_last_comms_error()
    {
        var c = new Client(A, "{}", files: ["Shared.lua"]);
        c.Lua("""
            SlashCmdList = {}
            WoWCharacterTrackerDB = {}
            function GetBuildInfo() return '1.60.1' end
            function UnitLevel() return 10 end
            function UnitName() return 'Deadbeck' end
            function IsInGuild() return false end
            function GetGuildInfo() return nil end
            ns.playedNow = function() return 0 end
            ns.getTalents = function() return '' end
            ns.activeTraitTree = function() error('none') end
            ns.isHardcore = function() return false end
            ns.lastCommsError = 'boom'
            """);
        c.Script.Call(c.Script.LoadString(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, "Commands.lua"))), "WoWCharacterTracker", c.Ns);
        c.Lua("SlashCmdList.CHARACTERTRACKER('debug')");
        Assert.True(c.Lua("for _, l in ipairs(WoWCharacterTrackerDB.debug) do if l:find('comms error: boom', 1, true) then return true end end return false").Boolean);
    }

    [Fact]
    public void A_flood_of_hellos_gets_one_answer()
    {
        var a = HelloDone();
        for (var i = 2; i <= 6; i++) a.Receive($"1:HI:{i}:1:1:0.33.0", BAccount);
        a.Run(5);
        Assert.True(Count(a, "HEY") <= 1);
        Assert.Equal(0, Count(a, "DATA")); // sent a moment ago
    }

    // A map position doesn't wait behind a backlog of characters going out.
    [Fact]
    public void Map_messages_go_before_a_backlog()
    {
        var c = CommsClient();
        c.Lua("ns.comms.send(2, 'DATA', string.rep('x', 2400)); ns.comms.send(2, 'POS', '1,0.5,0.5,5,0')");
        c.Run(1);
        var first = c.Sent().Take(2).Select(m => m.Get("text").String).ToList();
        Assert.Contains(":POS:", first[1]); // the first DATA part went at once, the position right after it
    }

    [Fact]
    public void The_send_queue_has_a_cap()
    {
        var c = CommsClient();
        c.Lua("inCombat = true; for i = 1, 500 do ns.comms.send(2, 'HI', 'x') end");
        Assert.True(c.Lua("return ns.comms.pending()").Number <= 400);
    }

    [Fact]
    public void A_friends_version_text_is_cleaned_and_a_huge_protocol_ignored()
    {
        var b = new Client(B, BFriends, BDb(BReceives));
        b.Receive("1:HI:1:1:1:" + new string('9', 100) + "\u0001", AAccount);
        Assert.True(b.Lua("return WoWCharacterTrackerDB.friends['JDybeck#1111'].version").IsNil());
        b.Receive("1:HI:2:1:1:0.33.0", AAccount);
        Assert.Equal("0.33.0", Str(b, "WoWCharacterTrackerDB.friends['JDybeck#1111'].version"));
        b.Receive(new string('9', 400) + ":HI:3:1:1:x", AAccount);
        Assert.True(b.Lua("return WoWCharacterTrackerDB.friends['JDybeck#1111'].protocol").IsNil());
    }

    [Fact]
    public void Asking_while_sharing_is_off_says_it_turned_sharing_on()
    {
        var a = new Client(A, AFriends, ADb("{ enabled = false, contacts = { ['Zeb#2222'] = { share = true } }, at = 100 }"));
        a.Call("friendsCommand", "ask Zeb#2222");
        Assert.Contains(a.Said(), s => s.Contains("Sharing with friends is now on"));
    }

    [Fact]
    public void A_popup_the_game_couldnt_show_doesnt_block_later_requests()
    {
        var a = new Client(A, AFriends, ADb("{ enabled = true, at = 100 }"));
        a.Lua("realShow = StaticPopup_Show; StaticPopup_Show = function() return nil end");
        a.Receive("1:REQ:1:1:1:", BAccount);
        a.Lua("StaticPopup_Show = realShow");
        a.Call("showNextFriendRequest");
        Assert.Equal(1, a.Lua("return #shown").Number);
        a.Lua("StaticPopupDialogs.CHARACTERTRACKER_FRIEND_REQUEST.OnHide(nil, shown[1].data)"); // closed without an answer
        a.Lua("shown = {}");
        a.Call("offerFriendRequest", a.Lua("return { tag = 'Other#1', account = 9 }"));
        Assert.Equal(1, a.Lua("return #shown").Number);
    }

    [Fact]
    public void Parts_are_never_cut_inside_a_letter()
    {
        var c = CommsClient();
        // MoonSharp strings are UTF-16, so the game's bytes are written out: \195\169 is UTF-8 for é.
        c.Lua("body = string.rep('a', 239) .. '\\195\\169' .. string.rep('b', 300); ns.comms.send(2, 'DATA', body)");
        c.Run(5);
        var first = c.Sent()[0].Get("text").String;
        Assert.EndsWith(":" + new string('a', 239), first); // é went whole to the next part
        foreach (var m in c.Sent()) c.Receive(m.Get("text").String, 7);
        Assert.True(c.Lua("return got[1].body == body").Boolean);
    }

    [Fact]
    public void One_friend_cant_take_every_half_message_slot()
    {
        var c = CommsClient();
        for (var id = 1; id <= 8; id++) c.Receive($"1:DATA:{id}:1:2:x", 7);
        c.Receive("1:DATA:50:1:2:x", 8);
        c.Receive("1:DATA:50:2:2:y", 8);
        Assert.Equal("xy", Str(c, "got[1].body"));
    }

    [Fact]
    public void A_copy_is_deep_and_separate()
    {
        var c = new Client("Player-1-0000000A", "{}", files: ["Shared.lua"]);
        c.Lua("original = { a = { b = 1 } }; copied = ns.copy(original); copied.a.b = 2");
        Assert.Equal(1, c.Lua("return original.a.b").Number);
        Assert.Equal(5, c.Lua("return ns.copy(5)").Number);
    }

    // Friends tab (Window.lua uses these) ------------------------------------------------------

    static int Whispers(Client c) => (int)c.Lua("return #whispers").Number;

    static void CountChanges(Client c) => c.Lua("changes = 0; ns.friendsChanged = function() changes = changes + 1 end");

    static int Changes(Client c) => (int)c.Lua("return changes").Number;

    [Fact]
    public void A_ping_gets_one_pong_a_minute_even_with_sharing_off_and_nothing_else()
    {
        var a = new Client(A, AFriends, ADb("{ enabled = false, at = 100 }"));
        a.Receive("1:PING:1:1:1:", BAccount);
        a.Run(1);
        Assert.Single(a.Sent());
        Assert.Equal("1:PONG:1:1:1:0.33.0", a.Sent()[0].Get("text").String);
        a.Run(10);
        a.Receive("1:PING:2:1:1:", BAccount);
        a.Run(1);
        Assert.Single(a.Sent()); // within a minute
        a.Run(60);
        a.Receive("1:PING:3:1:1:", BAccount);
        a.Receive("1:PING:4:1:1:", 99); // not a Battle.net friend
        a.Run(1);
        Assert.Equal(2, a.Sent().Count);
        Assert.All(a.Sent(), m => Assert.Equal(2, m.Get("to").Number));
        Assert.Equal(0, Whispers(a));
    }

    [Fact]
    public void Finding_online_friends_shows_who_has_the_addon_after_their_pong()
    {
        var a = new Client(A, AFriends, ADb("nil"));
        var b = new Client(B, BFriends, BDb("nil"));
        CountChanges(a);
        a.Call("findOnlineFriends");
        Assert.True(a.Lua("return ns.onlineFriends()[1].hasAddon").IsNil()); // not known yet
        a.Run(1);
        Assert.Equal(1, Count(a, "PING"));
        Deliver(a, b, AAccount);
        b.Run(1);
        Assert.Single(b.Sent());
        Assert.Equal(1, Count(b, "PONG"));
        Deliver(b, a, BAccount);
        Assert.True(a.Lua("return ns.onlineFriends()[1].hasAddon").Boolean);
        Assert.True(Changes(a) >= 1);
        a.Run(11);
        Assert.True(a.Lua("return ns.onlineFriends()[1].hasAddon").Boolean); // the timeout doesn't undo it
        Assert.Equal(0, Whispers(a) + Whispers(b));
        Assert.True(b.Lua("return WoWCharacterTrackerDB.friends").IsNil());
        Assert.Empty(b.Lua("return shown").Table.Values); // no popup either
    }

    [Fact]
    public void A_friend_who_doesnt_answer_in_4_seconds_has_no_addon()
    {
        var a = new Client(A, AFriends, ADb("nil"));
        CountChanges(a);
        a.Call("findOnlineFriends");
        a.Run(3);
        Assert.True(a.Lua("return ns.onlineFriends()[1].hasAddon").IsNil());
        a.Run(2);
        Assert.False(a.Lua("return ns.onlineFriends()[1].hasAddon").Boolean);
        Assert.Equal(1, Changes(a));
        a.Receive("1:HI:1:1:1:0.33.0", BAccount); // any message later says they do
        Assert.True(a.Lua("return ns.onlineFriends()[1].hasAddon").Boolean);
    }

    [Fact]
    public void Finding_twice_within_a_minute_pings_once_and_each_account_once_a_session()
    {
        var a = new Client(A, AFriends, ADb("nil"));
        a.Call("findOnlineFriends");
        a.Run(1);
        a.Call("findOnlineFriends");
        a.Run(1);
        Assert.Equal(1, Count(a, "PING"));
        a.Lua("table.insert(friends, { tag = 'Bo#3333', account = 3 })");
        a.Call("findOnlineFriends");
        a.Run(1);
        Assert.Equal(2, Count(a, "PING")); // a friend who came online since is pinged, even within the minute
        Assert.Equal(3, a.Sent().Last().Get("to").Number); // only the new one
        a.Run(60);
        a.Call("findOnlineFriends");
        a.Run(1);
        Assert.Equal(2, Count(a, "PING")); // nobody twice
    }

    // A PING still waiting (combat, a full queue) isn't "no answer": the 10 s start once it went out.
    [Fact]
    public void A_ping_waiting_in_combat_is_not_no_addon()
    {
        var a = new Client(A, AFriends, ADb("nil"));
        a.Lua("inCombat = true");
        a.Call("findOnlineFriends");
        a.Run(15);
        Assert.True(a.Lua("return ns.onlineFriends()[1].hasAddon").IsNil());
        a.Lua("inCombat = false");
        a.Run(6);
        Assert.Equal(1, Count(a, "PING"));
        Assert.True(a.Lua("return ns.onlineFriends()[1].hasAddon").IsNil()); // it only just went
        a.Run(20);
        Assert.False(a.Lua("return ns.onlineFriends()[1].hasAddon").Boolean);
    }

    [Fact]
    public void A_ping_that_could_not_be_sent_is_not_counted()
    {
        var a = new Client(A, AFriends, ADb("nil"));
        a.Lua("realSend = ns.comms.send; ns.comms.send = function() return false end");
        a.Call("findOnlineFriends");
        a.Run(11);
        Assert.True(a.Lua("return ns.onlineFriends()[1].hasAddon").IsNil());
        a.Lua("ns.comms.send = realSend");
        a.Run(60);
        a.Call("findOnlineFriends");
        a.Run(1);
        Assert.Equal(1, Count(a, "PING")); // tried again
    }

    // Older addons don't answer PING: someone who is a contact or shared with you has the addon anyway.
    [Fact]
    public void Invite_never_goes_to_a_contact_or_someone_still_being_checked()
    {
        var contact = new Client(A, "{ { tag = 'Zeb#2222', account = 2, bnet = 77 } }", ADb(AShares));
        contact.Call("findOnlineFriends");
        contact.Run(11);
        Assert.False(contact.Lua("return (ns.inviteFriend('Zeb#2222'))").Boolean);
        var sharedWithMe = new Client(A, "{ { tag = 'Zeb#2222', account = 2, bnet = 77 } }", ADb("nil"));
        sharedWithMe.Lua("WoWCharacterTrackerDB.friends = { ['Zeb#2222'] = { chars = {} } }");
        sharedWithMe.Call("findOnlineFriends");
        sharedWithMe.Run(11);
        Assert.False(sharedWithMe.Lua("return (ns.inviteFriend('Zeb#2222'))").Boolean);
        var checking = new Client(A, "{ { tag = 'Zeb#2222', account = 2, bnet = 77 } }", ADb("nil"));
        checking.Call("findOnlineFriends");
        Assert.False(checking.Lua("return (ns.inviteFriend('Zeb#2222'))").Boolean); // no answer yet
        Assert.Equal(0, Whispers(contact) + Whispers(sharedWithMe) + Whispers(checking));
    }

    const string TwoFriends = """
        { { tag = 'Zeb#2222', account = 2, guid = 'Player-1-0000000B', char = 'Zeb', classFile = 'WARRIOR', level = 60, zone = 'Orgrimmar' },
          { tag = 'Al#4444', account = 4, char = 'Alby', className = 'Mage', level = 12 } }
        """;

    [Fact]
    public void Online_friends_rows_have_what_the_tab_shows_sorted_by_label()
    {
        var a = new Client(A, TwoFriends,
            ADb("{ enabled = true, contacts = { ['Zeb#2222'] = { share = true, receive = true } }, blocked = { ['Al#4444'] = true }, at = 100 }"),
            settings: "{ friendNames = { ['Zeb#2222'] = 'Zebbe' } }");
        a.Lua("LOCALIZED_CLASS_NAMES_MALE = { MAGE = 'Mage' }; WoWCharacterTrackerDB.friends = { ['Zeb#2222'] = { chars = { x = { char = 'Zeb', at = 1 } } } }");
        a.Lua("rows = ns.onlineFriends(); al, zeb = rows[1], rows[2]");
        Assert.Equal("Al#4444", Str(a, "al.label"));
        Assert.Equal("Zebbe (Zeb#2222)", Str(a, "zeb.label"));
        Assert.Equal("Zeb#2222", Str(a, "zeb.tag"));
        Assert.Equal(2, a.Lua("return zeb.account").Number);
        Assert.Equal("Zeb", Str(a, "zeb.character"));
        Assert.Equal("WARRIOR", Str(a, "zeb.classFile"));
        Assert.Equal(60, a.Lua("return zeb.level").Number);
        Assert.Equal("Orgrimmar", Str(a, "zeb.zone"));
        Assert.True(a.Lua("return zeb.contact.share").Boolean);
        Assert.True(a.Lua("return zeb.sharesWithYou").Boolean);
        Assert.False(a.Lua("return zeb.blocked").Boolean);
        Assert.True(a.Lua("return zeb.invitedAt").IsNil());
        Assert.Equal("MAGE", Str(a, "al.classFile")); // from the class name when the game gives no file name
        Assert.True(a.Lua("return al.blocked").Boolean);
        Assert.True(a.Lua("return al.contact").IsNil());
        Assert.False(a.Lua("return al.sharesWithYou").Boolean);
        Assert.True(a.Lua("return al.zone").IsNil());
    }

    [Fact]
    public void Ask_and_share_from_the_tab_do_what_the_commands_do()
    {
        var a = new Client(A, AFriends, ADb("{ enabled = true, at = 100 }"));
        var b = new Client(B, BFriends, BDb("{ enabled = true, at = 100 }"));
        var asked = b.Call("askFriend", "JDybeck#1111").String;
        Assert.StartsWith("Asked JDybeck#1111", asked);
        Assert.Contains(asked, b.Said());
        b.Run(1);
        Assert.Equal(1, Count(b, "REQ"));
        Assert.True(b.Lua("return ns.shareSettings().contacts['JDybeck#1111'].receive").Boolean);
        Assert.Contains("a moment ago", b.Call("askFriend", "JDybeck#1111").String);

        var shared = a.Call("shareWithFriend", "Zeb#2222").String;
        Assert.StartsWith("Zeb#2222 gets your shared characters", shared);
        Assert.Contains(shared, a.Said());
        Assert.True(a.Lua("return ns.shareSettings().contacts['Zeb#2222'].share").Boolean);
        Assert.Equal(0, Whispers(a) + Whispers(b));
    }

    const string InviteText = "I use WoW Character Tracker to share characters with friends: https://github.com/JDybeck/wow-character-tracker";

    [Fact]
    public void Invite_sends_one_whisper_a_day_and_only_when_asked()
    {
        var a = new Client(A, "{ { tag = 'Zeb#2222', account = 2, bnet = 77 } }", ADb("nil"));
        a.Call("findOnlineFriends");
        a.Run(11);
        Assert.Equal(0, Whispers(a));
        var first = a.Lua("return ns.inviteFriend('Zeb#2222')");
        Assert.True(first.Tuple[0].Boolean);
        Assert.Equal(1, Whispers(a));
        Assert.Equal(77, a.Lua("return whispers[1].to").Number);
        Assert.Equal(InviteText, Str(a, "whispers[1].text"));
        Assert.Equal(1011, a.Lua("return ns.onlineFriends()[1].invitedAt").Number);
        Assert.Equal(1011, a.Lua("return WoWCharacterTrackerDB.invitedAt['Zeb#2222']").Number);

        var again = a.Lua("return ns.inviteFriend('Zeb#2222')");
        Assert.False(again.Tuple[0].Boolean);
        Assert.False(string.IsNullOrEmpty(again.Tuple[1].String));
        Assert.Equal(1, Whispers(a));
        a.Run(86400);
        Assert.True(a.Lua("return (ns.inviteFriend('Zeb#2222'))").Boolean);
        Assert.Equal(2, Whispers(a));
        Assert.Empty(a.Sent().Where(m => !m.Get("text").String.Contains(":PING:"))); // nothing else went out
    }

    [Fact]
    public void Invite_refuses_a_friend_with_the_addon_or_one_not_online()
    {
        var a = new Client(A, AFriends, ADb("nil"));
        a.Receive("1:PONG:1:1:1:0.33.0", BAccount);
        Assert.False(a.Lua("return (ns.inviteFriend('Zeb#2222'))").Boolean);
        Assert.False(a.Lua("return (ns.inviteFriend('Nobody#1'))").Boolean);
        a.Lua("friends[1].online = false");
        Assert.False(a.Lua("return (ns.inviteFriend('Zeb#2222'))").Boolean);
        Assert.Equal(0, Whispers(a));
        Assert.True(a.Lua("return WoWCharacterTrackerDB.invitedAt").IsNil());
    }

    [Fact]
    public void Requests_are_listed_and_answering_from_the_list_hides_the_popup()
    {
        var a = new Client(A, TwoFriends, ADb("{ enabled = true, at = 100 }"), settings: "{ friendNames = { ['Zeb#2222'] = 'Zebbe' } }");
        CountChanges(a);
        a.Receive("1:REQ:1:1:1:", BAccount);
        a.Run(5);
        a.Receive("1:REQ:1:1:1:", 4);
        Assert.True(Changes(a) >= 2);
        a.Lua("pending = ns.pendingRequests()");
        Assert.Equal(2, a.Lua("return #pending").Number);
        Assert.Equal("Zeb#2222", Str(a, "pending[1].tag"));
        Assert.Equal(2, a.Lua("return pending[1].account").Number);
        Assert.Equal("Zebbe (Zeb#2222)", Str(a, "pending[1].label"));
        Assert.Equal(1000, a.Lua("return pending[1].at").Number);
        Assert.Equal("Al#4444", Str(a, "pending[2].tag")); // waiting behind the popup

        a.Lua("ns.answerFriendRequest(pending[1], 'allow')"); // the one in the popup
        Assert.Equal("CHARACTERTRACKER_FRIEND_REQUEST", Str(a, "hidden[1]"));
        Assert.True(a.Lua("return ns.shareSettings().contacts['Zeb#2222'].share").Boolean);
        Assert.Equal(2, a.Lua("return #shown").Number); // the next one shows
        Assert.Equal("Al#4444", Str(a, "shown[2].data.tag"));
        Assert.Equal(1, a.Lua("return #ns.pendingRequests()").Number);
        Assert.True(Changes(a) >= 3);

        Answer(a, "notnow"); // from the popup: nothing to hide
        Assert.Equal(1, a.Lua("return #hidden").Number);
        Assert.Equal(0, a.Lua("return #ns.pendingRequests()").Number);
    }

    [Fact]
    public void A_waiting_request_answered_from_the_list_never_shows()
    {
        var a = new Client(A, TwoFriends, ADb("{ enabled = true, at = 100 }"));
        a.Receive("1:REQ:1:1:1:", BAccount);
        a.Receive("1:REQ:1:1:1:", 4);
        a.Lua("ns.answerFriendRequest(ns.pendingRequests()[2], 'never')");
        Assert.Equal(0, a.Lua("return #hidden").Number); // Zeb's popup stays
        Assert.True(a.Lua("return ns.shareSettings().blocked['Al#4444']").Boolean);
        Assert.Equal("Zeb#2222", Str(a, "ns.pendingRequests()[1].tag"));
        Answer(a, "notnow");
        Assert.Equal(1, a.Lua("return #shown").Number);
        Assert.Equal(0, a.Lua("return #ns.pendingRequests()").Number);
    }

    [Fact]
    public void Shared_with_you_lists_only_friends_you_keep_newest_first()
    {
        var a = new Client(A, AFriends, ADb("""
            { enabled = true, at = 100, blocked = { ['Al#4444'] = true }, contacts = {
              ['Zeb#2222'] = { receive = true }, ['Al#4444'] = { receive = true }, ['Bo#3333'] = { receive = false }, ['Cy#5555'] = { receive = true } } }
            """), settings: "{ friendNames = { ['Zeb#2222'] = 'Zebbe' } }");
        a.Lua("""
            WoWCharacterTrackerDB.friends = {
              ['Zeb#2222'] = { chars = { old = { char = 'Old', at = 5 }, new = { char = 'New', at = 9 } } },
              ['Al#4444'] = { chars = { x = { char = 'Blocked', at = 1 } } },
              ['Bo#3333'] = { chars = { x = { char = 'NotKept', at = 1 } } },
              ['Dee#6666'] = { chars = { x = { char = 'NoContact', at = 1 } } },
              ['Cy#5555'] = { chars = {} } }
            got = ns.sharedWithYou()
            """);
        Assert.Equal(1, a.Lua("return #got").Number);
        Assert.Equal("Zeb#2222", Str(a, "got[1].tag"));
        Assert.Equal("Zebbe (Zeb#2222)", Str(a, "got[1].label"));
        Assert.Equal("New", Str(a, "got[1].chars[1].char"));
        Assert.Equal("Old", Str(a, "got[1].chars[2].char"));
    }

    [Fact]
    public void New_data_from_a_friend_tells_the_tab()
    {
        var b = new Client(B, BFriends, BDb(BReceives));
        b.Receive("1:PONG:1:1:1:0.33.0", AAccount); // known to have the addon already
        CountChanges(b);
        b.Receive("1:DATA:2:1:1:guid=Player-1-0000000A;char=Deadbeck", AAccount);
        Assert.Equal(1, Changes(b));
    }
}
