namespace WoWCharacterTracker.Tests;

public class SearchTests
{
    [Theory]
    [InlineData("zeb", "Zeb", "", 0)]                    // the name starts with it
    [InlineData("raket", "Ronnie Raket", "", 1)]         // a word in the name does
    [InlineData("nni", "Ronnie Raket", "", 2)]           // somewhere in the name
    [InlineData("paladin", "Zeb", "Paladin Human", 3)]   // only the extra words
    [InlineData("fagel", "Fågelmannen", "", 0)]          // accents don't count
    [InlineData("ZEB", "zeb", "", 0)]                    // nor case
    public void Scores_how_well_it_fits(string query, string name, string words, int expected) =>
        Assert.Equal(expected, Search.Score(query, name, words));

    [Fact]
    public void Every_word_has_to_be_found() =>
        Assert.Equal(3, Search.Score("human paladin", "Zeb", "Paladin Human EU"));

    [Theory]
    [InlineData("warrior")]
    [InlineData("zeb warrior")]
    [InlineData("")]
    [InlineData("   ")]
    public void No_match_is_null(string query) =>
        Assert.Null(Search.Score(query, "Zeb", "Paladin Human"));

    [Fact]
    public void Topics_open_pages_that_exist()
    {
        string[] pages = ["settings", "sharing", "data", "uninstall", "friends", "stats", "professions", "followed"];
        Assert.All(SearchTopic.All, t => Assert.Contains(t.Page, pages));
        Assert.All(SearchTopic.All.Where(t => t.Place.Length > 0), t => Assert.Contains(t.Place, new[] { "discord", "stream" }));
    }

    [Fact]
    public void Webhook_finds_the_discord_settings() =>
        Assert.Contains(SearchTopic.All, t => t.Place == "discord" && Search.Score("webhook", t.Title, t.Words) is not null);
}
