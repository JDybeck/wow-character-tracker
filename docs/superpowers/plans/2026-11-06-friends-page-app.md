# Friends page, steps 2 and 3 (app) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The app reads what Battle.net friends shared (`WoWCharacterTrackerDB.friends`), shows it on a new Friends page per game tab, and lets the user change sharing settings, give friends nicknames and hide friends' characters.

**Architecture:** A friend's character becomes a plain `Snapshot` plus an `Inventory`, so `CharacterRow.Build` makes the same rows the Characters page shows and the gear tab works unchanged. Parsing lives in `Services/FriendData` (validated again, it's someone else's data). Sharing settings sync like notes: the app takes `WoWCharacterTrackerDB.share` when its `at` is newer and writes Settings.lua `share` with a newer `at` when changed in the app. One new `FriendsViewModel` per game tab, one new `Views/FriendsPage`. The character row header moves into a shared `Views/CharacterRowView` so both pages use one copy.

**Tech Stack:** .NET 10 WPF, MoonSharp, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-06-friends-sharing-design.md` (sections App, Privacy, Testing). Addon side: `docs/superpowers/plans/2026-11-06-friends-sharing-addon.md`, merged into this branch's base `feat/friends-sharing`.

## Global Constraints

- Branch `feat/friends-page`, cut from `feat/friends-sharing`; its own PR (base `feat/friends-sharing` until that merges, then `main`). No pushes without Jesper asking.
- Friend data never reaches Discord posts, the stream overlay, `ArmoryExport`, the Items page, totals or the console beyond counts. Friend rows live only in `FriendsViewModel`, never in `FlavorViewModel.Characters` or `allRows`.
- Validation mirrors `FriendData.lua`: field allowlist, string caps (char 24, realm 40, class/race 24, classFile/raceFile 16, faction 10, spec 32, talents 1024, talentCalc 256, gear 512, enchants 2048, zone 64), numbers finite, GUID `^Player-\d+-[0-9A-Fa-f]+$`, at most 50 characters per contact, version `^[\w.\-]{1,16}$`.
- Settings.lua `share` is written only when the app has a copy (`FlavorSettings.Share` not null): an app that never touched sharing must not reset it. `friendNames` is app-owned.
- `share` shape (same as the addon): `{ enabled, newChars, defaults = { gear, talents, location, gold }, chars = { [guid] = bool }, contacts = { [tag] = { share, receive, gear?, talents?, location?, gold? } }, blocked = { [tag] = true }, at }`. A contact's category nil = follows `defaults`.
- One of each (CLAUDE.md): pictures through `ImageCache`/`WebImage`, game things through `ui:ItemTip.Info`, files through `Files.WriteAtomic`, themed brushes `DynamicResource`, boxes `CardBorder`/`PanelBorder`, no effects on repeated elements.
- Nothing new on disk (the Uninstall page needs no change): friend data stays in SavedVariables, settings in config.json.
- Commits: Conventional Commits, scope `companion`, trailers `Co-Authored-By: Claude <noreply@anthropic.com>` and `Claude-Session: 5273ae34-7426-4d14-b965-12f2bb2aaef0`.
- Validation command: `dotnet test companion/WoWCharacterTracker.Tests`. UI changes also need a screenshot-mode render (testing.md: set `CHARACTERTRACKER_DATA_DIR` and fake game folders, never the real game).

## Review Focus

1. **A friend's saved data is malformed or huge** (wrong types, 10 000 characters, NUL bytes): the sync still works and the page shows what's valid. Pinned by `Hostile_friend_data_is_cleaned_or_dropped` (Task 1).
2. **The app changes sharing while the game has a newer in-game change**: the game's change wins on the next sync, the app's edit doesn't overwrite it. Pinned by `The_newest_share_wins_both_ways` (Task 2).
3. **An app that never touched sharing writes Settings.lua**: the in-game settings survive. Pinned by `Settings_lua_has_no_share_until_the_app_has_one` (Task 2).
4. **Friends' characters leak into your own lists** (Characters page, gold total, Items, stream): pinned by `Friend_characters_are_not_your_characters` (Task 1) and `Friend_rows_never_reach_the_tabs_own_rows` (Task 3).
5. **A friend renames nothing but you give them a nickname**: the chip keeps its selection (key = BattleTag). Pinned by `Chips_show_nicknames_but_keep_the_battletag_as_key` (Task 3).

---

### Task 1: Reading friend data

**Files:**
- Create: `companion/WoWCharacterTracker/Models/Friends.cs`
- Create: `companion/WoWCharacterTracker/Services/FriendData.cs`
- Modify: `companion/WoWCharacterTracker/Models/Snapshot.cs` (`TrackerData`: `Friends`, `Share`)
- Modify: `companion/WoWCharacterTracker/Services/SavedVariables.cs:23-30`
- Test: `companion/WoWCharacterTracker.Tests/FriendDataTests.cs`

**Interfaces:**
- Produces: `record FriendContact(string Tag, long Seen, string Version, int Protocol, bool Refused, List<Snapshot> Characters, List<Inventory> Inventories)`; `record ShareSettings` (see Step 3); `FriendData.ReadFriends(DynValue) -> List<FriendContact>`; `FriendData.ReadShare(DynValue) -> ShareSettings?`; `TrackerData.Friends`, `TrackerData.Share`.

- [ ] **Step 1: Failing tests** (`FriendDataTests.cs`)

```csharp
using MoonSharp.Interpreter;
using WoWCharacterTracker.Services;

