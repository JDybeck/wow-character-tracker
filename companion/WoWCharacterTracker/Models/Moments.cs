namespace WoWCharacterTracker.Models;

// The Sharing table: one row per moment, a column per place it can go. Each row reads and writes the
// FlavorSettings fields and kind lists that already exist, so configs and Settings.lua don't change.
// Shot: the screenshot kind (shared where the moment is the same event: max level is a level-up, epic
// and rare are both loot). Announce/Ask: the addon's
// kind keys. Alert: the stream overlay's alert key. Test: the sample post (SampleEvents).
sealed record Moment(string Key, string Name, string Tip,
    Func<FlavorSettings, bool> GetPost, Action<FlavorSettings, bool> SetPost,
    string? Shot = null, string? Announce = null, string? AnnounceSample = null,
    string? Ask = null, string? Alert = null, string Test = "");

static class Moments
{
    public static readonly IReadOnlyList<Moment> All =
    [
        new("level", "Level-ups", "Dings, with how long the level took", s => s.PostLevels, (s, on) => s.PostLevels = on,
            Shot: "level", Announce: "level", AnnounceSample: "\"Ding! Level 23. 22 to 23 took 41m 12s.\"", Ask: "level", Alert: "level", Test: "level"),
        new("maxlevel", "Max level", "Posted with extra flair, even when the level rule skips that level", s => s.PostMaxLevel, (s, on) => s.PostMaxLevel = on,
            Shot: "level", Announce: "maxlevel", AnnounceSample: "\"Reached max level 60! Total played 6d 4h 12m.\"", Ask: "maxlevel", Alert: "maxlevel", Test: "max"),
        new("epic", "Epic loot", "Purple items. Legendary gear is always posted. The screenshot is for any posted loot.", s => s.PostEpic, (s, on) => s.PostEpic = on,
            Shot: "loot", Announce: "loot", AnnounceSample: "\"Looted [Item]!\" For epic and legendary items.", Ask: "epic", Alert: "epic", Test: "epic"),
        new("rare", "Rare loot", "Blue items", s => s.PostRare, (s, on) => s.PostRare = on, Shot: "loot", Announce: "rare", AnnounceSample: "\"Looted [Item]!\" For blue items (Bind on Equip only when that's on).", Ask: "rare", Alert: "rare", Test: "rare"),
        new("followed", "Followed items", "Anything on your Followed items list, whatever its quality", s => s.PostFollowed, (s, on) => s.PostFollowed = on,
            Announce: "followed", AnnounceSample: "\"Got [Item] from my followed items!\"", Alert: "followed", Test: "followed"),
        new("mount", "New mounts", "Bought or learned", s => s.PostMounts, (s, on) => s.PostMounts = on,
            Shot: "mount", Announce: "mount", AnnounceSample: "\"Got a new mount: Swift Palomino!\"", Ask: "mount", Alert: "mount", Test: "mount"),
        new("boss", "End boss kills", "The last boss of a dungeon or raid, with the group", s => s.PostBosses, (s, on) => s.PostBosses = on,
            Shot: "boss", Announce: "boss", AnnounceSample: "\"Killed Edwin VanCleef in The Deadmines!\"", Ask: "boss", Alert: "boss", Test: "boss"),
        new("run", "Dungeon runs", "One post per dungeon run: the bosses, the group with roles and who got what. The run's boss kills and loot don't post on their own (legendaries still do).",
            s => s.PostRuns, (s, on) => s.PostRuns = on, Test: "run"),
        new("keyrun", "Mythic+ runs", "Finished keys from the level set under Advanced", s => s.PostKeyRuns, (s, on) => s.PostKeyRuns = on,
            Shot: "keyrun", Announce: "keyrun", AnnounceSample: "\"Timed +12 Ara-Kara, City of Echoes (+2)!\"", Ask: "keyrun", Alert: "keyrun", Test: "keyrun"),
        new("death", "Hardcore deaths", "What killed them, total played and the gold they had", s => s.PostDeaths, (s, on) => s.PostDeaths = on,
            Shot: "death", Announce: "death", AnnounceSample: "\"Fell at level 34 to Hogger. Rest in peace.\"", Ask: "death", Alert: "death", Test: "death"),
        new("duel", "Duels won", "Who you beat, or who ran away", s => s.PostDuels, (s, on) => s.PostDuels = on,
            Shot: "duel", Announce: "duel", AnnounceSample: "\"Won a duel against Grimtusk.\"", Ask: "duel", Alert: "duel", Test: "duel"),
        new("makgora", "Mak'gora won", "The hardcore duel to the death", s => s.PostMakgora, (s, on) => s.PostMakgora = on,
            Shot: "makgora", Announce: "makgora", AnnounceSample: "\"Won a Mak'gora against Grimtusk!\"", Ask: "makgora", Alert: "makgora", Test: "makgora"),
        new("bg", "Battleground wins", "Which battleground you won", s => s.PostBattlegrounds, (s, on) => s.PostBattlegrounds = on,
            Shot: "bg", Announce: "bg", AnnounceSample: "\"Won Warsong Gulch!\"", Ask: "bg", Alert: "bg", Test: "bg"),
        new("profession", "Profession milestones", "A main profession passing one of the steps", s => s.PostProfessions, (s, on) => s.PostProfessions = on,
            Shot: "profession", Announce: "profession", AnnounceSample: "\"Reached 150 Mining!\"", Ask: "profession", Alert: "profession", Test: "profession"),
        new("mplus", "Mythic+ rating", "Passing a rating step", s => s.PostMPlus, (s, on) => s.PostMPlus = on,
            Announce: "mplus", AnnounceSample: "\"Reached 2500 Mythic+ rating!\"", Ask: "mplus", Alert: "mplus", Test: "mplus"),
        new("pvp", "PvP rating", "Passing a rating step", s => s.PostPvp, (s, on) => s.PostPvp = on,
            Announce: "pvp", AnnounceSample: "\"Reached 1800 rating in 3v3!\"", Ask: "pvp", Alert: "pvp", Test: "pvp"),
    ];

    public static IReadOnlyList<Moment> For(Flavor flavor) => [.. All.Where(m => EventKinds.OfferedIn(m.Key, flavor.HasRatings))];

    // The rule under a row's name, for the rows that have one.
    public static string? Rule(Moment m, FlavorSettings s) => m.Key switch
    {
        "level" => s.LevelEveryBefore == s.LevelEveryAfter
            ? (s.LevelEveryBefore == 1 ? "every level" : $"every {s.LevelEveryBefore}")
            : $"every {s.LevelEveryBefore} to {s.LevelSplit}, then every {(s.LevelEveryAfter == 1 ? "level" : s.LevelEveryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture))}",
        "rare" => s.RareBoeOnly ? "Bind on Equip only" : null,
        "boss" => s.FirstBossKillOnly ? "first kill only" : null,
        "run" => "in place of the run's boss and loot posts",
        "keyrun" => $"+{s.KeyRunMinLevel} and up" + (s.KeyRunTimedOnly ? ", in time" : ""),
        "profession" => string.Join(", ", s.ProfessionSteps),
        _ => null,
    };
}
