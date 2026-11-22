using MoonSharp.Interpreter;
using WoWCharacterTracker.Services;

namespace WoWCharacterTracker.Tests;

// Friends' gold and played time added up, per friend and for all of them, with each friend's share:
// the app's FriendTotals (Friends page cards and footer) and the addon's ns.friendTotals (Friends tab).
public class FriendTotalsTests
{
    const long Gold = 10000; // copper

    [Fact]
    public void Each_friend_gets_their_sum_and_share_of_all_friends()
    {
        var t = FriendTotals.FromChars([("Zeb#2222", 300 * Gold, 3600), ("Zeb#2222", 100 * Gold, 3600), ("Kalle#3333", 600 * Gold, 0)]);
        Assert.Equal(1000 * Gold, t.Money);
        Assert.Equal(7200, t.Played);
        Assert.Equal(3, t.Characters);
        Assert.Equal(2, t.Friends);
        var zeb = t.For("Zeb#2222")!;
        Assert.Equal((400 * Gold, 7200L, 2, 40, 100), (zeb.Money, zeb.Played, zeb.Characters, zeb.GoldPercent, zeb.PlayedPercent));
        var kalle = t.For("Kalle#3333")!;
        Assert.Equal((60, 0), (kalle.GoldPercent, kalle.PlayedPercent));
        Assert.Equal("400g (40%) · 2h 0m played", t.Text("Zeb#2222")); // the only one with played: no percent
        Assert.Equal("600g (60%)", t.Text("Kalle#3333")); // no played time shared: left out
    }

    // Gold is its own category (off until turned on) and played comes with Level times: a friend who
    // shares neither adds nothing and isn't counted as sharing it.
    [Fact]
    public void Friends_who_dont_share_gold_or_played_are_left_out_of_those()
    {
        var t = FriendTotals.FromChars([("Zeb#2222", 0, 0), ("Kalle#3333", 5 * Gold, 0)]);
        Assert.Equal(2, t.Friends);
        Assert.Equal(1, t.WithGold);
        Assert.Equal(0, t.WithPlayed);
        Assert.Equal("", t.Text("Zeb#2222"));
        Assert.Equal("", t.Text("Nobody#1"));
        Assert.Equal(100, t.For("Kalle#3333")!.GoldPercent);
        Assert.Equal(0, FriendTotals.FromChars([]).Money);
        Assert.Null(FriendTotals.FromChars([]).For("Zeb#2222"));
    }

    const string FriendsLua = """
        { ['Zeb#2222'] = { seen = 2000, version = '0.33.0', chars = {
            ['Player-1-0000000B'] = { guid = 'Player-1-0000000B', char = 'Zeb', realm = 'Firemaw', level = 42, classFile = 'WARRIOR', at = 1900, money = 3000000, played = 90000 },
            ['Player-1-0000000C'] = { guid = 'Player-1-0000000C', char = 'Zebalt', realm = 'Firemaw', level = 12, classFile = 'MAGE', at = 1000, money = 1000000, played = 3600 } } },
          ['Kalle#3333'] = { seen = 1500, chars = {
            ['Player-1-0000000E'] = { guid = 'Player-1-0000000E', char = 'Kalle', realm = 'Firemaw', level = 60, classFile = 'ROGUE', at = 1400, played = 600000 } } } }
        """;

    const string KeepBoth = "{ enabled = true, at = 100, contacts = { ['Zeb#2222'] = { receive = true }, ['Kalle#3333'] = { receive = true } } }";

    static (FriendsViewModel Vm, Config Config, TrackerData Data) Page()
    {
        var script = new Script();
        var data = new TrackerData([], [])
        {
            Friends = FriendData.ReadFriends(script.DoString("return " + FriendsLua)),
            Share = FriendData.ReadShare(script.DoString("return " + KeepBoth)),
        };
        var settings = new FlavorSettings();
        settings.TakeShare(data.Share);
        var config = new Config();
        var vm = new FriendsViewModel(Flavors.Forever, new GameLookup(Flavors.Forever, () => { }), config, () => settings, () => { }, () => { });
        vm.Show(data);
        return (vm, config, data);
    }

