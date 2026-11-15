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

    [Fact]
    public void Only_Forever_has_this_armory()
    {
        Assert.Equal("forever", Flavors.Forever.ArmoryGame);
        Assert.Equal("", Flavors.Retail.ArmoryGame);
    }
}
