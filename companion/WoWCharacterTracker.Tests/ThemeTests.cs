using WoWCharacterTracker.Ui;

namespace WoWCharacterTracker.Tests;

public class ThemeTests
{
    [Fact]
    public void Unknown_or_missing_theme_falls_back_to_the_games_default()
    {
        Assert.Equal("midnight", Theme.Find(Flavors.Retail, null).Key);
        Assert.Equal("classic", Theme.Find(Flavors.Forever, "no-such-theme").Key);
    }

    // Every theme in both games, the game's own first, so saved picks of either game keep working.
    [Fact]
    public void Every_theme_is_offered_in_both_games()
    {
        var retail = Theme.OptionsFor(Flavors.Retail).Select(o => o.Key).ToList();
        var forever = Theme.OptionsFor(Flavors.Forever).Select(o => o.Key).ToList();
        Assert.Equal(retail.Order(), forever.Order());
        foreach (var key in new[] { "midnight", "arcane", "silvermoon", "classic", "wisp", "emerald", "ember", "alliance", "horde", "darnassus", "undercity", "icecrown" })
        {
            Assert.Equal(key, Theme.Find(Flavors.Retail, key).Key);
            Assert.Equal(key, Theme.Find(Flavors.Forever, key).Key);
        }
        Assert.Equal(["midnight", "arcane", "silvermoon"], retail.Take(3));
        Assert.Equal(["classic", "wisp", "emerald", "ember"], forever.Take(4));
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
