namespace WoWCharacterTracker.Models;

// The three "how much gets shared" choices on the Sharing page. Normal is the game's defaults, what a
// new user starts with. A preset sets what gets posted,
// screenshots, what the addon tells chat and asks about. It never touches webhooks, the on/off switch
// for each place, followed items, names or anything else personal.
static class SharingPresets
{
    public const string Quiet = "quiet", Normal = "normal", Everything = "everything", Custom = "custom";
    public static readonly string[] Keys = [Quiet, Normal, Everything];

    public static void Apply(FlavorSettings s, Flavor flavor, string preset)
    {
        var d = flavor.Defaults();
        var rows = Moments.For(flavor);
        var on = preset switch
        {
            Quiet => rows.Where(m => m.Key is "level" or "maxlevel" or "epic" or "followed"),
            Normal => rows.Where(m => m.GetPost(d)),
            _ => rows.Where(m => m.Key != "run"), // runs trade the loot posts for one, a style, not more
        };
        var keys = on.Select(m => m.Key).ToHashSet();
        foreach (var m in Moments.All) m.SetPost(s, keys.Contains(m.Key));

        (s.LevelSplit, s.LevelEveryBefore, s.LevelEveryAfter) = preset switch
        {
            Quiet => (d.LevelSplit, 10, 10),
            Everything => (d.LevelSplit, 1, 1),
            _ => (d.LevelSplit, d.LevelEveryBefore, d.LevelEveryAfter),
        };
        s.FirstBossKillOnly = preset != Everything;
        s.RareBoeOnly = true;

        var shots = preset switch
        {
            Quiet => [],
            Normal => EventKinds.ShotKinds.Where(d.Shoots).ToHashSet(),
            _ => rows.Select(m => m.Shot).OfType<string>().ToHashSet(),
        };
        s.Screenshot = EventKinds.ShotKinds.ToDictionary(k => k, shots.Contains);
        s.PostScreenshot = EventKinds.ShotKinds.ToDictionary(k => k, shots.Contains);

        var announce = rows.Where(m => m.Announce is { } a && preset switch
        {
            Quiet => m.Key is "level" or "maxlevel" or "epic",
            Normal => d.AnnounceKinds.Contains(a),
            _ => true,
        }).Select(m => m.Announce!).ToList();
        (s.AnnounceKinds, s.AnnouncePartyKinds) = (announce, [.. announce]);

        s.AskKinds = preset == Everything ? [] : [.. d.AskKinds ?? []];
    }

    // A preset picked in game (the addon window's Sharing tab, an In game setting): applied when it's
    // one of the three and the settings don't match it yet. True when something changed.
    public static bool TakeFromGame(FlavorSettings s, Flavor flavor, string? preset)
    {
        if (preset is null || !Keys.Contains(preset) || Detect(s, flavor) == preset) return false;
        Apply(s, flavor, preset);
        return true;
    }

    // The preset these settings match, or Custom.
    public static string Detect(FlavorSettings s, Flavor flavor)
    {
        foreach (var key in Keys)
        {
            var probe = new FlavorSettings();
            Apply(probe, flavor, key);
            if (Same(s, probe, flavor)) return key;
        }
        return Custom;
    }

    // Only what the game offers counts: the default lists carry kinds the other game has.
    static bool Same(FlavorSettings a, FlavorSettings b, Flavor flavor)
    {
        var rows = Moments.For(flavor);
        var shots = rows.Select(m => m.Shot).OfType<string>().ToHashSet();
        var announce = rows.Select(m => m.Announce).OfType<string>().ToHashSet();
        var ask = rows.Select(m => m.Ask).OfType<string>().ToHashSet();
        bool SameKinds(IEnumerable<string> x, IEnumerable<string> y, HashSet<string> offered) =>
            x.Where(offered.Contains).Order().SequenceEqual(y.Where(offered.Contains).Order());
        return rows.All(m => m.GetPost(a) == m.GetPost(b))
            && (a.LevelSplit, a.LevelEveryBefore, a.LevelEveryAfter, a.FirstBossKillOnly, a.RareBoeOnly)
                == (b.LevelSplit, b.LevelEveryBefore, b.LevelEveryAfter, b.FirstBossKillOnly, b.RareBoeOnly)
            && shots.All(k => a.Shoots(k) == b.Shoots(k) && a.PostsShot(k) == b.PostsShot(k))
            && SameKinds(a.AnnounceKinds, b.AnnounceKinds, announce)
            && SameKinds(a.AnnouncePartyKinds ?? a.AnnounceKinds, b.AnnounceKinds, announce)
            && SameKinds(a.AskKinds ?? [], b.AskKinds ?? [], ask);
    }
}
