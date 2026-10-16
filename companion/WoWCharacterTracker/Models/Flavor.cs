namespace WoWCharacterTracker.Models;

// A game version the app has a tab for. Each one lives in its own folder inside the WoW install.
sealed record Flavor(string Key, string Name, string[] Folders, string WowheadPath, Func<FlavorSettings> Defaults)
{
    // Shown on the tab. Loaded from Warcraft Wiki at runtime and cached (Logos.cs), never shipped.
    public string LogoUrl { get; init; } = "";
}

static class Flavors
{
    // Retail has fewer low level moments worth a post: every 10 levels, then every level at the top.
    public static readonly Flavor Retail = new("retail", "Retail", ["_retail_"], "", () => new FlavorSettings
    {
        LevelSplit = 80, LevelEveryBefore = 10, LevelEveryAfter = 1,
        PostRare = false, PostEpic = false, PostMounts = true, PostBosses = false, PostDeaths = false,
    }) { LogoUrl = "https://warcraft.wiki.gg/images/thumb/Midnight_2024_logo.png/250px-Midnight_2024_logo.png" };

    // Forever is what this is really made for. Its final folder name isn't known yet, so the
    // beta folder is the fallback.
    public static readonly Flavor Forever = new("forever", "Forever", ["_forever_", "_forever_beta_", "_classic_beta_"], "forever/", () => new FlavorSettings
    {
        LevelSplit = 30, LevelEveryBefore = 5, LevelEveryAfter = 1,
        PostRare = true, PostEpic = true, PostMounts = true, PostBosses = true, PostDeaths = true,
        Screenshot = new() { ["level"] = true, ["mount"] = true, ["boss"] = true, ["death"] = true },
        PostScreenshot = new() { ["level"] = true, ["mount"] = true, ["boss"] = true, ["death"] = true },
    }) { LogoUrl = "https://warcraft.wiki.gg/images/thumb/WoW_Forever_Logo.png/250px-WoW_Forever_Logo.png" };

    // The kinds of events that can get a screenshot, as the addon names them.
    public static readonly string[] ShotKinds = ["level", "loot", "mount", "boss", "death", "duel", "makgora"];

    public static readonly IReadOnlyList<Flavor> All = [Retail, Forever];
}
