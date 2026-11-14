using MoonSharp.Interpreter;

namespace WoWCharacterTracker.Tests;

// FriendMap.lua: friends on the world map, through the real Friends.lua and Comms.lua. A
// (JDybeck#1111) is the one moving, B (Zeb#2222) has the map open.
public class FriendMapLuaTests
{
    const string A = "Player-1-0000000A", B = "Player-1-0000000B";
    const int AAccount = 1, BAccount = 2; // each one's game account as the other's friends list has it
    static readonly string[] Files = [.. FriendsLuaTests.AllFiles, "Layer.lua", "FriendMap.lua"];

    // Where you are, your faction, and whether you're in an instance, per client. see(guid, pet)
    // shows a creature as the target, the way Layer.lua hears of one.
    const string MapStubs = """
        inInstance, myFaction, myMap, myX, myY = false, 'Horde', 1433, 0.4567, 0.1234
        function IsInInstance() return inInstance end
        function UnitFactionGroup() return myFaction, myFaction end
        function GetRealmName() return 'Firemaw' end
        C_Map = {
          GetBestMapForUnit = function() return myMap end,
          GetPlayerMapPosition = function() if not myX then return nil end return { x = myX, y = myY } end,
        }
        units = {}
        local groupGuid = UnitGUID
        function UnitGUID(u) if units[u] then return units[u].guid end return groupGuid(u) end
        function UnitPlayerControlled(u) return units[u] ~= nil and units[u].pet == true end
        function see(guid, pet) units.target = { guid = guid, pet = pet }; ns.observeLayer('target') end
        """;

    // A creature on a layer (server part) and continent (map part); spawn tells creatures apart.
    static string Creature(int server, int spawn, int map = 0) => $"Creature-0-{server}-{map}-8-299-0000{spawn:X6}";

    static void OnLayer(FriendsLuaTests.Client c, int server, int map = 0)
    {
        c.Lua($"see('{Creature(server, 1, map)}')");
        c.Lua($"see('{Creature(server, 2, map)}')");
    }

    static string? Layer(FriendsLuaTests.Client c)
    {
        var got = c.Lua("local s, m = ns.currentLayer(); return s and (s .. ',' .. m) or nil");
        return got.IsNil() ? null : got.String;
    }

    static string FriendsOf(string tag, int account, string guid, string character, string faction = "Horde") =>
        $"{{ {{ tag = '{tag}', account = {account}, guid = '{guid}', char = '{character}', classFile = 'WARRIOR', level = 42, faction = '{faction}', realm = 'Firemaw' }} }}";

    // A shares with Zeb (zebContact), B keeps what JDybeck sends (bContact). aShare goes first in A's share table.
    static (FriendsLuaTests.Client A, FriendsLuaTests.Client B) Pair(string zebContact = "share = true, receive = true, map = true",
        string aShare = "", string bContact = "receive = true", string zebFaction = "Horde")
    {
        var a = new FriendsLuaTests.Client(A, FriendsOf("Zeb#2222", BAccount, B, "Zeb", zebFaction),
            $"{{ share = {{ {aShare} enabled = true, at = 100, contacts = {{ ['Zeb#2222'] = {{ {zebContact} }} }} }}, chars = {{}}, items = {{}} }}", files: Files);
        var b = new FriendsLuaTests.Client(B, FriendsOf("JDybeck#1111", AAccount, A, "Deadbeck"),
            $"{{ share = {{ enabled = true, at = 100, contacts = {{ ['JDybeck#1111'] = {{ {bContact} }} }} }}, chars = {{}}, items = {{}} }}", files: Files);
        a.Lua(MapStubs);
        b.Lua(MapStubs);
        return (a, b);
    }

    // Both run one second at a time and hand over what they sent. Returns the POS bodies A sent.
    static List<string> Pass(FriendsLuaTests.Client a, FriendsLuaTests.Client b, int seconds)
    {
        var bodies = new List<string>();
        for (var i = 0; i < seconds; i++)
        {
            a.Run(1);
            b.Run(1);
            bodies.AddRange(a.Sent().Select(m => m.Get("text").String).Where(t => t.Contains(":POS:")).Select(t => t.Split(':', 6)[5]));
            FriendsLuaTests.Deliver(a, b, AAccount);
            FriendsLuaTests.Deliver(b, a, BAccount);
        }
        return bodies;
    }