namespace WoWCharacterTracker.Tests;

public class FriendDataTests
{
    static DynValue Lua(string table) => new Script().DoString("return " + table);

    [Fact]
    public void A_friends_characters_become_snapshots_and_gear()
    {
        var friends = FriendData.ReadFriends(Lua("""
            { ['Zeb#2222'] = { seen = 2000, version = '0.33.0', chars = {
                ['Player-1-0000000B'] = { guid = 'Player-1-0000000B', char = 'Zeb', realm = 'Firemaw', level = 42, class = 'Warrior', classFile = 'WARRIOR',
                  race = 'Orc', raceFile = 'Orc', faction = 'Horde', spec = 'Arms', ilvl = 38, at = 1900, zone = 'Duskwood', money = 5000,
                  gear = '1~19019,16~12345', enchants = '16~12345~Crusader~1900', talentCalc = '--505', received = 1950 } } } }
            """));
        var zeb = Assert.Single(friends);
        Assert.Equal("Zeb#2222", zeb.Tag);
        Assert.Equal("0.33.0", zeb.Version);
        var s = Assert.Single(zeb.Characters);
        Assert.Equal(("Zeb", "Firemaw", 42, "WARRIOR", "Duskwood", 1900L, 5000L), (s.Char, s.Realm, s.Level, s.ClassFile, s.Zone, s.LastSeen, s.Money));
        Assert.Equal("--505", s.TalentCalc);
        var inv = Assert.Single(zeb.Inventories);
        Assert.Equal(2, inv.Gear.Count);
        Assert.Equal("Crusader", Assert.Single(inv.Enchants).Text);
    }

    [Fact]
    public void Hostile_friend_data_is_cleaned_or_dropped()
    {
        var friends = FriendData.ReadFriends(Lua("""
            { ['Zeb#2222'] = { version = string.rep('x', 40) .. '\0', chars = {
                ['Player-1-0000000B'] = { guid = 'Player-1-0000000B', char = string.rep('Z', 100), level = 'abc', ilvl = 1/0, spec = 'A|cff00ff00B' },
                ['bad'] = { guid = 'Creature-1', char = 'X' },
                ['Player-1-0000000C'] = 'not a table' } },
              [5] = { chars = {} } }
            """));
        var zeb = Assert.Single(friends);
        Assert.Equal("", zeb.Version);
        var s = Assert.Single(zeb.Characters);
        Assert.Equal(24, s.Char.Length);
        Assert.Equal(0, s.Level);
        Assert.Equal(0, s.ItemLevel);
        Assert.Equal("Acff00ff00B", s.Spec);
    }

    [Fact]
    public void At_most_50_characters_per_friend_are_read()
    {
        var chars = string.Concat(Enumerable.Range(1, 60).Select(i => $"['Player-1-{i:X8}'] = {{ guid = 'Player-1-{i:X8}', char = 'A{i}', at = {i} }},"));
        var zeb = Assert.Single(FriendData.ReadFriends(Lua($"{{ ['Zeb#2222'] = {{ chars = {{ {chars} }} }} }}")));
        Assert.Equal(50, zeb.Characters.Count);
        Assert.DoesNotContain(zeb.Characters, c => c.Char == "A1"); // the ones seen longest ago go
    }

