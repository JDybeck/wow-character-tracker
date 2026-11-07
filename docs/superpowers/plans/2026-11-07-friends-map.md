# Friends on the World Map Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Battle.net friends who both run the addon see each other as pins on the world map, live while the map is open, opt-in per friend.

**Architecture:** Two new addon kinds on the existing friends comms (`Comms.lua`): the one with the map open sends `WATCH`, a friend who allows it answers with `POS`. `FriendMap.lua` holds that logic (no frames, tested in MoonSharp), `MapPins.lua` + `MapPins.xml` draw through Blizzard's map data provider API. Settings (`map`, `otherFaction`, `mapRate`) live in the existing `share` table, so the app only reads and writes them.

**Tech Stack:** Lua addon (no build step), .NET 10 WPF app, xUnit + MoonSharp for the addon's Lua.

**Spec:** `docs/superpowers/specs/2026-11-07-friends-map-design.md`

## Global Constraints

- Branch `feat/friends-map`. Never push, tag or release unless JDybeck asks in the session.
- Commits: Conventional Commits, scopes `addon`, `companion`, or none for docs. Each ends with `Co-Authored-By: Claude <noreply@anthropic.com>` and `Claude-Session: <session id>`. Author is the GitHub noreply address (already set in this repo).
- Refer to the owner as JDybeck in every file, never by first name.
- Writing: casual and direct, no em dashes, brief comments (only what's needed to understand the code).
- Addon taint: never replace Blizzard globals, never register `COMBAT_LOG_EVENT_UNFILTERED`, no CVar changes. Our pin's own mixin may define methods (it's our table).
- Positions are never written to SavedVariables or any file.
- `Map position` off by default (`defaults.map = false`), `otherFaction` off by default (= hidden from the other faction), `mapRate` 5, 60 or 300, anything else reads as 5.
- `POS` body: `mapID,x,y,rate,age`, x and y with 3 decimals. Empty body = remove the pin.
- Timings: `WATCH_EVERY = 20`, `WATCH_LASTS = 30`, `TICK = 5`, `RESEND = 20`. Pin fades after `max(30, 2 x rate)` s, goes after `max(120, 4 x rate)` s.
- Validation command (there is no `verify.cmd` in the repo): `dotnet test companion\WoWCharacterTracker.Tests\WoWCharacterTracker.Tests.csproj -warnaserror --nologo`. Known failure on main, not ours: `RatingTests.The_built_in_changelog_starts_at_this_version_and_uses_the_groups` (csproj 0.37.1 vs CHANGELOG v0.37.2). `LiveGridTests.A_new_shot_is_picked_up_quickly` is flaky (file lock), rerun alone if it fails.
- Testing the app: `CHARACTERTRACKER_DATA_DIR` plus fake flavor folders, never the real game (`.claude/notes/testing.md`). Never stop processes by name.

## Review Focus

1. **The friend's state changes mid-watch** (switches to the other faction, hides the character, turns sharing off, takes Map position away): the pin must go within one tick, not linger until it times out. Test: Task 2 `Losing_the_position_or_the_right_to_it_removes_the_pin`.
2. **Hostile or broken `POS` bodies** (x outside 0..1, unknown rate, mapID 0, a huge body that makes `tonumber` give infinity): dropped, nothing drawn. Test: Task 2 `A_position_that_wasnt_asked_for_or_makes_no_sense_is_dropped`.
3. **A friend going offline** while their pin shows: the pin goes on the next redraw, no stale ghost. Test: Task 2 `A_friend_going_offline_loses_the_pin`.
4. **Two clocks**: a slow-rate reading resent to a new watch must show its real age, not "just now". Test: Task 2 `A_slow_rate_gives_a_new_watch_the_last_reading_with_its_age`.
5. **An app config saved before this change** (no `MapRate` in the JSON): must come back as live (5), not 0. Test: Task 4 `An_old_config_without_map_settings_reads_as_live_and_off`.

---

### Task 1: Map settings in the addon (Friends.lua, Window.lua)

**Files:**
- Modify: `addon/WoWCharacterTracker/Friends.lua` (top constants, `normalize`, `ns.allowedFor`, `ns.friendsOnline`, after `local on = {}`, `HELP`, `IS_CATEGORY`, `picked`, `ns.setContactCategory`, `ns.contactSharing`, `set`, `ns.friendsCommand`)
- Modify: `addon/WoWCharacterTracker/Window.lua` (`SHARING_HEIGHT`, `CATEGORY_NAMES`, sharing panel button loop, `PRESETS`, Sharing tab `buildSharing`, the `sharing` tab height)
- Test: `companion/WoWCharacterTracker.Tests/FriendsLuaTests.cs`, `companion/WoWCharacterTracker.Tests/AddonWindowTests.cs`

**Interfaces:**
- Produces: `ns.allowedFor(s, contact)` now also has `.map` and `.otherFaction`; `ns.shareSettings().mapRate` (5, 60 or 300); `ns.SHARE_RULES = { "otherFaction" }`; `ns.onFriendKind(kind, handler)` where handler is `function(f, contact, body)` with `f = { tag, account }`, called only for contacts with sharing on; rows of `ns.friendsOnline()` and `ns.onlineFriends()` get `faction` (Battle.net `factionName`) and `realm` (`realmName`); `ns.contactSharing(tag).categories` lists CATEGORIES then RULES, rule entries have `rule = true`.

- [ ] **Step 1: Give the test friends a faction and realm**

In `FriendsLuaTests.cs`, `Stubs`, the `GetFriendGameAccountInfo` line becomes:

```lua
            return f and { isOnline = f.online ~= false, clientProgram = 'WoW', wowProjectID = 5, gameAccountID = f.account, playerGuid = f.guid,
              characterName = f.char, className = f.className, classFilename = f.classFile, characterLevel = f.level, areaName = f.zone,
              factionName = f.faction, realmName = f.realm }
```

Also make the two-client helpers reusable by Task 2: change `static void Deliver(` to `internal static void Deliver(` and `static void Talk(` to `internal static void Talk(`.

- [ ] **Step 2: Write the failing Lua tests**

Add to `FriendsLuaTests.cs` (after `Professions_go_only_when_allowed_and_are_cut_to_length` or near the other settings tests):

```csharp
    // Map position (off) and the other faction rule (off) work like the categories: general, or per friend.
    [Fact]
    public void Map_position_and_the_other_faction_are_off_unless_turned_on_and_can_be_set_per_friend()
    {
        var a = new Client(A, AFriends, ADb(AShares));
        Assert.False(a.Lua("return ns.allowedFor(ns.shareSettings(), nil).map").Boolean);
        Assert.False(a.Lua("return ns.allowedFor(ns.shareSettings(), nil).otherFaction").Boolean);
        a.Call("friendsCommand", "set Zeb#2222 map on");
        a.Call("friendsCommand", "set zeb#2222 OtherFaction on"); // any case
        Assert.True(a.Lua("local s = ns.shareSettings(); return ns.allowedFor(s, s.contacts['Zeb#2222']).map").Boolean);
        Assert.True(a.Lua("return ns.shareSettings().contacts['Zeb#2222'].otherFaction").Boolean);
        a.Call("friendsCommand", "set all map on");
        Assert.True(a.Lua("return ns.shareSettings().defaults.map").Boolean);
        Assert.Contains("Friends now get map: on.", a.Said());
        Assert.Equal("gear, talents, location, professions, map position", Str(a, "ns.shareSummary(ns.allowedFor(ns.shareSettings(), nil))"));
    }

    [Fact]
    public void Contact_sharing_lists_the_other_faction_rule_after_the_categories()
    {
        var a = new Client(A, AFriends, ADb(AShares));
        a.Lua("o = ns.contactSharing('Zeb#2222')");
        Assert.Equal(7, a.Lua("return #o.categories").Number);
        Assert.Equal("map", Str(a, "o.categories[6].key"));
        Assert.Equal("otherFaction", Str(a, "o.categories[7].key"));
        Assert.True(a.Lua("return o.categories[7].rule").Boolean);
        Assert.True(a.Lua("return o.categories[6].rule").IsNil());
        a.Call("cycleContactCategory", "Zeb#2222", "otherFaction");
        Assert.True(a.Lua("return ns.shareSettings().contacts['Zeb#2222'].otherFaction").Boolean);
    }

    [Theory]
    [InlineData("live", 5)]
    [InlineData("1m", 60)]
    [InlineData("5M", 300)]
    public void The_map_rate_is_live_unless_set_to_one_of_the_steps(string word, int rate)
    {
        var a = new Client(A, AFriends, ADb("{ enabled = true, mapRate = 7, at = 100 }"));
        Assert.Equal(5, a.Lua("return ns.shareSettings().mapRate").Number); // 7 isn't a step
        a.Call("friendsCommand", "maprate " + word);
        Assert.Equal(rate, a.Lua("return ns.shareSettings().mapRate").Number);
        a.Call("friendsCommand", "maprate 2m");
        Assert.Equal(rate, a.Lua("return ns.shareSettings().mapRate").Number); // unchanged
        Assert.Contains(a.Said(), s => s.StartsWith("/wct friends maprate", StringComparison.Ordinal));
    }

    [Fact]
    public void Online_friends_carry_their_faction_and_realm()
    {
        var a = new Client(A, "{ { tag = 'Zeb#2222', account = 2, guid = 'Player-1-0000000B', faction = 'Alliance', realm = 'Firemaw' } }");
        Assert.Equal("Alliance", Str(a, "ns.friendsOnline()[1].faction"));
        Assert.Equal("Firemaw", Str(a, "ns.onlineFriends()[1].realm"));
        Assert.Equal("Alliance", Str(a, "ns.onlineFriends()[1].faction"));
    }

    // FriendMap.lua adds its own kinds through this; unknown kinds are still dropped quietly.
    [Fact]
    public void Another_file_can_handle_a_kind_and_unknown_kinds_are_dropped()
    {
        var a = new Client(A, AFriends, ADb(AShares));
        a.Lua("got = nil; ns.onFriendKind('ZZ', function(f, contact, body) got = f.tag .. ':' .. body end)");
        a.Receive("1:ZZ:4:1:1:hello", BAccount);
        Assert.Equal("Zeb#2222:hello", Str(a, "got"));
        a.Receive("1:QQ:5:1:1:x", BAccount);
        Assert.True(a.Lua("return ns.lastCommsError").IsNil());
    }
```

- [ ] **Step 3: Run them and see them fail**

Run: `dotnet test companion\WoWCharacterTracker.Tests\WoWCharacterTracker.Tests.csproj -warnaserror --nologo --filter "FullyQualifiedName~FriendsLuaTests"`
Expected: the five new tests FAIL (`map` is nil, `onFriendKind` is nil, `maprate` prints the help), everything else passes.