    static Table? Pin(FriendsLuaTests.Client b)
    {
        var pin = b.Lua("return ns.friendPins()[1]");
        return pin.IsNil() ? null : pin.Table;
    }

    [Fact]
    public void Map_position_is_off_by_default_so_a_watch_gets_nothing()
    {
        var (a, b) = Pair(zebContact: "share = true, receive = true");
        b.Call("setMapOpen", true);
        Assert.Empty(Pass(a, b, 10));
        Assert.Null(Pin(b));
    }

    [Fact]
    public void An_open_map_shows_where_the_friend_is()
    {
        var (a, b) = Pair();
        b.Call("setMapOpen", true);
        Assert.Equal(["1433,0.457,0.123,5,0"], Pass(a, b, 3));
        var pin = Pin(b)!;
        Assert.Equal("JDybeck#1111", pin.Get("label").String);
        Assert.Equal("Deadbeck", pin.Get("character").String);
        Assert.Equal(1433, pin.Get("mapID").Number);
        Assert.Equal(0.457, pin.Get("x").Number, 3);
        Assert.Equal(0.123, pin.Get("y").Number, 3);
        Assert.False(pin.Get("otherRealm").Boolean);
        Assert.False(pin.Get("faded").Boolean);
    }

    // Ticks every 5 s from the first WATCH: a move goes on the next tick, standing still only every 20 s.
    [Fact]
    public void Moving_sends_again_and_standing_still_only_every_20_seconds()
    {
        var (a, b) = Pair();
        b.Call("setMapOpen", true);
        Pass(a, b, 3);
        Assert.Empty(Pass(a, b, 10));
        a.Lua("myX = 0.5");
        Assert.Single(Pass(a, b, 6));
        Assert.Equal(0.5, Pin(b)!.Get("x").Number, 3);
        Assert.Single(Pass(a, b, 20));
    }

    [Fact]
    public void A_watch_that_is_not_renewed_ends()
    {
        var (a, b) = Pair();
        b.Call("setMapOpen", true);
        Pass(a, b, 3);
        b.Call("setMapOpen", false);
        Pass(a, b, 30);
        a.Lua("myX = 0.9");
        Assert.Empty(Pass(a, b, 15));
    }

    [Theory]
    [InlineData("Alliance", "", "share = true, receive = true, map = true", false)]
    [InlineData("Alliance", "defaults = { otherFaction = true },", "share = true, receive = true, map = true", true)]
    [InlineData("Alliance", "", "share = true, receive = true, map = true, otherFaction = true", true)]
    [InlineData("", "", "share = true, receive = true, map = true", false)] // faction unknown
    public void The_other_faction_gets_nothing_unless_allowed(string zebFaction, string aShare, string zebContact, bool gets)
    {
        var (a, b) = Pair(zebContact, aShare, zebFaction: zebFaction);
        b.Call("setMapOpen", true);
        Assert.Equal(gets ? 1 : 0, Pass(a, b, 3).Count);
        Assert.Equal(gets, Pin(b) is not null);
    }

    [Theory]
    [InlineData("inInstance = true")]
    [InlineData("myX = nil")]
    [InlineData("friends[1].faction = 'Alliance'")]
    [InlineData("ns.friendsCommand('hide')")]
    [InlineData("ns.friendsCommand('off')")]
    [InlineData("ns.friendsCommand('set Zeb#2222 map off')")]
    public void Losing_the_position_or_the_right_to_it_removes_the_pin(string change)
    {
        var (a, b) = Pair();
        b.Call("setMapOpen", true);
        Pass(a, b, 3);
        Assert.NotNull(Pin(b));
        a.Lua(change);
        Assert.Equal([""], Pass(a, b, 6));
        Assert.Null(Pin(b));
        Assert.Empty(Pass(a, b, 10)); // the empty one goes once
    }

    [Fact]
    public void Someone_who_never_got_a_position_never_gets_an_empty_one()
    {
        var (a, b) = Pair(zebContact: "share = true, receive = true");
        b.Call("setMapOpen", true);
        a.Lua("inInstance = true");
        Assert.Empty(Pass(a, b, 10));
    }

