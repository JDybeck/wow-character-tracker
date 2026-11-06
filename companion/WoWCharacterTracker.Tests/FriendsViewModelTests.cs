using MoonSharp.Interpreter;
using WoWCharacterTracker.Services;

namespace WoWCharacterTracker.Tests;

// The Friends page: friends' characters as rows, chips per friend, hiding, and the sharing settings.
public class FriendsViewModelTests
{
    const string Friends = """
        { ['Zeb#2222'] = { seen = 2000, version = '0.33.0', chars = {
            ['Player-1-0000000B'] = { guid = 'Player-1-0000000B', char = 'Zeb', realm = 'Firemaw', level = 42, classFile = 'WARRIOR', at = 1900 },
            ['Player-1-0000000C'] = { guid = 'Player-1-0000000C', char = 'Zebalt', realm = 'Firemaw', level = 12, classFile = 'MAGE', at = 1000 } } },
          ['Kalle#3333'] = { seen = 1500, protocol = 2, chars = {
            ['Player-1-0000000E'] = { guid = 'Player-1-0000000E', char = 'Kalle', realm = 'Firemaw', level = 60, classFile = 'ROGUE', at = 1400 } } } }
        """;

    sealed class Fixture
    {
        public Config Config { get; } = new();
        public FlavorSettings Settings { get; } = new();
        public int Changes { get; private set; }
        public FriendsViewModel Vm { get; }
        public TrackerData Data { get; }

        public Fixture(string share = "nil")
        {
            var script = new Script();
            Data = new TrackerData([], [new Snapshot("Player-1-0000000A", "Deadbeck", "Firemaw", 10, "Warrior", "WARRIOR", "Orc", "Orc", "male", "", "", 10, 0, "", "", "", 5, false, "")])
            {
                Friends = FriendData.ReadFriends(script.DoString("return " + Friends)),
                Share = FriendData.ReadShare(script.DoString("return " + share)),
            };
            Settings.TakeShare(Data.Share);
            Vm = new FriendsViewModel(Flavors.Forever, new GameLookup(Flavors.Forever, () => { }), Config, () => Settings, () => Changes++, () => { });
            Vm.Show(Data);
        }
    }

    [Fact]
    public void Friend_rows_never_reach_the_tabs_own_rows()
    {
        var f = new Fixture();
        Assert.Equal(["Kalle", "Zeb", "Zebalt"], f.Vm.Characters.Select(c => c.Name).Order());
        Assert.All(f.Vm.Characters, c => Assert.True(c.IsFriend));
        Assert.DoesNotContain(f.Vm.Characters, c => c.Guid == "Player-1-0000000A");
        var own = CharacterRow.Build(f.Data, Flavors.Forever, new GameLookup(Flavors.Forever, () => { }));
        Assert.Equal(["Player-1-0000000A"], own.Select(r => r.Guid));
    }

    [Fact]
    public void Chips_show_nicknames_but_keep_the_battletag_as_key()
    {
        var f = new Fixture();
        f.Config.FriendNicknames["Zeb#2222"] = "Zebbe";
        f.Vm.Show(f.Data);
        var chip = f.Vm.Contacts.Chips.Single(c => c.Key == "Zeb#2222");
        Assert.Equal("Zebbe (2)", chip.Label);
        f.Vm.Contacts.Selected = chip;
        Assert.Equal(["Zeb", "Zebalt"], f.Vm.Characters.Select(c => c.Name).Order());
        f.Vm.ContactRows.Single(c => c.Tag == "Zeb#2222").Nickname = "Z";
        Assert.Equal("Zeb#2222", f.Vm.Contacts.Key); // still picked
        Assert.Equal("Z (2)", f.Vm.Contacts.Chips.Single(c => c.Key == "Zeb#2222").Label);
        Assert.Equal("Z", f.Vm.Characters.First().FriendName);
    }

    [Fact]
    public void Hidden_friend_characters_fold_away()
    {
        var f = new Fixture();
        var zebalt = f.Vm.Characters.Single(c => c.Name == "Zebalt");
        f.Vm.HideCommand.Execute(zebalt);
        Assert.DoesNotContain(f.Vm.Characters, c => c.Name == "Zebalt");
        Assert.Equal(1, f.Vm.HiddenCount);
        Assert.Contains("Player-1-0000000C", f.Config.HiddenFriendCharacters);
        f.Vm.ShowHidden = true;
        Assert.Contains(f.Vm.Characters, c => c.Name == "Zebalt");
        f.Vm.UnhideCommand.Execute(f.Vm.Characters.Single(c => c.Name == "Zebalt"));
        Assert.Equal(0, f.Vm.HiddenCount);
    }

    [Fact]
    public void Changing_a_category_for_one_friend_edits_the_share_and_tells_the_addon()
    {
        var f = new Fixture("{ enabled = true, at = 100, contacts = { ['Zeb#2222'] = { share = true, receive = true } } }");
        var zeb = f.Vm.ContactRows.Single(c => c.Tag == "Zeb#2222");
        zeb.Gold = true;
        Assert.True(f.Settings.Share!.Contacts["Zeb#2222"].Gold);
        Assert.True(f.Settings.Share.At > 100);
        Assert.Equal(1, f.Changes);
        zeb.Gold = null;
        Assert.Null(f.Settings.Share.Contacts["Zeb#2222"].Gold); // the general choice again
        zeb.YouShare = false;
        Assert.False(f.Settings.Share.Contacts["Zeb#2222"].Share);
    }

    [Fact]
    public void General_categories_default_like_the_addon()
    {
        var f = new Fixture();
        Assert.True(f.Vm.ShareGear);
        Assert.False(f.Vm.ShareGold);
        Assert.Null(f.Settings.Share);
        f.Vm.ShareGold = true;
        Assert.True(f.Settings.Share!.Defaults.Gold);
        Assert.Equal(1, f.Changes);
    }

    [Fact]
    public void A_friend_with_another_protocol_says_needs_update()
    {
        var f = new Fixture();
        var kalle = f.Vm.ContactRows.Single(c => c.Tag == "Kalle#3333");
        Assert.True(kalle.NeedsUpdate);
        Assert.Contains("needs to update", kalle.VersionText);
        Assert.False(f.Vm.ContactRows.Single(c => c.Tag == "Zeb#2222").NeedsUpdate);
    }

    [Fact]
    public void Your_own_characters_can_be_shared_or_hidden()
    {
        var f = new Fixture("{ enabled = true, at = 100, newChars = false }");
        var own = Assert.Single(f.Vm.OwnCharacters);
        Assert.Equal("Deadbeck", own.Name);
        Assert.False(own.IsShared); // new characters hidden
        own.IsShared = true;
        Assert.True(f.Settings.Share!.Chars["Player-1-0000000A"]);
    }
}