- [ ] **Step 4: The settings in Friends.lua**

Replace the top block (lines 15-18):

```lua
-- What friends get, unless set for one of them. Gold and map position stay off until turned on.
local CATEGORIES = { "gear", "talents", "location", "professions", "gold", "map" }
-- Set like a category, but not data: otherFaction off means a friend playing the other faction
-- gets no map position.
local RULES = { "otherFaction" }
local DEFAULTS = { gear = true, talents = true, location = true, professions = true, gold = false, map = false, otherFaction = false }
local MAP_RATES = { [5] = true, [60] = true, [300] = true } -- seconds between two map position readings
ns.SHARE_CATEGORIES = CATEGORIES
ns.SHARE_RULES = RULES
```

In `normalize`, after `if s.enabled == nil then ... end`:

```lua
  if not MAP_RATES[s.mapRate] then s.mapRate = 5 end
```

Replace `ns.allowedFor`:

```lua
-- The categories and rules a contact gets: their own choice where there is one, else the general one.
function ns.allowedFor(s, contact)
  local out = {}
  for _, list in ipairs({ CATEGORIES, RULES }) do
    for _, k in ipairs(list) do
      local own = contact and contact[k]
      if own == nil then out[k] = s.defaults[k] == true else out[k] = own == true end
    end
  end
  return out
end
```

In `ns.friendsOnline`, update the comment to `-- bnet, character, classFile, level, zone, faction, realm }.` and the row:

```lua
          out[#out + 1] = { tag = info.battleTag, account = g.gameAccountID, guid = g.playerGuid, bnet = info.bnetAccountID,
            character = g.characterName, classFile = classFileOf(g), level = g.characterLevel, zone = g.areaName,
            faction = g.factionName, realm = g.realmName }
```

Right after `local on = {}` (line 298):

```lua
-- Another file's handler for a kind (FriendMap.lua: WATCH and POS), called like these: (f, contact,
-- body), only for contacts, only with sharing on.
function ns.onFriendKind(kind, handler) on[kind] = handler end
```

In `ns.onlineFriends`, add `faction = f.faction, realm = f.realm,` to the row (after `zone = f.zone,`).

- [ ] **Step 5: Commands, summary and the per-friend panel data**

Replace `HELP` (keep the last four lines as they are, after the two new ones):

```lua
local HELP = {
  "/wct friends lists your contacts and what they get.",
  "/wct friends on or off turns sharing with Battle.net friends on or off.",
  "/wct friends ask <BattleTag> asks a friend for their characters.",
  "/wct friends share <BattleTag> shares your characters with them.",
  "/wct friends set all gear/talents/location/professions/gold/map on/off changes what friends get. Gold and map (your spot on their world map) are off until you turn them on.",
  "/wct friends set <BattleTag> gear/talents/location/professions/gold/map on/off/default changes it for one friend.",
  "/wct friends set all/<BattleTag> otherfaction on/off/default: on lets friends playing the other faction see you on the map too.",
  "/wct friends maprate live/1m/5m sets how often friends' maps get your position.",
  "/wct friends stop <BattleTag> stops sharing with them, remove <BattleTag> forgets them and what they sent.",
  "/wct friends hide or unhide keeps this character from friends, or shares it again.",
  "/wct friends unblock <BattleTag> lets someone you said Never to ask again.",
}
```

Replace `IS_CATEGORY` and `picked`:

```lua
-- What set takes: a category or a rule, its key exact (IS_CHOICE) or typed in any case (CHOICE_BY_WORD).
local IS_CHOICE, CHOICE_BY_WORD = {}, {}
for _, list in ipairs({ CATEGORIES, RULES }) do
  for _, k in ipairs(list) do IS_CHOICE[k], CHOICE_BY_WORD[k:lower()] = true, k end
end

local SUMMARY_NAMES = { map = "map position" }

local function picked(allowed)
  local out = {}
  for _, k in ipairs(CATEGORIES) do
    if allowed[k] then out[#out + 1] = SUMMARY_NAMES[k] or k end
  end
  return #out > 0 and table.concat(out, ", ") or "only the basics"
end
```

In `ns.setContactCategory`, `if not IS_CATEGORY[key]` becomes `if not IS_CHOICE[key]`.

In `ns.contactSharing`, after the CATEGORIES loop:

```lua
  for _, k in ipairs(RULES) do
    categories[#categories + 1] = { key = k, own = c[k], general = s.defaults[k] == true, on = allowed[k], rule = true }
  end
```

and update its comment to `-- categories: { key, own (true, false or nil = like all friends), general, on, rule } for CATEGORIES, then RULES (rule = true).`

Replace `set`:

```lua
-- /wct friends set all|<BattleTag> <category or rule> <on|off|default>
local function set(typed, what, value)
  local word, key = (what or ""):lower(), CHOICE_BY_WORD[(what or ""):lower()]
  value = (value or ""):lower()
  local general = (typed or ""):lower() == "all"
  if not key or not (value == "on" or value == "off" or (value == "default" and not general)) then
    ns.say(HELP[5])
    ns.say(HELP[6])
    return ns.say(HELP[7])
  end
  if general then
    ns.editShare(function(s) s.defaults[key] = value == "on" end)
    return ns.say(string.format("Friends now get %s: %s.", word, value))
  end
  local tag = ns.findFriendTag(typed)
  if not tag then return ns.say((typed or "That") .. " isn't on your Battle.net friends list.") end
  if not ns.shareSettings().contacts[tag] then return ns.say(label(tag) .. " isn't a contact. /wct friends share " .. tag .. " adds them.") end
  local own
  if value ~= "default" then own = value == "on" end
  ns.setContactCategory(tag, key, own)
  ns.say(string.format("%s: %s is now %s.", label(tag), word, value == "default" and "the same as for all friends" or value))
end

local RATE_WORDS = { live = 5, ["1m"] = 60, ["5m"] = 300 }
local RATE_NAMES = { [5] = "live", [60] = "once a minute", [300] = "every 5 minutes" }
```

In `ns.friendsCommand`, after the `hide`/`unhide` block and before `local typed, what, value = ...`:

```lua
  if cmd == "maprate" then
    local rate = RATE_WORDS[rest:lower()]
    if not rate then return ns.say(HELP[8]) end
    ns.editShare(function(s) s.mapRate = rate end)
    return ns.say("Friends' maps now get your position " .. RATE_NAMES[rate] .. ".")
  end
```

- [ ] **Step 6: Run the Lua tests**

Run: `dotnet test companion\WoWCharacterTracker.Tests\WoWCharacterTracker.Tests.csproj -warnaserror --nologo --filter "FullyQualifiedName~FriendsLuaTests"`
Expected: all PASS (`Friends_help_lines_have_no_raw_pipes` still finds `gear/talents/location/professions/gold` inside the longer line).

- [ ] **Step 7: Write the failing window tests**

In `AddonWindowTests.cs`, `Sharing_opens_what_you_share_with_that_friend_and_changes_it`, the first button list assertion becomes:

```csharp
        Assert.Equal("Gear: like all friends (on);Talents: like all friends (on);Location: like all friends (on);Professions: like all friends (on);Gold: like all friends (off);Map position: like all friends (off);Map for the other faction: like all friends (off)",
            c.Lua("return sharingButtons()").String);
```

and `Assert.EndsWith("Gold: on", c.Lua("return sharingButtons()").String);` becomes:

```csharp
        Assert.Contains(";Gold: on;Map position", c.Lua("return sharingButtons()").String);
```

Add:

```csharp
    [Fact]
    public void The_sharing_tab_sets_map_position_the_other_faction_and_the_map_rate()
    {
        var c = Load();
        c.Call("openWindow", "sharing");
        Assert.False(Bool(c, "find('Map position').checked"));
        c.Lua("click('Map position')");
        Assert.True(Bool(c, "WoWCharacterTrackerDB.share.defaults.map"));
        Assert.False(Bool(c, "find('Show my map position to the other faction').checked"));
        c.Lua("click('Show my map position to the other faction')");
        Assert.True(Bool(c, "WoWCharacterTrackerDB.share.defaults.otherFaction"));
        c.Lua("local b = find('Map updates: live'); b.scripts.OnClick(b)");
        Assert.Equal(60, Num(c, "WoWCharacterTrackerDB.share.mapRate"));
        Assert.True(Bool(c, "find('Map updates: every minute') ~= nil"));
    }
```

- [ ] **Step 8: Run them and see them fail**

Run: `dotnet test companion\WoWCharacterTracker.Tests\WoWCharacterTracker.Tests.csproj -warnaserror --nologo --filter "FullyQualifiedName~AddonWindowTests"`
Expected: the two tests above FAIL (the panel shows `map: like all friends (off)` and no rule button, the Sharing tab has no Map position).

- [ ] **Step 9: The window**

In `Window.lua`:

```lua
local SHARING_HEIGHT = 412
local CATEGORY_NAMES = { gear = "Gear", talents = "Talents", location = "Location", professions = "Professions", gold = "Gold",
  map = "Map position", otherFaction = "Map for the other faction" }
```

In `buildSharing` (the side panel), the button loop counts rules too:

```lua
  for i = 1, #(ns.SHARE_CATEGORIES or {}) + #(ns.SHARE_RULES or {}) do
```

`PRESETS`:

```lua
local PRESETS = {
  { "Like all friends" },
  { "Everything", { gear = true, talents = true, location = true, professions = true, gold = true, map = true } },
  { "Basics only", { gear = false, talents = false, location = false, professions = false, gold = false, map = false } },
}
```

The Sharing tab's `buildSharing(p)`: replace the `ui.checkGrid(...)` call at its end with:

```lua
  local gridLast = ui.checkGrid(p, { { "gear", "Gear" }, { "talents", "Talents" }, { "location", "Location" }, { "professions", "Professions" },
    { "gold", "Gold" }, { "map", "Map position" } }, whatTitle,
    function(key) return ns.shareSettings().defaults[key] == true end,
    function(key, on) ns.editShare(function(s) s.defaults[key] = on end) end)
  local otherFaction = ui.checkbox(p, "Show my map position to the other faction",
    "Off: a friend playing the other faction doesn't see you on the map, even with Map position on.",
    function() return ns.shareSettings().defaults.otherFaction == true end,
    function(on) ns.editShare(function(s) s.defaults.otherFaction = on end) end)
  otherFaction:SetPoint("TOPLEFT", gridLast, "BOTTOMLEFT", 0, -10)
  -- One rate for all friends: click to go from live to every minute to every 5 minutes.
  local RATE_TEXT = { [5] = "Map updates: live", [60] = "Map updates: every minute", [300] = "Map updates: every 5 minutes" }
  local NEXT_RATE = { [5] = 60, [60] = 300, [300] = 5 }
  local rate = ui.button(p, RATE_TEXT[5], 240, function(self)
    ns.editShare(function(s) s.mapRate = NEXT_RATE[s.mapRate] or 5 end)
    self:SetText(RATE_TEXT[ns.shareSettings().mapRate])
  end)
  rate:SetPoint("TOPLEFT", otherFaction, "BOTTOMLEFT", 4, -24)
  ui.onRefresh(p, function() rate:SetText(RATE_TEXT[ns.shareSettings().mapRate]) end)
```