    [Fact]
    public void A_slow_rate_gives_a_new_watch_the_last_reading_with_its_age()
    {
        var (a, b) = Pair(aShare: "mapRate = 60,");
        b.Call("setMapOpen", true);
        Assert.Equal(["1433,0.457,0.123,60,0"], Pass(a, b, 3));
        b.Call("setMapOpen", false);
        Pass(a, b, 40); // the watch ends
        a.Lua("myX = 0.9");
        b.Call("setMapOpen", true); // 43 s after the reading
        var again = Pass(a, b, 3);
        Assert.Single(again);
        Assert.StartsWith("1433,0.457,0.123,60,", again[0], StringComparison.Ordinal);
        Assert.InRange(int.Parse(again[0].Split(',')[4]), 40, 46);
        Assert.InRange(Pin(b)!.Get("age").Number, 40, 47);
        Assert.Contains(Pass(a, b, 20), p => p.StartsWith("1433,0.900,", StringComparison.Ordinal)); // a minute after the first reading
    }

    [Fact]
    public void A_pin_fades_to_last_seen_and_stays()
    {
        var (a, b) = Pair();
        b.Call("setMapOpen", true);
        Pass(a, b, 3);
        b.Run(31); // nothing more arrives
        Assert.True(Pin(b)!.Get("faded").Boolean);
        b.Run(600);
        Assert.True(Pin(b)!.Get("faded").Boolean);
    }

    [Fact]
    public void A_friend_going_offline_loses_the_pin()
    {
        var (a, b) = Pair();
        b.Call("setMapOpen", true);
        Pass(a, b, 3);
        b.Lua("friends[1].online = false");
        Assert.Null(Pin(b));
    }

    [Fact]
    public void A_position_from_a_friend_we_watch_is_kept()
    {
        var (_, b) = Pair();
        b.Call("setMapOpen", true);
        b.Receive("1:POS:9:1:1:1433,0.5,0.25,60,30", AAccount);
        Assert.Equal(0.25, Pin(b)!.Get("y").Number, 3);
        Assert.InRange(Pin(b)!.Get("age").Number, 30, 31);
    }

    [Theory]
    [InlineData("1433,0.5,0.5,5,0", false)] // no WATCH asked for it
    [InlineData("1433,1.5,0.5,5,0", true)]  // off the map
    [InlineData("1433,0.5,0.5,7,0", true)]  // not a rate
    [InlineData("0,0.5,0.5,5,0", true)]     // no map
    [InlineData("1433,0.5,0.5,5", true)]    // no age
    [InlineData("1433,0.5,0.5,5,0,1", true)]
    public void A_position_that_wasnt_asked_for_or_makes_no_sense_is_dropped(string body, bool watching)
    {
        var (_, b) = Pair();
        if (watching) b.Call("setMapOpen", true);
        b.Receive($"1:POS:9:1:1:{body}", AAccount);
        Assert.Null(Pin(b));
    }

    [Fact]
    public void A_huge_position_is_dropped()
    {
        var (_, b) = Pair();
        b.Call("setMapOpen", true);
        b.Receive("1:POS:9:1:1:" + new string('9', 200) + ",0.5,0.5,5,0", AAccount);
        Assert.Null(Pin(b));
    }

    [Fact]
    public void Nobody_gets_a_watch_unless_we_keep_what_they_send()
    {
        var (_, b) = Pair(bContact: "receive = false");
        b.Call("setMapOpen", true);
        b.Run(1);
        Assert.DoesNotContain(b.Sent(), m => m.Get("text").String.Contains(":WATCH:"));
    }

    // A pin kept from an earlier look only comes back if the friend still allows it (review finding).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_pin_from_before_a_watch_gap_comes_back_only_if_still_allowed(bool hidden)
    {
        var (a, b) = Pair(aShare: "mapRate = 300,");
        b.Call("setMapOpen", true);
        Pass(a, b, 3);
        b.Call("setMapOpen", false);
        Pass(a, b, 40); // the watch ends
        if (hidden) a.Call("friendsCommand", "hide");
        b.Call("setMapOpen", true);
        Pass(a, b, 6);
        Assert.Equal(!hidden, Pin(b) is not null);
    }

