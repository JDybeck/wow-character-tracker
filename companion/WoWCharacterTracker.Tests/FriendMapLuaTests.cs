using MoonSharp.Interpreter;

namespace WoWCharacterTracker.Tests;

// FriendMap.lua: friends on the world map, through the real Friends.lua and Comms.lua. A
// (JDybeck#1111) is the one moving, B (Zeb#2222) has the map open.
public class FriendMapLuaTests
{
    const string A = "Player-1-0000000A", B = "Player-1-0000000B";
    const int AAccount = 1, BAccount = 2; // each one's game account as the other's friends list has it
    static readonly string[] Files = [.. FriendsLuaTests.AllFiles, "FriendMap.lua"];

    // Where you are, your faction, and whether you're in an instance, per client.
    const string MapStubs = """
        inInstance, myFaction, myMap, myX, myY = false, 'Horde', 1433, 0.4567, 0.1234
        function IsInInstance() return inInstance end
        function UnitFactionGroup() return myFaction, myFaction end
        function GetRealmName() return 'Firemaw' end
        C_Map = {
          GetBestMapForUnit = function() return myMap end,
          GetPlayerMapPosition = function() if not myX then return nil end return { x = myX, y = myY } end,
        }
        """;

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
    public void A_pin_fades_and_then_goes_by_the_senders_rate()
    {
        var (a, b) = Pair();
        b.Call("setMapOpen", true);
        Pass(a, b, 3);
        b.Run(31); // nothing more arrives
        Assert.True(Pin(b)!.Get("faded").Boolean);
        b.Run(90);
        Assert.Null(Pin(b));
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

    // Comms holds everything in combat: nothing piles up meanwhile, and the next position is fresh.
    [Fact]
    public void Nothing_piles_up_in_combat()
    {
        var (a, b) = Pair();
        b.Call("setMapOpen", true);
        Pass(a, b, 3);
        a.Lua("inCombat = true");
        b.Lua("inCombat = true");
        for (var i = 0; i < 60; i++)
        {
            a.Lua("myX = myX + 0.005");
            Pass(a, b, 1);
        }
        Assert.Equal(0, a.Lua("return ns.comms.pending()").Number);
        Assert.Equal(0, b.Lua("return ns.comms.pending()").Number);
        a.Lua("inCombat = false");
        b.Lua("inCombat = false");
        var after = Pass(a, b, 25);
        Assert.NotEmpty(after);
        Assert.EndsWith(",5,0", after[^1], StringComparison.Ordinal); // read just now, not a minute ago
    }

    // The game writes our realm with spaces, Battle.net theirs without. On Forever the number before
    // the ruleset is a layer ("CAMWOW EU5 Realm 6 PvP"), so the same ruleset means another layer.
    [Theory]
    [InlineData(false, "CAMWOW EU5 Realm 6 PvP", "CAMWOWEU5Realm6PvP", false, null)]
    [InlineData(false, "CAMWOW EU5 Realm 5 PvP", "CAMWOWEU5Realm6PvP", true, "On another layer")]
    [InlineData(false, "CAMWOW EU5 Realm 5 PvP", "CAMWOWEU5Realm2Normal", true, "On another realm: Normal")]
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
