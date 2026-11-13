using MoonSharp.Interpreter;
using WoWCharacterTracker.Services;

namespace WoWCharacterTracker.Tests;

// The Friends page: friends' characters as rows, chips per friend, hiding, and the sharing settings.
public class FriendsViewModelTests
{
    const string Friends = """
        { ['Zeb#2222'] = { seen = 2000, version = '0.33.0', chars = {
            ['Player-1-0000000B'] = { guid = 'Player-1-0000000B', char = 'Zeb', realm = 'Firemaw', level = 42, classFile = 'WARRIOR', raceFile = 'Orc', at = 1900, professions = 'Mining 150/225' },
            ['Player-1-0000000C'] = { guid = 'Player-1-0000000C', char = 'Zebalt', realm = 'Firemaw', level = 12, classFile = 'MAGE', at = 1000 } } },
          ['Kalle#3333'] = { seen = 1500, protocol = 2, chars = {
            ['Player-1-0000000E'] = { guid = 'Player-1-0000000E', char = 'Kalle', realm = 'Firemaw', level = 60, classFile = 'ROGUE', raceFile = 'Orc', at = 1400, professions = 'Mining 300/300' } } } }
        """;

    // Both friends as contacts whose characters you keep.
    const string KeepBoth = "{ enabled = true, at = 100, contacts = { ['Zeb#2222'] = { receive = true }, ['Kalle#3333'] = { receive = true } } }";

    sealed class Fixture
    {
        public Config Config { get; } = new();
        public FlavorSettings Settings { get; } = new();
        public int Changes { get; private set; }
        public FriendsViewModel Vm { get; }
        public TrackerData Data { get; }

        public Fixture(string share = KeepBoth)
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

    // The page groups by friend: friends by name (their nickname when set), each one's characters
    // the one seen last first.
    [Fact]
    public void Characters_come_by_friend_the_newest_first()
    {
        var f = new Fixture();
        Assert.Equal(["Kalle", "Zeb", "Zebalt"], f.Vm.Characters.Select(c => c.Name));
        f.Config.FriendNicknames["Zeb#2222"] = "Aaron";
        f.Vm.Show(f.Data);
        Assert.Equal(["Zeb", "Zebalt", "Kalle"], f.Vm.Characters.Select(c => c.Name));
        Assert.Equal(["Aaron", "Aaron", "Kalle#3333"], f.Vm.Characters.Select(c => c.FriendName));
        Assert.Equal([2000, 2000, 1500], f.Vm.Characters.Select(c => c.FriendSynced)); // when each friend was last heard from (seen)
    }

    // The grid of friends follows the dragged order (shared with the addon), the others by name;
    // dragging moves the card and letting go hands the new order on. A click lists only that friend.
    [Fact]
    public void Friend_cards_follow_the_dragged_order_and_pick_a_friend()
    {
        var f = new Fixture();
        Assert.Equal(["Kalle#3333", "Zeb#2222"], f.Vm.Cards.Select(c => c.Tag)); // by name
        Assert.Equal("Zeb", f.Vm.Cards[1].Current.Name); // the one played last
        Assert.Equal("2 characters", f.Vm.Cards[1].CountText);

        f.Config.FriendOrder = ["Zeb#2222"];
        f.Vm.Show(f.Data);
        Assert.Equal(["Zeb#2222", "Kalle#3333"], f.Vm.Cards.Select(c => c.Tag));
        Assert.Equal(["Zeb", "Zebalt", "Kalle"], f.Vm.Characters.Select(c => c.Name)); // the list follows it

        IReadOnlyList<string>? kept = null;
        f.Vm.OrderChanged += o => kept = o;
        var before = f.Vm.CardOrder;
        f.Vm.MoveCard("Kalle#3333", "Zeb#2222");
        Assert.Equal(["Kalle#3333", "Zeb#2222"], f.Vm.CardOrder); // moved live
        f.Vm.EndDrag(before, dropped: false); // Esc: back as it was, nothing kept
        Assert.Equal(["Zeb#2222", "Kalle#3333"], f.Vm.CardOrder);
        Assert.Null(kept);
        f.Vm.EndDrag(f.Vm.CardOrder, dropped: true); // dropped where it was: nothing to keep
        Assert.Null(kept);
        f.Vm.MoveCard("Kalle#3333", "Zeb#2222");
        f.Vm.EndDrag(before, dropped: true);
        Assert.Equal(["Kalle#3333", "Zeb#2222"], kept);

        f.Vm.SelectFriendCommand.Execute(f.Vm.Cards.Single(c => c.Tag == "Zeb#2222"));
        Assert.Equal(["Zeb", "Zebalt"], f.Vm.Characters.Select(c => c.Name));
        Assert.True(f.Vm.Cards.Single(c => c.Tag == "Zeb#2222").IsSelected);
        f.Vm.SelectFriendCommand.Execute(f.Vm.Cards.Single(c => c.Tag == "Zeb#2222")); // again: everyone
        Assert.Equal(3, f.Vm.Characters.Count);
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
        var f = new Fixture("nil");
        Assert.True(f.Vm.ShareGear);
        Assert.False(f.Vm.ShareGold);
        Assert.True(f.Vm.ShareLocation);
        Assert.True(f.Vm.ShareProfessions);
        Assert.Null(f.Settings.Share);
        f.Vm.ShareGold = true;
        Assert.True(f.Settings.Share!.Defaults.Gold);
        Assert.Equal(1, f.Changes);
    }

