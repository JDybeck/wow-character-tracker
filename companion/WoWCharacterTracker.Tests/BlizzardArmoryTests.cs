using WoWCharacterTracker.Services;

namespace WoWCharacterTracker.Tests;

public class BlizzardArmoryTests
{
    [Fact]
    public void The_picture_comes_from_the_number_in_the_guid()
    {
        // A real character, its picture checked on the armory.
        Assert.Equal("https://render.worldofwarcraft.com/forever-eu/character/189/4017085-avatar.jpg",
            BlizzardArmory.AvatarUrl("forever", "eu", "Player-6948-003D4BBD"));
    }

    [Fact]
    public void The_old_models_have_their_own_picture() =>
        Assert.Equal("https://render.worldofwarcraft.com/forever-eu/character/189/4017085-avatar-sd.jpg",
            BlizzardArmory.AvatarUrl("forever", "eu", "Player-6948-003D4BBD", sd: true));

    [Theory]
    [InlineData("", "eu", "Player-6948-003D4BBD")]
    [InlineData("forever", "", "Player-6948-003D4BBD")]
    [InlineData("forever", "eu", "")]
    [InlineData("forever", "eu", "Creature-0-6948-0-1-2")]
    [InlineData("forever", "eu", "Player-6948-XYZ")]
    public void No_picture_without_game_region_or_player_guid(string game, string region, string guid) =>
        Assert.Null(BlizzardArmory.AvatarUrl(game, region, guid));

    [Fact]
    public void The_page_is_found_by_name_and_surname() =>
        Assert.Equal("https://worldofwarcraft.blizzard.com/en-us/forever/eu/armory/character/zeb-drygbeck",
            BlizzardArmory.ProfileUrl("forever", "eu", "Zeb", "Drygbeck"));

    [Fact]
    public void No_page_without_a_surname() =>
        Assert.Null(BlizzardArmory.ProfileUrl("forever", "eu", "Zeb", ""));

    static CharacterRow FriendRow()
    {
        var snap = Snapshot.Of(new Entry { Kind = "snapshot", Guid = "Player-6948-003D4BBD", Char = "Zeb", Surname = "Drygbeck", Realm = "R", Level = 60, At = 1, Region = "eu" });
        var row = CharacterRow.Build(new TrackerData([], [snap]), Flavors.Forever, new GameLookup(Flavors.Forever, () => { })).Single();
        row.IsFriend = true;
        return row;
    }

    [Fact]
    public void A_characters_own_model_wins_until_the_switch_matches_it()
    {
        var row = FriendRow();
        bool? saved = null;
        row.SetArmoryChoice(sdDefault: false, sdOverride: null, choice => saved = choice);
        Assert.True(row.UseArmoryHd);

        row.UseArmorySd = true; // differs from the switch: kept
        Assert.Equal((true, true), (row.ArmorySd, saved));

        row.SetArmoryDefault(true); // the switch now says the same: follows it
        row.SetArmoryDefault(false);
        Assert.False(row.ArmorySd);
    }

    [Fact]
    public void Picking_the_switchs_model_saves_no_override()
    {
        var row = FriendRow();
        bool? saved = true;
        var standard = true;
        row.SetPortraitChoice(true, s => standard = s);
        row.SetArmoryChoice(sdDefault: true, sdOverride: null, choice => saved = choice);
        Assert.True(row.UseStandardPortrait);

        row.UseArmorySd = true; // from Standard back to the armory, as the switch has it
        Assert.Equal((false, (bool?)null), (standard, saved));
        Assert.True(row.UseArmorySd);
    }

    [Fact]
    public void Only_Forever_has_this_armory()
    {
        Assert.Equal("forever", Flavors.Forever.ArmoryGame);
        Assert.Equal("", Flavors.Retail.ArmoryGame);
    }
}