In `TABS`, the `sharing` tab's `height = 300` becomes `height = 400`.

- [ ] **Step 10: Run all tests**

Run: `dotnet test companion\WoWCharacterTracker.Tests\WoWCharacterTracker.Tests.csproj -warnaserror --nologo`
Expected: all PASS except the known `RatingTests` changelog failure.

- [ ] **Step 11: Commit**

```bash
git add addon/WoWCharacterTracker/Friends.lua addon/WoWCharacterTracker/Window.lua companion/WoWCharacterTracker.Tests/FriendsLuaTests.cs companion/WoWCharacterTracker.Tests/AddonWindowTests.cs
git commit -m "feat(addon): add map position sharing settings"
```

(with the two trailers from Global Constraints)

---

### Task 2: Watching and positions (FriendMap.lua)

**Files:**
- Create: `addon/WoWCharacterTracker/FriendMap.lua`
- Modify: `addon/WoWCharacterTracker/WoWCharacterTracker.toc` (add `FriendMap.lua` right after `Friends.lua`)
- Modify: `addon/WoWCharacterTracker/Comms.lua:86` (the comment's kind list)
- Test: `companion/WoWCharacterTracker.Tests/FriendMapLuaTests.cs` (new)

**Interfaces:**
- Consumes (Task 1): `ns.onFriendKind`, `ns.allowedFor(...).map/.otherFaction`, `ns.shareSettings().mapRate`, `ns.onlineFriends()` rows `{ tag, account, label, character, classFile, level, faction, realm, contact, blocked, hasAddon }`, `ns.isCharShared`, `ns.comms.send(account, kind, body)`, `FriendsLuaTests.Deliver` (now internal).
- Produces: `ns.setMapOpen(open)` (MapPins calls it), `ns.friendPins()` returning a list sorted by label of `{ tag, label, character, classFile, level, realm, otherRealm, mapID, x, y, age, faded }`, `ns.friendMapTick()` (returns true while anyone watches), calls `ns.friendPinsChanged()` when set (MapPins sets it).

- [ ] **Step 1: Write the failing tests**

Create `companion/WoWCharacterTracker.Tests/FriendMapLuaTests.cs`:

```csharp
using MoonSharp.Interpreter;

namespace WoWCharacterTracker.Tests;

// FriendMap.lua: friends on the world map, through the real Friends.lua and Comms.lua. A
// (JDybeck#1111) is the one moving, B (Zeb#2222) has the map open.
public class FriendMapLuaTests
{
    const string A = "Player-1-0000000A", B = "Player-1-0000000B";
    const int AAccount = 1, BAccount = 2; // each one's game account as the other's friends list has it
    static readonly string[] Files = [.. FriendsLuaTests.AllFiles, "FriendMap.lua"];

    // Where you are, your faction, and whether you're in an instance, per client.
    const string MapStubs = """
        inInstance, myFaction, myMap, myX, myY = false, 'Horde', 1433, 0.4567, 0.1234
        function IsInInstance() return inInstance end
        function UnitFactionGroup() return myFaction, myFaction end
        function GetRealmName() return 'Firemaw' end
        C_Map = {
          GetBestMapForUnit = function() return myMap end,
          GetPlayerMapPosition = function() if not myX then return nil end return { x = myX, y = myY } end,
        }
        """;

    static string FriendsOf(string tag, int account, string guid, string character, string faction = "Horde") =>
        $"{{ {{ tag = '{tag}', account = {account}, guid = '{guid}', char = '{character}', classFile = 'WARRIOR', level = 42, faction = '{faction}', realm = 'Firemaw' }} }}";

    // A shares with Zeb (zebContact), B keeps what JDybeck sends (bContact). aShare goes first in A's share table.
    static (FriendsLuaTests.Client A, FriendsLuaTests.Client B) Pair(string zebContact = "share = true, receive = true, map = true",
        string aShare = "", string bContact = "receive = true", string zebFaction = "Horde")
    {
        var a = new FriendsLuaTests.Client(A, FriendsOf("Zeb#2222", BAccount, B, "Zeb", zebFaction),
            $"{{ share = {{ {aShare} enabled = true, at = 100, contacts = {{ ['Zeb#2222'] = {{ {zebContact} }} }} }}, chars = {{}}, items = {{}} }}", files: Files);
        var b = new FriendsLuaTests.Client(B, FriendsOf("JDybeck#1111", AAccount, A, "Deadbeck"),
            $"{{ share = {{ enabled = true, at = 100, contacts = {{ ['JDybeck#1111'] = {{ {bContact} }} }} }}, chars = {{}}, items = {{}} }}", files: Files);
        a.Lua(MapStubs);
        b.Lua(MapStubs);
        return (a, b);
    }

    // Both run one second at a time and hand over what they sent. Returns the POS bodies A sent.
    static List<string> Pass(FriendsLuaTests.Client a, FriendsLuaTests.Client b, int seconds)
    {
        var bodies = new List<string>();
        for (var i = 0; i < seconds; i++)
        {
            a.Run(1);
            b.Run(1);
            bodies.AddRange(a.Sent().Select(m => m.Get("text").String).Where(t => t.Contains(":POS:")).Select(t => t.Split(':', 6)[5]));
            FriendsLuaTests.Deliver(a, b, AAccount);
            FriendsLuaTests.Deliver(b, a, BAccount);
        }
        return bodies;
    }

    static Table? Pin(FriendsLuaTests.Client b)
    {
        var pin = b.Lua("return ns.friendPins()[1]");
        return pin.IsNil() ? null : pin.Table;
    }

    [Fact]
    public void Map_position_is_off_by_default_so_a_watch_gets_nothing()
    {
        var (a, b) = Pair(zebContact: "share = true, receive = true");
        b.Call("setMapOpen", true);
        Assert.Empty(Pass(a, b, 10));
        Assert.Null(Pin(b));
    }

    [Fact]
    public void An_open_map_shows_where_the_friend_is()
    {
        var (a, b) = Pair();
        b.Call("setMapOpen", true);
        Assert.Equal(["1433,0.457,0.123,5,0"], Pass(a, b, 3));
        var pin = Pin(b)!;
        Assert.Equal("JDybeck#1111", pin.Get("label").String);
        Assert.Equal("Deadbeck", pin.Get("character").String);
        Assert.Equal(1433, pin.Get("mapID").Number);
        Assert.Equal(0.457, pin.Get("x").Number, 3);
        Assert.Equal(0.123, pin.Get("y").Number, 3);
        Assert.False(pin.Get("otherRealm").Boolean);
        Assert.False(pin.Get("faded").Boolean);
    }

    // Ticks every 5 s from the first WATCH (1001): a move goes on the next tick, standing still only every 20 s.
    [Fact]
    public void Moving_sends_again_and_standing_still_only_every_20_seconds()
    {
        var (a, b) = Pair();
        b.Call("setMapOpen", true);
        Pass(a, b, 3);
        Assert.Empty(Pass(a, b, 10));
        a.Lua("myX = 0.5");
        Assert.Single(Pass(a, b, 6));
        Assert.Equal(0.5, Pin(b)!.Get("x").Number, 3);
        Assert.Single(Pass(a, b, 20));
    }

    [Fact]
    public void A_watch_that_is_not_renewed_ends()
    {
        var (a, b) = Pair();
        b.Call("setMapOpen", true);
        Pass(a, b, 3);
        b.Call("setMapOpen", false);
        Pass(a, b, 30);
        a.Lua("myX = 0.9");
        Assert.Empty(Pass(a, b, 15));
    }

    [Theory]
    [InlineData("Alliance", "", "share = true, receive = true, map = true", false)]
    [InlineData("Alliance", "defaults = { otherFaction = true },", "share = true, receive = true, map = true", true)]
    [InlineData("Alliance", "", "share = true, receive = true, map = true, otherFaction = true", true)]
    [InlineData("", "", "share = true, receive = true, map = true", false)] // faction unknown
    public void The_other_faction_gets_nothing_unless_allowed(string zebFaction, string aShare, string zebContact, bool gets)
    {
        var (a, b) = Pair(zebContact, aShare, zebFaction: zebFaction);
        b.Call("setMapOpen", true);
        Assert.Equal(gets ? 1 : 0, Pass(a, b, 3).Count);
        Assert.Equal(gets, Pin(b) is not null);
    }

    [Theory]
    [InlineData("inInstance = true")]
    [InlineData("myX = nil")]
    [InlineData("friends[1].faction = 'Alliance'")]
    [InlineData("ns.friendsCommand('hide')")]
    [InlineData("ns.friendsCommand('off')")]
    [InlineData("ns.friendsCommand('set Zeb#2222 map off')")]
    public void Losing_the_position_or_the_right_to_it_removes_the_pin(string change)
    {
        var (a, b) = Pair();
        b.Call("setMapOpen", true);
        Pass(a, b, 3);
        Assert.NotNull(Pin(b));
        a.Lua(change);
        Assert.Equal([""], Pass(a, b, 6));
        Assert.Null(Pin(b));
        Assert.Empty(Pass(a, b, 10)); // the empty one goes once
    }

    [Fact]
    public void Someone_who_never_got_a_position_never_gets_an_empty_one()
    {
        var (a, b) = Pair(zebContact: "share = true, receive = true");
        b.Call("setMapOpen", true);
        a.Lua("inInstance = true");
        Assert.Empty(Pass(a, b, 10));
    }

    [Fact]
    public void A_slow_rate_gives_a_new_watch_the_last_reading_with_its_age()
    {
        var (a, b) = Pair(aShare: "mapRate = 60,");
        b.Call("setMapOpen", true);
        Assert.Equal(["1433,0.457,0.123,60,0"], Pass(a, b, 3));
        b.Call("setMapOpen", false);
        Pass(a, b, 40); // the watch ends
        a.Lua("myX = 0.9");
        b.Call("setMapOpen", true); // 43 s after the reading
        var again = Pass(a, b, 3);
        Assert.Single(again);
        Assert.StartsWith("1433,0.457,0.123,60,", again[0], StringComparison.Ordinal);
        Assert.InRange(int.Parse(again[0].Split(',')[4]), 40, 46);
        Assert.InRange(Pin(b)!.Get("age").Number, 40, 47);
        Assert.Contains(Pass(a, b, 20), p => p.StartsWith("1433,0.900,", StringComparison.Ordinal)); // a minute after the first reading
    }

    [Fact]
    public void A_pin_fades_and_then_goes_by_the_senders_rate()
    {
        var (a, b) = Pair();
        b.Call("setMapOpen", true);
        Pass(a, b, 3);
        b.Run(31); // nothing more arrives
        Assert.True(Pin(b)!.Get("faded").Boolean);
        b.Run(90);
        Assert.Null(Pin(b));
    }

    [Fact]
    public void A_friend_going_offline_loses_the_pin()
    {
        var (a, b) = Pair();
        b.Call("setMapOpen", true);
        Pass(a, b, 3);
        b.Lua("friends[1].online = false");
        Assert.Null(Pin(b));
    }

    [Fact]
    public void A_position_from_a_friend_we_watch_is_kept()
    {
        var (_, b) = Pair();
        b.Call("setMapOpen", true);
        b.Receive("1:POS:9:1:1:1433,0.5,0.25,60,30", AAccount);
        Assert.Equal(0.25, Pin(b)!.Get("y").Number, 3);
        Assert.InRange(Pin(b)!.Get("age").Number, 30, 31);
    }

    [Theory]
    [InlineData("1433,0.5,0.5,5,0", false)] // no WATCH asked for it
    [InlineData("1433,1.5,0.5,5,0", true)]  // off the map
    [InlineData("1433,0.5,0.5,7,0", true)]  // not a rate
    [InlineData("0,0.5,0.5,5,0", true)]     // no map
    [InlineData("1433,0.5,0.5,5", true)]    // no age
    [InlineData("1433,0.5,0.5,5,0,1", true)]
    public void A_position_that_wasnt_asked_for_or_makes_no_sense_is_dropped(string body, bool watching)
    {
        var (_, b) = Pair();
        if (watching) b.Call("setMapOpen", true);
        b.Receive($"1:POS:9:1:1:{body}", AAccount);
        Assert.Null(Pin(b));
    }

    [Fact]
    public void A_huge_position_is_dropped()
    {
        var (_, b) = Pair();
        b.Call("setMapOpen", true);
        b.Receive("1:POS:9:1:1:" + new string('9', 200) + ",0.5,0.5,5,0", AAccount);
        Assert.Null(Pin(b));
    }

    [Fact]
    public void Nobody_gets_a_watch_unless_we_keep_what_they_send()
    {
        var (_, b) = Pair(bContact: "receive = false");
        b.Call("setMapOpen", true);
        b.Run(1);
        Assert.DoesNotContain(b.Sent(), m => m.Get("text").String.Contains(":WATCH:"));
    }

    [Fact]
    public void Positions_are_never_saved()
    {
        var (a, b) = Pair();
        const string Keys = "local n = 0; for _ in pairs(WoWCharacterTrackerDB) do n = n + 1 end; return n";
        var before = b.Lua(Keys).Number;
        b.Call("setMapOpen", true);
        Pass(a, b, 3);
        Assert.NotNull(Pin(b));
        Assert.Equal(before, b.Lua(Keys).Number);
        Assert.True(b.Lua("return WoWCharacterTrackerDB.friends == nil").Boolean);
    }
}
```

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test companion\WoWCharacterTracker.Tests\WoWCharacterTracker.Tests.csproj -warnaserror --nologo --filter "FullyQualifiedName~FriendMapLuaTests"`
Expected: FAIL, `FriendMap.lua` not found.

- [ ] **Step 3: Write FriendMap.lua**

Create `addon/WoWCharacterTracker/FriendMap.lua`:

```lua
-- Friends on the world map, the logic (MapPins.lua draws). While your world map is open, contacts
-- you keep things from get WATCH every WATCH_EVERY seconds. A contact who allows you Map position
-- answers with POS while that watch lasts: their position, their rate and how old the reading is.
-- Positions live in memory only, never in the saved file, so the app never sees them.
local ADDON, ns = ...

local WATCH_EVERY = 20 -- seconds between two WATCHes to one friend while the map is open
local WATCH_LASTS = 30 -- a watch that isn't renewed ends
local TICK = 5         -- the sender looks at its position this often while anyone watches
local RESEND = 20      -- a position that didn't move still goes this often
local RATES = { [5] = true, [60] = true, [300] = true }

-- Sending
local watchers = {} -- [account] = { untilAt, sent (the reading they got last), sentAt, hadPosition, cleared }
local reading       -- { mapID, x, y, at }: our last position reading
local ticking = false

-- Receiving
local mapOpen = false
local watchSentAt = {} -- [account] = when WATCH last went to them
local positions = {}   -- [account] = { mapID, x, y, rate, readAt }
local watchLoop = false

local function round(v) return math.floor(v * 1000 + 0.5) / 1000 end

local function pinsChanged()
  if ns.friendPinsChanged then ns.friendPinsChanged() end
end

local function online()
  local ok, list = pcall(ns.onlineFriends)
  return ok and list or {}
end

local function friendByAccount(account)
  for _, f in ipairs(online()) do
    if f.account == account then return f end
  end
  return nil
end

-- Sending -------------------------------------------------------------------------------------
-- Our position: mapID, x, y, or nil in an instance or where the game gives none.
local function readPosition()
  if IsInInstance and IsInInstance() then return nil end
  if not (C_Map and C_Map.GetBestMapForUnit and C_Map.GetPlayerMapPosition) then return nil end
  local mapID = C_Map.GetBestMapForUnit("player")
  local pos = mapID and C_Map.GetPlayerMapPosition(mapID, "player")
  if not pos or not pos.x or not pos.y or (pos.x == 0 and pos.y == 0) then return nil end
  return mapID, pos.x, pos.y
end

-- The reading to send: a new one once the rate allows, else the last one; nil without a position.
local function currentReading(now, rate)
  local mapID, x, y = readPosition()
  if not mapID then
    reading = nil
    return nil
  end
  if reading and now - reading.at < rate then return reading end
  reading = { mapID = mapID, x = round(x), y = round(y), at = now }
  return reading
end

-- Battle.net's factionName against either of UnitFactionGroup's names. Unknown is never the same.
local function sameFaction(faction)
  if type(faction) ~= "string" or faction == "" or not UnitFactionGroup then return false end
  local english, localized = UnitFactionGroup("player")
  return faction == english or faction == localized
end

-- f (a row of ns.onlineFriends) may get our position right now.
local function mayGet(f)
  local s = ns.shareSettings()
  if not s.enabled or s.blocked[f.tag] then return false end
  local contact = s.contacts[f.tag]
  if not (contact and contact.share) then return false end
  if not ns.isCharShared(s, UnitGUID("player")) then return false end
  local allowed = ns.allowedFor(s, contact)
  return allowed.map and (allowed.otherFaction or sameFaction(f.faction))
end

local function moved(r, last)
  return r.mapID ~= last.mapID or r.x ~= last.x or r.y ~= last.y
end

-- r nil: one empty POS, only if they had a position from us. Else the reading, when it's new to
-- them and moved, or RESEND after the last one they got.
local function sendTo(account, w, r, rate, now)
  if not r then
    if w.hadPosition and not w.cleared then
      w.cleared, w.sent = true, nil
      ns.comms.send(account, "POS", "")
    end
    return
  end
  if w.sent == r then return end
  if w.sent and not moved(r, w.sent) and now - w.sentAt < RESEND then return end
  w.sent, w.sentAt, w.hadPosition, w.cleared = r, now, true, false
  ns.comms.send(account, "POS", string.format("%d,%.3f,%.3f,%d,%d", r.mapID, r.x, r.y, rate, now - r.at))
end

-- Each watcher gets what they may. True while anyone still watches.
function ns.friendMapTick()
  local now, rate = time(), ns.shareSettings().mapRate
  local r, looked, any = nil, false, false
  for account, w in pairs(watchers) do
    if now > w.untilAt then
      watchers[account] = nil
    else
      any = true
      if not looked then r, looked = currentReading(now, rate), true end
      local f = friendByAccount(account)
      sendTo(account, w, (f and mayGet(f)) and r or nil, rate, now)
    end
  end
  return any
end

local function keepTicking()
  if ticking then return end
  ticking = true
  C_Timer.After(TICK, function()
    ticking = false
    if ns.friendMapTick() then keepTicking() end
  end)
end

-- A contact has their map open: answer now, then every TICK while the watch lasts.
ns.onFriendKind("WATCH", function(f)
  local w = watchers[f.account] or {}
  w.untilAt = time() + WATCH_LASTS
  watchers[f.account] = w
  ns.friendMapTick()
  keepTicking()
end)

-- Receiving -----------------------------------------------------------------------------------
-- Kept only from a contact we keep things from, and only while our WATCH to them is recent.
ns.onFriendKind("POS", function(f, contact, body)
  if not contact.receive then return end
  local asked = watchSentAt[f.account]
  if not asked or time() - asked > WATCH_LASTS then return end
  if body == nil or body == "" then
    if positions[f.account] then
      positions[f.account] = nil
      pinsChanged()
    end
    return
  end
  if #body > 64 then return end
  local mapID, x, y, rate, age = string.match(body, "^(%d+),([%d%.]+),([%d%.]+),(%d+),(%d+)$")
  mapID, x, y, rate, age = tonumber(mapID), tonumber(x), tonumber(y), tonumber(rate), tonumber(age)
  if not (mapID and x and y and rate and age) or mapID <= 0 or x < 0 or x > 1 or y < 0 or y > 1 or not RATES[rate] then return end
  positions[f.account] = { mapID = mapID, x = x, y = y, rate = rate, readAt = time() - age }
  pinsChanged()
end)

-- Faded after the first, gone after the second (seconds), by the sender's rate.
local function limits(rate) return math.max(30, 2 * rate), math.max(120, 4 * rate) end

-- What MapPins.lua draws: friends online here with a position that isn't too old, by label.
function ns.friendPins()
  local now, out, rows = time(), {}, {}
  for _, f in ipairs(online()) do rows[f.account] = f end
  local myRealm = GetRealmName and GetRealmName()
  for account, p in pairs(positions) do
    local fade, gone = limits(p.rate)
    local age = math.max(0, now - p.readAt)
    local f = rows[account]
    if not f or age > gone then
      positions[account] = nil
    else
      out[#out + 1] = { tag = f.tag, label = f.label, character = f.character, classFile = f.classFile, level = f.level,
        realm = f.realm, otherRealm = f.realm ~= nil and myRealm ~= nil and f.realm ~= myRealm,
        mapID = p.mapID, x = p.x, y = p.y, age = age, faded = age > fade }
    end
  end
  table.sort(out, function(x, y) return x.label < y.label end)
  return out
end

-- WATCH to every contact online here we keep things from, at most every WATCH_EVERY seconds each
-- (a second less, so the renewal timer never just misses it).
local function sendWatches()
  if not ns.shareSettings().enabled then return end
  local now = time()
  for _, f in ipairs(online()) do
    local c, last = f.contact, watchSentAt[f.account]
    if c and c.receive and not f.blocked and f.hasAddon ~= false and (not last or now - last >= WATCH_EVERY - 1) then
      if ns.comms.send(f.account, "WATCH", "") then watchSentAt[f.account] = now end
    end
  end
end

local function keepWatching()
  if watchLoop then return end
  watchLoop = true
  C_Timer.After(WATCH_EVERY, function()
    watchLoop = false
    if not mapOpen then return end
    sendWatches()
    keepWatching()
  end)
end

-- MapPins.lua: the world map opened or closed. Closing sends nothing, the watches just run out.
function ns.setMapOpen(open)
  mapOpen = open == true
  if mapOpen then
    sendWatches()
    keepWatching()
  end
end
```

In `WoWCharacterTracker.toc`, after the `Friends.lua` line:

```
FriendMap.lua
```

In `Comms.lua:86`, the comment's list becomes `-- Queues kind (HI, HEY, REQ, DATA, NO, PING, PONG, WATCH, POS) with body for one game account. False when`.

- [ ] **Step 4: Run them**

Run: `dotnet test companion\WoWCharacterTracker.Tests\WoWCharacterTracker.Tests.csproj -warnaserror --nologo --filter "FullyQualifiedName~FriendMapLuaTests"`
Expected: all PASS. If a timing test is off by a tick, check the tick times in its comment against `Pass` (both clocks start at 1000 and move 1 s per step) before touching the code: the rules in the spec are what must hold, the test windows can move.

- [ ] **Step 5: Run all tests**

Run: `dotnet test companion\WoWCharacterTracker.Tests\WoWCharacterTracker.Tests.csproj -warnaserror --nologo`
Expected: all PASS except the known `RatingTests` changelog failure (`Addon_file_compiles` now covers FriendMap.lua too).

- [ ] **Step 6: Commit**

```bash
git add addon/WoWCharacterTracker/FriendMap.lua addon/WoWCharacterTracker/WoWCharacterTracker.toc addon/WoWCharacterTracker/Comms.lua companion/WoWCharacterTracker.Tests/FriendMapLuaTests.cs
git commit -m "feat(addon): send friends map positions while their map is open"
```

---

### Task 3: Pins on the world map (MapPins.lua, MapPins.xml)

**Files:**
- Create: `addon/WoWCharacterTracker/MapPins.lua`, `addon/WoWCharacterTracker/MapPins.xml`
- Modify: `addon/WoWCharacterTracker/Shared.lua` (add `ns.ago`), `addon/WoWCharacterTracker/Window.lua:215-221` (use it), `addon/WoWCharacterTracker/WoWCharacterTracker.toc`, `addon/WoWCharacterTracker/WoWCharacterTracker.lua` (`on.ADDON_LOADED`, `on.PLAYER_LOGIN`)
- Test: `companion/WoWCharacterTracker.Tests/MapPinsLuaTests.cs` (new)

**Interfaces:**
- Consumes (Task 2): `ns.friendPins()`, `ns.setMapOpen(open)`; sets `ns.friendPinsChanged`.
- Produces: `ns.pinSpot(pin, shownMapID)` returning `x, y` or nil; `ns.attachMapPins()` returning true when it attached; `ns.ago(at)` ("just now", "3m ago", "2h ago", "1d ago"); global `WoWCharacterTrackerFriendPinMixin`; XML template `WoWCharacterTrackerFriendPinTemplate`.

- [ ] **Step 1: Write the failing tests**

Create `companion/WoWCharacterTracker.Tests/MapPinsLuaTests.cs`:

```csharp
using MoonSharp.Interpreter;

namespace WoWCharacterTracker.Tests;

// MapPins.lua with a stand-in world map: where pins land, and what the provider puts on the map.
public class MapPinsLuaTests
{
    // Zone 1433 sits at 1000..1100 (x) and 0..100 (y) on continent 0, which spans 0..2000 on map 13.
    const string Map = """
        Enum = { UIMapType = { Cosmic = 0, World = 1, Continent = 2, Zone = 3, Dungeon = 4, Micro = 5, Orphan = 6 } }
        function CreateVector2D(x, y) return { x = x, y = y } end
        maps = { [1433] = { mapType = 3, name = 'Redridge Mountains' }, [13] = { mapType = 2 }, [1429] = { mapType = 3 }, [1581] = { mapType = 4 } }
        C_Map = {
          GetMapInfo = function(id) return maps[id] end,
          GetWorldPosFromMapPos = function(id, pos) return 0, { x = 1000 + pos.x * 100, y = pos.y * 100 } end,
          GetMapPosFromWorldPos = function(continent, world, shown)
            if shown == 13 then return 13, { x = world.x / 2000, y = world.y / 2000 } end
            return shown, { x = world.x / 100, y = world.y / 100 } -- another zone: far off it
          end,
        }
        function Mixin(t, ...) for i = 1, select('#', ...) do for k, v in pairs((select(i, ...))) do t[k] = v end end return t end
        function CreateFromMixins(...) return Mixin({}, ...) end
        MapCanvasPinMixin = { CheckMouseButtonPassthrough = function() error('SetPassThroughButtons') end }
        MapCanvasDataProviderMixin = { OnAdded = function(self, map) self.map = map end, GetMap = function(self) return self.map end }
        acquired = {}
        WorldMapFrame = { mapID = 1433, hooks = {}, shown = false,
          AddDataProvider = function(self, p) self.provider = p; p:OnAdded(self) end,
          HookScript = function(self, event, fn) self.hooks[event] = fn end,
          IsShown = function(self) return self.shown end,
          GetMapID = function(self) return self.mapID end,
          RemoveAllPinsByTemplate = function() acquired = {} end,
          AcquirePin = function(self, template, friend, x, y) acquired[#acquired + 1] = { template = template, friend = friend, x = x, y = y } end,
        }
        """;

    static Script Load()
    {
        var script = new Script();
        script.DoString(AddonLuaTests.FakeTimers);
        script.DoString(Map);
        var ns = new Table(script);
        script.Globals["ns"] = ns;
        script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, "MapPins.lua")), codeFriendlyName: "MapPins.lua"), "WoWCharacterTracker", ns);
        script.DoString("""
            opened = nil
            ns.setMapOpen = function(open) opened = open end
            ns.friendPins = function() return { { label = 'Zeb#2222', mapID = 1433, x = 0.5, y = 0.5, age = 3 } } end
            """);
        return script;
    }

    [Theory]
    [InlineData(1433, 0.5, 0.5)]   // the friend's own map
    [InlineData(13, 0.525, 0.025)] // the continent above it
    public void A_pin_lands_on_its_map_and_the_maps_above(int shown, double x, double y)
    {
        var script = Load();
        var spot = script.DoString($"return {{ ns.pinSpot({{ mapID = 1433, x = 0.5, y = 0.5 }}, {shown}) }}").Table;
        Assert.Equal(x, spot.Get(1).Number, 3);
        Assert.Equal(y, spot.Get(2).Number, 3);
    }

    [Theory]
    [InlineData(1429)] // another zone
    [InlineData(1581)] // a dungeon map
    [InlineData(999)]  // a map the game doesn't know
    public void A_pin_stays_off_other_zones_and_dungeon_maps(int shown) =>
        Assert.True(Load().DoString($"return ns.pinSpot({{ mapID = 1433, x = 0.5, y = 0.5 }}, {shown})").IsNil());

    [Fact]
    public void The_pins_join_the_world_map_and_say_when_it_opens()
    {
        var script = Load();
        Assert.True(script.DoString("return ns.attachMapPins()").Boolean);
        Assert.False(script.DoString("return ns.attachMapPins()").Boolean); // once
        script.DoString("WorldMapFrame.shown = true; WorldMapFrame.hooks.OnShow()");
        Assert.True(script.DoString("return opened").Boolean);
        script.DoString("WorldMapFrame.provider:RefreshAllData()");
        Assert.Equal(1, script.DoString("return #acquired").Number);
        Assert.Equal("WoWCharacterTrackerFriendPinTemplate", script.DoString("return acquired[1].template").String);
        Assert.Equal(0.5, script.DoString("return acquired[1].x").Number, 3);
        script.DoString("WorldMapFrame.mapID = 1581; WorldMapFrame.provider:RefreshAllData()");
        Assert.Equal(0, script.DoString("return #acquired").Number);
        script.DoString("WorldMapFrame.hooks.OnHide()");
        Assert.False(script.DoString("return opened").Boolean);
    }

    // The map's AcquirePin calls this on every pin; Blizzard's calls SetPassThroughButtons, ours nothing.
    [Fact]
    public void Our_pins_never_set_pass_through_buttons()
    {
        var script = Load();
        script.DoString("ns.attachMapPins()");
        script.DoString("WoWCharacterTrackerFriendPinMixin.CheckMouseButtonPassthrough({}, 'RightButton')");
    }

    [Fact]
    public void Nothing_happens_without_a_world_map()
    {
        var script = Load();
        script.DoString("WorldMapFrame = nil");
        Assert.False(script.DoString("return ns.attachMapPins()").Boolean);
    }

    [Theory]
    [InlineData(10, "just now")]
    [InlineData(180, "3m ago")]
    [InlineData(7200, "2h ago")]
    [InlineData(90000, "1d ago")]
    public void Ages_read_like_the_window_shows_them(int seconds, string text)
    {
        var script = new Script();
        var ns = new Table(script);
        script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, "Shared.lua"))), "WoWCharacterTracker", ns);
        script.DoString("function time() return 100000 end");
        Assert.Equal(text, script.Call(ns.Get("ago"), 100000 - seconds).String);
    }
}
```

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test companion\WoWCharacterTracker.Tests\WoWCharacterTracker.Tests.csproj -warnaserror --nologo --filter "FullyQualifiedName~MapPinsLuaTests"`
Expected: FAIL, `MapPins.lua` not found and `ns.ago` is nil.

