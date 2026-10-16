using System.Text.Json;
using System.Text.Json.Serialization;

namespace WoWCharacterTracker.Models;

// Per tab: which folder, which webhooks and what to post.
class FlavorSettings
{
    public string? Path { get; set; }

    // Webhooks this tab posts to. Null means never chosen: the first webhook is used.
    // An empty list means the user turned them all off.
    public List<string>? ActiveWebhookIds { get; set; }

    // From before several webhooks could be active. Moved into ActiveWebhookIds on load.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActiveWebhookId { get; set; }

    // Level-ups: up to LevelSplit, post every LevelEveryBefore levels; after that every LevelEveryAfter.
    public bool PostLevels { get; set; } = true;
    public int LevelSplit { get; set; } = 30;
    public int LevelEveryBefore { get; set; } = 5;
    public int LevelEveryAfter { get; set; } = 1;

    public bool PostRare { get; set; }
    public bool PostEpic { get; set; }   // epic and legendary
    public bool PostMounts { get; set; } = true;
    public bool PostBosses { get; set; }
    public bool PostDeaths { get; set; }

    // Duels, hardcore Mak'gora, and retail rating milestones (posted when a step is passed).
    public bool PostDuels { get; set; }
    public bool PostMakgora { get; set; } = true;
    public bool PostMPlus { get; set; } = true;
    public bool PostPvp { get; set; } = true;
    public List<int> MPlusSteps { get; set; } = [1000, 1500, 2000, 2500, 3000, 3500];
    public List<int> PvpSteps { get; set; } = [1400, 1600, 1800, 2100, 2400];

    // Boss kills aren't posted when someone in the group matches this regex (e.g. friends who run
    // the tracker too, so the same kill isn't posted several times). Empty means never skip.
    public string SkipBossGroupRegex { get; set; } = "";

    // Forever has surnames. Which name posts use: "full" (Ronnie Raket), "first" or "last".
    public string NameStyle { get; set; } = "full";

    // The member that matches, or null.
    public GroupMember? SkipBossBecause(Entry e)
    {
        if (string.IsNullOrWhiteSpace(SkipBossGroupRegex)) return null;
        try
        {
            var re = new System.Text.RegularExpressions.Regex(SkipBossGroupRegex,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
            return e.Members.FirstOrDefault(m => re.IsMatch(m.Name) || re.IsMatch(m.FullName));
        }
        catch (ArgumentException) { return null; } // invalid regex: the settings page says so
    }

    // Per kind of event: take a screenshot in game, and attach it to the Discord post.
    public Dictionary<string, bool> Screenshot { get; set; } = [];
    public Dictionary<string, bool> PostScreenshot { get; set; } = [];
    public bool HideChatInShots { get; set; } = true; // chat made see-through while a screenshot is taken

    // Characters removed in the app: guid -> when. A character played after that shows up again.
    public Dictionary<string, long> Removed { get; set; } = [];

    public bool Shoots(string kind) => Screenshot.GetValueOrDefault(kind);
    public bool PostsShot(string kind) => Shoots(kind) && PostScreenshot.GetValueOrDefault(kind);

    public bool LevelWanted(int level)
    {
        if (!PostLevels) return false;
        var every = Math.Max(1, level <= LevelSplit ? LevelEveryBefore : LevelEveryAfter);
        return level % every == 0;
    }

    public static FlavorSettings DefaultsFor(Flavor f) => f.Defaults();
}
