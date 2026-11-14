# UX cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Phase 1 of the UX cleanup: one Sharing page (presets, one moment table, Discord and Stream cards) replacing What gets posted and In game, plus a separate test build JDybeck can try with real data without touching the running app.

**Architecture:** A new catalog `Models/Moments` lists every row of the Sharing table with accessors into the existing `FlavorSettings` fields, so no saved setting moves or gets renamed. `Models/SharingPresets` edits a `FlavorSettings` the way `CopyPostingFrom` does. `ViewModels/SharingRow` is one table row; `Views/SharingPage` hosts the rows, the presets, the existing `DiscordPanel`/`StreamPanel`, and the advanced options moved from the old pages.

**Tech Stack:** .NET 10 WPF, xUnit, MoonSharp (addon tests, untouched in phase 1).

**Spec:** `docs/superpowers/specs/2026-11-08-ux-cleanup-design.md`

## Global Constraints

- Warnings are errors; `.editorconfig` style is checked in the build. File-scoped namespaces.
- No saved setting renamed or moved: configs and Settings.lua from 0.38.1 load unchanged.
- One of each: `Files.WriteAtomic` for app files, `Infrastructure/Web` only HttpClient, themed brushes via `DynamicResource`, boxes `CardBorder`/`PanelBorder`, no effects on repeated rows.
- Never tell Retail apart by `flavor == Flavors.X`: use `Flavor.HasRatings` / `EventKinds.OfferedIn`.
- Comments brief, no em dashes in any text, casual direct UI copy.
- Testing the app: `CHARACTERTRACKER_DATA_DIR` set and both game paths pointed at fake flavor folders. Never stop processes by name.
- Do not replace or stop JDybeck's running `dist\WoWCharacterTracker.exe`. The test build goes to `test-build\` (gitignored).
- Commits: Conventional Commits, author `35696089+JDybeck@users.noreply.github.com`, Co-Authored-By and Claude-Session trailers. No push.

## Review Focus