- [ ] **Step 3: Move `ago` to Shared.lua**

Append to `Shared.lua`:

```lua
-- "just now", "3m ago", "2h ago" or "1d ago" since at (a time()).
function ns.ago(at)
  local s = math.max(0, time() - (at or time()))
  if s < 60 then return "just now" end
  if s < 3600 then return math.floor(s / 60) .. "m ago" end
  if s < 86400 then return math.floor(s / 3600) .. "h ago" end
  return math.floor(s / 86400) .. "d ago"
end
```

In `Window.lua`, replace the local `ago` function (lines 215-221) with:

```lua
local ago = ns.ago
```

(If `DAY` is then unused in Window.lua, leave it only if something else uses it; the build has no Lua lint, so check with a search.)

- [ ] **Step 4: Write MapPins.xml and MapPins.lua**

Create `addon/WoWCharacterTracker/MapPins.xml`:

```xml
<Ui xmlns="http://www.blizzard.com/wow/ui/">
  <!-- A friend's pin on the world map (MapPins.lua): mouse-over for the tooltip, no clicks. -->
  <Frame name="WoWCharacterTrackerFriendPinTemplate" mixin="WoWCharacterTrackerFriendPinMixin" enableMouse="false" enableMouseMotion="true" virtual="true"/>
</Ui>
```