    [Fact]
    public void A_pin_goes_when_the_friend_logs_onto_another_character()
    {
        var (a, b) = Pair();
        b.Call("setMapOpen", true);
        Pass(a, b, 3);
        b.Lua("friends[1].guid = 'Player-1-0000000C'; friends[1].char = 'Drygbeck'");
        Assert.Null(Pin(b));
    }

    [Theory]
    [InlineData("ns.setContactReceive('JDybeck#1111', false)")]
    [InlineData("ns.friendsCommand('off')")]
    [InlineData("ns.editShare(function(s) s.blocked['JDybeck#1111'] = true end)")]
    [InlineData("ns.editShare(function(s) s.contacts['JDybeck#1111'] = nil end)")]
    public void Your_own_changes_take_the_pin_away_at_once(string change)
    {
        var (a, b) = Pair();
        b.Call("setMapOpen", true);
        Pass(a, b, 3);
        Assert.NotNull(Pin(b));
        b.Lua(change);
        Assert.Null(Pin(b));
    }

    // Both fighting for a minute, moving all the time: the pin keeps up, at most every 2 s, and
    // nothing piles up.
    [Fact]
    public void Positions_go_in_combat_at_most_every_2_seconds()
    {
        var (a, b) = Pair();
        b.Call("setMapOpen", true);
        Pass(a, b, 3);
        a.Lua("inCombat = true");
        b.Lua("inCombat = true");
        var sent = new List<string>();
        for (var i = 0; i < 60; i++)
        {
            a.Lua("myX = myX + 0.005");
            sent.AddRange(Pass(a, b, 1));
        }
        Assert.InRange(sent.Count, 28, 31);
        Assert.Equal(0, a.Lua("return ns.comms.pending()").Number);
        Assert.Equal(0, b.Lua("return ns.comms.pending()").Number);
        Assert.False(Pin(b)!.Get("faded").Boolean);
        Assert.InRange(Pin(b)!.Get("x").Number, 0.7, 0.76);
    }

    // The chat lockdown still holds positions: the pin turns into last seen instead of going away.
    [Fact]
    public void A_lockdown_leaves_the_pin_as_last_seen()
    {
        var (a, b) = Pair();
        b.Call("setMapOpen", true);
        Pass(a, b, 3);
        a.Lua("lockdown = true");
        Assert.Empty(Pass(a, b, 180));
        Assert.True(Pin(b)!.Get("faded").Boolean);
        Assert.Equal(0, a.Lua("return ns.comms.pending()").Number);
    }

    // The game writes our realm with spaces, Battle.net theirs without. On Forever the number before
    // the ruleset is only the home realm ("CAMWOW EU5 Realm 6 PvP"): the same ruleset is one world,
    // and with no layer known there's nothing to say.
    [Theory]
    [InlineData(false, "CAMWOW EU5 Realm 6 PvP", "CAMWOWEU5Realm6PvP", false, null)]
    [InlineData(false, "CAMWOW EU5 Realm 6 PvP", "CamWowEu5Realm6PVP", false, null)] // Battle.net's case differs
    [InlineData(false, "CAMWOW EU5 Realm 4 PvP", "CAMWOWEU5Realm6PvP", false, null)] // seen in game: Realm 4 and 6 on one layer
    [InlineData(false, "CAMWOW EU5 Realm 5 PvP", "CAMWOWEU5Realm2Normal", true, "On another realm: Normal")]
    [InlineData(false, "CAMWOW EU5 Realm 6 PvP", "CAMWOWEU4Realm4PvP", false, null)] // seen in game: EU4 and EU5 on one layer too
    [InlineData(false, "CAMWOW EU5 Realm 5 PvP", "CAMWOWEU5Realm1RPPvP", true, "On another realm: RP-PvP")]
    [InlineData(false, "Firemaw", "Gehennas", true, "On another realm: Gehennas")]
    [InlineData(true, "Tarren Mill", "TarrenMill", false, null)]
    [InlineData(true, "Firemaw", "TarrenMill", true, "On another realm: TarrenMill")]
    public void A_friend_elsewhere_reads_as_another_layer_or_realm(bool retail, string mine, string theirs, bool other, string? note)
    {
        var (a, b) = Pair();
        b.Lua($"ns.IS_RETAIL = {(retail ? "true" : "false")}; function GetRealmName() return '{mine}' end; friends[1].realm = '{theirs}'");
        b.Call("setMapOpen", true);
        Pass(a, b, 3);
        var pin = Pin(b)!;
        Assert.Equal(other, pin.Get("otherRealm").Boolean);
        Assert.Equal(note, pin.Get("realmNote").IsNil() ? null : pin.Get("realmNote").String);
    }