- A config where the settings match no preset: the page must show Custom and change nothing on load.
- Picking a preset on a Retail tab must not turn on Forever-only kinds (boss, death, makgora) or the other way round.
- `DiscordPaused` on: nothing posts for that game, including followed items and legendaries, and Test still sends (it's a test).
- A preset must never touch webhooks, `AnnounceGuild`/`AnnounceParty`, `FollowedItems`, `NameStyle`, `Ignored`, `Path`.
- The table's guild/group cells must stay in sync with the addon (changes bump `InGameAt`, like the old In game page).

---

## File Structure

- Create `companion/WoWCharacterTracker/Models/Moments.cs`: the catalog of table rows (`Moment` record, `Moments.All`, `Moments.For(Flavor)`).
- Create `companion/WoWCharacterTracker/Models/SharingPresets.cs`: `Apply`, `Detect`, preset keys.
- Modify `companion/WoWCharacterTracker/Models/FlavorSettings.cs`: `DiscordPaused`, checked in `Wants`.
- Create `companion/WoWCharacterTracker/ViewModels/SharingRow.cs`: one table row.
- Modify `companion/WoWCharacterTracker/ViewModels/FlavorViewModel.cs`: `SharingRows`, `Preset*`, `DiscordPaused`; announce/ask rows built from `Moments`; `AnnounceNames`, `AskNames`, `AnnounceRow` use removed.
- Delete `companion/WoWCharacterTracker/ViewModels/AnnounceRow.cs`.
- Create `companion/WoWCharacterTracker/Views/SharingPage.xaml(.cs)`.
- Delete `Views/PostingPage.xaml(.cs)`, `Views/InGamePage.xaml(.cs)`.
- Modify `MainWindow.xaml`: tabs, right column (Discord and Stream panels move into Sharing).
- Modify `ViewModels/IntroViewModel.cs`, `Ui/ScreenshotMode.cs`, `MainWindow.xaml.cs` (tour targets, page keys).
- Create `companion/WoWCharacterTracker.Tests/SharingTests.cs`.
- Create `tools/test-build.ps1`; modify `.gitignore`.

---

### Task 1: DiscordPaused

**Files:**
- Modify: `companion/WoWCharacterTracker/Models/FlavorSettings.cs` (field next to `ActiveWebhookIds`; `Wants(Entry, IEnumerable<Entry>)`)
- Test: `companion/WoWCharacterTracker.Tests/SharingTests.cs`

**Interfaces:**
- Produces: `bool FlavorSettings.DiscordPaused { get; set; }` (saved, default false).

- [ ] **Step 1: Write the failing test**

```csharp
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
```

(If `Entry` has required members, build it the way `SampleEvents` does: copy the base `e` it starts from.)

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test companion/WoWCharacterTracker.Tests --filter DiscordPausedTests`
Expected: build error, `DiscordPaused` not defined.

- [ ] **Step 3: Implement**

In `FlavorSettings`, after `ActiveWebhookId`:

```csharp
    // The rail's Discord switch: nothing for this game posts while paused, and what happens meanwhile
    // isn't posted later. Webhooks stay as they are.
    public bool DiscordPaused { get; set; }
```

and change the history-aware `Wants`:

```csharp
    public bool Wants(Entry e, IEnumerable<Entry> history) =>
        !DiscordPaused && !Ignored.Contains(e.Guid) && Wants(e) && !(e.Kind == "boss" && IsRepeatKill(e, history));
```

Check `Tracker` calls the two-argument `Wants` for real posts and that Test posts don't go through it (`SendKindTestAsync`); if a real post path calls the one-argument `Wants`, add `!DiscordPaused` there instead, so Test still sends.

- [ ] **Step 4: Run tests, expect PASS**

Run: `dotnet test companion/WoWCharacterTracker.Tests --filter DiscordPausedTests`

- [ ] **Step 5: Commit** `feat(companion): add a pause switch for discord posts per game`

---

### Task 2: Moments catalog

**Files:**
- Create: `companion/WoWCharacterTracker/Models/Moments.cs`
- Test: `companion/WoWCharacterTracker.Tests/SharingTests.cs`

**Interfaces:**
- Produces:

```csharp
sealed record Moment(string Key, string Name, string Tip,
    Func<FlavorSettings, bool> GetPost, Action<FlavorSettings, bool> SetPost,
    string? Shot = null, string? Announce = null, string? AnnounceSample = null,
    string? Ask = null, string? Alert = null, string Test = "")
static class Moments
{
    public static readonly IReadOnlyList<Moment> All;
    public static IReadOnlyList<Moment> For(Flavor flavor);   // EventKinds.OfferedIn(Key, flavor.HasRatings)
    public static string? Rule(Moment m, FlavorSettings s);  // the one-line rule under the name, or null
}
```

`Key` is the `EventKinds`/announce key (`level`, `maxlevel`, `loot` is split into `epic` and `rare`, `followed`, `mount`, `boss`, `keyrun`, `death`, `duel`, `makgora`, `bg`, `profession`, `mplus`, `pvp`). `OfferedIn` already returns true for keys it doesn't know (`maxlevel`, `epic`, `rare`, `followed`).

- [ ] **Step 1: Write the failing tests**

```csharp
public class MomentsTests
{
    [Fact]
    public void Every_announce_and_ask_kind_has_a_row()
    {
        var announce = Moments.All.Select(m => m.Announce).OfType<string>().Order();
        Assert.Equal(FlavorSettings.DefaultAnnounceKinds.Concat(["followed", "duel", "makgora", "bg", "keyrun", "death"]).Distinct().Order(), announce);
        Assert.Equal(FlavorSettings.AllAskKinds.Order(), Moments.All.Select(m => m.Ask).OfType<string>().Order());
    }

    [Fact]
    public void Every_shot_kind_is_in_the_table() =>
        Assert.Equal(EventKinds.ShotKinds.Order(), Moments.All.Select(m => m.Shot).OfType<string>().Distinct().Order());

    [Fact]
    public void Games_get_their_own_rows()
    {
        var forever = Moments.For(Flavors.Forever).Select(m => m.Key).ToList();
        var retail = Moments.For(Flavors.Retail).Select(m => m.Key).ToList();
        Assert.Contains("boss", forever); Assert.Contains("death", forever); Assert.DoesNotContain("keyrun", forever);
        Assert.Contains("keyrun", retail); Assert.Contains("mplus", retail); Assert.DoesNotContain("makgora", retail);
    }

    [Fact]
    public void Post_column_reads_and_writes_the_settings_field()
    {
        var s = new FlavorSettings();
        var epic = Moments.All.Single(m => m.Key == "epic");
        epic.SetPost(s, true);
        Assert.True(s.PostEpic);
        Assert.True(epic.GetPost(s));
    }

    [Fact]
    public void Every_row_can_be_tested() =>
        Assert.All(Moments.All, m => Assert.False(string.IsNullOrEmpty(m.Test)));
}
```

- [ ] **Step 2: Run, expect build failure** (`Moments` not defined).

- [ ] **Step 3: Implement `Models/Moments.cs`**

```csharp
namespace WoWCharacterTracker.Models;

// The Sharing table: one row per moment, a column per place it can go. Each row reads and writes the
// FlavorSettings fields and kind lists that already exist, so configs and Settings.lua don't change.
// Shot: the screenshot kind (loot's is shared by epic and rare, shown on epic). Announce/Ask: the addon's
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
            Shot: "level", Announce: "level", AnnounceSample: "\"Ding! Level 23. 22 to 23 took 41m 12s.\"", Alert: "level", Test: "level"),
        new("maxlevel", "Max level", "Posted with extra flair, even when the level rule skips that level", s => s.PostMaxLevel, (s, on) => s.PostMaxLevel = on,
            Announce: "maxlevel", AnnounceSample: "\"Reached max level 60! Total played 6d 4h 12m.\"", Alert: "maxlevel", Test: "max"),
        new("epic", "Epic loot", "Purple items. Legendary gear is always posted.", s => s.PostEpic, (s, on) => s.PostEpic = on,
            Shot: "loot", Announce: "loot", AnnounceSample: "\"Looted [Item]!\" For epic and legendary items.", Ask: "epic", Alert: "epic", Test: "epic"),
        new("rare", "Rare loot", "Blue items", s => s.PostRare, (s, on) => s.PostRare = on, Ask: "rare", Alert: "rare", Test: "rare"),
        new("followed", "Followed items", "Anything on your Followed items list, whatever its quality", s => s.PostFollowed, (s, on) => s.PostFollowed = on,
            Announce: "followed", AnnounceSample: "\"Got [Item] from my followed items!\"", Alert: "followed", Test: "followed"),
        new("mount", "New mounts", "Bought or learned", s => s.PostMounts, (s, on) => s.PostMounts = on,
            Shot: "mount", Announce: "mount", AnnounceSample: "\"Got a new mount: Swift Palomino!\"", Alert: "mount", Test: "mount"),
        new("boss", "End boss kills", "The last boss of a dungeon or raid, with the group", s => s.PostBosses, (s, on) => s.PostBosses = on,
            Shot: "boss", Announce: "boss", AnnounceSample: "\"Killed Edwin VanCleef in The Deadmines!\"", Ask: "boss", Alert: "boss", Test: "boss"),
        new("keyrun", "Mythic+ runs", "Finished keys from the level set under Advanced", s => s.PostKeyRuns, (s, on) => s.PostKeyRuns = on,
            Shot: "keyrun", Announce: "keyrun", AnnounceSample: "\"Timed +12 Ara-Kara, City of Echoes (+2)!\"", Alert: "keyrun", Test: "keyrun"),
        new("death", "Hardcore deaths", "What killed them, total played and the gold they had", s => s.PostDeaths, (s, on) => s.PostDeaths = on,
            Shot: "death", Announce: "death", AnnounceSample: "\"Fell at level 34 to Hogger. Rest in peace.\"", Alert: "death", Test: "death"),
        new("duel", "Duels won", "Who you beat, or who ran away", s => s.PostDuels, (s, on) => s.PostDuels = on,
            Shot: "duel", Announce: "duel", AnnounceSample: "\"Won a duel against Grimtusk.\"", Alert: "duel", Test: "duel"),
        new("makgora", "Mak'gora won", "The hardcore duel to the death", s => s.PostMakgora, (s, on) => s.PostMakgora = on,
            Shot: "makgora", Announce: "makgora", AnnounceSample: "\"Won a Mak'gora against Grimtusk!\"", Alert: "makgora", Test: "makgora"),
        new("bg", "Battleground wins", "Which battleground you won", s => s.PostBattlegrounds, (s, on) => s.PostBattlegrounds = on,
            Announce: "bg", AnnounceSample: "\"Won Warsong Gulch!\"", Alert: "bg", Test: "bg"),
        new("profession", "Profession milestones", "A main profession passing one of the steps", s => s.PostProfessions, (s, on) => s.PostProfessions = on,
            Announce: "profession", AnnounceSample: "\"Reached 150 Mining!\"", Alert: "profession", Test: "profession"),
        new("mplus", "Mythic+ rating", "Passing a rating step", s => s.PostMPlus, (s, on) => s.PostMPlus = on,
            Announce: "mplus", AnnounceSample: "\"Reached 2500 Mythic+ rating!\"", Alert: "mplus", Test: "mplus"),
        new("pvp", "PvP rating", "Passing a rating step", s => s.PostPvp, (s, on) => s.PostPvp = on,
            Announce: "pvp", AnnounceSample: "\"Reached 1800 rating in 3v3!\"", Alert: "pvp", Test: "pvp"),
    ];

    public static IReadOnlyList<Moment> For(Flavor flavor) => [.. All.Where(m => EventKinds.OfferedIn(m.Key, flavor.HasRatings))];

    // The rule under a row's name, for the rows that have one.
    public static string? Rule(Moment m, FlavorSettings s) => m.Key switch
    {
        "level" => s.LevelEveryBefore == s.LevelEveryAfter
            ? $"every {s.LevelEveryBefore}"
            : $"every {s.LevelEveryBefore} to {s.LevelSplit}, then every {s.LevelEveryAfter}",
        "rare" => s.RareBoeOnly ? "Bind on Equip only" : null,
        "boss" => s.FirstBossKillOnly ? "first kill only" : null,
        "keyrun" => $"+{s.KeyRunMinLevel} and up" + (s.KeyRunTimedOnly ? ", in time" : ""),
        "profession" => string.Join(", ", s.ProfessionSteps),
        _ => null,
    };
}
```

Note: `EventKinds.OfferedIn` knows `boss`, `keyrun`, `death`, `makgora`, `mplus`, `pvp` and treats unknown keys (`epic`, `rare`, `maxlevel`, `followed`) as offered everywhere, which is right.

- [ ] **Step 4: Run, expect PASS.** Adjust the first test's expected set only if `DefaultAnnounceKinds` changed; the announce set must equal the old `AnnounceNames` kinds: level, maxlevel, boss, keyrun, loot, followed, mount, duel, makgora, bg, profession, mplus, pvp, death.

- [ ] **Step 5: Commit** `feat(companion): add one catalog of shareable moments`

---

### Task 3: SharingPresets

**Files:**
- Create: `companion/WoWCharacterTracker/Models/SharingPresets.cs`
- Test: `companion/WoWCharacterTracker.Tests/SharingTests.cs`

**Interfaces:**
- Consumes: `Moments.For(Flavor)`, `Flavor.Defaults()`.
- Produces:

```csharp
static class SharingPresets
{
    public const string Quiet = "quiet", Normal = "normal", Everything = "everything", Custom = "custom";
    public static readonly string[] Keys = [Quiet, Normal, Everything];
    public static void Apply(FlavorSettings s, Flavor flavor, string preset);
    public static string Detect(FlavorSettings s, Flavor flavor); // a key from Keys, or Custom
}
```

What a preset sets (and nothing else): every `Moment.SetPost`; `LevelSplit/LevelEveryBefore/LevelEveryAfter`; `PostMaxLevel`; `FirstBossKillOnly`; `RareBoeOnly`; `Screenshot`/`PostScreenshot`; `AnnounceKinds` and `AnnouncePartyKinds` (the same list); `AskKinds`. Never: webhooks, `DiscordPaused`, `AnnounceGuild/Party`, `FollowedItems`, `ReadyMadeLists`, `NameStyle`, `Ignored`, `Path`, steps lists, `KeyRunMinLevel`, `SkipBossGroupRegex`, stream options.

Definitions (only rows `Moments.For(flavor)` offers are touched):
- Quiet: on = maxlevel, epic, followed. Levels on, every 10 before and after the split (split from defaults). Screenshots: none. Announce: level, maxlevel, loot. Ask: boss, rare (only the offered ones: on Retail `AskKinds` = []... see below).
- Normal: the game's `Flavor.Defaults()` posting (`CopyPostingFrom(flavor.Defaults())`) then: followed on, profession on, makgora on; Forever: mount, boss, death on with first kill only. Screenshots: level, boss, death (Forever) or level, keyrun (Retail). Announce: `DefaultAnnounceKinds` plus followed. Ask: the game's default `AskKinds`.
- Everything: every offered row on, every level (`LevelEveryBefore = LevelEveryAfter = 1`), `RareBoeOnly = true`, screenshots on (and posted) for every offered `Shot` kind, announce every offered kind, `AskKinds = []`.
- Retail's ask kinds stay `[]` in every preset (its defaults say "blues and bosses come by the dozen").

`Detect` applies each preset to a clone and compares the fields listed above; equal = that key.

- [ ] **Step 1: Write the failing tests**

```csharp
public class SharingPresetsTests
{
    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (var f in Flavors.All) foreach (var p in SharingPresets.Keys) data.Add(f.Key, p);
        return data;
    }

    static Flavor F(string key) => Flavors.All.Single(f => f.Key == key);

    [Theory, MemberData(nameof(Cases))]
    public void Applied_preset_is_detected(string flavor, string preset)
    {
        var s = F(flavor).Defaults();
        SharingPresets.Apply(s, F(flavor), preset);
        Assert.Equal(preset, SharingPresets.Detect(s, F(flavor)));
    }

    [Theory, MemberData(nameof(Cases))]
    public void Preset_leaves_personal_settings_alone(string flavor, string preset)
    {
        var s = F(flavor).Defaults();
        s.ActiveWebhookIds = ["a"]; s.AnnounceGuild = true; s.DiscordPaused = true; s.NameStyle = "first";
        s.FollowedItems = [new FollowedItem { Id = 19019 }]; s.Ignored = ["g"]; s.Path = "x";
        SharingPresets.Apply(s, F(flavor), preset);
        Assert.Equal(["a"], s.ActiveWebhookIds); Assert.True(s.AnnounceGuild); Assert.True(s.DiscordPaused);
        Assert.Equal("first", s.NameStyle); Assert.Single(s.FollowedItems); Assert.Contains("g", s.Ignored); Assert.Equal("x", s.Path);
    }

    [Fact]
    public void Retail_presets_never_turn_on_forever_kinds()
    {
        foreach (var p in SharingPresets.Keys)
        {
            var s = Flavors.Retail.Defaults();
            SharingPresets.Apply(s, Flavors.Retail, p);
            Assert.False(s.PostBosses || s.PostDeaths || s.PostMakgora);
            Assert.Empty(s.AskKinds!);
        }
    }

    [Fact]
    public void A_mix_is_custom()
    {
        var s = Flavors.Forever.Defaults();
        SharingPresets.Apply(s, Flavors.Forever, SharingPresets.Quiet);
        s.PostDuels = true;
        Assert.Equal(SharingPresets.Custom, SharingPresets.Detect(s, Flavors.Forever));
    }

    [Fact]
    public void Everything_posts_every_level() 
    {
        var s = Flavors.Forever.Defaults();
        SharingPresets.Apply(s, Flavors.Forever, SharingPresets.Everything);
        Assert.All(Enumerable.Range(2, 59), l => Assert.True(s.LevelWanted(l)));
    }
}
```

(Use `FollowedItem`'s real constructor; check `Models/FollowedItem*.cs`.)

- [ ] **Step 2: Run, expect build failure.**

- [ ] **Step 3: Implement** with these shapes:

```csharp
namespace WoWCharacterTracker.Models;