Create `addon/WoWCharacterTracker/MapPins.lua`:

```lua
-- Friends' pins on the world map: drawing only, FriendMap.lua knows the positions. Our own data
-- provider and pin template on WorldMapFrame, nothing of Blizzard's replaced. The map's AcquirePin
-- sets right-click pass-through on every pin (SetPassThroughButtons, a known taint source), so our
-- pins skip that and take mouse-over only: clicks reach the map on their own.
local ADDON, ns = ...

local TEMPLATE = "WoWCharacterTrackerFriendPinTemplate"
local SIZE = 14
local REFRESH = 5 -- seconds between redraws while the map is open, so pins fade and go
local CIRCLE = "Interface\\CharacterFrame\\TempPortraitAlphaMask"
local WHITE = "Interface\\Buttons\\WHITE8X8"

local provider
local redrawing = false

-- Where a pin falls on the shown map: x, y in 0..1, or nil. Through world coordinates for the maps
-- above the friend's (continent, world); never on dungeon maps.
function ns.pinSpot(pin, shownMapID)
  if pin.mapID == shownMapID then return pin.x, pin.y end
  if not (C_Map and C_Map.GetMapInfo and C_Map.GetWorldPosFromMapPos and C_Map.GetMapPosFromWorldPos and CreateVector2D) then return nil end
  local info = C_Map.GetMapInfo(shownMapID)
  local dungeon = (Enum and Enum.UIMapType and Enum.UIMapType.Dungeon) or 4
  if not info or (info.mapType or dungeon) >= dungeon then return nil end
  local continentID, world = C_Map.GetWorldPosFromMapPos(pin.mapID, CreateVector2D(pin.x, pin.y))
  if not continentID or not world then return nil end
  local _, spot = C_Map.GetMapPosFromWorldPos(continentID, world, shownMapID)
  if not spot or spot.x < 0 or spot.x > 1 or spot.y < 0 or spot.y > 1 then return nil end
  return spot.x, spot.y
end

local function classColor(file)
  local c = file and RAID_CLASS_COLORS and RAID_CLASS_COLORS[file]
  if c then return c.r, c.g, c.b end
  return 0.8, 0.8, 0.8
end

-- A round texture inset from the pin's edges, in one color.
local function circle(pin, layer, inset, r, g, b)
  local t = pin:CreateTexture(nil, layer)
  t:SetTexture(WHITE)
  t:SetVertexColor(r, g, b)
  t:SetPoint("TOPLEFT", inset, -inset)
  t:SetPoint("BOTTOMRIGHT", -inset, inset)
  local mask = pin:CreateMaskTexture()
  mask:SetTexture(CIRCLE, "CLAMPTOBLACKADDITIVE", "CLAMPTOBLACKADDITIVE")
  mask:SetAllPoints(t)
  t:AddMaskTexture(mask)
  return t
end

-- Filled in once the map's own mixins exist (attach); the XML template names it.
WoWCharacterTrackerFriendPinMixin = {}
local Pin = WoWCharacterTrackerFriendPinMixin

local function buildPin()
  Mixin(Pin, MapCanvasPinMixin)

  function Pin:OnLoad()
    self:SetSize(SIZE, SIZE)
    self:UseFrameLevelType("PIN_FRAME_LEVEL_GROUP_MEMBER")
    self:SetScalingLimits(1, 1.0, 1.2)
    self.border = circle(self, "BACKGROUND", 0, 0, 0, 0)
    self.fill = circle(self, "ARTWORK", 2, 1, 1, 1)
    self.hole = circle(self, "OVERLAY", 5, 0, 0, 0) -- a ring instead of a dot: another realm
  end

  function Pin:OnAcquired(friend, x, y)
    self.friend = friend
    self:SetPosition(x, y)
    self.fill:SetVertexColor(classColor(friend.classFile))
    self.hole:SetShown(friend.otherRealm == true)
    self:SetAlpha(friend.faded and 0.45 or 1)
  end

  -- Blizzard's sets pass-through buttons (see the top); ours has no clicks to pass.
  function Pin:CheckMouseButtonPassthrough() end

  function Pin:OnMouseEnter()
    local f = self.friend
    if not (f and GameTooltip) then return end
    GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
    GameTooltip:SetText(f.label, 1, 1, 1)
    local className = LOCALIZED_CLASS_NAMES_MALE and f.classFile and LOCALIZED_CLASS_NAMES_MALE[f.classFile]
    local r, g, b = classColor(f.classFile)
    GameTooltip:AddLine(string.format("%s, level %s%s", f.character or "?", tostring(f.level or "?"), className and (" " .. className) or ""), r, g, b)
    local info = C_Map.GetMapInfo and C_Map.GetMapInfo(f.mapID)
    if info and info.name then GameTooltip:AddLine(info.name, 0.8, 0.8, 0.8) end
    if f.otherRealm then GameTooltip:AddLine("On another realm: " .. (f.realm or "?"), 1, 0.6, 0.2) end
    GameTooltip:AddLine(ns.ago(time() - (f.age or 0)), 0.6, 0.6, 0.6)
    GameTooltip:Show()
  end

  function Pin:OnMouseLeave()
    if GameTooltip then GameTooltip:Hide() end
  end
end

local function buildProvider()
  local p = CreateFromMixins(MapCanvasDataProviderMixin)
  function p:RemoveAllData() self:GetMap():RemoveAllPinsByTemplate(TEMPLATE) end
  function p:RefreshAllData()
    self:RemoveAllData()
    local map = self:GetMap()
    local shown = map and map:GetMapID()
    if not shown then return end
    for _, friend in ipairs(ns.friendPins and ns.friendPins() or {}) do
      local x, y = ns.pinSpot(friend, shown)
      if x then map:AcquirePin(TEMPLATE, friend, x, y) end
    end
  end
  return p
end

local function redraw()
  if not (provider and WorldMapFrame:IsShown()) then
    redrawing = false
    return
  end
  provider:RefreshAllData()
  C_Timer.After(REFRESH, redraw)
end

local function startRedrawing()
  if redrawing then return end
  redrawing = true
  C_Timer.After(REFRESH, redraw)
end

-- FriendMap.lua: a position came or went.
function ns.friendPinsChanged()
  if provider and WorldMapFrame and WorldMapFrame:IsShown() then provider:RefreshAllData() end
end

-- Adds the pins to the world map once it exists (PLAYER_LOGIN, or when Blizzard_WorldMap loads).
function ns.attachMapPins()
  if provider or not (WorldMapFrame and WorldMapFrame.AddDataProvider and MapCanvasDataProviderMixin and MapCanvasPinMixin
    and Mixin and CreateFromMixins) then return false end
  buildPin()
  provider = buildProvider()
  WorldMapFrame:AddDataProvider(provider)
  WorldMapFrame:HookScript("OnShow", function()
    ns.setMapOpen(true)
    startRedrawing()
  end)
  WorldMapFrame:HookScript("OnHide", function() ns.setMapOpen(false) end)
  if WorldMapFrame:IsShown() then ns.setMapOpen(true) end
  return true
end
```

