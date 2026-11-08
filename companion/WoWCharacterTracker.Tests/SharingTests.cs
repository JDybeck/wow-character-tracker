namespace WoWCharacterTracker.Tests;

public class DiscordPausedTests
{
    static Entry Level(int level) => new() { Kind = "level", Guid = "g1", Level = level, At = 1 };

    [Fact]
    public void Paused_tab_wants_nothing()
    {
        var s = Flavors.Forever.Defaults();
        Assert.True(s.Wants(Level(30), []));
        s.DiscordPaused = true;
        Assert.False(s.Wants(Level(30), []));
        Assert.False(s.Wants(new Entry { Kind = "loot", Guid = "g1", Quality = 5, At = 1 }, []));
    }
}
