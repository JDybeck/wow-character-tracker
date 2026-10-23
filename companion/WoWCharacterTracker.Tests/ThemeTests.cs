using WoWCharacterTracker.Ui;

namespace WoWCharacterTracker.Tests;

public class ThemeTests
{
    [Fact]
    public void Unknown_or_missing_theme_falls_back_to_the_games_default()
    {
        Assert.Equal("midnight", Theme.Find(Flavors.Retail, null).Key);
        Assert.Equal("classic", Theme.Find(Flavors.Forever, "no-such-theme").Key);
        // A Retail theme picked for Forever (a hand-edited config) doesn't leak across games.
        Assert.Equal("classic", Theme.Find(Flavors.Forever, "midnight").Key);
    }

    [Fact]
    public void Shared_themes_are_offered_in_both_games()
    {
        foreach (var key in new[] { "alliance", "horde", "darnassus", "undercity", "icecrown" })
        {
            Assert.Equal(key, Theme.Find(Flavors.Retail, key).Key);
            Assert.Equal(key, Theme.Find(Flavors.Forever, key).Key);
        }
    }

    [Fact]
    public void Theme_keys_are_unique_per_game()
    {
        foreach (var flavor in new[] { Flavors.Retail, Flavors.Forever })
        {
            var keys = Theme.OptionsFor(flavor).Select(o => o.Key).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
        }
    }
}

public class TurnedOffTests
{
    [Fact]
    public void A_game_is_on_unless_turned_off()
    {
        var config = new Config();
        Assert.True(config.IsOn(Flavors.Retail));
        config.TurnedOff.Add(Flavors.Retail.Key);
        Assert.False(config.IsOn(Flavors.Retail));
        Assert.True(config.IsOn(Flavors.Forever));
    }
}