In `WoWCharacterTracker.toc`, after `FriendMap.lua`:

```
MapPins.lua
MapPins.xml
```

In `WoWCharacterTracker.lua`, the start of `on.ADDON_LOADED`:

```lua
function on.ADDON_LOADED(name)
  if name == "Blizzard_WorldMap" then return ns.attachMapPins() end -- loaded on demand on some clients
  if name ~= ADDON then return end
```

and in `on.PLAYER_LOGIN`, after `ns.createMinimapButton()`:

```lua
  ns.attachMapPins() -- friends on the world map (MapPins.lua)
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test companion\WoWCharacterTracker.Tests\WoWCharacterTracker.Tests.csproj -warnaserror --nologo --filter "FullyQualifiedName~MapPinsLuaTests|FullyQualifiedName~AddonWindowTests|FullyQualifiedName~AddonLuaTests"`
Expected: all PASS (the window tests prove `ago` still reads the same there).

- [ ] **Step 6: Run all tests**

Run: `dotnet test companion\WoWCharacterTracker.Tests\WoWCharacterTracker.Tests.csproj -warnaserror --nologo`
Expected: all PASS except the known `RatingTests` changelog failure.

- [ ] **Step 7: Commit**

```bash
git add addon/WoWCharacterTracker/MapPins.lua addon/WoWCharacterTracker/MapPins.xml addon/WoWCharacterTracker/Shared.lua addon/WoWCharacterTracker/Window.lua addon/WoWCharacterTracker/WoWCharacterTracker.toc addon/WoWCharacterTracker/WoWCharacterTracker.lua companion/WoWCharacterTracker.Tests/MapPinsLuaTests.cs
git commit -m "feat(addon): show friends as pins on the world map"
```

---

### Task 4: Map settings in the app

**Files:**
- Modify: `companion/WoWCharacterTracker/Models/Friends.cs`
- Modify: `companion/WoWCharacterTracker/Services/FriendData.cs:64-87` (`ReadShare`)
- Modify: `companion/WoWCharacterTracker/Services/AddonInstaller.cs:141-157` (`ShareLua`)
- Modify: `companion/WoWCharacterTracker/ViewModels/FriendsViewModel.cs` (`RaiseSharing`, general properties, `General`, `FriendContactRow`)
- Modify: `companion/WoWCharacterTracker/Views/FriendsPage.xaml:155-164`
- Test: `companion/WoWCharacterTracker.Tests/FriendDataTests.cs`, `companion/WoWCharacterTracker.Tests/FriendsViewModelTests.cs`

