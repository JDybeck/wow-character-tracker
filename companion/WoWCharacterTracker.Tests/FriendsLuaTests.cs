using System.Globalization;
using MoonSharp.Interpreter;

namespace WoWCharacterTracker.Tests;

// Sharing with Battle.net friends (FriendData.lua, Comms.lua, Friends.lua) in MoonSharp, with
// Battle.net stubbed. Two clients can talk: what one sends with SendGameData is handed to the
// other's BN_CHAT_MSG_ADDON handler.
public class FriendsLuaTests
{
    const string Stubs = """
        function time() return math.floor(clock) end
        function strtrim(s) return (s:gsub('^%s+', ''):gsub('%s+$', '')) end
        inCombat, lockdown, groupGuids, sendResult = false, false, {}, 0
        function InCombatLockdown() return inCombat end
        function IsEncounterInProgress() return false end
        C_ChatInfo = { RegisterAddonMessagePrefix = function() end, InChatMessagingLockdown = function() return lockdown end }
        BNET_CLIENT_WOW, WOW_PROJECT_ID = 'WoW', 5
        sent, shown, said = {}, {}, {}
        function BNGetNumFriends() return #friends end
        C_BattleNet = {
          GetFriendAccountInfo = function(i) return friends[i] and { battleTag = friends[i].tag } end,
          GetFriendNumGameAccounts = function(i) return friends[i] and 1 or 0 end,
          GetFriendGameAccountInfo = function(i)
            local f = friends[i]
            return f and { isOnline = f.online ~= false, clientProgram = 'WoW', wowProjectID = 5, gameAccountID = f.account, playerGuid = f.guid }
          end,
          SendGameData = function(to, prefix, text) table.insert(sent, { to = to, prefix = prefix, text = text }) return sendResult end,
        }
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

    internal static readonly string[] AllFiles = ["Shared.lua", "FriendData.lua", "Comms.lua", "Friends.lua"];

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

    [Fact]
    public void A_copy_is_deep_and_separate()
    {
        var c = new Client("Player-1-0000000A", "{}", files: ["Shared.lua"]);
        c.Lua("original = { a = { b = 1 } }; copied = ns.copy(original); copied.a.b = 2");
        Assert.Equal(1, c.Lua("return original.a.b").Number);
        Assert.Equal(5, c.Lua("return ns.copy(5)").Number);
    }
}