    [Fact]
    public void A_category_chip_cycles_from_all_friends_to_on_to_off()
    {
        var f = new Fixture("{ enabled = true, at = 100, defaults = { gold = false }, contacts = { ['Zeb#2222'] = { share = true, receive = true } } }");
        var zeb = f.Vm.ContactRows.Single(c => c.Tag == "Zeb#2222");
        Assert.Equal("Gold: like all friends (off)", zeb.Categories.Single(c => c.Key == "gold").Text);
        zeb.CycleCommand.Execute("gold");
        zeb = f.Vm.ContactRows.Single(c => c.Tag == "Zeb#2222");
        Assert.Equal("Gold: on", zeb.Categories.Single(c => c.Key == "gold").Text);
        zeb.CycleCommand.Execute("gold");
        zeb = f.Vm.ContactRows.Single(c => c.Tag == "Zeb#2222");
        Assert.False(f.Settings.Share!.Contacts["Zeb#2222"].Gold);
        zeb.CycleCommand.Execute("gold");
        Assert.Null(f.Settings.Share.Contacts["Zeb#2222"].Gold);
    }

    // A friend's highlights show in their character's details; posting them stays yours to do.
    [Fact]
    public void Highlights_are_a_category_and_a_friends_show_on_their_character()
    {
        var f = new Fixture("{ enabled = true, at = 100, contacts = { ['Zeb#2222'] = { share = true, receive = true } } }");
        Assert.True(f.Vm.ShareHighlights);
        var row = f.Vm.ContactRows.Single(c => c.Tag == "Zeb#2222");
        Assert.Equal("Highlights: like all friends (on)", row.Categories.Single(c => c.Key == "highlights").Text);
        row.CycleCommand.Execute("highlights");
        Assert.True(f.Settings.Share!.Contacts["Zeb#2222"].Highlights);

        var script = new Script();
        var data = f.Data with
        {
            Friends = FriendData.ReadFriends(script.DoString("""
                return { ['Zeb#2222'] = { chars = { ['Player-1-0000000B'] = { guid = 'Player-1-0000000B', char = 'Zeb', level = 42, classFile = 'WARRIOR', at = 1900,
                  highlights = 'boss~1800~42~Hogger~Elwynn~5\nloot~1700~41~Thunderfury~19019~5' } } } }
                """)),
        };
        f.Vm.Show(data);
        var zeb = f.Vm.Characters.Single(c => c.Name == "Zeb");
        Assert.Equal(["Killed Hogger (Elwynn)", "Looted Thunderfury"], zeb.Highlights.Select(h => h.Text));
        Assert.False(zeb.IsOwn); // no Post button (CharacterOverview)
    }

    [Fact]
    public void Map_position_is_a_category_and_the_rate_a_choice()
    {
        var f = new Fixture("{ enabled = true, at = 100, contacts = { ['Zeb#2222'] = { share = true, receive = true } } }");
        Assert.False(f.Vm.ShareMap);
        Assert.False(f.Vm.ShareOtherFaction);
        Assert.True(f.Vm.IsMapLive);
        var zeb = f.Vm.ContactRows.Single(c => c.Tag == "Zeb#2222");
        Assert.Equal("Map position: like all friends (off)", zeb.Categories.Single(c => c.Key == "map").Text);
        Assert.Equal("Map for the other faction: like all friends (off)", zeb.Categories.Single(c => c.Key == "otherFaction").Text);
        zeb.CycleCommand.Execute("map");
        Assert.True(f.Settings.Share!.Contacts["Zeb#2222"].Map);
        zeb.CycleCommand.Execute("otherFaction");
        Assert.True(f.Settings.Share.Contacts["Zeb#2222"].OtherFaction);
        f.Vm.ShareMap = true;
        Assert.True(f.Settings.Share.Defaults.Map);
        f.Vm.MapRateCommand.Execute("60");
        Assert.Equal(60, f.Settings.Share.MapRate);
        Assert.True(f.Vm.IsMapMinute);
        f.Vm.MapRateCommand.Execute("7"); // not a step
        Assert.Equal(60, f.Settings.Share.MapRate);
    }