**Interfaces:**
- Consumes (Task 1): the addon's `share.defaults.map/otherFaction`, `contacts[tag].map/otherFaction`, `share.mapRate`.
- Produces: `GeneralCategories.Map`, `.OtherFaction`; `ShareContact.Map`, `.OtherFaction` (`bool?`); `ShareSettings.MapRate` (int, default 5), `ShareSettings.MapRates` (`[5, 60, 300]`); VM `ShareMap`, `ShareOtherFaction`, `IsMapLive`, `IsMapMinute`, `IsMapFiveMinutes`, `MapRateCommand` (parameter "5", "60" or "300"); chip keys `map` and `otherFaction`.

- [ ] **Step 1: Write the failing tests**

Add to `FriendDataTests.cs`:

```csharp
    [Fact]
    public void Map_settings_are_off_and_live_unless_set()
    {
        var empty = FriendData.ReadShare(Lua("{ at = 5 }"))!;
        Assert.False(empty.Defaults.Map);
        Assert.False(empty.Defaults.OtherFaction);
        Assert.Equal(5, empty.MapRate);
        Assert.Equal(5, FriendData.ReadShare(Lua("{ at = 5, mapRate = 7 }"))!.MapRate); // not a step
        var share = FriendData.ReadShare(Lua("{ at = 5, mapRate = 300, defaults = { map = true }, contacts = { ['Zeb#2222'] = { share = true, map = false, otherFaction = true } } }"))!;
        Assert.True(share.Defaults.Map);
        Assert.Equal(300, share.MapRate);
        Assert.False(share.Contacts["Zeb#2222"].Map);
        Assert.True(share.Contacts["Zeb#2222"].OtherFaction);
    }

    [Fact]
    public void The_addon_reads_the_map_settings_the_app_wrote()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wct-share-" + Guid.NewGuid().ToString("N"));
        try
        {
            var s = new FlavorSettings();
            s.TakeShare(FriendData.ReadShare(Lua("{ enabled = true, at = 200, mapRate = 60, defaults = { otherFaction = true }, contacts = { ['Zeb#2222'] = { share = true, map = true }, ['Kalle#3333'] = { share = true, otherFaction = false } } }")));
            var file = WriteSettings(dir, s);
            var c = new FriendsLuaTests.Client("Player-1-0000000A", "{}", db: "{ share = { enabled = false, at = 100 } }");
            c.Lua(File.ReadAllText(file));
            Assert.Equal(60, c.Lua("return ns.shareSettings().mapRate").Number);
            Assert.True(c.Lua("local s = ns.shareSettings(); return ns.allowedFor(s, s.contacts['Zeb#2222']).map").Boolean);
            Assert.True(c.Lua("local s = ns.shareSettings(); return ns.allowedFor(s, s.contacts['Zeb#2222']).otherFaction").Boolean);
            Assert.False(c.Lua("local s = ns.shareSettings(); return ns.allowedFor(s, s.contacts['Kalle#3333']).otherFaction").Boolean);
            Assert.False(c.Lua("local s = ns.shareSettings(); return ns.allowedFor(s, s.contacts['Kalle#3333']).map").Boolean);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // Config.json saved before map sharing has none of it: live, and both off.
    [Fact]
    public void An_old_config_without_map_settings_reads_as_live_and_off()
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<ShareSettings>(
            """{"Enabled":true,"NewChars":true,"Defaults":{"Gear":true},"Chars":{},"Contacts":{"Zeb#2222":{"Share":true,"Receive":true}},"Blocked":[],"At":5}""")!;
        Assert.Equal(5, old.MapRate);
        Assert.False(old.Defaults.Map);
        Assert.Null(old.Contacts["Zeb#2222"].Map);
    }
```

Add to `FriendsViewModelTests.cs`:

```csharp
    [Fact]
    public void Map_position_is_a_category_and_the_rate_a_choice()
    {
        var f = new Fixture("{ enabled = true, at = 100, contacts = { ['Zeb#2222'] = { share = true, receive = true } } }");
        Assert.False(f.Vm.ShareMap);
        Assert.False(f.Vm.ShareOtherFaction);
        Assert.True(f.Vm.IsMapLive);
        var zeb = f.Vm.ContactRows.Single(c => c.Tag == "Zeb#2222");
        Assert.Equal("Map position: like all friends (off)", zeb.Categories.Single(c => c.Key == "map").Text);
        Assert.Equal("Map for the other faction: like all friends (off)", zeb.Categories.Single(c => c.Key == "otherFaction").Text);
        zeb.CycleCommand.Execute("map");
        Assert.True(f.Settings.Share!.Contacts["Zeb#2222"].Map);
        zeb.CycleCommand.Execute("otherFaction");
        Assert.True(f.Settings.Share.Contacts["Zeb#2222"].OtherFaction);
        f.Vm.ShareMap = true;
        Assert.True(f.Settings.Share.Defaults.Map);
        f.Vm.MapRateCommand.Execute("60");
        Assert.Equal(60, f.Settings.Share.MapRate);
        Assert.True(f.Vm.IsMapMinute);
        f.Vm.MapRateCommand.Execute("7"); // not a step
        Assert.Equal(60, f.Settings.Share.MapRate);
    }
```

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test companion\WoWCharacterTracker.Tests\WoWCharacterTracker.Tests.csproj -warnaserror --nologo --filter "FullyQualifiedName~FriendDataTests|FullyQualifiedName~FriendsViewModelTests"`
Expected: build FAILS (`Map`, `MapRate`, `ShareMap` don't exist).

- [ ] **Step 3: Models**

`Models/Friends.cs`, replace the three records:

```csharp
// What all friends get, unless set for one of them. Gold and map position stay off until turned on.
// OtherFaction isn't data: on lets a friend playing the other faction see your map position.
record GeneralCategories(bool Gear = true, bool Talents = true, bool Location = true, bool Gold = false, bool Professions = true,
    bool Map = false, bool OtherFaction = false);

// One contact: whether we share with them and keep what they send, and their own categories (null = the general choice).
record ShareContact(bool Share, bool Receive, bool? Gear = null, bool? Talents = null, bool? Location = null, bool? Gold = null, bool? Professions = null,
    bool? Map = null, bool? OtherFaction = null);

// Sharing with friends, the same shape as the addon's `share` (Friends.lua). Changed in the app or in
// game, the newest At wins. MapRate: seconds between two map position readings, one of MapRates.
record ShareSettings(bool Enabled, bool NewChars, GeneralCategories Defaults, Dictionary<string, bool> Chars,
    Dictionary<string, ShareContact> Contacts, HashSet<string> Blocked, long At, int MapRate = 5)
{
    public static readonly int[] MapRates = [5, 60, 300]; // live, every minute, every 5 minutes
    public static ShareSettings Empty => new(true, true, new(), [], [], [], 0); // on, as in the addon: only allowing a request shares
}
```

- [ ] **Step 4: Reading and writing**

`FriendData.ReadShare`:

```csharp
        if (d is not null)
            general = new(Flag(d, "gear") ?? general.Gear, Flag(d, "talents") ?? general.Talents,
                Flag(d, "location") ?? general.Location, Flag(d, "gold") ?? general.Gold, Flag(d, "professions") ?? general.Professions,
                Flag(d, "map") ?? general.Map, Flag(d, "otherFaction") ?? general.OtherFaction);
```

the contact line:

```csharp
                contacts[p.Key.String] = new(Flag(c, "share") == true, Flag(c, "receive") == true,
                    Flag(c, "gear"), Flag(c, "talents"), Flag(c, "location"), Flag(c, "gold"), Flag(c, "professions"),
                    Flag(c, "map"), Flag(c, "otherFaction"));
```

and the end:

```csharp
        var blocked = Pairs(t.Get("blocked")).Where(p => p.Value.CastToBool()).Select(p => p.Key.String).ToHashSet();
        var rate = (int)Number(t.Get("mapRate"));
        return new(Flag(t, "enabled") ?? true, Flag(t, "newChars") ?? true, general, chars, contacts, blocked, (long)Number(t.Get("at")),
            ShareSettings.MapRates.Contains(rate) ? rate : 5);
```

`AddonInstaller.ShareLua`, the contact line gets `+ Opt("map", c.Value.Map) + Opt("otherFaction", c.Value.OtherFaction)` before `+ " }, "`, and the return becomes:

```csharp
        return $"  share = {{ enabled = {B(share.Enabled)}, newChars = {B(share.NewChars)}, at = {share.At}, mapRate = {share.MapRate},\n"
               + $"    defaults = {{ gear = {B(d.Gear)}, talents = {B(d.Talents)}, location = {B(d.Location)}, gold = {B(d.Gold)}, professions = {B(d.Professions)}, map = {B(d.Map)}, otherFaction = {B(d.OtherFaction)} }},\n"
               + $"    contacts = {{ {contacts}}},\n"
               + $"    chars = {{ {chars}}},\n"
               + $"    blocked = {{ {blocked}}} }},\n";
```

- [ ] **Step 5: The view model**

`FriendsViewModel.cs`: add `using System.Globalization;` at the top. In `RaiseSharing`, add `nameof(ShareMap), nameof(ShareOtherFaction), nameof(IsMapLive), nameof(IsMapMinute), nameof(IsMapFiveMinutes)` to the array. After `ShareGold`:

```csharp
    public bool ShareMap { get => Share.Defaults.Map; set => Edit(s => s with { Defaults = s.Defaults with { Map = value } }); }
    public bool ShareOtherFaction { get => Share.Defaults.OtherFaction; set => Edit(s => s with { Defaults = s.Defaults with { OtherFaction = value } }); }

    // How often friends' maps get your position: one rate for all friends.
    public bool IsMapLive => Share.MapRate == 5;
    public bool IsMapMinute => Share.MapRate == 60;
    public bool IsMapFiveMinutes => Share.MapRate == 300;
    public ICommand MapRateCommand => mapRate ??= new RelayCommand(p =>
    {
        if (p is string text && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rate) && ShareSettings.MapRates.Contains(rate))
            Edit(s => s with { MapRate = rate });
    });
    ICommand? mapRate;
```

`General`:

```csharp
    internal bool General(string category) => category switch
    {
        "gear" => Share.Defaults.Gear,
        "talents" => Share.Defaults.Talents,
        "location" => Share.Defaults.Location,
        "professions" => Share.Defaults.Professions,
        "map" => Share.Defaults.Map,
        "otherFaction" => Share.Defaults.OtherFaction,
        _ => Share.Defaults.Gold,
    };
```

`FriendContactRow`: after `Gold`:

```csharp
    public bool? Map { get => C.Map; set => owner.EditContact(Tag, c => c with { Map = value }); }
    public bool? OtherFaction { get => C.OtherFaction; set => owner.EditContact(Tag, c => c with { OtherFaction = value }); }