// The three "how much gets shared" choices on the Sharing page (and the setup helper). A preset sets what
// gets posted, screenshots, what the addon tells chat and asks about. It never touches webhooks, the
// on/off switches for each place, followed items, names or anything personal.
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
            Quiet => ["level", "maxlevel", "epic", "followed"],
            Normal => NormalKeys(flavor, d, rows),
            _ => rows.Select(m => m.Key).ToHashSet(),
        };
        foreach (var m in Moments.All) m.SetPost(s, on.Contains(m.Key) && rows.Contains(m));
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
            Normal => rows.Where(m => m.Key is "level" or "boss" or "death" or "keyrun").Select(m => m.Shot!).ToList(),
            _ => rows.Select(m => m.Shot).OfType<string>().Distinct().ToList(),
        };
        s.Screenshot = EventKinds.ShotKinds.ToDictionary(k => k, shots.Contains);
        s.PostScreenshot = EventKinds.ShotKinds.ToDictionary(k => k, shots.Contains);
        var announce = rows.Where(m => m.Announce is not null && preset switch
        {
            Quiet => m.Key is "level" or "maxlevel" or "epic",
            Normal => FlavorSettings.DefaultAnnounceKinds.Contains(m.Announce) || m.Key == "followed",
            _ => true,
        }).Select(m => m.Announce!).ToList();
        (s.AnnounceKinds, s.AnnouncePartyKinds) = (announce, [.. announce]);
        s.AskKinds = preset == Everything ? [] : [.. d.AskKinds ?? []];
    }

    static HashSet<string> NormalKeys(Flavor flavor, FlavorSettings d, IReadOnlyList<Moment> rows) =>
        [.. rows.Where(m => m.GetPost(d) || m.Key is "followed" or "profession" or "makgora").Select(m => m.Key)];

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

    static bool Same(FlavorSettings a, FlavorSettings b, Flavor flavor) =>
        Moments.For(flavor).All(m => m.GetPost(a) == m.GetPost(b))
        && (a.LevelSplit, a.LevelEveryBefore, a.LevelEveryAfter, a.FirstBossKillOnly, a.RareBoeOnly)
            == (b.LevelSplit, b.LevelEveryBefore, b.LevelEveryAfter, b.FirstBossKillOnly, b.RareBoeOnly)
        && EventKinds.ShotKinds.All(k => a.Shoots(k) == b.Shoots(k) && a.PostsShot(k) == b.PostsShot(k))
        && a.AnnounceKinds.Order().SequenceEqual(b.AnnounceKinds.Order())
        && (a.AnnouncePartyKinds ?? a.AnnounceKinds).Order().SequenceEqual(b.AnnounceKinds.Order())
        && (a.AskKinds ?? []).Order().SequenceEqual(b.AskKinds ?? []);
}
```

Note on Normal for Forever: `Flavor.Defaults()` has rare on; Normal keeps whatever the defaults say for rows the defaults set, plus followed, profession, makgora. That's fine and keeps "Normal = what new users get today" (plus a little). The Review Focus test `Retail_presets_never_turn_on_forever_kinds` holds because `rows` is `Moments.For(Retail)`.

- [ ] **Step 4: Run, expect PASS.** If `Applied_preset_is_detected` fails for two presets that come out equal on one game, make the definitions differ there (they shouldn't: Quiet has 10/10 levels, Everything 1/1).

- [ ] **Step 5: Commit** `feat(companion): add quiet, normal and everything sharing presets`

---

### Task 4: SharingRow and the view model

**Files:**
- Create: `companion/WoWCharacterTracker/ViewModels/SharingRow.cs`
- Modify: `companion/WoWCharacterTracker/ViewModels/FlavorViewModel.cs` (lines ~190-330: posting props, `AnnounceNames`, `AnnounceTable`, `AskNames`, `AskKinds`, `TakeInGameSettings`, `PostingReplaced`)
- Delete: `companion/WoWCharacterTracker/ViewModels/AnnounceRow.cs`
- Test: `companion/WoWCharacterTracker.Tests/SharingTests.cs`

**Interfaces:**
- Consumes: `Moment`, `Moments.For`, `Moments.Rule`, `SharingPresets`.
- Produces:

```csharp
sealed class SharingRow : Observable
{
    public SharingRow(Moment moment, FlavorSettings settings, Func<List<string>> streamAlerts, Action posted, Action inGame, Action stream);
    public Moment Moment { get; }
    public string Name { get; }  public string Tip { get; }  public string? Rule { get; }
    public bool Post { get; set; }
    public bool HasShot { get; }  public bool Shot { get; set; }
    public bool HasAnnounce { get; }  public bool Guild { get; set; }  public bool Group { get; set; }
    public bool HasAsk { get; }  public bool Ask { get; set; }
    public bool HasAlert { get; }  public bool Alert { get; set; }
    public void Refresh();
}
// FlavorViewModel
public IReadOnlyList<SharingRow> SharingRows { get; }
public string Preset { get; }                 // a SharingPresets key or Custom
public bool PresetQuiet { get; set; }  public bool PresetNormal { get; set; }  public bool PresetEverything { get; set; }
public bool IsCustom { get; }
public bool DiscordPaused { get; set; }
```

`Post` set: `moment.SetPost(settings, v); posted()` (posted = `Changed` path: `AddonSettingsChanged`). `Shot` set: `Screenshot[k] = v; PostScreenshot[k] = v; posted()` (one column: shoot and attach together; the separate "post screenshot" choice goes under Advanced as "Attach screenshots to posts" for all kinds, see Task 5). `Guild`/`Group` set: add/remove `Announce` in `AnnounceKinds` / `AnnouncePartyKinds ??= [..AnnounceKinds]`, then `inGame()` (bumps `InGameAt`). `Ask`: same on `AskKinds ??= []`, `inGame()`. `Alert`: same on `streamAlerts()`, then `stream()` (saves config, refreshes overlay via `StreamViewModel`'s existing `OptionsChanged` path; expose a public `AlertsChanged()` on `StreamViewModel` that calls it).

Preset setters: `PresetX = true` -> if `IsCustom`, ask with the app's existing confirm pattern (look for how `ConfirmRemove` works on webhooks: an inline "Replace your custom choices? Yes / No" row bound to `ConfirmPreset` string), else `SharingPresets.Apply(Settings, Flavor, key)` then the existing `PostingReplaced()` (which refreshes everything) plus `InGameChanged()` since chat/ask lists changed.

- [ ] **Step 1: Write the failing test** (logic only, no WPF):

```csharp
public class SharingRowTests
{
    [Fact]
    public void Guild_cell_edits_the_announce_list_and_marks_in_game_changed()
    {
        var s = Flavors.Forever.Defaults();
        s.AnnounceKinds = [];
        var inGame = 0;
        var row = new SharingRow(Moments.All.Single(m => m.Key == "mount"), s, () => [], () => { }, () => inGame++, () => { });
        row.Guild = true;
        Assert.Contains("mount", s.AnnounceKinds);
        Assert.Equal(1, inGame);
        Assert.False(row.Group);
    }