    [Fact]
    public void The_friends_page_shows_each_friends_totals_on_their_card_and_all_of_them_under_the_list()
    {
        var (vm, config, _) = Page();
        Assert.Equal("400g · 1d 2h 0m played (13%)", vm.Cards.Single(c => c.Tag == "Zeb#2222").TotalsText);
        Assert.Equal("6d 22h 40m played (87%)", vm.Cards.Single(c => c.Tag == "Kalle#3333").TotalsText);
        Assert.True(vm.ShowsTotals);
        Assert.Equal("Total gold of 1 friend (2 characters):", vm.TotalGoldText);
        Assert.Equal(400 * Gold, vm.Listed.Money);
        Assert.Equal("Total played: 8d 0h 40m", vm.TotalPlayedText);
        Assert.Equal("1 friend doesn't share gold", vm.NotSharedText);

        // One friend picked: only theirs, by their name.
        config.FriendNicknames["Zeb#2222"] = "Zebbe";
        vm.Contacts.Selected = vm.Contacts.Chips.Single(c => c.Key == "Zeb#2222");
        Assert.Equal("Total gold of Zebbe (2 characters):", vm.TotalGoldText);
        Assert.Equal("Total played: 1d 2h 0m", vm.TotalPlayedText);
        Assert.Equal("", vm.NotSharedText);

        // Hidden characters are left out of the totals, also while shown.
        vm.Contacts.Reset();
        vm.HideCommand.Execute(vm.Characters.Single(c => c.Name == "Zebalt"));
        Assert.Equal(300 * Gold, vm.Listed.Money);
        Assert.StartsWith("300g · ", vm.Cards.Single(c => c.Tag == "Zeb#2222").TotalsText);
        vm.ShowHidden = true;
        Assert.Equal(300 * Gold, vm.Listed.Money);
    }

    [Fact]
    public void The_addon_adds_up_what_friends_share_with_each_ones_share()
    {
        var c = new FriendsLuaTests.Client("Player-1-0000000A", "{}", $"{{ chars = {{}}, items = {{}}, friends = {FriendsLua}, share = {KeepBoth} }}");
        c.Lua("""
            t = ns.friendTotals(ns.sharedWithYou())
            zeb = ns.friendTotalsLine(t, 'Zeb#2222')
            kalle = ns.friendTotalsLine(t, 'Kalle#3333')
            all = ns.allFriendsTotalsLine(t)
            hidden = ns.friendTotals(ns.sharedWithYou(), function(ch) return ch.char == 'Zebalt' end)
            """);
        Assert.Equal(4000000, c.Lua("return t.money").Number);
        Assert.Equal(693600, c.Lua("return t.played").Number);
        Assert.Equal(2, c.Lua("return t.friends").Number);
        Assert.Equal(1, c.Lua("return t.withGold").Number);
        Assert.Equal("400g  ·  1d 2h 0m played (13%)", c.Lua("return zeb").String); // the only one with gold: no percent
        Assert.Equal("6d 22h 40m played (87%)", c.Lua("return kalle").String);
        Assert.Equal("All friends: 400g, 8d 0h 40m played (1 of 2 share gold)", c.Lua("return all").String);
        Assert.Equal(3000000, c.Lua("return hidden.byTag['Zeb#2222'].money").Number);
        Assert.Equal("", c.Lua("return ns.friendTotalsLine(t, 'Nobody#1')").String);
        Assert.Equal("", c.Lua("return ns.allFriendsTotalsLine(ns.friendTotals({}))").String);
        Assert.Equal("12,345g", c.Lua("return ns.formatGold(123456789)").String);
        Assert.Equal("0g", c.Lua("return ns.formatGold(9999)").String);
    }
}