    // The game shows group members on the map itself, so ours would be a second pin on top.
    [Fact]
    public void A_friend_in_your_group_gets_no_pin_and_no_watch()
    {
        var (a, b) = Pair();
        b.Call("setMapOpen", true);
        Pass(a, b, 3);
        Assert.NotNull(Pin(b));
        b.Lua($"groupGuids = {{ '{A}' }}");
        Assert.Null(Pin(b));
        b.Lua("sent = {}");
        Pass(a, b, 25);
        Assert.DoesNotContain(b.Sent(), m => m.Get("text").String.Contains(":WATCH:"));
        b.Lua("groupGuids = {}");
        Pass(a, b, 25); // the next watch round
        Assert.NotNull(Pin(b));
    }

    [Theory]
    [InlineData("CAMWOW EU5 Realm 6 PvP", "PvP", 6)]
    [InlineData("CAMWOWEU5Realm2Normal", "Normal", 2)]
    [InlineData("CAMWOW EU5 Realm 12 RP-PvP", "RP-PvP", 12)]
    [InlineData("CAMWOWEU5Realm1RPPvP", "RP-PvP", 1)]
    [InlineData("Firemaw", null, null)]
    [InlineData("", null, null)]
    public void The_realm_type_and_home_realm_come_from_the_end_of_a_Forever_realm_name(string realm, string? type, int? number)
    {
        var (_, b) = Pair();
        var got = b.Lua($"return {{ ns.realmType('{realm}') }}").Table;
        Assert.Equal(type, got.Get(1).IsNil() ? null : got.Get(1).String);
        Assert.Equal(number, got.Get(2).IsNil() ? null : (int)got.Get(2).Number);
    }

    [Theory]
    [InlineData("Creature-0-6843-0-8-299-00001A2B3C", "6843,0")]
    [InlineData("Vehicle-0-6843-1-12-3456-00001A2B3C", "6843,1")]
    [InlineData("Pet-0-6843-0-8-299-0100001A2B", null)]
    [InlineData("Player-1-0A1B2C3D", null)]
    [InlineData("GameObject-0-6843-0-8-1731-00001A2B3C", null)]
    [InlineData("", null)]
    public void The_layer_is_the_server_part_of_a_creatures_guid(string guid, string? layer)
    {
        var (_, b) = Pair();
        var got = b.Lua($"local s, m = ns.layerFromGuid('{guid}'); return s and (s .. ',' .. m) or nil");
        Assert.Equal(layer, got.IsNil() ? null : got.String);
    }

    // One creature could be a fluke, and one seen twice still counts once. Pets carry where they
    // were summoned, and a dungeon's creatures are the dungeon's.
    [Fact]
    public void A_layer_counts_once_two_different_creatures_agree()
    {
        var (_, b) = Pair();
        b.Lua($"see('{Creature(6843, 1)}'); see('{Creature(6843, 1)}')");
        Assert.Null(Layer(b));
        b.Lua($"see('{Creature(6843, 2)}', true)");
        Assert.Null(Layer(b));
        b.Lua($"inInstance = true; see('{Creature(6843, 3)}')");
        Assert.Null(Layer(b));
        b.Lua($"inInstance = false; see('{Creature(6843, 4)}')");
        Assert.Equal("6843,0", Layer(b));
    }

    // After a swap the old layer's nameplates are still in view: those were seen, so they don't
    // count again, and the new layer's creatures move us.
    [Fact]
    public void A_layer_swap_moves_us_once_two_new_creatures_agree()
    {
        var (_, b) = Pair();
        OnLayer(b, 6843);
        b.Lua($"see('{Creature(7001, 10)}')");
        Assert.Equal("6843,0", Layer(b));
        b.Lua($"see('{Creature(6843, 1)}'); see('{Creature(7001, 11)}')");
        Assert.Equal("7001,0", Layer(b));
    }

