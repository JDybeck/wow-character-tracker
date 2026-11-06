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
                  race = 'Orc', raceFile = 'Orc', faction = 'Horde', spec = 'Arms', ilvl = 38, at = 1900, zone = 'Duskwood', money = 5000,
                  gear = '1~19019,16~12345', enchants = '16~12345~Crusader~1900', talentCalc = '--505', received = 1950 } } } }
            """));
        var zeb = Assert.Single(friends);
        Assert.Equal("Zeb#2222", zeb.Tag);
        Assert.Equal("0.33.0", zeb.Version);
        var s = Assert.Single(zeb.Characters);
        Assert.Equal(("Zeb", "Firemaw", 42, "WARRIOR", "Duskwood", 1900L, 5000L), (s.Char, s.Realm, s.Level, s.ClassFile, s.Zone, s.LastSeen, s.Money));
        Assert.Equal("--505", s.TalentCalc);
        var inv = Assert.Single(zeb.Inventories);
        Assert.Equal(2, inv.Gear.Count);
        Assert.Equal("Crusader", Assert.Single(inv.Enchants).Text);
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
    public void Share_settings_are_read_with_defaults_for_what_is_missing()
    {
        var share = FriendData.ReadShare(Lua("""
            { enabled = true, at = 300, defaults = { gold = true }, chars = { ['Player-1-0000000D'] = false },
              contacts = { ['Zeb#2222'] = { share = true, receive = true, location = false } }, blocked = { ['Bad#1'] = true } }
            """))!;
        Assert.True(share.Enabled);
        Assert.Equal(300, share.At);
        Assert.True(share.Defaults.Gold);
        Assert.True(share.Defaults.Gear);       // missing: the addon's default
        Assert.False(share.Defaults.Location);
        Assert.False(share.Chars["Player-1-0000000D"]);
        Assert.False(share.Contacts["Zeb#2222"].Location);
        Assert.Null(share.Contacts["Zeb#2222"].Gold); // follows the general choice
        Assert.Contains("Bad#1", share.Blocked);
        Assert.Null(FriendData.ReadShare(Lua("nil")));
    }
}