    [Fact]
    public void Friend_characters_are_not_your_characters()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wct-friends-{Guid.NewGuid():N}.lua");
        File.WriteAllText(path, """
            WoWCharacterTrackerDB = { chars = { ['Player-1-0000000A'] = { char = 'Deadbeck', realm = 'Firemaw', level = 10, lastSeen = 5 } },
              friends = { ['Zeb#2222'] = { chars = { ['Player-1-0000000B'] = { guid = 'Player-1-0000000B', char = 'Zeb' } } } } }
            """);
        try
        {
            var data = SavedVariables.Read(path);
            Assert.Equal(["Player-1-0000000A"], data.Characters.Select(c => c.Guid));
            Assert.Equal("Zeb", Assert.Single(Assert.Single(data.Friends).Characters).Char);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Share_settings_are_read_with_defaults_for_what_is_missing()
    {
        var share = FriendData.ReadShare(Lua("""
            { enabled = true, at = 300, defaults = { gold = true }, chars = { ['Player-1-0000000D'] = false },
              contacts = { ['Zeb#2222'] = { share = true, receive = true, location = false } }, blocked = { ['Bad#1'] = true } }
            """))!;
        Assert.True(share.Enabled);
        Assert.Equal(300, share.At);
        Assert.True(share.Defaults.Gold);
        Assert.True(share.Defaults.Gear);       // missing: the addon's default
        Assert.False(share.Defaults.Location);
        Assert.False(share.Chars["Player-1-0000000D"]);
        Assert.False(share.Contacts["Zeb#2222"].Location);
        Assert.Null(share.Contacts["Zeb#2222"].Gold); // follows the general choice
        Assert.Contains("Bad#1", share.Blocked);
        Assert.Null(FriendData.ReadShare(Lua("nil")));
    }
}
```

- [ ] **Step 2: Run, expect FAIL** (`FriendData` doesn't exist: build error).

Run: `dotnet test companion/WoWCharacterTracker.Tests --filter "FullyQualifiedName~FriendDataTests"`

- [ ] **Step 3: Models** (`Models/Friends.cs`)

```csharp
namespace WoWCharacterTracker.Models;

// What one Battle.net friend shared (the addon's WoWCharacterTrackerDB.friends[battleTag]): their
// characters as snapshots and gear, so the app shows them with the same rows as yours.
record FriendContact(string Tag, long Seen, string Version, int Protocol, bool Refused, List<Snapshot> Characters, List<Inventory> Inventories);

// What friends get, per category: the general choice, or one friend's own (null = follows the general one).
record ShareCategories(bool? Gear, bool? Talents, bool? Location, bool? Gold);
record GeneralCategories(bool Gear = true, bool Talents = true, bool Location = false, bool Gold = false);
record ShareContact(bool Share, bool Receive, bool? Gear, bool? Talents, bool? Location, bool? Gold);

// Sharing with friends, the same shape as the addon's `share` (Friends.lua). The newest At wins.
record ShareSettings(bool Enabled, bool NewChars, GeneralCategories Defaults, Dictionary<string, bool> Chars,
    Dictionary<string, ShareContact> Contacts, HashSet<string> Blocked, long At);
```

Ruling allowed: drop `ShareCategories` if unused after Task 3 (it's only there if the view model needs a tri-state set).

- [ ] **Step 4: `Services/FriendData.cs`** reading `friends` and `share` with the caps from Global Constraints. Snapshot fields: `Snapshot(guid, char, realm, level, class, classFile, race, raceFile, Sex: "", Spec: spec, Role: "", ItemLevel: ilvl, Played: 0, Zone: zone, Professions: "", Talents: talents, LastSeen: at, Hardcore: false, Ruleset: "", Money: money, TalentCalc: talentCalc, Faction: faction)`. Inventory: `Inventory.Parse(guid, k => k is "gear" or "enchants" ? value : "", _ => 0)`. Clean strings: drop `|` and control characters (keep `\n` only in enchants), cut to length. Numbers: `CastToNumber()`, finite only, else 0. Keep the 50 newest by `at`. Contacts keyed by a string tag only.

- [ ] **Step 5: Wire it** in `TrackerData` (`public List<FriendContact> Friends { get; init; } = [];`, `public ShareSettings? Share { get; init; }`) and `SavedVariables.Read` (`Friends = FriendData.ReadFriends(db.Table.Get("friends")), Share = FriendData.ReadShare(db.Table.Get("share"))`).

- [ ] **Step 6: Run all tests, expect PASS.** `dotnet test companion/WoWCharacterTracker.Tests`

- [ ] **Step 7: Commit** `feat(companion): read what battle.net friends share from the saved file`

---

### Task 2: Sharing settings sync and Settings.lua

**Files:**
- Modify: `companion/WoWCharacterTracker/Models/FlavorSettings.cs` (`Share`, `TakeShare`)
- Modify: `companion/WoWCharacterTracker/Models/Config.cs` (`FriendNicknames`, `HiddenFriendCharacters`)
- Modify: `companion/WoWCharacterTracker/Services/AddonInstaller.cs:79-119` (`share`, `friendNames`)
- Modify: `companion/WoWCharacterTracker/ViewModels/FlavorViewModel.cs` (`ShowData` takes `data.Share`)
- Test: `companion/WoWCharacterTracker.Tests/FriendDataTests.cs`

**Interfaces:**
- Produces: `FlavorSettings.Share` (`ShareSettings?`), `bool FlavorSettings.TakeShare(ShareSettings? game)` (true when taken), `FlavorSettings.EditShare(Func<ShareSettings, ShareSettings> edit)` (sets `At = max(now, old At + 1)`), `Config.FriendNicknames` (`Dictionary<string, string>`, by BattleTag, all games), `Config.HiddenFriendCharacters` (`HashSet<string>`, GUIDs), `AddonInstaller.WriteSettings(..., IReadOnlyDictionary<string, string>? friendNames = null)`.

- [ ] **Step 1: Failing tests**

```csharp
    [Fact]
    public void The_newest_share_wins_both_ways()
    {
        var s = new FlavorSettings();
        var game = FriendData.ReadShare(Lua("{ enabled = true, at = 200 }"));
        Assert.True(s.TakeShare(game));
        Assert.True(s.Share!.Enabled);
        s.EditShare(x => x with { NewChars = false });
        Assert.True(s.Share.At > 200);
        Assert.False(s.TakeShare(game)); // older than the app's edit now
        Assert.False(s.Share.NewChars);
    }