    [Fact]
    public void A_loading_screen_forgets_the_layer_and_retail_has_none()
    {
        var (_, b) = Pair();
        OnLayer(b, 6843);
        b.Call("forgetLayer");
        Assert.Null(Layer(b));
        OnLayer(b, 6843); // the same creatures count again after it
        Assert.Equal("6843,0", Layer(b));
        b.Call("forgetLayer");
        b.Lua("ns.IS_RETAIL = true");
        OnLayer(b, 6843);
        Assert.Null(Layer(b));
    }

    // Both on a known layer: same server = same layer. Another continent's layer can't be compared.
    [Theory]
    [InlineData(6843, 0, 6843, 0, false, "Same layer as you")]
    [InlineData(6843, 0, 7001, 0, true, "On another layer")]
    [InlineData(6843, 0, 6843, 1, false, null)]
    public void Friends_layers_are_compared_on_the_pin(int mine, int myMap, int theirs, int theirMap, bool other, string? note)
    {
        var (a, b) = Pair();
        b.Lua("ns.IS_RETAIL = false; function GetRealmName() return 'CAMWOW EU5 Realm 4 PvP' end; friends[1].realm = 'CAMWOWEU5Realm6PvP'");
        OnLayer(a, theirs, theirMap);
        OnLayer(b, mine, myMap);
        b.Call("setMapOpen", true);
        Pass(a, b, 3);
        var pin = Pin(b)!;
        Assert.Equal(other, pin.Get("otherRealm").Boolean);
        Assert.Equal(note, pin.Get("realmNote").IsNil() ? null : pin.Get("realmNote").String);
    }

    // The layer goes when it changes, not with every position, and a friend moving layer shows.
    [Fact]
    public void A_friend_changing_layer_updates_the_pin()
    {
        var (a, b) = Pair();
        OnLayer(b, 6843);
        b.Call("setMapOpen", true);
        Pass(a, b, 3);
        Assert.True(Pin(b)!.Get("realmNote").IsNil()); // A's layer not known yet
        OnLayer(a, 6843);
        Pass(a, b, 3);
        Assert.Equal("Same layer as you", Pin(b)!.Get("realmNote").String);
        a.Lua("sent = {}");
        Pass(a, b, 10);
        Assert.DoesNotContain(a.Sent(), m => m.Get("text").String.Contains(":LAYER:"));
        a.Lua($"see('{Creature(7001, 20)}'); see('{Creature(7001, 21)}')");
        Pass(a, b, 3);
        Assert.Equal("On another layer", Pin(b)!.Get("realmNote").String);
        a.Call("forgetLayer"); // a loading screen: not known any more
        Pass(a, b, 3);
        Assert.True(Pin(b)!.Get("realmNote").IsNil());
    }

    [Theory]
    [InlineData("6843,0", false)] // no WATCH asked for it
    [InlineData("6843", true)]
    [InlineData("abc,0", true)]
    [InlineData("12345678,0", true)]
    public void A_layer_that_wasnt_asked_for_or_makes_no_sense_is_dropped(string body, bool watching)
    {
        var (_, b) = Pair();
        OnLayer(b, 6843);
        if (watching) b.Call("setMapOpen", true);
        b.Receive("1:POS:9:1:1:1433,0.5,0.25,60,30", AAccount);
        b.Receive($"1:LAYER:10:1:1:{body}", AAccount);
        var pin = Pin(b);
        Assert.True(pin is null || pin.Get("realmNote").IsNil());
    }

    [Fact]
    public void Positions_are_never_saved()
    {
        var (a, b) = Pair();
        const string Keys = "local n = 0; for _ in pairs(WoWCharacterTrackerDB) do n = n + 1 end; return n";
        var before = b.Lua(Keys).Number;
        b.Call("setMapOpen", true);
        Pass(a, b, 3);
        Assert.NotNull(Pin(b));
        Assert.Equal(before, b.Lua(Keys).Number);
        Assert.True(b.Lua("return WoWCharacterTrackerDB.friends == nil").Boolean);
    }
}