    [Fact]
    public void Shot_cell_shoots_and_attaches()
    {
        var s = new FlavorSettings();
        var row = new SharingRow(Moments.All.Single(m => m.Key == "boss"), s, () => [], () => { }, () => { }, () => { });
        row.Shot = true;
        Assert.True(s.PostsShot("boss"));
    }

    [Fact]
    public void Rows_without_a_column_say_so()
    {
        var rare = new SharingRow(Moments.All.Single(m => m.Key == "rare"), new FlavorSettings(), () => [], () => { }, () => { }, () => { });
        Assert.False(rare.HasShot); Assert.False(rare.HasAnnounce); Assert.True(rare.HasAsk);
    }
}
```

- [ ] **Step 2: Run, expect build failure.**
- [ ] **Step 3: Implement `SharingRow`** per the interface above (each setter: `Set`-style raise of its own property, then the callback). Implement the `FlavorViewModel` additions; build `SharingRows` lazily like `announceKinds` (`sharingRows ??= [.. Moments.For(Flavor).Select(...)]`) and null it where `announceKinds`/`askKinds` are nulled today (`TakeInGameSettings`, `PostingReplaced`). Remove `AnnounceNames`, `AnnounceTable`, `AnnounceKinds`, `AskNames`, `AskKinds` and delete `AnnounceRow.cs` once Task 5 no longer binds them (do the deletion in Task 5's last step so the build stays green).
- [ ] **Step 4: Run all tests, expect PASS.** `dotnet test companion/WoWCharacterTracker.Tests`
- [ ] **Step 5: Commit** `feat(companion): add sharing table rows and presets to the game tab`

---

### Task 5: Sharing page

**Files:**
- Create: `companion/WoWCharacterTracker/Views/SharingPage.xaml`, `SharingPage.xaml.cs`
- Delete: `Views/PostingPage.xaml(.cs)`, `Views/InGamePage.xaml(.cs)`, `ViewModels/AnnounceRow.cs`
- Modify: `MainWindow.xaml` (tabs 65-72, pages 114-115, right column 133-134), `ViewModels/IntroViewModel.cs` (steps "posting" -> "sharing", drop the "In game" sentence, Discord/Stream steps point at "sharing"), `MainWindow.xaml.cs` (`TourTarget` names), `Ui/ScreenshotMode.cs` (accept `sharing`, keep `posting`/`ingame` as aliases to `sharing`), `ViewModels/FlavorViewModel.cs` (`ResetPage` if it lists pages), anywhere `"posting"`/`"ingame"` page keys are used (grep).

**Layout of `SharingPage` (DataContext `SelectedFlavor`, Visibility `PageIs sharing`, `CardBorder`, `ScrollViewer`, `ui:Motion.SlideIndex="3"`):**

1. Title "Sharing for {Name}" and one hint line: "Who hears about your moments: Discord, your guild or group, and your stream."
2. "Where" row, three `PanelBorder` cards in a 3-column `UniformGrid`:
   - Discord: title, `ToggleSwitch`-style `CheckBox` bound to an inverted `DiscordPaused` (add `DiscordOn` to FlavorViewModel: `get => !DiscordPaused`), then the existing `views:DiscordPanel` with `DataContext="{Binding DataContext.Discord, RelativeSource={RelativeSource AncestorType=Window}}"` below the cards row, spanning the width (the panel has its own header; hide that header's duplicate title only if it reads twice).
   - Guild and group chat: two switches `AnnounceGuild`, `AnnounceParty`, hint "A line as you in chat, out of combat. Changes reach the game on /reload."
   - Stream overlay: the existing `views:StreamPanel` with `DataContext` = window's `Stream` (it already has On, address and its options expander, which is the "Layout").
3. "How much gets shared": three `RadioButton`s styled as cards (`PresetQuiet`, `PresetNormal`, `PresetEverything`), each a title and one line (copy from the spec). A fourth, non-clickable "Custom" label shows when `IsCustom`. The confirm row for replacing custom choices under it.
4. "Customize" `Expander` (`IsExpanded` bound one-way to `IsCustom` on first show), containing a header `Grid` (MOMENT, DISCORD, SCREENSHOT, GUILD, GROUP, ASK FIRST, STREAM, and a Test column) and an `ItemsControl` over `SharingRows`. Row template: a `Grid` with the same `SharedSizeGroup` columns; name + tip tooltip + `Rule` hint; `CheckBox` per column with `Visibility` from `Has*` (a "–" `TextBlock` when not); a `ChipButton` "Test" bound to the window's `TestKindCommand` with `CommandParameter="{Binding Moment.Test}"`, `Opacity` 0 and set to 1 by a `DataTrigger` on the row's `IsMouseOver` (a `Style` on the row `Border`, background `Hover` brush on mouse over). No effects.
5. "Advanced" `Expander`, collapsed: move these blocks over from `PostingPage.xaml` unchanged (same bindings): the level rule form and its preview, profession steps, rating steps and key run options (Retail), first kill only, rare BoE only (`RareBoeOnly`), skip-boss regex and its problem text, name in posts, hide chat / hide UI in screenshots, copy/paste/reset buttons. From `InGamePage.xaml`: the ask box time choices, "Keeping the app up to date" (flight reload, chat reminder), keep all history, data grid corner and label. Group them under small `SectionTitle`s: "Posting", "Screenshots", "In game".

- [ ] **Step 1:** Grep the page keys: `rg -n "\"posting\"|\"ingame\"|ConverterParameter=posting|ConverterParameter=ingame" companion/WoWCharacterTracker` and list every hit to change.
- [ ] **Step 2:** Create `SharingPage.xaml` + code-behind (`public partial class SharingPage : UserControl { public SharingPage() => InitializeComponent(); }`, matching the other pages' code-behind).
- [ ] **Step 3:** Replace the two tabs in `MainWindow.xaml` with one:

```xml
          <Button x:Name="SharingTab" Style="{StaticResource PageTab}" Tag="{Binding Page, Converter={StaticResource ActiveIf}, ConverterParameter=sharing}"
                  Command="{Binding ShowPageCommand}" CommandParameter="sharing">
            <StackPanel Orientation="Horizontal"><TextBlock Style="{StaticResource Icon}" Text="&#xE724;" FontSize="13" Margin="0,0,8,0"/><TextBlock Text="Sharing"/></StackPanel>
          </Button>