    [Fact]
    public void Settings_lua_has_no_share_until_the_app_has_one()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wct-share-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Interface", "AddOns", "WoWCharacterTracker"));
        try
        {
            var s = new FlavorSettings();
            AddonInstaller.WriteSettings(dir, s);
            var file = Path.Combine(dir, "Interface", "AddOns", "WoWCharacterTracker", "Settings.lua");
            Assert.DoesNotContain("share =", File.ReadAllText(file));
            s.TakeShare(FriendData.ReadShare(Lua("{ enabled = true, at = 200, contacts = { ['Zeb#2222'] = { share = true, gold = true } } }")));
            AddonInstaller.WriteSettings(dir, s, friendNames: new Dictionary<string, string> { ["Zeb#2222"] = "Zebbe \"Z\"" });
            var script = new Script();
            script.DoString(File.ReadAllText(file));
            var share = FriendData.ReadShare(script.Globals.Get("WoWCharacterTrackerSettings").Table.Get("share"))!;
            Assert.True(share.Enabled);
            Assert.Equal(200, share.At);
            Assert.True(share.Contacts["Zeb#2222"].Gold);
            Assert.Equal("Zebbe \"Z\"", script.DoString("return WoWCharacterTrackerSettings.friendNames['Zeb#2222']").String);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
```

And one through the addon itself: load the written Settings.lua plus `Shared.lua`, `FriendData.lua`, `Comms.lua`, `Friends.lua` the way `FriendsLuaTests.Client` does (stubs from there; make `Stubs` internal) and assert `ns.shareSettings().contacts['Zeb#2222'].gold == true` and `ns.allowedFor(...)`.gold. Name: `The_addon_reads_the_share_the_app_wrote`.

- [ ] **Step 2: Run, expect FAIL** (build errors: `TakeShare`, `EditShare`, `friendNames` missing).

- [ ] **Step 3: Implement.** `TakeShare`: `if (game is null || game.At <= (Share?.At ?? 0)) return false; Share = game; return true;`. `EditShare`: `Share = edit(Share ?? Empty) with { At = Math.Max(now, (Share?.At ?? 0) + 1) }` where `Empty` = `new(false, true, new(), [], [], [], 0)`. JSON: records serialize with System.Text.Json as config already does (check `Config.Save` options handle records with constructor parameters; if not, a parameterless shape, ruling). `WriteSettings` appends `share = {...}` only when `s.Share` is set (Lua via `LuaString` for tags/GUIDs, `true`/`false`, a contact's null categories left out) and `friendNames = { [tag] = name, ... }` (sorted, ordinal).

- [ ] **Step 4: Sync in `FlavorViewModel.ShowData`**, next to `TakeInGameSettings(data.InGame)`: `if (Settings.TakeShare(data.Share)) { main.Config.Save(); WriteAddonSettings(); }`. Pass `main.Config.FriendNicknames` to `WriteSettings` in `WriteAddonSettings`.

- [ ] **Step 5: Run all tests, expect PASS.**

- [ ] **Step 6: Commit** `feat(companion): sync sharing settings and friend nicknames with the addon`

---

### Task 3: FriendsViewModel

**Files:**
- Create: `companion/WoWCharacterTracker/ViewModels/FriendsViewModel.cs`
- Modify: `companion/WoWCharacterTracker/ViewModels/ChipFilter.cs:37-47` (optional label function)
- Modify: `companion/WoWCharacterTracker/ViewModels/CharacterRow.cs` (`IsFriend`, `FriendTag`, `FriendName`)
- Modify: `companion/WoWCharacterTracker/ViewModels/FlavorViewModel.cs` (`Friends` property, `ShowData` calls `Friends.Show(data)`, `ResetPage("friends")`)
- Test: `companion/WoWCharacterTracker.Tests/FriendsViewModelTests.cs`

**Interfaces:**
- Consumes: `TrackerData.Friends`, `FlavorSettings.Share/EditShare`, `Config.FriendNicknames/HiddenFriendCharacters`.
- Produces: `FriendsViewModel(Flavor flavor, GameLookup lookup, Func<Config> config, Func<FlavorSettings> settings, Action changed)` with `Show(TrackerData data)`, `Reset()`, `ChipFilter Contacts`, `ObservableCollection<CharacterRow> Characters`, `CharacterRow? SelectedCharacter`, `int HiddenCount`, `bool ShowHidden`, `ICommand HideCommand/UnhideCommand` (a row), `ObservableCollection<FriendContactRow> ContactRows` (Tag, Name, Nickname get/set, SeenText, VersionText, NeedsUpdate, Share/Receive, Gear/Talents/Location/Gold as `bool?`), `bool Enabled`, `bool NewChars`, `bool ShareGear/ShareTalents/ShareLocation/ShareGold` (general), `IReadOnlyList<OwnCharacterShare> OwnCharacters` (name, `IsShared`), `IReadOnlyList<string> Blocked` + `UnblockCommand`. `changed` saves config and writes Settings.lua (`FlavorViewModel.AddonSettingsChanged`). `ChipFilter.Fill<T>(items, keyOf, Func<string, string>? labelOf = null)`.

- [ ] **Step 1: Failing tests** (build a `FriendsViewModel` with a real `Config`, a `FlavorSettings`, a `GameLookup` the way existing VM tests do; check `SmallLogicTests` for how `GameLookup` is made in tests and reuse it)

- `Friend_rows_never_reach_the_tabs_own_rows`: `Show` with own `Characters` and two friends; `Characters` holds only friend rows, all `IsFriend`, and `FlavorViewModel`-side lists are untouched (assert `CharacterRow.Build(data, ...)` for own data has no friend GUID).
- `Chips_show_nicknames_but_keep_the_battletag_as_key`: chip labels "Zebbe (2)", keys "Zeb#2222"; pick the chip, set a nickname, `Show` again, the same chip is still picked.
- `Hidden_friend_characters_fold_away`: hide one row, `Characters` loses it, `HiddenCount` 1, `ShowHidden` brings it back; the GUID is in `Config.HiddenFriendCharacters`.
- `Changing_a_category_for_one_friend_edits_the_share_and_tells_the_addon`: set `ContactRows[0].Gold = true`, `settings.Share.Contacts["Zeb#2222"].Gold == true`, `At` moved, `changed` ran once; set it to null and it follows the general choice again.
- `General_categories_default_like_the_addon`: with no `Share`, `ShareGear` true, `ShareGold` false, and toggling one creates a `Share`.
- `A_friend_with_another_protocol_says_needs_update`: `Protocol = 2` gives `NeedsUpdate` and a version text "needs to update".

- [ ] **Step 2: Run, expect FAIL** (build errors).

- [ ] **Step 3: Implement.** Rows: per contact, `CharacterRow.Build(new TrackerData([], contact.Characters) { Inventories = contact.Inventories }, flavor, lookup)`, then set `IsFriend = true`, `FriendTag`, `FriendName` (nickname or tag). Sort by name within a contact; with All picked, ordered by friend name then character. `LoadPictureAsync(flavor)` like `ShowData` (Forever portraits are by GUID and won't exist for friends: the class/race picture follows).

- [ ] **Step 4: Run all tests, expect PASS.**

- [ ] **Step 5: Commit** `feat(companion): friends view model with chips, nicknames and sharing settings`

---

### Task 4: Friends page

**Files:**
- Create: `companion/WoWCharacterTracker/Views/CharacterRowView.xaml(.cs)` (the row header from `CharactersPage.xaml:126-211`, moved, not copied)
- Modify: `companion/WoWCharacterTracker/Views/CharactersPage.xaml(.cs)` (uses `CharacterRowView`; the close-on-click handler moves with it and clears the parent `ListBox`'s selection, so it works for both pages)
- Create: `companion/WoWCharacterTracker/Views/FriendsPage.xaml(.cs)`
- Modify: `companion/WoWCharacterTracker/Views/CharacterOverview.xaml` (tags, Post to Discord, card options, remove, export, notes and highlight post buttons hidden when `IsFriend`; a "Shared by Zebbe" line instead)
- Modify: `companion/WoWCharacterTracker/MainWindow.xaml:49-100` (a Friends page tab after Followed items, icon `&#xE716;`, and `views:FriendsPage` in the per-tab grid, `ui:Motion.SlideIndex="5"`); `Views/UninstallPage.xaml` slide index 6
- Modify: `companion/WoWCharacterTracker/Ui/ScreenshotMode.cs` (header comment + `friends` page works through `vm.Page`; extra2 `sharing` opens the sharing panel)
- Test: screenshot renders (no new unit tests; the row move is covered by the existing screenshot of the Characters page looking the same)

**Page layout:**
- Card (`CardBorder`), header: `ChipBar` bound to `Friends.Contacts`, then a muted line "What friends share reaches the app on their next /reload or logout. Refresh in game: /wct friends ask <BattleTag>."
- List: `ListBox` of `CharacterRowView` rows with the details below the selected one (Overview read-only, Gear and bags), the same `CharacterDetails` structure as CharactersPage with two tabs. Under the list: "N hidden, show" when `HiddenCount > 0`. Each row's context menu or a small ghost button in Overview: Hide / Show again.
- Empty state: "No friends yet" and what to do: turn on sharing (in game or below), then `/wct friends ask <BattleTag>`.
- Sharing panel (a toggle button "Sharing settings" in the header, a `PanelBorder` box): Share with friends on/off; Share new characters; What friends get (four `OptionChip` checkboxes); Contacts table (nickname `TextBox`, Shares with you, You share, Gear/Talents/Location/Gold as three-state checkboxes with tooltip "Filled: same as all friends"); Your characters (shared/hidden chips); Blocked (Unblock). Tooltips through the standard ToolTip style.

- [ ] **Step 1: Move the row header into `CharacterRowView`** and check the Characters page renders the same (screenshot before and after: `CHARACTERTRACKER_SCREENSHOT_VIEW=forever:characters:testchars`).
- [ ] **Step 2: Build `FriendsPage`, the tab, the slide index** and the read-only Overview.
- [ ] **Step 3: Fake friend data for the render**: a fake `_forever_` folder with a SavedVariables file that has `friends` (two contacts, five characters, gear on one) and `share`. Render `forever:friends`, `forever:friends::gear`, `forever:friends::sharing`, plus `forever:characters:testchars` to confirm the Characters page is unchanged. Check the PNGs by eye (Jesper prefers to see them: send them).
- [ ] **Step 4: Run all tests, expect PASS.**
- [ ] **Step 5: Commit** `feat(companion): friends page with shared characters and sharing settings`

---

### Task 5: Docs

**Files:** `docs/GUIDE.md` (`## The app`: a Friends paragraph; the in-game section's "A Friends page in the app is coming next" becomes a pointer to it), `CHANGELOG.md` (the v0.33.0 Share with Battle.net friends line gains "and see what friends share on the app's new Friends page"), `.claude/notes/friends.md` (steps 2 and 3 built, the app's classes), `.claude/notes/companion.md` and `companion-ui.md` (`FriendData`, `FriendsViewModel`, `FriendsPage`, `CharacterRowView`, slide index 5).

- [ ] **Step 1: Write them.**
- [ ] **Step 2: `dotnet test companion/WoWCharacterTracker.Tests`, expect PASS** (the changelog test reads the section).
- [ ] **Step 3: Commit** `docs: describe the friends page`
- [ ] **Step 4: Rebuild dist and restart Jesper's copy** (memory: stop by PID, `dotnet publish companion/WoWCharacterTracker -c Release -o dist -nologo`, start it again).