    [Fact]
    public void Professions_are_a_category_like_the_others()
    {
        var f = new Fixture("{ enabled = true, at = 100, contacts = { ['Zeb#2222'] = { share = true, receive = true } } }");
        var zeb = f.Vm.ContactRows.Single(c => c.Tag == "Zeb#2222");
        Assert.Equal("Professions: like all friends (on)", zeb.Categories.Single(c => c.Key == "professions").Text);
        zeb.CycleCommand.Execute("professions");
        Assert.True(f.Settings.Share!.Contacts["Zeb#2222"].Professions);
        f.Vm.ShareProfessions = false;
        Assert.False(f.Settings.Share.Defaults.Professions);
    }

    // The professions view shows friends' characters under each friend's name (nickname first).
    [Fact]
    public void Friends_professions_are_grouped_by_friend()
    {
        var f = new Fixture();
        f.Config.FriendNicknames["Zeb#2222"] = "Zebbe";
        f.Vm.Show(f.Data);
        Assert.Equal(["Kalle#3333", "Zebbe"], f.Vm.Professions.Owners.Select(o => o.Name));
        var mining = f.Vm.Professions.Owners[1].Realms.Single().Professions.Single();
        Assert.Equal(("Mining", "Zeb", "150/225"), (mining.Name, mining.Characters.Single().Name, mining.Characters.Single().SkillText));
        f.Vm.HideCommand.Execute(f.Vm.Characters.Single(c => c.Name == "Zeb"));
        Assert.Equal(["Kalle#3333"], f.Vm.Professions.Owners.Select(o => o.Name)); // hidden characters too
    }

    [Fact]
    public void Hidden_rows_know_they_are_hidden()
    {
        var f = new Fixture();
        f.Vm.HideCommand.Execute(f.Vm.Characters.Single(c => c.Name == "Zeb"));
        f.Vm.ShowHidden = true;
        Assert.True(f.Vm.Characters.Single(c => c.Name == "Zeb").IsHiddenFriend);
        Assert.False(f.Vm.Characters.Single(c => c.Name == "Kalle").IsHiddenFriend);
    }

    [Fact]
    public void A_friend_who_shares_no_gear_has_no_inventory()
    {
        var f = new Fixture();
        Assert.Empty(f.Data.Friends.Single(c => c.Tag == "Kalle#3333").Inventories);
        Assert.False(f.Vm.Characters.Single(c => c.Name == "Kalle").ShowsGold);
    }

    // A sync while you type a nickname mustn't replace the row (and the text box) under you.
    [Fact]
    public void A_sync_keeps_the_sharing_rows_when_the_friends_are_the_same()
    {
        var f = new Fixture("{ enabled = true, at = 100, contacts = { ['Zeb#2222'] = { share = true, receive = true }, ['Kalle#3333'] = { receive = true } } }");
        var zeb = f.Vm.ContactRows.Single(c => c.Tag == "Zeb#2222");
        f.Vm.Show(f.Data);
        Assert.Same(zeb, f.Vm.ContactRows.Single(c => c.Tag == "Zeb#2222"));
    }

    [Fact]
    public void A_sync_keeps_the_open_friends_tab()
    {
        var f = new Fixture("{ enabled = true, at = 100, contacts = { ['Zeb#2222'] = { receive = true }, ['Kalle#3333'] = { receive = true } } }");
        f.Vm.SelectedCharacter = f.Vm.Characters.Single(c => c.Name == "Zeb");
        f.Vm.SelectedCharacter.DetailTab = "gear";
        f.Vm.Show(f.Data);
        Assert.Equal("Zeb", f.Vm.SelectedCharacter?.Name);
        Assert.Equal("gear", f.Vm.SelectedCharacter!.DetailTab);
    }

    [Fact]
    public void What_you_no_longer_keep_or_blocked_is_not_shown()
    {
        var f = new Fixture("{ enabled = true, at = 100, contacts = { ['Zeb#2222'] = { receive = false } }, blocked = { ['Kalle#3333'] = true } }");
        Assert.Empty(f.Vm.Characters);
        Assert.False(f.Vm.ContactRows.Single(c => c.Tag == "Kalle#3333").Receive); // not a contact: nothing kept
    }

    // A character removed on the Characters page isn't offered for sharing either, until it's played again.
    [Fact]
    public void Removed_characters_are_not_among_your_characters()
    {
        var f = new Fixture();
        f.Settings.Removed["Player-1-0000000A"] = 10; // after its LastSeen (5)
        f.Vm.Show(f.Data);
        Assert.Empty(f.Vm.OwnCharacters);
        f.Settings.Removed["Player-1-0000000A"] = 1; // played since
        f.Vm.Show(f.Data);
        Assert.Single(f.Vm.OwnCharacters);
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
