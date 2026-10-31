namespace WoWCharacterTracker.Models;

// A game version the app has a tab for. Each one lives in its own folder inside the WoW install.
// What sets the games apart is said here once, as what each game has, so the rest of the app asks
// "flavor.HasRatings" instead of "is this Retail".
sealed record Flavor(string Key, string Name, string[] Folders, string WowheadPath, Func<FlavorSettings> Defaults)
{
    // Shown on the tab. Loaded from Warcraft Wiki at runtime and kept on disk (Ui/ImageCache), never shipped.
    public string LogoUrl { get; init; } = "";

    // Characters have a ruleset (hardcore, self-found...): the list is grouped by it instead of by
    // realm, webhooks can be limited to some, cards show it, and the test characters live here.
    public bool HasRulesets { get; init; }
    public string GroupLabel => HasRulesets ? "Ruleset" : "Realm";

    // UnitName gives a surname (Ronnie Raket), so posts can use the first, last or full name.
    public bool HasSurnames { get; init; }

    // No armory: the character's picture is the portrait the addon shows in its check-in shot.
    public bool HasInGamePortraits { get; init; }

    // Raider.IO has a render of the character (needs the region).
    public bool HasRenders { get; init; }

    // Specializations with roles, Mythic+ and rated PvP, the talent loadout export string.
    public bool HasSpecs { get; init; }
    public bool HasRatings { get; init; }

    // The classic three talent trees, linked to Wowhead with its classic-style calculator code.
    public bool HasClassicTalents { get; init; }

    // Slot 18 (ranged or relic) and no catalyst (Retail dropped slot 18 in Legion).
    public bool HasRangedSlot { get; init; }

    // The XP table has gaps, so time to max is estimated from Classic's table (CharacterLeveling).
    public bool EstimatesXp { get; init; }

    // The ready-made lists of items it offers to follow.
    public IReadOnlyList<ReadyMadeList> ReadyMadeLists { get; init; } = [];

    // The level cap, for the max level sample post.
    public int MaxLevel { get; init; }
}

static class Flavors
{
    // Retail has fewer low level moments worth a post: every 10 levels, then every level at the top.
    public static readonly Flavor Retail = new("retail", "Retail", ["_retail_"], "", () => new FlavorSettings
    {
        LevelSplit = 80, LevelEveryBefore = 10, LevelEveryAfter = 1,
        PostRare = false, PostEpic = false, PostMounts = true, PostBosses = false, PostDeaths = false,
        ProfessionSteps = [.. FlavorSettings.RetailProfessionSteps], // each expansion's skill line tops out at 100
    })
    {
        LogoUrl = "https://warcraft.wiki.gg/images/thumb/Midnight_2024_logo.png/250px-Midnight_2024_logo.png",
        HasRenders = true, HasSpecs = true, HasRatings = true, MaxLevel = 90,
    };

    // Forever is what this is really made for. Live is _forever_; the beta folders are the fallback,
    // and a saved beta folder moves to _forever_ once that's installed (Wow.NewestFolder).
    public static readonly Flavor Forever = new("forever", "Forever", ["_forever_", "_forever_beta_", "_classic_beta_"], "forever/", () => new FlavorSettings
    {
        LevelSplit = 30, LevelEveryBefore = 5, LevelEveryAfter = 1,
        PostRare = true, PostEpic = true, PostMounts = true, PostBosses = true, PostDeaths = true,
        ProfessionSteps = [.. FlavorSettings.ClassicProfessionSteps],
        Screenshot = new() { ["level"] = true, ["mount"] = true, ["boss"] = true, ["death"] = true },
        PostScreenshot = new() { ["level"] = true, ["mount"] = true, ["boss"] = true, ["death"] = true },
    })
    {
        LogoUrl = "https://warcraft.wiki.gg/images/thumb/WoW_Forever_Logo.png/250px-WoW_Forever_Logo.png",
        HasRulesets = true, HasSurnames = true, HasInGamePortraits = true, HasClassicTalents = true,
        HasRangedSlot = true, EstimatesXp = true, ReadyMadeLists = ReadyMadeList.All, MaxLevel = 60,
    };

    public static readonly IReadOnlyList<Flavor> All = [Retail, Forever];

    // The game a stream snapshot names (StreamSnapshot.Game is the flavor's name).
    public static Flavor? Named(string name) => All.FirstOrDefault(f => f.Name == name);
}