```

  replace `<views:PostingPage .../>` and `<views:InGamePage .../>` with `<views:SharingPage Grid.Row="2"/>`, and remove `DiscordView`/`StreamView` from the right column (keep `SetupBar` and `ConsolePanel` until phase 2). Fix every `x:Name` reference in `MainWindow.xaml.cs` (`PostingTab`, `DiscordView`, `StreamView`) found by the build.
- [ ] **Step 4:** Update `IntroViewModel.Steps` targets and text, `ScreenshotMode` page aliases, and the `FitRightColumn` code if it measures the removed panels.
- [ ] **Step 5:** Delete `PostingPage`, `InGamePage`, `AnnounceRow` and the `FlavorViewModel` members only they used (`AnnounceKinds`, `AskKinds`, `Shots`, `ShotOption.cs` if nothing else binds it). Build: `dotnet build companion/WoWCharacterTracker -c Debug`. Expected: 0 warnings, 0 errors.
- [ ] **Step 6:** Run all tests: `dotnet test companion/WoWCharacterTracker.Tests`. Fix `StreamTests` or others that referenced removed members.
- [ ] **Step 7:** Screenshot mode, with the scratchpad fake folders: views `forever:sharing`, `retail:sharing`, `forever:sharing:testall`, and with `CHARACTERTRACKER_SCREENSHOT_SIZE=1320x2400` to see it all; also at `1100x680`. Look at each: no clipping, columns line up, Retail rows differ, Custom shows for a hand-edited config.
- [ ] **Step 8: Commit** `feat(companion): merge what gets posted and in game into one sharing page`

---

### Task 6: Test build for JDybeck

**Files:**
- Create: `tools/test-build.ps1`
- Modify: `.gitignore` (add `test-build/`)
- Modify: `.claude/notes/testing.md` (one line on the script)

The test app must not touch the running app, the real game folders, or post anything on its own:

- Published like `build.cmd` (single file) into `test-build\app\WoWCharacterTracker.exe`. Never `dist\`.
- Its own data dir `test-build\data` (`CHARACTERTRACKER_DATA_DIR`), which also gives it its own single-instance name.
- Its config is JDybeck's config copied once (webhooks, themes, settings), with each game's `Path` pointed at `test-build\game\<flavor folder>`.
- Those fake game folders get a copy of the real `WTF\Account\*\SavedVariables\WoWCharacterTracker.lua` (and `.lua.bak` not needed) and the installed addon folder, so the test app shows real characters. Nothing new arrives there, so nothing posts by itself (first run marks what's there as handled). Test buttons do post to the real webhook: that's how Sharing gets tried.
- A `test-build\Run test app.cmd` sets the env var and starts it.

- [ ] **Step 1:** Write `tools/test-build.ps1`:

```powershell
# Builds the app from this checkout into test-build\ and sets it up to run next to the real one:
# its own data folder and single-instance name, a copy of your settings, and copies of your saved
# game data in fake game folders, so it never writes to the real game or posts on its own.
param([switch]$FreshData)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'test-build'
$app = Join-Path $out 'app'
$data = Join-Path $out 'data'
$game = Join-Path $out 'game'

