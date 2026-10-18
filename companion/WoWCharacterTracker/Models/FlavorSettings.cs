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
    public List<int> MPlusSteps { get; set; } = [.. DefaultMPlusSteps];
    public List<int> PvpSteps { get; set; } = [.. DefaultPvpSteps];
    public static readonly int[] DefaultMPlusSteps = [2000, 2500, 3000, 3500, 4000];
    public static readonly int[] DefaultPvpSteps = [1800, 2100, 2400, 2700];
    // The first defaults, replaced on load by the ones above if they were never changed.
    public static readonly int[] OldMPlusSteps = [1000, 1500, 2000, 2500, 3000, 3500];
    public static readonly int[] OldPvpSteps = [1400, 1600, 1800, 2100, 2400];

    // Reaching max level: posted with extra flair even when the level rule skips that level.
    // Off means max level is just another level for the level rule.
    public bool PostMaxLevel { get; set; } = true;

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
    public bool HideUiInShots { get; set; } // the whole interface hidden for the shot, like Alt+Z
    public bool FlightReloadPrompt { get; set; } = true; // the addon offers a /reload on flight paths when bags changed
    public bool ChatReloadReminder { get; set; } = true; // and reminds in chat (good moments, every 30 minutes)

    // The addon tells the guild and/or the group in game chat (Announce.lua), for these kinds:
    // level, maxlevel, boss, loot (epic and better, as a link), mount, duel, makgora, bg, mplus, pvp, death.
    public bool AnnounceGuild { get; set; }
    public bool AnnounceParty { get; set; }
    public List<string> AnnounceKinds { get; set; } = [.. DefaultAnnounceKinds];
    public static readonly string[] DefaultAnnounceKinds = ["level", "maxlevel", "boss", "loot", "mount", "mplus", "pvp"];

    // Where the addon draws its data grid for the instant posts (TOPLEFT, TOPRIGHT, BOTTOMLEFT,
    // BOTTOMRIGHT) and whether the small label shows next to it. The app reads every corner either way.
    public string GridCorner { get; set; } = "TOPLEFT";
    public bool GridLabel { get; set; } = true;
    public static readonly string[] Corners = ["TOPLEFT", "TOPRIGHT", "BOTTOMLEFT", "BOTTOMRIGHT"];
    public static string CornerOrDefault(string? corner) => Corners.Contains(corner) ? corner! : "TOPLEFT";
    public bool? AdvancedCombatLog { get; set; } // the game's Advanced Combat Logging; null = leave the player's setting alone

    // Characters removed in the app: guid -> when. A character played after that shows up again.
    public Dictionary<string, long> Removed { get; set; } = [];

    // What gets posted (the level rule, each kind, rating steps, screenshots) taken over from other
    // settings: Reset to defaults and pasted settings codes. Folder, webhooks and names stay.
    public void CopyPostingFrom(FlavorSettings d)
    {
        (PostLevels, LevelSplit, LevelEveryBefore, LevelEveryAfter) = (d.PostLevels, d.LevelSplit, d.LevelEveryBefore, d.LevelEveryAfter);
        (PostRare, PostEpic, PostMounts, PostBosses, PostDeaths) = (d.PostRare, d.PostEpic, d.PostMounts, d.PostBosses, d.PostDeaths);
        (PostMaxLevel, PostDuels, PostMakgora, PostMPlus, PostPvp) = (d.PostMaxLevel, d.PostDuels, d.PostMakgora, d.PostMPlus, d.PostPvp);
        (MPlusSteps, PvpSteps) = ([.. d.MPlusSteps], [.. d.PvpSteps]);
        Screenshot = new(d.Screenshot);
        PostScreenshot = new(d.PostScreenshot);
    }

    public bool Shoots(string kind) => Screenshot.GetValueOrDefault(kind);
    public bool PostsShot(string kind) => Shoots(kind) && PostScreenshot.GetValueOrDefault(kind);

    public bool LevelWanted(int level)
    {
        if (!PostLevels) return false;
        var every = Math.Max(1, level <= LevelSplit ? LevelEveryBefore : LevelEveryAfter);
        return level % every == 0;
    }

    // Whether this tab posts the event. Legendary loot is always posted, with extra flair.
    public bool Wants(Entry e) => e.Kind switch
    {
        "level" => (e.MaxLevel && PostMaxLevel) || LevelWanted(e.Level),
        "loot" => e.Quality >= 5 || (e.Quality == 4 ? PostEpic : e.Quality == 3 && PostRare),
        "mount" => PostMounts,
        "boss" => PostBosses && SkipBossBecause(e) is null,
        "death" => PostDeaths,
        "duel" => PostDuels && !e.Lost, // only wins are posted
        "makgora" => PostMakgora && !e.Lost,
        "mplus" => PostMPlus,
        "pvp" => PostPvp,
        _ => false,
    };

}
