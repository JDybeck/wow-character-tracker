namespace WoWCharacterTracker.Models;

// The latest state of a character, saved by the addon on every /played, /reload and logout.
// Xp, XpMax, MaxLevel, LevelTime (played on the current level) and Recipes ("Alchemy~2329,2330" lines,
// ProfessionSkills.Recipes) come from the saved file only.
record Snapshot(string Guid, string Char, string Realm, int Level, string Class, string ClassFile,
    string Race, string RaceFile, string Sex, string Spec, string Role, double ItemLevel, long Played,
    string Zone, string Professions, string Talents, long LastSeen, bool Hardcore, string Ruleset,
    long Money = 0, string Crests = "", string? Guild = null, string Surname = "", string TalentPicks = "", string TalentExport = "",
    string Stats = "", string TalentCalc = "", string Region = "", long Xp = 0, long XpMax = 0, int MaxLevel = 0,
    string Keystone = "", string ZoneType = "", int MplusScore = 0, string PvpRatings = "", string Faction = "", long LevelTime = 0, string Recipes = "")
{
    // The character as an event (or live check-in) describes it. Read with the same keys as events
    // (Entry.From), so a new character field is added there once; what only the saved file has
    // (talent picks, stats, XP) is filled in by SavedVariables.
    public static Snapshot Of(Entry e) => new(e.Guid, e.Char, e.Realm, e.Level, e.Class, e.ClassFile,
        e.Race, e.RaceFile, e.Sex, e.Spec, e.Role, e.ItemLevel, e.Played, e.Zone, e.Professions, e.Talents,
        e.At, e.Hardcore, e.Ruleset, e.Money, e.Crests, e.Guild, e.Surname, TalentExport: e.TalentExport, TalentCalc: e.TalentCalc,
        Region: e.Region, Keystone: e.Keystone, ZoneType: e.ZoneType, MplusScore: e.MplusScore, PvpRatings: e.PvpRatings, Faction: e.Faction);
}

// The In game page's settings as last changed in game (the addon's options page), and when. Null:
// the change doesn't have that setting (an older addon), so the app's own value stays, as in the addon.
record InGameSettings(long At, bool? AnnounceGuild, bool? AnnounceParty, List<string>? AnnounceKinds, List<string>? AnnouncePartyKinds, List<string>? AskKinds,
    bool? FlightReload, bool? ChatReminder, string? GridCorner, bool? GridLabel, bool? KeepAllHistory, int? AskSeconds = null, List<string>? Ignored = null,
    string? Preset = null);

// One character's day (the addon's today[guid]), Day as "2026-11-09": item level and copper at the
// day's start and now, and what was turned in and gathered.
record DayTotals(string Guid, string Day, double IlvlStart, double Ilvl, long MoneyStart, long Money,
    int Quests, int Herbs, int Ore, int Skins, int Fish)
{
    // A friend's day also counts their moments (FriendData.lua encodeDays), from all their events.
    // HasMoments: it did (a friend sharing highlights, on 0.41.0 or newer); then these beat their highlights.
    public bool HasMoments { get; init; }
    public int Bosses { get; init; }
    public int Epics { get; init; }
    public int Mounts { get; init; }
    public int Duels { get; init; }
    public int Battlegrounds { get; init; }
    public int Deaths { get; init; }
}

// Everything read for one game tab: events, character snapshots, what each character carries, and
// the XP each level needs on that client ([level] = XP).
record TrackerData(List<Entry> Events, List<Snapshot> Characters)
{
    public List<Inventory> Inventories { get; init; } = [];
    public Dictionary<int, long> XpTable { get; init; } = [];
    public Dictionary<int, int> ItemClasses { get; init; } = []; // itemId -> the game's item class (Inventory.lua)
    public List<GameNote> Notes { get; init; } = []; // notes edited in game (NoteBox.lua)
    public List<GameNickname> Nicknames { get; init; } = []; // friends' nicknames set in game (Friends.lua)
    public GameFriendOrder? FriendOrder { get; init; } // the Friends tab's order, dragged in game
    public InGameSettings? InGame { get; init; } // In game page settings changed in game (Shared.lua)
    public List<FriendContact> Friends { get; init; } = []; // what Battle.net friends shared (Friends.lua), never your own characters
    public ShareSettings? Share { get; init; } // sharing settings as changed in game
    public List<IncognitoWindow> Incognito { get; init; } = []; // stretches of incognito: nothing from them is posted (Shared.lua)
    public List<DayTotals> Today { get; init; } = []; // each character's day so far (Today.lua)
    public List<DayTotals> Days { get; init; } = [];  // finished days, the last 8 per character (Today.lua days), for the Stats page

    public static TrackerData Empty { get; } = new([], []);
}
