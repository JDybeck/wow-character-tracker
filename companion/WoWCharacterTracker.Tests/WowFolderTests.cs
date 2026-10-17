namespace WoWCharacterTracker.Tests;

// Wow.Resolve on a made-up World of Warcraft folder (in the temp folder): what the user may pick in
// the folder picker, and what they're told when it isn't right.
public sealed class WowFolderTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "wct-tests-" + Guid.NewGuid().ToString("N"), "World of Warcraft");

    public WowFolderTests()
    {
        foreach (var game in new[] { "_retail_", "_classic_beta_" })
        {
            Directory.CreateDirectory(Path.Combine(root, game, "Interface", "AddOns"));
            Directory.CreateDirectory(Path.Combine(root, game, "WTF", "Account"));
        }
    }

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(root)!, recursive: true);

    [Fact]
    public void The_wow_folder_finds_the_games_folder()
    {
        Assert.Equal(Path.Combine(root, "_retail_"), Wow.Resolve(root, Flavors.Retail).Path);
        Assert.Equal(Path.Combine(root, "_classic_beta_"), Wow.Resolve(root, Flavors.Forever).Path);
    }

    [Fact]
    public void Folders_inside_the_game_folder_work_too()
    {
        Assert.Equal(Path.Combine(root, "_retail_"), Wow.Resolve(Path.Combine(root, "_retail_", "Interface", "AddOns"), Flavors.Retail).Path);
        Assert.Equal(Path.Combine(root, "_classic_beta_"), Wow.Resolve(Path.Combine(root, "_classic_beta_", "WTF") + "\\", Flavors.Forever).Path);
    }

    [Fact]
    public void The_other_games_folder_points_to_this_one_next_to_it()
    {
        // Picking Forever's folder on the Retail tab used to be taken as Retail.
        Assert.Equal(Path.Combine(root, "_retail_"), Wow.Resolve(Path.Combine(root, "_classic_beta_"), Flavors.Retail).Path);
    }

    [Fact]
    public void A_missing_game_is_explained()
    {
        Directory.Delete(Path.Combine(root, "_retail_"), recursive: true);
        var (path, problem) = Wow.Resolve(Path.Combine(root, "_classic_beta_"), Flavors.Retail);
        Assert.Null(path);
        Assert.Contains("Forever folder (_classic_beta_), not Retail", problem);

        (path, problem) = Wow.Resolve(root, Flavors.Retail);
        Assert.Null(path);
        Assert.Contains("has _classic_beta_ but no _retail_", problem);
    }

    [Fact]
    public void Some_other_folder_says_what_to_pick()
    {
        var (path, problem) = Wow.Resolve(Path.GetTempPath(), Flavors.Forever);
        Assert.Null(path);
        Assert.Contains("Pick your World of Warcraft folder", problem);
        Assert.Contains("_classic_beta_", problem);
    }
}