dotnet publish (Join-Path $root 'companion\WoWCharacterTracker') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $app -v q -nologo
if ($LASTEXITCODE) { throw 'publish failed' }

if ($FreshData -and (Test-Path $data)) { Remove-Item -Recurse -Force $data, $game }
if (-not (Test-Path (Join-Path $data 'config.json'))) {
    New-Item -ItemType Directory -Force $data | Out-Null
    $real = Join-Path $env:LOCALAPPDATA 'WoWCharacterTracker\config.json'
    $cfg = Get-Content $real -Raw | ConvertFrom-Json
    foreach ($p in $cfg.Flavors.PSObject.Properties) {
        $src = $p.Value.Path
        if (-not $src -or -not (Test-Path $src)) { continue }
        $leaf = Split-Path $src -Leaf
        $dst = Join-Path $game $leaf
        New-Item -ItemType Directory -Force (Join-Path $dst 'WTF'), (Join-Path $dst 'Interface\AddOns') | Out-Null
        Get-ChildItem (Join-Path $src 'WTF\Account') -Recurse -Filter 'WoWCharacterTracker.lua' -ErrorAction SilentlyContinue |
            Where-Object { $_.Directory.Name -eq 'SavedVariables' } | ForEach-Object {
                $rel = $_.FullName.Substring($src.Length + 1)
                $to = Join-Path $dst $rel
                New-Item -ItemType Directory -Force (Split-Path $to) | Out-Null
                Copy-Item $_.FullName $to
            }
        $addon = Join-Path $src 'Interface\AddOns\WoWCharacterTracker'
        if (Test-Path $addon) { Copy-Item -Recurse -Force $addon (Join-Path $dst 'Interface\AddOns\') }
        $build = Join-Path (Split-Path $src -Parent) '.build.info'
        if (Test-Path $build) { Copy-Item $build (Join-Path $game '.build.info') }
        $p.Value.Path = $dst
    }
    $cfg | ConvertTo-Json -Depth 50 | Set-Content (Join-Path $data 'config.json') -Encoding utf8
}

@"
@echo off
set CHARACTERTRACKER_DATA_DIR=%~dp0data
start "" "%~dp0app\WoWCharacterTracker.exe"
"@ | Set-Content (Join-Path $out 'Run test app.cmd') -Encoding ascii
Write-Host "Test app ready: $out\Run test app.cmd"
```

  Check the publish flags against `build.cmd` and use the same ones. Check `ConvertTo-Json` keeps the config readable by the app (it's `System.Text.Json` with default options: property names as written, so PascalCase stays). If the app re-detects real folders because a fake folder has no `WTF`/`Interface`, the script created both, so it won't.
- [ ] **Step 2:** Add `test-build/` to `.gitignore`.
- [ ] **Step 3:** Run `powershell -File tools/test-build.ps1`. Start it with the cmd file and the env var, take one screenshot-mode render of `forever:sharing` with `CHARACTERTRACKER_DATA_DIR=test-build\data` to confirm it shows JDybeck's real characters and webhooks. Confirm the real config file's last write time didn't change and no file under the real game folders changed.
- [ ] **Step 4:** Commit `build: add a script for a side-by-side test build` (script, .gitignore, testing note).

---

### Task 7: Verify phase 1

- [ ] `dotnet build companion/WoWCharacterTracker -c Debug` (0 warnings) and `dotnet test companion/WoWCharacterTracker.Tests` (all pass).
- [ ] Screenshot renders reviewed (Task 5 Step 7).
- [ ] Settings map check: every control removed from PostingPage/InGamePage has a home on SharingPage (go through the old XAML's bindings list one by one).
- [ ] CHANGELOG: an `## Unreleased` section line: "Sharing: What gets posted and In game are one page now, with Quiet / Normal / Everything presets and one table for Discord, screenshots, guild and group chat, asking and stream alerts."
- [ ] Commit.

---

## Release notes draft

CHANGELOG's top section must match the csproj version (a test checks), so the notes wait here until the release step bumps the version:

- Sharing: What gets posted and In game are one page now. Switch Discord, guild chat and group chat on or off at the top, pick Quiet, Normal or Everything, or open Customize for one table with a row per moment and a box for Discord, screenshot, guild, group, ask first and stream alerts. Test shows on the row under your mouse. The Discord webhooks and the stream overlay moved onto this page, and the rarely needed options are folded under Advanced.
- Discord can be paused per game without touching your webhooks. What happens while paused isn't posted later.
- (phase 3) Home: the character you're playing, today's moments and a feed of every character's newest moments with Share.
- (phase 4) Help me set it up replaces the tour: game folders, the addon, Discord and how much gets shared, one step at a time.
- (phase 5, addon) The /wct window has four tabs: Sharing (pick Quiet, Normal or Everything in game), Friends, Note and Settings. Reload reminders are one choice: Off, Gentle or Every chance. /wct help lists the four commands you need.
- (phase 2) Three tabs: Characters (with List, Items, Professions and Followed inside), Friends and Sharing. The right column is a slim rail of quick switches for Discord, guild chat, group chat and the stream overlay; the console slides out from it. Settings is its own page from the gear, with the addon's in game options. Search items from the header, and help, what's new and feedback live in the ? menu.

## Phase 2 (branch `feat/ux-shell`, on top of phase 1)

Decisions made while planning, against the spec:
- Home waits for phase 3: an empty Home tab now is worse than none. Phase 2 ships 3 tabs (Characters, Friends, Sharing).
- No "Synced N min ago" line: the status line already says what's watched, and a sync time needs new plumbing for little gain. Sync now becomes a small refresh icon (spins while syncing) instead of the big orange button.
- `SetupBar` stays as a notice strip across the top: update banner, /reload reminder, old-folder note and setup problems. Its chips row only shows while something needs doing. `Config.SetupPlacement` and its three-way choice go away (old files still load: unknown keys are ignored, a test checks).
- The credit line (made by ZebDryg, tip) moves to the bottom of the Settings page.

Tasks:
1. `ActiveIf` and `PageIs` accept `a|b|c` (one place: a shared `Matches` helper in `Ui/Converters.cs`), with a test. Characters tab active for `characters|items|professions|followed`; a view switch (List, Items, Professions, Followed) in the tab row while one of those is open. The Items, Professions and Followed items tabs go. Tour steps updated.
2. Header: What's new, Feedback, the tour button and the info tooltip move into one `?` menu (popup): Help me set it up (the tour for now), What's new, In-game commands (the old tooltip text, shown in the menu), Feedback, Check for updates. The update button stays in the header when there is one. Sync now becomes an icon button. A search box ("Search items on all your characters") that opens Items with the text in `ItemsViewModel`'s search.
3. Settings page (`Page = "settings"`, from the gear): App (open with WoW, hide on close, auto update, games tracked, animations, test characters, colors), In game for the selected game (the In game group moved from Sharing's Advanced), Uninstall, credit line. The settings popup goes.
4. Rail: the right column becomes a 56 px rail: Discord (`DiscordOn`), Guild chat, Group chat, Stream (`Stream.Enabled`) as toggle buttons with a status dot, then Console (opens `ConsolePanel` as a flyout over the page: 300 ms hover or click, leaves when the mouse leaves, pin keeps it, saved in `Config.ConsolePinned`). Console dot amber after a warning or error until opened. The page gets the full width.
5. Verify: build, tests, screenshot renders at 1320x900 and 1100x680, test build rebuilt with `-FreshData` off (keeps JDybeck's test settings).

## Phase 3 (branch `feat/ux-home`, on top of phase 2)

Decisions against the spec:
- "Posted" needs no new file: `Tracker` already keeps `id@webhook` keys for every post that went out. It exposes the ids (`Tracker.SentIds`, a snapshot refreshed after each sync and post). A Share from Home marks its row for the session.
- "This session" becomes **Today** (levels, boss kills, epics and posts since midnight, all characters of the tab): the session start needs the game's process start time, and today reads the same and needs nothing new.

Tasks:
1. `HighlightRow.From(Entry)` and `HighlightRow.LoadIconAsync(GameLookup, Flavor)` move out of `CharacterRow` (one builder, one icon loader); level-ups get a line ("Reached level 40", took 1h 12m). Tests on the text.
2. `Tracker.SentIds` (ids posted to at least one webhook), with a test on the key parsing.
3. `HomeViewModel` per game tab (like `ItemsViewModel`): the hero (newest character, `StreamSnapshot.Build` for its leveling numbers, playing when `Wow.IsRunning`), today's counts, the feed (`MomentRow`: the highlight, who, when, posted, big). Built in `FlavorViewModel.ShowData`, refreshed on `Tracker.Posted` and when a game starts or stops.
4. `HomePage.xaml`: two columns, hero card left with today under it, feed right with Share on unposted rows. Home is the first tab and the start page. Share posts through the existing `PostHighlightCommand`, now taking the character with it.
5. Tour, screenshot views (`forever:home:testchars`), test build, renders at both sizes.

## Phase 4 (branch `feat/ux-setup`, on top of phase 3)

The helper replaces the tour in place: `IntroViewModel` keeps its name, `Config.IntroDone`/`WantsIntro` keep their meaning, `Views/IntroTour` (the spotlight) is replaced by `Views/SetupHelper` (a card over the dimmed window).

1. Steps, each with Back, Skip (or Next) and its own action: **Games** (each game's `FolderStep` with its Browse/Change button and the Track switch), **Addon** (each tracked game's `AddonStep` with Install/Update), **Discord** (the whole `DiscordPanel`: add a webhook, Send test), **How much** (the preset cards and guild chat for the selected game), **Done** (where the quick switches and Home are). Step content shows by `Step.Key` through `PageIs` (no new converter).
2. `PresetCard` moves from `SharingPage` to `Templates.xaml` so both use one style.
3. Closing or finishing marks `IntroDone`; the ? menu's first line ("Help me set it up") opens it again. `OpenAt(key)` opens it at a step. The "Don't show again" box goes (closing is the answer).
4. `MainWindow` loses the tour's targets (`TourTarget`, `FindTarget`); tests on the step keys; screenshot view `forever:home:intro:N` keeps working.

## Phase 5 (branch `feat/ux-addon`, on top of phase 4)

1. `ui.choice` in Ui.lua: a row of buttons, one picked. Used by the preset and the reload reminders.
2. Window tabs: **Sharing** (preset with the app; guild/party; ask box and its time; without the app the whole chat table), **Friends** (unchanged lists, then "What you share with friends" in a frame placed after them), **Note**, **Settings** (instant posting, reload reminders, minimap, Advanced: corner, label, keep all history; this character; what the app set). Old tab keys (`general`, `app`, `chat`, `highlights`) map to the new ones.
3. Reload reminders: Off / Gentle / Every chance over `popup`, `flightReload`, `chatReminder`, no new key. Against the spec: **Gentle = the chat line and the flight box** (today's defaults), so nobody's reminders change; Every chance adds the ding popup.
4. `preset` In game key: the app writes `preset = SharingPresets.Detect(...)` into Settings.lua; the addon window edits it; `SavedVariables.ReadInGame` reads it; `SharingPresets.TakeFromGame` applies it after `TakeInGame`.
5. `/wct help` lists `/wct`, `/wct note`, `/wct friends`, `/wct test`; the rest still work. The Options page already was one line and a button.
6. GUIDE and README rewritten for the new layout.

## Release step (after all five PRs are merged)

- Bump the app (csproj) and the addon (.toc, since `addon/` changed), and move the release notes draft above into CHANGELOG.
- Retake the pictures (README picture process in testing.md): Home, Sharing, Settings, the setup helper, the addon window. Remove `what-gets-posted.png`, `in-game.png` and `settings.png`, which nothing links to anymore.
- JDybeck checks in game: the addon window's four tabs, the preset round trip (pick Quiet in game, /reload, the app shows Quiet), reload reminders.

## Later phases (planned in detail when phase 1 is merged)

- **Phase 2, shell:** 4 tabs (Home placeholder until phase 3), Characters with List/Items/Professions/Followed switch, header search + `?` menu + "Synced" line, Settings page from the gear (with the In game advanced group moved there from Sharing), the rail of quick toggles replacing `SetupBar` and the right column, console flyout with pin. Stream kind tables in `StreamViewModel` read names from `Moments` where they overlap.
- **Phase 3, Home:** live/last played card from `StreamSnapshot.Latest`, session strip, recent moments feed from one highlight builder shared with `CharacterOverview`, posted ids kept in the state file.
- **Phase 4, setup helper:** 4 steps replacing `IntroTour`, reusing the folder detection, `AddonInstaller`, the add-webhook form and `SharingPresets`.
- **Phase 5, addon:** 4-tab window, `ui.choice`, reload reminder choice, `preset` In game key end to end, Options page link, `/wct help`. Addon `.toc` version bump.