```

`Names` and `Own`:

```csharp
    static readonly (string Key, string Name)[] Names = [("gear", "Gear"), ("talents", "Talents"), ("location", "Location"), ("professions", "Professions"), ("gold", "Gold"),
        ("map", "Map position"), ("otherFaction", "Map for the other faction")];
    bool? Own(string key) => key switch
    {
        "gear" => Gear, "talents" => Talents, "location" => Location, "professions" => Professions,
        "map" => Map, "otherFaction" => OtherFaction, _ => Gold,
    };
```

and in `CycleCommand`'s switch, before `default`:

```csharp
            case "map": Map = next; break;
            case "otherFaction": OtherFaction = next; break;
```

- [ ] **Step 6: The page**

`FriendsPage.xaml`, in the WHAT FRIENDS GET `WrapPanel`, after the Gold chip:

```xml
            <CheckBox Style="{StaticResource OptionChip}" Content="Map position" IsChecked="{Binding ShareMap}" Margin="0,0,6,6"
                      ToolTip="Your spot on their world map, while they have it open. Off until you turn it on"/>
```

and after that `WrapPanel`, before the FRIENDS label:

```xml
          <TextBlock Style="{StaticResource FactLabel}" Text="MAP POSITION"/>
          <TextBlock FontSize="12" Foreground="{StaticResource Muted}" Margin="0,0,0,6" TextWrapping="Wrap"
                     Text="Only for friends who get Map position. It goes out only while a friend has their world map open, and never from a dungeon, raid or battleground."/>
          <WrapPanel>
            <CheckBox Style="{StaticResource OptionChip}" Content="Show to the other faction" IsChecked="{Binding ShareOtherFaction}" Margin="0,0,6,6"
                      ToolTip="Off: a friend playing the other faction doesn't see you on the map"/>
          </WrapPanel>
          <!-- No GroupName: both game tabs have this page, and radio buttons group by their panel without one. -->
          <WrapPanel>
            <RadioButton Style="{StaticResource ChoiceChip}" Margin="0,0,6,6" Content="Live" IsChecked="{Binding IsMapLive, Mode=OneWay}"
                         Command="{Binding MapRateCommand}" CommandParameter="5" ToolTip="Every few seconds while they look"/>
            <RadioButton Style="{StaticResource ChoiceChip}" Margin="0,0,6,6" Content="Every minute" IsChecked="{Binding IsMapMinute, Mode=OneWay}"
                         Command="{Binding MapRateCommand}" CommandParameter="60"/>
            <RadioButton Style="{StaticResource ChoiceChip}" Margin="0,0,6,6" Content="Every 5 minutes" IsChecked="{Binding IsMapFiveMinutes, Mode=OneWay}"
                         Command="{Binding MapRateCommand}" CommandParameter="300"/>
          </WrapPanel>
```

- [ ] **Step 7: Run all tests**

Run: `dotnet test companion\WoWCharacterTracker.Tests\WoWCharacterTracker.Tests.csproj -warnaserror --nologo`
Expected: all PASS except the known `RatingTests` changelog failure. If `An_old_config_without_map_settings_reads_as_live_and_off` fails with `MapRate == 0`, System.Text.Json didn't use the parameter default. Then fix it where config loads (`Config.cs`, next to the other migrations around line 210): any flavor's `Share` with a `MapRate` not in `ShareSettings.MapRates` becomes `Share with { MapRate = 5 }`, and change the test to go through that load. Keep the test either way.

- [ ] **Step 8: Render the sharing settings**

Build and render the Friends page's sharing settings in screenshot mode against a fake data dir and fake flavor folders (`.claude/notes/testing.md`): `CHARACTERTRACKER_DATA_DIR=<scratch>\data`, `CHARACTERTRACKER_SCREENSHOT=<scratch>\friends-sharing.png`, `CHARACTERTRACKER_SCREENSHOT_VIEW=forever:friends::sharing`, both game paths in the test config pointing at fake `_forever_` and `_retail_` folders with an `Interface` folder in each. Look at the PNG: the Map position chip, the MAP POSITION section with the faction chip and three rate chips, Live picked. Send it to JDybeck.

- [ ] **Step 9: Commit**

```bash
git add companion/WoWCharacterTracker/Models/Friends.cs companion/WoWCharacterTracker/Services/FriendData.cs companion/WoWCharacterTracker/Services/AddonInstaller.cs companion/WoWCharacterTracker/ViewModels/FriendsViewModel.cs companion/WoWCharacterTracker/Views/FriendsPage.xaml companion/WoWCharacterTracker.Tests/FriendDataTests.cs companion/WoWCharacterTracker.Tests/FriendsViewModelTests.cs
git commit -m "feat(companion): add map position to the friends sharing settings"
```

---

### Task 5: Docs, notes and the local build

**Files:**
- Modify: `docs/GUIDE.md` (the "Sharing with Battle.net friends" section), `CHANGELOG.md`, `.claude/notes/friends.md` (not tracked by git, edit it anyway)

- [ ] **Step 1: GUIDE**

In `docs/GUIDE.md`, "Sharing with Battle.net friends", add a bullet after the "What friends get" bullet:

```markdown
- **Friends on the map**: friends who get **Map position** (off until you turn it on, for all friends or just one) show up as a dot in their class color on your world map while you have it open, with a tooltip saying who, where and how long ago. A ring instead of a dot is a friend on another realm. Your position only goes out while a friend has their map open, never from a dungeon, raid or battleground, and never to a friend playing the other faction unless you turn on **Show to the other faction**. **Map updates** sets how often: live, every minute or every 5 minutes. Positions are never saved anywhere. `/wct friends set all map on`, `/wct friends set <BattleTag> otherfaction on` and `/wct friends maprate 1m` do the same from chat.
```

- [ ] **Step 2: CHANGELOG**

At the top of `CHANGELOG.md`, above `## v0.37.2`:

```markdown
## v0.38.0

Update the addon with the app (it does this by itself) and /reload in game.

### New

- **Friends on the world map**: Battle.net friends who share Map position with you show up as dots on your world map while you have it open. Off until you turn it on, for all friends or one at a time, with a choice to hide from friends on the other faction (hidden unless you allow it) and how often your position goes out (live, every minute or every 5 minutes). Nothing goes out while nobody is looking, nothing from dungeons, raids or battlegrounds, and positions are never saved. Set it on the Sharing tab in game or on the Friends page in the app.
```

(`RatingTests.The_built_in_changelog_starts_at_this_version_and_uses_the_groups` keeps failing until the release bumps the csproj to 0.38.0. It fails on main already.)

- [ ] **Step 3: friends.md**

Add to `.claude/notes/friends.md`:

```markdown
- Map (0.38.0, spec `docs/superpowers/specs/2026-11-07-friends-map-design.md`): `FriendMap.lua` (WATCH while your map is open, renewed every 20 s, lasts 30 s; POS `mapID,x,y,rate,age` back while a watch lasts, empty = remove; positions in memory only), `MapPins.lua` + `MapPins.xml` (data provider and pin template on `WorldMapFrame`, our pin mixin has an empty `CheckMouseButtonPassthrough` because AcquirePin's calls `SetPassThroughButtons`). Settings: category `map` (off), rule `otherFaction` (off = hidden from the other faction; `RULES` next to `CATEGORIES`, walked by `ns.allowedFor`, listed after the categories in `ns.contactSharing` with `rule = true`), `share.mapRate` (5/60/300). Other files add kinds with `ns.onFriendKind`. App: `GeneralCategories.Map/OtherFaction`, `ShareContact.Map/OtherFaction`, `ShareSettings.MapRate`. Tests: `FriendMapLuaTests` (two clients, `Pass` steps 1 s), `MapPinsLuaTests` (a stand-in WorldMapFrame).
```

And add WATCH and POS to its "Kinds" line: `WATCH (my map is open), POS (a map position, empty = gone).`

- [ ] **Step 4: Full validation**

Run: `dotnet test companion\WoWCharacterTracker.Tests\WoWCharacterTracker.Tests.csproj -warnaserror --nologo`
Expected: all PASS except the known `RatingTests` changelog failure.

- [ ] **Step 5: Commit**

```bash
git add docs/GUIDE.md CHANGELOG.md
git commit -m "docs: describe friends on the world map"
```

- [ ] **Step 6: Rebuild JDybeck's local copy**

Per the "always update local dist" memory: if JDybeck's copy runs from `dist`, find its PID (`Get-Process WoWCharacterTracker | Where-Object { $_.Path -like '*\dist\*' }`, read the PID, stop only that one by `Stop-Process -Id <pid>`), then `dotnet publish companion\WoWCharacterTracker\WoWCharacterTracker.csproj -c Release -o dist -p:Version=0.38.0` and start `dist\WoWCharacterTracker.exe` again. It installs the addon into the game folders by itself.

- [ ] **Step 7: Hand JDybeck the in-game checklist**

Not automatable. Report this list (and add the answers to `friends.md` and the spec's in-game section afterwards):

1. With a friend or a second account, both with the new addon, both Horde (or both Alliance), on Forever: turn on Map position for each other, open the world map. Does the friend's dot show within a few seconds, in the right spot, on the zone map and on the continent map? Does it move while they walk?
2. Hover the dot: label, character, level and class, zone, age. Does "On another realm" show only when it should (compare a friend on another realm)?
3. One of you switches to a character on the other faction: does the dot go within a few seconds? With Show to the other faction on, does it come back?
4. Go into a dungeon: the dot goes.
5. Map updates every minute: the dot stays and its age counts up between updates.
6. Retail: open the world map, then in combat open and close it, then open the talents or the spellbook after the fight. Any "blocked from an action" line in chat or in `/wct` (the addon reports them)? Any Lua error?
7. Is `factionName` filled in for Forever friends? (`/dump C_BattleNet.GetFriendGameAccountInfo(1, 1).factionName` with a friend online.)
8. Any Battle.net throttling (a friend's dot stops updating while live) with 3 or more friends watching at once?
```

- [ ] **Step 8: Wrap up**

Nothing is pushed. Tell JDybeck the branch is ready for a PR once the in-game checks look good, and that the release (csproj and `.toc` version to 0.38.0, `release.cmd`) waits for them.

---

## Self-review notes

- Spec coverage: settings (Task 1, 4), WATCH/POS and sender/receiver rules (Task 2), pins, tooltip, other realm ring, continent maps, dungeon maps (Task 3), app (Task 4), docs and in-game checks (Task 5). Minimap, per-friend rate and clicking pins stay out (spec "Later").
- The `.toc` `## Version` bump is part of the release, not this branch: the "addon version must rise" memory applies when releasing.
- `ns.ago` moved to Shared.lua because MapPins needs it too (reuse rule).
