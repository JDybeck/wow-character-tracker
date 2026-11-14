# Friends sharing, step 1 (addon) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Two players who are Battle.net friends and both run the addon can share their characters with each other in game, with consent per contact, per category and per character, and what a friend shares lands in `WoWCharacterTrackerDB.friends` for the app (steps 2 and 3, separate plans).

**Architecture:** Three new addon files. `FriendData.lua` turns one character into text and back, with the field allowlist and caps (pure Lua). `Comms.lua` is the only file that sends or receives Battle.net addon messages: chunking, pacing, guards, reassembly, no knowledge of friends. `Friends.lua` holds the sharing settings, contacts, consent popup, cadence and what to keep, plus `/wct friends`. Two small shared helpers move into `Shared.lua` (`ns.copy`, `ns.popup`).

**Tech Stack:** WoW addon Lua (5.1), tested with MoonSharp from xUnit (`companion/WoWCharacterTracker.Tests`).

**Spec:** `docs/superpowers/specs/2026-11-06-friends-sharing-design.md`

## Global Constraints

- No Blizzard globals replaced, no `COMBAT_LOG_EVENT_UNFILTERED`, no CVar changes, nothing protected touched (CLAUDE.md taint rules).
- Never tell Retail apart by an API existing; this feature has no flavor checks at all.
- Prefix `WCTF`; protocol version `1` is the first field of every message.
- Chunk size 240 bytes, at most 40 parts (more than the 8 KB body cap).
- Cadence: 120 s in your group, 600 s online elsewhere, heartbeat 1800 s, tick every 30 s, request cooldown 30 s.
- At most 50 characters kept per contact.
- Defaults for a contact you share with: gear and talents on, location and gold off. Sharing as a whole is off.
- Off means off: with `enabled` off nothing is sent and nothing received is kept or answered.
- Silent: nothing printed about sharing unless the player typed a command or answered the popup.
- `accountName` (can be a real name) is never read or stored; only the BattleTag.
- Comments brief, no em dashes in comments, docs or chat lines (use commas).
- Commits: Conventional Commits, scope `addon` (or `docs`), ending with `Co-Authored-By: Claude <noreply@anthropic.com>` and `Claude-Session: 5273ae34-7426-4d14-b965-12f2bb2aaef0`. Commit as `35696089+JDybeck@users.noreply.github.com` (already the repo's git user). No pushes.
- Validation: `verify.cmd` from CLAUDE.md is not on main (it lives on the unmerged `chore/agent-setup` branch). Use `dotnet test companion/WoWCharacterTracker.Tests`.
- No version bumps in this plan (`.toc` and csproj change at release time, `releases.md`).

## Review Focus

1. **A friend relogs and gets a new game account id**: our tick sees a new account and says HI again; nothing stalls. Pinned by the cadence test sending to whatever account the friends list reports (Task 4).
2. **Both players share with each other and both reload at the same time**: one HI and one HEY each way, then quiet, no message storm. Pinned by `Two_clients_go_quiet_after_saying_hello` (Task 4).
3. **An older app writes Settings.lua without `share`**: the sharing settings changed in game survive. Pinned by `Sharing_settings_changed_in_game_survive_an_app_that_doesnt_know_them` (Task 4).
4. **A friend's addon sends something our code chokes on**: no Lua error popup in game, the message is dropped and the error kept for `/wct debug`. Pinned by `A_handler_error_does_not_escape_the_receive` (Task 3).
5. **A request arrives mid-fight**: the popup waits until combat ends instead of showing (or failing) in combat. Pinned by `A_request_in_combat_waits_for_the_fight_to_end` (Task 5).

---

### Task 1: Shared helpers and the test harness

**Files:**
- Modify: `addon/WoWCharacterTracker/Shared.lua` (`local function copy` at lines 90-95, `ns.editSettings` at 98-112)
- Modify: `addon/WoWCharacterTracker/Events.lua:104-117` (`askReload`)
- Modify: `companion/WoWCharacterTracker.Tests/AddonLuaTests.cs:9` and `:27` (make `AddonDir` and `FakeTimers` internal)
- Create: `companion/WoWCharacterTracker.Tests/FriendsLuaTests.cs`

**Interfaces:**
- Produces: `ns.copy(v)` deep copy of a table (non-tables returned as is). `ns.popup(name, dialog, textArg, data)` shows a Blizzard popup, adding `dialog` to `StaticPopupDialogs[name]` only the first time; `data` reaches `OnAccept(self, data)`, `OnCancel(self, data)`, `OnAlt(self, data)`.
- Produces (tests): `FriendsLuaTests.Client(me, friendsLua, db, settings, files)` with `Lua(code)`, `Call(name, args)`, `Run(seconds)`, `Sent()`, `Comms`, `Ns`. Lua globals in a client: `ns` (the addon table), `sent`, `shown`, `said`, `friends`, `groupGuids`, `inCombat`, `lockdown`, `sendResult`, `clock`.

- [ ] **Step 1: Make the Lua test helpers reusable**

In `AddonLuaTests.cs`, change `static string AddonDir` to `internal static string AddonDir` and `const string FakeTimers` to `internal const string FakeTimers`. Nothing else changes.

- [ ] **Step 2: Write the harness and the failing tests**

Create `companion/WoWCharacterTracker.Tests/FriendsLuaTests.cs`:

```csharp
using System.Globalization;
using MoonSharp.Interpreter;

namespace WoWCharacterTracker.Tests;

// Sharing with Battle.net friends (FriendData.lua, Comms.lua, Friends.lua) in MoonSharp, with
// Battle.net stubbed. Two clients can talk: what one sends with SendGameData is handed to the
// other's BN_CHAT_MSG_ADDON handler.
public class FriendsLuaTests
{
    const string Stubs = """
        function time() return math.floor(clock) end
        function strtrim(s) return (s:gsub('^%s+', ''):gsub('%s+$', '')) end
        inCombat, lockdown, groupGuids, sendResult = false, false, {}, 0
        function InCombatLockdown() return inCombat end
        function IsEncounterInProgress() return false end
        C_ChatInfo = { RegisterAddonMessagePrefix = function() end, InChatMessagingLockdown = function() return lockdown end }
        BNET_CLIENT_WOW, WOW_PROJECT_ID = 'WoW', 5
        sent, shown, said = {}, {}, {}
        function BNGetNumFriends() return #friends end
        C_BattleNet = {
          GetFriendAccountInfo = function(i) return friends[i] and { battleTag = friends[i].tag } end,
          GetFriendNumGameAccounts = function(i) return friends[i] and 1 or 0 end,
          GetFriendGameAccountInfo = function(i)
            local f = friends[i]
            return f and { isOnline = f.online ~= false, clientProgram = 'WoW', wowProjectID = 5, gameAccountID = f.account, playerGuid = f.guid }
          end,
          SendGameData = function(to, prefix, text) table.insert(sent, { to = to, prefix = prefix, text = text }) return sendResult end,
        }
        function IsInGroup() return #groupGuids > 0 end
        function IsInRaid() return false end
        function GetNumGroupMembers() return #groupGuids end
        function UnitGUID(u)
          if u == 'player' then return MY_GUID end
          local i = tonumber(u:match('^party(%d+)$') or '')
          return i and groupGuids[i] or nil
        end
        StaticPopupDialogs = {}
        function StaticPopup_Show(name, arg, _, data) table.insert(shown, { name = name, arg = arg, data = data }) return {} end
        function GetAddOnMetadata() return '0.33.0' end
        """;

    internal static readonly string[] AllFiles = ["Shared.lua", "FriendData.lua", "Comms.lua", "Friends.lua"];

    // One game client. me: this player's GUID; friendsLua: the Battle.net friends list as a Lua
    // table of { tag, account, guid, online }; db and settings: the saved file and Settings.lua.
    internal sealed class Client
    {
        public Script Script { get; } = new();
        public Table Ns { get; }
        public Table Comms => Ns.Get("comms").Table;

        public Client(string me, string friendsLua, string db = "{ chars = {}, items = {} }", string settings = "{}", string[]? files = null)
        {
            Script.DoString(AddonLuaTests.FakeTimers);
            Script.DoString(Stubs);
            Script.DoString($"MY_GUID = '{me}'; friends = {friendsLua}; WoWCharacterTrackerDB = {db}; WoWCharacterTrackerSettings = {settings}");
            Ns = new Table(Script);
            Script.Globals["ns"] = Ns;
            Ns["say"] = (Action<string>)(s => Script.Globals.Get("said").Table.Append(DynValue.NewString(s)));
            foreach (var file in files ?? AllFiles)
                Script.Call(Script.LoadString(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, file)), codeFriendlyName: file), "WoWCharacterTracker", Ns);
        }

        public DynValue Lua(string code) => Script.DoString(code);
        public DynValue Call(string name, params object[] args) => Script.Call(Ns.Get(name), args);
        public void Run(double seconds) => Script.DoString($"runTimers(clock + {seconds.ToString(CultureInfo.InvariantCulture)})");
        public List<Table> Sent() => [.. Script.Globals.Get("sent").Table.Values.Select(v => v.Table)];
        public List<string> Said() => [.. Script.Globals.Get("said").Table.Values.Select(v => v.String)];
        public void Receive(string text, int fromAccount, string prefix = "WCTF") =>
            Script.Call(Comms.Get("receive"), prefix, text, "WHISPER", fromAccount);
    }

    static string Str(Client c, string expr) => c.Lua("return " + expr).String;

    [Fact]
    public void A_popup_is_added_to_the_games_list_once_and_gets_its_data()
    {
        var c = new Client("Player-1-0000000A", "{}", files: ["Shared.lua"]);
        c.Lua("""
            ns.popup('WCT_TEST', { text = 'first %s', button1 = 'OK' }, 'x', { n = 1 })
            ns.popup('WCT_TEST', { text = 'second %s', button1 = 'OK' }, 'y', { n = 2 })
            """);
        Assert.Equal("first %s", Str(c, "StaticPopupDialogs.WCT_TEST.text"));
        Assert.True(c.Lua("return StaticPopupDialogs.WCT_TEST.hideOnEscape").Boolean);
        Assert.Equal(2, c.Lua("return #shown").Number);
        Assert.Equal("y", Str(c, "shown[2].arg"));
        Assert.Equal(2, c.Lua("return shown[2].data.n").Number);
    }

    [Fact]
    public void A_copy_is_deep_and_separate()
    {
        var c = new Client("Player-1-0000000A", "{}", files: ["Shared.lua"]);
        c.Lua("original = { a = { b = 1 } }; copied = ns.copy(original); copied.a.b = 2");
        Assert.Equal(1, c.Lua("return original.a.b").Number);
        Assert.Equal(5, c.Lua("return ns.copy(5)").Number);
    }
}
```

- [ ] **Step 3: Run the tests to see them fail**

Run: `dotnet test companion/WoWCharacterTracker.Tests --filter "FullyQualifiedName~FriendsLuaTests"`
Expected: both FAIL (`attempt to call a nil value` for `ns.popup` / `ns.copy`).

- [ ] **Step 4: Add `ns.copy` and `ns.popup` to Shared.lua**

In `Shared.lua`, replace

```lua
local function copy(v)
  if type(v) ~= "table" then return v end
  local out = {}
  for k, inner in pairs(v) do out[k] = copy(inner) end
  return out
end
```

with

```lua
-- A deep copy of a table (anything else as it is).
function ns.copy(v)
  if type(v) ~= "table" then return v end
  local out = {}
  for k, inner in pairs(v) do out[k] = ns.copy(inner) end
  return out
end
local copy = ns.copy
```

and append at the end of the file:

```lua
-- A Blizzard popup. It's only added to StaticPopupDialogs when first used: anything an addon puts
-- in Blizzard's tables can spread taint, so it stays out unless it's needed. data reaches the
-- dialog's OnAccept, OnCancel and OnAlt as their second argument.
function ns.popup(name, dialog, textArg, data)
  if not StaticPopupDialogs[name] then
    dialog.timeout = dialog.timeout or 0
    dialog.whileDead, dialog.hideOnEscape, dialog.preferredIndex = true, true, 3
    StaticPopupDialogs[name] = dialog
  end
  return StaticPopup_Show(name, textArg, nil, data)
end
```

- [ ] **Step 5: Make `askReload` use it**

In `Events.lua`, replace the body of the reload prompts block (lines 104-117) with:

```lua
-- Reload prompts ----------------------------------------------------------------
-- Small boxes that offer a /reload (the game only writes the saved file then, and only a click may reload).
local function askReload(name, text, accept, decline, arg)
  ns.popup(name, { text = text, button1 = accept, button2 = decline, OnAccept = function() ReloadUI() end }, arg)
end
```

- [ ] **Step 6: Run all tests**

Run: `dotnet test companion/WoWCharacterTracker.Tests`
Expected: PASS, including the two new ones and every existing `AddonLuaTests` test.

- [ ] **Step 7: Commit**

```bash
git add addon/WoWCharacterTracker/Shared.lua addon/WoWCharacterTracker/Events.lua companion/WoWCharacterTracker.Tests/AddonLuaTests.cs companion/WoWCharacterTracker.Tests/FriendsLuaTests.cs
git commit -m "refactor(addon): share the popup and copy helpers" -m "Co-Authored-By: Claude <noreply@anthropic.com>" -m "Claude-Session: 5273ae34-7426-4d14-b965-12f2bb2aaef0"
```

---

### Task 2: FriendData.lua, one character as text

**Files:**
- Create: `addon/WoWCharacterTracker/FriendData.lua`
- Test: `companion/WoWCharacterTracker.Tests/FriendsLuaTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `ns.encodeFriendChar(guid, char, items, allowed) -> text, key`. `char` is a `WoWCharacterTrackerDB.chars[guid]` table, `items` its `items[guid]` (may be nil), `allowed` a table with `gear`, `talents`, `location`, `gold` booleans (a contact table works as is). `text` is one line, `key` is the same without `at` (for change detection).
- Produces: `ns.decodeFriendChar(text) -> table or nil`. Fields: `guid, char, realm, level, class, classFile, race, raceFile, faction, spec, ilvl, at, talents, talentCalc, gear, enchants, zone, money`.

- [ ] **Step 1: Write the failing tests**

Add to `FriendsLuaTests`:

```csharp
    static readonly string[] DataFiles = ["Shared.lua", "FriendData.lua"];

    const string Deadbeck = "{ char = 'Deadbeck', realm = 'Firemaw', level = 42, class = 'Warrior', classFile = 'WARRIOR', race = 'Orc', raceFile = 'Orc', faction = 'Horde', ilvl = 38, spec = 'Arms', zone = 'Stranglethorn Vale', money = 123456, talents = 'a;b\\\\c', guild = 'Secret', lastSeen = 900 }";

    [Fact]
    public void A_character_goes_as_one_line_with_only_the_allowed_categories_and_comes_back_whole()
    {
        var c = new Client("Player-1-0000000A", "{}", files: DataFiles);
        c.Lua($$"""
            char = {{Deadbeck}}
            items = { gear = '1~19019,16~12345', enchants = '16~12345~Crusader~1900\n5~3000~+4 Stats~1891' }
            text = ns.encodeFriendChar('Player-1-0000000A', char, items, { gear = true, talents = true })
            back = ns.decodeFriendChar(text)
            """);
        Assert.DoesNotContain("\n", Str(c, "text"));
        Assert.Equal("Player-1-0000000A", Str(c, "back.guid"));
        Assert.Equal("Deadbeck", Str(c, "back.char"));
        Assert.Equal(42, c.Lua("return back.level").Number);
        Assert.Equal(900, c.Lua("return back.at").Number);
        Assert.Equal("a;b\\c", Str(c, "back.talents"));
        Assert.Equal("1~19019,16~12345", Str(c, "back.gear"));
        Assert.Equal("16~12345~Crusader~1900\n5~3000~+4 Stats~1891", Str(c, "back.enchants"));
        Assert.True(c.Lua("return back.zone").IsNil());  // location not allowed
        Assert.True(c.Lua("return back.money").IsNil()); // gold not allowed
        Assert.True(c.Lua("return back.guild").IsNil()); // never sent
    }

    [Fact]
    public void What_a_friend_sends_is_cleaned_field_by_field()
    {
        var c = new Client("Player-1-0000000A", "{}", files: DataFiles);
        c.Lua("""
            back = ns.decodeFriendChar('guid=Player-1-0000000B;char=Ze|cffff0000b|r;level=abc;ilvl=1e999;spec=' .. string.rep('x', 100)
              .. ';evil=1;zone=Elwynn\\nForest;enchants=1~2~A\\n3~4~B')
            """);
        Assert.Equal("Zecffff0000br", Str(c, "back.char")); // no | escapes
        Assert.True(c.Lua("return back.level").IsNil());    // not a number
        Assert.True(c.Lua("return back.ilvl").IsNil());     // not a finite number
        Assert.Equal(32, c.Lua("return #back.spec").Number);
        Assert.True(c.Lua("return back.evil").IsNil());
        Assert.Equal("ElwynnForest", Str(c, "back.zone"));  // line breaks only in enchants
        Assert.Equal("1~2~A\n3~4~B", Str(c, "back.enchants"));
    }

    [Theory]
    [InlineData("char=Zeb")]                  // no guid
    [InlineData("guid=Creature-0-1;char=Zeb")] // not a player
    [InlineData("guid=Player-1-0000000B")]     // no name
    public void Text_that_isnt_a_character_is_dropped(string text)
    {
        var c = new Client("Player-1-0000000A", "{}", files: DataFiles);
        Assert.True(c.Lua($"return ns.decodeFriendChar('{text}')").IsNil());
    }

    [Fact]
    public void Text_over_8_kb_is_dropped()
    {
        var c = new Client("Player-1-0000000A", "{}", files: DataFiles);
        Assert.True(c.Lua("return ns.decodeFriendChar('guid=Player-1-0000000B;char=Zeb;talents=' .. string.rep('x', 8200))").IsNil());
    }

    [Fact]
    public void The_change_key_ignores_when_the_snapshot_was_taken()
    {
        var c = new Client("Player-1-0000000A", "{}", files: DataFiles);
        c.Lua($$"""
            char = {{Deadbeck}}
            t1, k1 = ns.encodeFriendChar('Player-1-0000000A', char, nil, {})
            char.lastSeen = 1900
            t2, k2 = ns.encodeFriendChar('Player-1-0000000A', char, nil, {})
            """);
        Assert.NotEqual(Str(c, "t1"), Str(c, "t2"));
        Assert.Equal(Str(c, "k1"), Str(c, "k2"));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test companion/WoWCharacterTracker.Tests --filter "FullyQualifiedName~FriendsLuaTests"`
Expected: the new tests FAIL (`FriendData.lua` not found).

- [ ] **Step 3: Write FriendData.lua**

```lua
-- One character as a friend sees it (Friends.lua): built from the saved snapshot, sent as one line
-- of text, and checked field by field when it arrives, since it comes from someone else's PC.
-- No game calls, so the app's tests load it.
local ADDON, ns = ...

-- Every field a friend may get: its type, longest length, and the category that allows it (none:
-- always sent). Only enchants keeps line breaks (one enchant per line).
local FIELDS = {
  guid = { kind = "string", max = 40 },
  char = { kind = "string", max = 24 },
  realm = { kind = "string", max = 40 },
  level = { kind = "number" },
  class = { kind = "string", max = 24 },
  classFile = { kind = "string", max = 16 },
  race = { kind = "string", max = 24 },
  raceFile = { kind = "string", max = 16 },
  faction = { kind = "string", max = 10 },
  spec = { kind = "string", max = 32 },
  ilvl = { kind = "number" },
  at = { kind = "number" },
  talents = { kind = "string", max = 1024, category = "talents" },
  talentCalc = { kind = "string", max = 256, category = "talents" },
  gear = { kind = "string", max = 512, category = "gear" },
  enchants = { kind = "string", max = 2048, category = "gear", lines = true },
  zone = { kind = "string", max = 64, category = "location" },
  money = { kind = "number", category = "gold" },
}
local MAX_TEXT = 8192

-- `\`, `;` (between fields) and line breaks are escaped, so the text is one line.
local function escape(s)
  s = s:gsub("\\", "\\\\")
  s = s:gsub("\n", "\\n")
  s = s:gsub(";", "\\s")
  return s
end

local function unescape(s)
  return (s:gsub("\\(.)", function(c)
    if c == "n" then return "\n" end
    if c == "s" then return ";" end
    return c
  end))
end

-- No | (WoW's escape character for links and textures) and no control characters.
local function clean(s, field)
  s = s:gsub("|", "")
  s = s:gsub("%c", function(c) return (field.lines and c == "\n") and c or "" end)
  return s:sub(1, field.max)
end

local function finite(n)
  return n ~= nil and n == n and n ~= math.huge and n ~= -math.huge
end

-- The text for one of your characters, and the same without `at` (it changes with every snapshot,
-- so change detection compares that one). allowed: the categories the friend may see.
function ns.encodeFriendChar(guid, char, items, allowed)
  local values = {}
  for key in pairs(FIELDS) do values[key] = char[key] end
  values.guid, values.at = guid, char.lastSeen
  values.gear, values.enchants = items and items.gear, items and items.enchants
  local parts = {}
  for key, field in pairs(FIELDS) do
    local v = values[key]
    if key ~= "at" and type(v) == field.kind and (not field.category or allowed[field.category]) then
      parts[#parts + 1] = key .. "=" .. escape(tostring(v))
    end
  end
  table.sort(parts) -- the same character is the same text
  local key = table.concat(parts, ";")
  return key .. ";at=" .. tostring(values.at or 0), key
end

-- The character in a friend's text, or nil when it isn't one. Only known fields are kept.
function ns.decodeFriendChar(text)
  if type(text) ~= "string" or #text > MAX_TEXT then return nil end
  local out = {}
  for part in text:gmatch("[^;]+") do
    local key, raw = part:match("^(%a+)=(.*)$")
    local field = key and FIELDS[key]
    if field then
      local v = unescape(raw)
      if field.kind == "number" then
        v = tonumber(v)
        if not finite(v) then v = nil end
      else
        v = clean(v, field)
        if v == "" then v = nil end
      end
      out[key] = v
    end
  end
  if not (out.guid and out.guid:match("^Player%-%d+%-%x+$") and out.char) then return nil end
  return out
end
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test companion/WoWCharacterTracker.Tests --filter "FullyQualifiedName~FriendsLuaTests"`
Expected: PASS. (`Addon_file_compiles` in `AddonLuaTests` picks up the new file by itself.)

- [ ] **Step 5: Commit**

```bash
git add addon/WoWCharacterTracker/FriendData.lua companion/WoWCharacterTracker.Tests/FriendsLuaTests.cs
git commit -m "feat(addon): turn a character into text for friends and back" -m "Co-Authored-By: Claude <noreply@anthropic.com>" -m "Claude-Session: 5273ae34-7426-4d14-b965-12f2bb2aaef0"
```

---

### Task 3: Comms.lua, Battle.net addon messages

**Files:**
- Create: `addon/WoWCharacterTracker/Comms.lua`
- Test: `companion/WoWCharacterTracker.Tests/FriendsLuaTests.cs`

**Interfaces:**
- Consumes: `C_Timer.After`, `GetTime` (game), nothing from earlier tasks.
- Produces: `ns.comms.send(account, kind, body) -> bool` (false when too long), `ns.comms.receive(prefix, text, channel, senderID)` (the `BN_CHAT_MSG_ADDON` handler), `ns.comms.onReceive(account, kind, body)` (set by Friends.lua; called with kind `"VERSION"` and the other side's protocol as body when it differs), `ns.comms.flush()`, `ns.comms.canSend()`, `ns.comms.PREFIX = "WCTF"`, `ns.comms.PROTOCOL = 1`, `ns.comms.CHUNK = 240`, `ns.lastCommsError`.
- Wire text: `protocol:kind:id:part:total:chunk`, kind upper-case letters.

- [ ] **Step 1: Write the failing tests**

Add to `FriendsLuaTests`:

```csharp
    static readonly string[] CommsFiles = ["Shared.lua", "Comms.lua"];

    // A client with only Comms.lua, whose whole messages land in the Lua table `got`.
    static Client CommsClient()
    {
        var c = new Client("Player-1-0000000A", "{}", files: CommsFiles);
        c.Lua("got = {}; ns.comms.onReceive = function(from, kind, body) table.insert(got, { from = from, kind = kind, body = body }) end");
        return c;
    }

    [Fact]
    public void A_long_text_goes_in_parts_and_comes_back_whole_in_any_order()
    {
        var c = CommsClient();
        c.Lua("long = string.rep('abcdefghij', 70); ns.comms.send(2, 'DATA', long)"); // 700 bytes: 3 parts
        c.Run(5);
        var sent = c.Sent();
        Assert.Equal(3, sent.Count);
        Assert.All(sent, m =>
        {
            Assert.Equal("WCTF", m.Get("prefix").String);
            Assert.Equal(2, m.Get("to").Number);
            Assert.StartsWith("1:DATA:", m.Get("text").String);
        });
        foreach (var m in Enumerable.Reverse(sent)) c.Receive(m.Get("text").String, 7);
        Assert.Equal(1, c.Lua("return #got").Number);
        Assert.Equal(7, c.Lua("return got[1].from").Number);
        Assert.Equal("DATA", Str(c, "got[1].kind"));
        Assert.Equal(Str(c, "long"), Str(c, "got[1].body"));
    }

    [Fact]
    public void Pipes_are_dropped_both_ways_and_strangers_prefixes_ignored()
    {
        var c = CommsClient();
        c.Lua("ns.comms.send(2, 'HI', 'a|b')");
        Assert.Equal("1:HI:1:1:1:ab", c.Sent()[0].Get("text").String);
        c.Receive("1:HI:1:1:1:x", 7, prefix: "OTHER");
        c.Receive("garbage", 7);
        c.Receive("1:HI:5:1:1:a|b", 7);
        Assert.Equal(1, c.Lua("return #got").Number);
        Assert.Equal("ab", Str(c, "got[1].body"));
    }

    [Fact]
    public void Another_protocol_is_reported_as_its_version()
    {
        var c = CommsClient();
        c.Receive("2:HI:1:1:1:0.40.0", 7);
        Assert.Equal("VERSION", Str(c, "got[1].kind"));
        Assert.Equal("2", Str(c, "got[1].body"));
    }

    [Fact]
    public void Half_a_message_is_dropped_after_30_seconds()
    {
        var c = CommsClient();
        c.Receive("1:DATA:9:1:2:abc", 7);
        c.Run(31);
        c.Receive("1:DATA:9:2:2:def", 7);
        Assert.Equal(0, c.Lua("return #got").Number);
        c.Receive("1:DATA:10:1:2:abc", 7);
        c.Receive("1:DATA:10:2:2:def", 7);
        Assert.Equal("abcdef", Str(c, "got[1].body"));
    }

    [Fact]
    public void At_most_eight_half_messages_wait_at_once()
    {
        var c = CommsClient();
        for (var id = 1; id <= 9; id++) c.Receive($"1:DATA:{id}:1:2:x", 7);
        c.Receive("1:DATA:9:2:2:y", 7); // the ninth was never kept
        Assert.Equal(0, c.Lua("return #got").Number);
        c.Receive("1:DATA:1:2:2:y", 7);
        Assert.Equal("xy", Str(c, "got[1].body"));
    }

    [Theory]
    [InlineData("inCombat")]
    [InlineData("lockdown")]
    public void Nothing_goes_out_in_combat_or_chat_lockdown_and_it_goes_after(string flag)
    {
        var c = CommsClient();
        c.Lua($"{flag} = true; ns.comms.send(2, 'HI', 'x')");
        c.Run(20);
        Assert.Empty(c.Sent());
        c.Lua($"{flag} = false");
        c.Run(6);
        Assert.Single(c.Sent());
    }

    [Fact]
    public void A_throttled_send_is_tried_three_times_then_dropped()
    {
        var c = CommsClient();
        c.Lua("sendResult = 3; ns.comms.send(2, 'HI', 'x')");
        c.Run(60);
        Assert.Equal(3, c.Sent().Count);
        c.Lua("sendResult = 0; ns.comms.send(2, 'HI', 'y')");
        c.Run(1);
        Assert.Equal("1:HI:2:1:1:y", c.Sent()[3].Get("text").String);
    }

    [Fact]
    public void A_text_over_40_parts_is_refused()
    {
        var c = CommsClient();
        Assert.False(c.Lua("return ns.comms.send(2, 'DATA', string.rep('x', 240 * 40 + 1))").Boolean);
        Assert.Empty(c.Sent());
    }

    [Fact]
    public void A_handler_error_does_not_escape_the_receive()
    {
        var c = CommsClient();
        c.Lua("ns.comms.onReceive = function() error('boom') end");
        c.Receive("1:HI:1:1:1:x", 7); // would throw without the guard
        Assert.Contains("boom", Str(c, "ns.lastCommsError"));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test companion/WoWCharacterTracker.Tests --filter "FullyQualifiedName~FriendsLuaTests"`
Expected: the new tests FAIL (`Comms.lua` not found).

- [ ] **Step 3: Write Comms.lua**

```lua
-- Addon messages between friends' game clients over Battle.net (C_BattleNet.SendGameData, received
-- on BN_CHAT_MSG_ADDON). The only file that sends or receives them. Long texts go in parts and are
-- put back together, sends are paced and wait out combat, boss fights and the game's chat lockdown,
-- and nothing is ever printed. What to send and what to keep is Friends.lua's (comms.onReceive).
local ADDON, ns = ...

local comms = {}
ns.comms = comms

comms.PREFIX = "WCTF"
comms.PROTOCOL = 1
comms.CHUNK = 240       -- body bytes per message: raise once the real limit is measured in game
local MAX_PARTS = 40    -- 40 x 240 is more than FriendData's 8 KB
local MAX_PARTIAL = 8   -- half messages waiting at once
local PART_TIMEOUT = 30 -- seconds half a message waits for the rest
local SPACING = 0.25    -- seconds between two sends
local RETRY = 5         -- while sending isn't allowed
local BACKOFF = 10      -- after the game turned a send down (throttled)
local MAX_TRIES = 3

if C_ChatInfo and C_ChatInfo.RegisterAddonMessagePrefix then
  pcall(C_ChatInfo.RegisterAddonMessagePrefix, comms.PREFIX)
end

local function sendGameData()
  return (C_BattleNet and C_BattleNet.SendGameData) or BNSendGameData
end

-- Not in combat, a boss fight or the game's chat lockdown (Midnight-era clients).
function comms.canSend()
  if InCombatLockdown and InCombatLockdown() then return false end
  if IsEncounterInProgress and IsEncounterInProgress() then return false end
  if C_ChatInfo and C_ChatInfo.InChatMessagingLockdown and C_ChatInfo.InChatMessagingLockdown() then return false end
  return sendGameData() ~= nil
end

local queue = {} -- { to, text, tries }
local nextId = 0
local waiting = false

-- Older clients return nothing, newer ones a result where 0 is success.
local function accepted(ok, result)
  return ok and (result == nil or result == true or result == 0)
end

function comms.flush()
  if waiting or #queue == 0 then return end
  local wait = RETRY
  if comms.canSend() then
    local item = queue[1]
    wait = SPACING
    if accepted(pcall(sendGameData(), item.to, comms.PREFIX, item.text)) then
      table.remove(queue, 1)
    else
      item.tries = item.tries + 1
      wait = BACKOFF
      if item.tries >= MAX_TRIES then table.remove(queue, 1) end
    end
    if #queue == 0 then return end
  end
  waiting = true
  C_Timer.After(wait, function()
    waiting = false
    comms.flush()
  end)
end

-- Queues kind (HI, HEY, REQ, DATA, NO) with body for one game account. False when it's too long.
function comms.send(account, kind, body)
  body = (body or ""):gsub("|", "")
  local total = math.max(1, math.ceil(#body / comms.CHUNK))
  if total > MAX_PARTS then return false end
  nextId = nextId % 9999 + 1
  for part = 1, total do
    local chunk = body:sub((part - 1) * comms.CHUNK + 1, part * comms.CHUNK)
    queue[#queue + 1] = { to = account, tries = 0,
      text = string.format("%d:%s:%d:%d:%d:%s", comms.PROTOCOL, kind, nextId, part, total, chunk) }
  end
  comms.flush()
  return true
end

local partial = {} -- ["sender:id"] = { parts, got, total, kind, at }

-- Drops half messages that waited too long; returns how many still wait.
local function expire(now)
  local count = 0
  for key, p in pairs(partial) do
    if now - p.at > PART_TIMEOUT then partial[key] = nil else count = count + 1 end
  end
  return count
end

-- A friend's code can't make ours throw: an error is kept for /wct debug and the message dropped.
local function deliver(account, kind, body)
  if not comms.onReceive then return end
  local ok, err = pcall(comms.onReceive, account, kind, body)
  if not ok then ns.lastCommsError = tostring(err) end
end

-- BN_CHAT_MSG_ADDON.
function comms.receive(prefix, text, _, senderID)
  if prefix ~= comms.PREFIX or type(text) ~= "string" or not senderID then return end
  local protocol, kind, id, part, total, chunk = text:match("^(%d+):(%u+):(%d+):(%d+):(%d+):(.*)$")
  if not protocol then return end
  if tonumber(protocol) ~= comms.PROTOCOL then return deliver(senderID, "VERSION", protocol) end
  part, total = tonumber(part), tonumber(total)
  if total < 1 or total > MAX_PARTS or part < 1 or part > total then return end
  chunk = chunk:gsub("|", "")
  if total == 1 then return deliver(senderID, kind, chunk) end
  local waitingNow = expire(GetTime())
  local key = senderID .. ":" .. id
  local p = partial[key]
  if not p or p.total ~= total or p.kind ~= kind then
    if waitingNow >= MAX_PARTIAL then return end
    p = { parts = {}, got = 0, total = total, kind = kind, at = GetTime() }
    partial[key] = p
  end
  if not p.parts[part] then
    p.parts[part] = chunk
    p.got = p.got + 1
  end
  if p.got < total then return end
  partial[key] = nil
  deliver(senderID, kind, table.concat(p.parts))
end
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test companion/WoWCharacterTracker.Tests --filter "FullyQualifiedName~FriendsLuaTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add addon/WoWCharacterTracker/Comms.lua companion/WoWCharacterTracker.Tests/FriendsLuaTests.cs
git commit -m "feat(addon): send and receive addon messages over battle.net" -m "Co-Authored-By: Claude <noreply@anthropic.com>" -m "Claude-Session: 5273ae34-7426-4d14-b965-12f2bb2aaef0"
```

---

### Task 4: Friends.lua, settings, hello and sharing the character being played

**Files:**
- Create: `addon/WoWCharacterTracker/Friends.lua`
- Test: `companion/WoWCharacterTracker.Tests/FriendsLuaTests.cs`

**Interfaces:**
- Consumes: `ns.copy`, `ns.encodeFriendChar`, `ns.decodeFriendChar`, `ns.comms.send`, `ns.comms.onReceive` (assigned here).
- Produces: `ns.shareSettings() -> s` (always has `enabled`, `newChars`, `contacts`, `chars`, `blocked`), `ns.editShare(fn)`, `ns.isCharShared(s, guid) -> bool`, `ns.friendsOnline() -> { { tag, account, guid } }`, `ns.friendsTick()`, `ns.startFriends()`. File locals Task 5 appends to and uses: `on` (handler table), `heard`, `lastSent`, `saidNo`, `online()`, `tagOf(account)`, `charText(guid, contact)`, `sendCurrent(f, contact, inGroup, force)`, `friendEntry(tag)`.
- Saved: `WoWCharacterTrackerDB.share`, `WoWCharacterTrackerDB.friends[tag] = { seen, version, protocol, refused, chars = { [guid] = { ...fields, received } } }`.

- [ ] **Step 1: Write the failing tests**

Add to `FriendsLuaTests`:

```csharp
    const string A = "Player-1-0000000A", B = "Player-1-0000000B";
    const int AAccount = 1, BAccount = 2; // each one's game account as the other's friends list has it
    const string AFriends = "{ { tag = 'Zeb#2222', account = 2, guid = 'Player-1-0000000B' } }";
    const string BFriends = "{ { tag = 'JDybeck#1111', account = 1, guid = 'Player-1-0000000A' } }";
    const string AShares = "{ enabled = true, contacts = { ['Zeb#2222'] = { share = true, receive = true, gear = true, talents = true, location = true, gold = false } }, chars = { ['Player-1-0000000D'] = false }, at = 100 }";
    const string BReceives = "{ enabled = true, contacts = { ['JDybeck#1111'] = { receive = true } }, at = 100 }";
    const string GotDeadbeck = "WoWCharacterTrackerDB.friends['JDybeck#1111'].chars['Player-1-0000000A']";

    // A's saved file: the character being played (A), an alt (C) and a hidden alt (D).
    static string ADb(string share) => $$"""
        { share = {{share}}, chars = {
            ['Player-1-0000000A'] = {{Deadbeck}},
            ['Player-1-0000000C'] = { char = 'Drygbeck', realm = 'Firemaw', level = 12, class = 'Mage', classFile = 'MAGE', lastSeen = 500 },
            ['Player-1-0000000D'] = { char = 'Hiddenbeck', realm = 'Firemaw', level = 5, class = 'Rogue', classFile = 'ROGUE', lastSeen = 400 } },
          items = { ['Player-1-0000000A'] = { gear = '1~19019,16~12345' } } }
        """;

    static string BDb(string share) => $"{{ share = {share}, chars = {{}}, items = {{}} }}";

    // Hands what from sent to to, as fromAccount (how to's friends list knows from).
    static void Deliver(Client from, Client to, int fromAccount)
    {
        var messages = from.Sent();
        from.Lua("sent = {}");
        foreach (var m in messages) to.Receive(m.Get("text").String, fromAccount, m.Get("prefix").String);
    }

    // Both clients send what they queued and answer each other, a few rounds.
    static void Talk(Client a, Client b, int rounds = 4)
    {
        for (var i = 0; i < rounds; i++)
        {
            a.Run(1);
            b.Run(1);
            Deliver(a, b, AAccount);
            Deliver(b, a, BAccount);
        }
    }

    static int Count(Client c, string kind) => c.Sent().Count(m => m.Get("text").String.Contains($":{kind}:"));

    [Fact]
    public void Sharing_is_off_until_turned_on()
    {
        var a = new Client(A, AFriends, ADb("nil"));
        a.Call("friendsTick");
        a.Run(5);
        Assert.Empty(a.Sent());
        a.Receive("1:HI:1:1:1:0.33.0", BAccount);
        a.Receive("1:DATA:2:1:1:guid=Player-1-0000000B;char=Zeb", BAccount);
        a.Run(5);
        Assert.Empty(a.Sent());
        Assert.True(a.Lua("return WoWCharacterTrackerDB.friends").IsNil());
    }

    [Fact]
    public void A_contact_gets_the_character_being_played_with_only_what_they_may_see()
    {
        var a = new Client(A, AFriends, ADb(AShares));
        var b = new Client(B, BFriends, BDb(BReceives));
        a.Call("friendsTick");
        b.Call("friendsTick");
        Talk(a, b);
        Assert.Equal("Stranglethorn Vale", Str(b, GotDeadbeck + ".zone"));
        Assert.Equal("1~19019,16~12345", Str(b, GotDeadbeck + ".gear"));
        Assert.True(b.Lua($"return {GotDeadbeck}.money").IsNil());
        Assert.True(b.Lua("return WoWCharacterTrackerDB.friends['JDybeck#1111'].chars['Player-1-0000000C']").IsNil()); // alts only when asked
        Assert.Equal("0.33.0", Str(b, "WoWCharacterTrackerDB.friends['JDybeck#1111'].version"));
        Assert.True(a.Lua("return WoWCharacterTrackerDB.friends and WoWCharacterTrackerDB.friends['Zeb#2222'] and next(WoWCharacterTrackerDB.friends['Zeb#2222'].chars)").IsNil()); // B shares nothing
    }

    [Fact]
    public void Two_clients_go_quiet_after_saying_hello()
    {
        const string BShares = "{ enabled = true, contacts = { ['JDybeck#1111'] = { share = true, receive = true, gear = true } }, at = 100 }";
        var a = new Client(A, AFriends, ADb(AShares));
        var b = new Client(B, BFriends, BDb(BShares).Replace("chars = {}", "chars = { ['Player-1-0000000B'] = { char = 'Zeb', lastSeen = 1 } }"));
        a.Call("friendsTick");
        b.Call("friendsTick");
        Talk(a, b);
        a.Run(1);
        b.Run(1);
        Assert.Empty(a.Sent());
        Assert.Empty(b.Sent());
        Assert.Equal("Zeb", Str(a, "WoWCharacterTrackerDB.friends['Zeb#2222'].chars['Player-1-0000000B'].char"));
    }

    [Fact]
    public void Data_from_someone_we_dont_keep_data_from_is_dropped()
    {
        var b = new Client(B, BFriends, BDb("{ enabled = true, contacts = { ['JDybeck#1111'] = { receive = false } }, at = 100 }"));
        b.Receive("1:DATA:1:1:1:guid=Player-1-0000000A;char=Deadbeck", AAccount);
        b.Receive("1:DATA:2:1:1:guid=Player-1-0000000E;char=Stranger", 99); // not a Battle.net friend
        Assert.True(b.Lua("return WoWCharacterTrackerDB.friends").IsNil());
    }

    // A after saying hello to B and getting B's answer (so A knows B has the addon).
    static Client HelloDone(string groupGuids = "{}")
    {
        var a = new Client(A, AFriends, ADb(AShares));
        a.Lua($"groupGuids = {groupGuids}");
        a.Call("friendsTick");
        a.Receive("1:HEY:1:1:1:0.33.0", BAccount);
        a.Run(1);
        Assert.Equal(1, Count(a, "DATA"));
        a.Lua("sent = {}");
        return a;
    }

    [Fact]
    public void The_character_being_played_goes_when_due_and_changed_or_as_a_heartbeat()
    {
        var a = HelloDone();
        a.Run(300);
        a.Call("friendsTick");
        a.Run(1);
        Assert.Equal(0, Count(a, "DATA")); // 10 minutes not up
        a.Run(300);
        a.Call("friendsTick");
        a.Run(1);
        Assert.Equal(0, Count(a, "DATA")); // up, but nothing changed
        a.Lua("WoWCharacterTrackerDB.chars['Player-1-0000000A'].zone = 'Booty Bay'");
        a.Call("friendsTick");
        a.Run(1);
        Assert.Equal(1, Count(a, "DATA"));
        a.Lua("sent = {}");
        a.Run(1800);
        a.Call("friendsTick");
        a.Run(1);
        Assert.Equal(1, Count(a, "DATA")); // the heartbeat
    }

    [Theory]
    [InlineData("{ 'Player-1-0000000B' }", 1)]
    [InlineData("{}", 0)]
    public void A_contact_in_your_group_gets_it_every_2_minutes(string group, int expected)
    {
        var a = HelloDone(group);
        a.Lua("WoWCharacterTrackerDB.chars['Player-1-0000000A'].zone = 'Booty Bay'");
        a.Run(121);
        a.Call("friendsTick");
        a.Run(1);
        Assert.Equal(expected, Count(a, "DATA"));
    }

    [Fact]
    public void At_most_50_characters_are_kept_per_contact()
    {
        var b = new Client(B, BFriends, BDb(BReceives));
        for (var i = 1; i <= 55; i++)
            b.Receive($"1:DATA:{i}:1:1:guid=Player-1-{i:X8};char=Alt{i};at={i}", AAccount);
        Assert.Equal(50, b.Lua("local n = 0; for _ in pairs(WoWCharacterTrackerDB.friends['JDybeck#1111'].chars) do n = n + 1 end; return n").Number);
        Assert.True(b.Lua("return WoWCharacterTrackerDB.friends['JDybeck#1111'].chars['Player-1-00000001']").IsNil()); // seen longest ago
        Assert.False(b.Lua("return WoWCharacterTrackerDB.friends['JDybeck#1111'].chars['Player-1-00000037']").IsNil()); // the 55th
    }

    [Fact]
    public void The_newest_sharing_settings_win_and_an_edit_in_game_is_newer_than_the_apps()
    {
        var a = new Client(A, AFriends, ADb("{ enabled = false, at = 100 }"), settings: "{ share = { enabled = true, at = 200 } }");
        Assert.True(a.Lua("return ns.shareSettings().enabled").Boolean);
        a.Lua("clock = 150; ns.editShare(function(s) s.newChars = false end)");
        Assert.Equal(201, a.Lua("return WoWCharacterTrackerDB.share.at").Number);
        Assert.False(a.Lua("return ns.shareSettings().newChars").Boolean);
        Assert.True(a.Lua("return ns.shareSettings().enabled").Boolean); // the app's copy was the base
    }

    [Fact]
    public void Sharing_settings_changed_in_game_survive_an_app_that_doesnt_know_them()
    {
        var a = new Client(A, AFriends, ADb(AShares), settings: "{ inGameAt = 99999, announce = {} }");
        Assert.True(a.Lua("return ns.shareSettings().enabled").Boolean);
        Assert.True(a.Lua("return ns.shareSettings().contacts['Zeb#2222'].share").Boolean);
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test companion/WoWCharacterTracker.Tests --filter "FullyQualifiedName~FriendsLuaTests"`
Expected: the new tests FAIL (`Friends.lua` not found).

- [ ] **Step 3: Write Friends.lua**

```lua
-- Sharing characters with Battle.net friends (contacts) who have the addon too. Off until turned
-- on. A contact gets only the categories allowed for them, from characters that are shared; what a
-- contact sends is kept (WoWCharacterTrackerDB.friends, for the app) only when receiving from them
-- is on. Messages go through Comms.lua, one character's text through FriendData.lua.
local ADDON, ns = ...

local GROUP_EVERY = 120  -- seconds between sends to a contact in your group
local ONLINE_EVERY = 600 -- to one online elsewhere
local HEARTBEAT = 1800   -- sent even when nothing changed
local MAX_CHARS = 50     -- characters kept per contact
local TICK = 30

-- This session only.
local helloSent = {} -- [account] = true: HI went out
local heard = {}     -- [account] = true: they answered, so they have the addon
local lastSent = {}  -- [account] = { at, key }: the character being played, last sent
local saidNo = {}    -- [tag] = true: NO went out

-- Sharing settings ----------------------------------------------------------------------------
-- Changed in game (WoWCharacterTrackerDB.share) or by the app (Settings.lua share): the newest
-- `at` wins, like notes. Its own time, not the In game settings' one, so an app that doesn't
-- write `share` can't reset it.
local function fromApp() return WoWCharacterTrackerSettings and WoWCharacterTrackerSettings.share end

local function normalize(s)
  s.contacts, s.chars, s.blocked = s.contacts or {}, s.chars or {}, s.blocked or {}
  if s.newChars == nil then s.newChars = true end
  return s
end

function ns.shareSettings()
  local game, app = WoWCharacterTrackerDB and WoWCharacterTrackerDB.share, fromApp()
  if app and (not game or (app.at or 0) > (game.at or 0)) then return normalize(app) end
  return normalize(game or { enabled = false })
end

-- Changes sharing settings: edit gets a copy of all of them, saved with a time newer than the app's.
function ns.editShare(edit)
  local s = ns.copy(ns.shareSettings())
  edit(s)
  local app = fromApp()
  s.at = math.max(time(), (app and app.at or 0) + 1)
  WoWCharacterTrackerDB.share = s
end

-- One of your characters goes to friends: its own choice, else the one for new characters.
function ns.isCharShared(s, guid)
  if not guid then return false end
  local v = s.chars[guid]
  if v == nil then return s.newChars ~= false end
  return v == true
end

-- Battle.net friends ------------------------------------------------------------------------
-- Each game account of a Battle.net friend that is online in this same game: { tag, account, guid }.
function ns.friendsOnline()
  local out = {}
  if not (BNGetNumFriends and C_BattleNet and C_BattleNet.GetFriendAccountInfo) then return out end
  local wow = BNET_CLIENT_WOW or "WoW"
  for i = 1, (BNGetNumFriends() or 0) do
    local info = C_BattleNet.GetFriendAccountInfo(i)
    if info and info.battleTag then
      for j = 1, (C_BattleNet.GetFriendNumGameAccounts(i) or 0) do
        local g = C_BattleNet.GetFriendGameAccountInfo(i, j)
        if g and g.isOnline and g.clientProgram == wow and g.wowProjectID == WOW_PROJECT_ID and g.gameAccountID then
          out[#out + 1] = { tag = info.battleTag, account = g.gameAccountID, guid = g.playerGuid }
        end
      end
    end
  end
  return out
end

local function online()
  local ok, list = pcall(ns.friendsOnline)
  return ok and list or {}
end

-- The BattleTag behind a game account that sent us something, or nil (not a friend online here).
local function tagOf(account)
  for _, f in ipairs(online()) do
    if f.account == account then return f.tag end
  end
  return nil
end

local function groupGuids()
  local set = {}
  if not (IsInGroup and IsInGroup()) then return set end
  local raid = IsInRaid and IsInRaid()
  for i = 1, (GetNumGroupMembers and GetNumGroupMembers() or 0) do
    local guid = UnitGUID((raid and "raid" or "party") .. i)
    if guid then set[guid] = true end
  end
  return set
end

local function version()
  local metadata = (C_AddOns and C_AddOns.GetAddOnMetadata) or GetAddOnMetadata
  return metadata and tostring(metadata(ADDON, "Version") or "") or ""
end

-- Sending -------------------------------------------------------------------------------------
-- One of your characters as a contact may see it, and its change key; nil without a snapshot.
local function charText(guid, contact)
  local db = WoWCharacterTrackerDB
  local char = db.chars and db.chars[guid]
  if not char then return nil end
  return ns.encodeFriendChar(guid, char, db.items and db.items[guid], contact)
end

-- The character being played, to one contact: when due (GROUP_EVERY or ONLINE_EVERY since the
-- last one, and changed or HEARTBEAT old), or right away when forced.
local function sendCurrent(f, contact, inGroup, force)
  local me = UnitGUID("player")
  if not ns.isCharShared(ns.shareSettings(), me) then return end
  local text, key = charText(me, contact)
  if not text then return end
  local last, now = lastSent[f.account], time()
  if not force and last then
    local since = now - last.at
    if since < (inGroup and GROUP_EVERY or ONLINE_EVERY) then return end
    if key == last.key and since < HEARTBEAT then return end
  end
  lastSent[f.account] = { at = now, key = key }
  ns.comms.send(f.account, "DATA", text)
end

-- Every TICK seconds, and soon after a friend comes online: hello to contacts newly online, and
-- the character being played to those that are due.
function ns.friendsTick()
  local s = ns.shareSettings()
  if not s.enabled then return end
  local group = groupGuids()
  for _, f in ipairs(online()) do
    local contact = s.contacts[f.tag]
    if contact and not s.blocked[f.tag] then
      if not helloSent[f.account] then
        helloSent[f.account] = true
        ns.comms.send(f.account, "HI", version())
      elseif heard[f.account] and contact.share then
        sendCurrent(f, contact, f.guid ~= nil and group[f.guid] == true, false)
      end
    end
  end
end

local ticker
function ns.startFriends()
  if ticker or not (C_Timer and C_Timer.NewTicker) then return end
  ticker = C_Timer.NewTicker(TICK, ns.friendsTick)
  ns.friendsTick()
end

-- Receiving -----------------------------------------------------------------------------------
local function friendEntry(tag)
  local db = WoWCharacterTrackerDB
  db.friends = db.friends or {}
  local e = db.friends[tag] or {}
  e.chars = e.chars or {}
  db.friends[tag] = e
  return e
end

-- Keeps a friend's character, at most MAX_CHARS per contact (the ones seen longest ago go).
local function keep(tag, char)
  local e = friendEntry(tag)
  char.received = time()
  e.chars[char.guid] = char
  e.seen, e.refused = time(), nil
  local guids = {}
  for guid in pairs(e.chars) do guids[#guids + 1] = guid end
  if #guids <= MAX_CHARS then return end
  table.sort(guids, function(x, y) return (e.chars[x].at or 0) > (e.chars[y].at or 0) end)
  for i = MAX_CHARS + 1, #guids do e.chars[guids[i]] = nil end
end

local function noteVersion(f, contact, body)
  if not contact.receive then return end
  local e = friendEntry(f.tag)
  e.version, e.protocol, e.seen = body, nil, time()
end

local on = {}

-- A contact came online (or reloaded): answer, and send them the character being played.
function on.HI(f, contact, body)
  heard[f.account] = true
  noteVersion(f, contact, body)
  ns.comms.send(f.account, "HEY", version())
  if contact.share then sendCurrent(f, contact, false, true) end
end

-- The answer to our hello (never answered back, so no ping-pong).
function on.HEY(f, contact, body)
  heard[f.account] = true
  noteVersion(f, contact, body)
  if contact.share and not lastSent[f.account] then sendCurrent(f, contact, false, true) end
end

function on.DATA(f, contact, body)
  heard[f.account] = true
  if not contact.receive then return end
  local char = ns.decodeFriendChar(body)
  if char then keep(f.tag, char) end
end

-- Their addon speaks another protocol: the app shows that one of you needs to update.
function on.VERSION(f, contact, protocol)
  if contact.receive then friendEntry(f.tag).protocol = tonumber(protocol) end
end

-- They said Never to our request.
function on.NO(f)
  friendEntry(f.tag).refused = time()
end

-- Everything from Comms.lua. Off means off. A blocked friend is told no once a session when they
-- ask; everything else needs them to be a contact, except a request (Task 5's on.REQ).
ns.comms.onReceive = function(account, kind, body)
  local s = ns.shareSettings()
  if not s.enabled then return end
  local tag = tagOf(account)
  if not tag then return end
  local f = { tag = tag, account = account }
  if s.blocked[tag] then
    if kind == "REQ" and not saidNo[tag] then
      saidNo[tag] = true
      ns.comms.send(account, "NO", "")
    end
    return
  end
  local contact = s.contacts[tag]
  if kind == "REQ" then
    if on.REQ then on.REQ(f, contact) end
  elseif contact and on[kind] then
    on[kind](f, contact, body)
  end
end
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test companion/WoWCharacterTracker.Tests --filter "FullyQualifiedName~FriendsLuaTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add addon/WoWCharacterTracker/Friends.lua companion/WoWCharacterTracker.Tests/FriendsLuaTests.cs
git commit -m "feat(addon): share the character being played with battle.net contacts" -m "Co-Authored-By: Claude <noreply@anthropic.com>" -m "Claude-Session: 5273ae34-7426-4d14-b965-12f2bb2aaef0"
```

---

### Task 5: Requests, the popup and /wct friends

**Files:**
- Modify: `addon/WoWCharacterTracker/Friends.lua` (append at the end)
- Test: `companion/WoWCharacterTracker.Tests/FriendsLuaTests.cs`

**Interfaces:**
- Consumes: Task 4's file locals (`on`, `heard`, `saidNo`, `online`, `charText`, `sendCurrent`, `friendEntry`), `ns.shareSettings`, `ns.editShare`, `ns.isCharShared`, `ns.friendsTick`, `ns.popup`, `ns.comms.send`, `ns.say`.
- Produces: `on.REQ(f, contact)`, `ns.offerFriendRequest(f)`, `ns.showNextFriendRequest()` (Task 6 calls it on `PLAYER_REGEN_ENABLED`), `ns.answerFriendRequest(f, choice)` with choice `"allow"`, `"notnow"` or `"never"`, `ns.findFriendTag(typed) -> tag or nil`, `ns.friendsCommand(args)` (Task 6 routes `/wct friends ...` to it, with the player's case kept).

- [ ] **Step 1: Write the failing tests**

Add to `FriendsLuaTests`:

```csharp
    // A and B after saying hello; B keeps A's data, A shares with B.
    static (Client A, Client B) Friends()
    {
        var a = new Client(A, AFriends, ADb(AShares));
        var b = new Client(B, BFriends, BDb(BReceives));
        a.Call("friendsTick");
        b.Call("friendsTick");
        Talk(a, b);
        return (a, b);
    }

    [Fact]
    public void Asking_gets_the_shared_alts_but_never_a_hidden_one()
    {
        var (a, b) = Friends();
        b.Call("friendsCommand", "ask jdybeck#1111"); // any case
        Talk(a, b);
        Assert.Equal("Drygbeck", Str(b, "WoWCharacterTrackerDB.friends['JDybeck#1111'].chars['Player-1-0000000C'].char"));
        Assert.True(b.Lua("return WoWCharacterTrackerDB.friends['JDybeck#1111'].chars['Player-1-0000000D']").IsNil());
        Assert.Contains(b.Said(), s => s.StartsWith("Asked JDybeck#1111"));
    }

    [Fact]
    public void Alts_go_once_a_session_and_asking_again_too_soon_waits()
    {
        var (a, b) = Friends();
        b.Call("friendsCommand", "ask JDybeck#1111");
        Talk(a, b);
        b.Call("friendsCommand", "ask JDybeck#1111");
        Assert.Contains(b.Said(), s => s.Contains("a moment ago"));
        a.Run(31);
        b.Run(31);
        b.Call("friendsCommand", "ask JDybeck#1111");
        b.Run(1);
        Deliver(b, a, BAccount);
        a.Run(1);
        Assert.Contains(a.Sent(), m => m.Get("text").String.Contains("char=Deadbeck"));
        Assert.DoesNotContain(a.Sent(), m => m.Get("text").String.Contains("char=Drygbeck"));
    }

    // A, who doesn't share with Zeb yet, gets a request from Zeb's game account.
    static Client Asked(string settings = "{}")
    {
        var a = new Client(A, AFriends, ADb("{ enabled = true, at = 100 }"), settings);
        a.Receive("1:REQ:1:1:1:", BAccount);
        return a;
    }

    static void Answer(Client a, string choice) =>
        a.Script.Call(a.Ns.Get("answerFriendRequest"), a.Lua("return shown[#shown].data"), choice);

    [Fact]
    public void A_request_shows_a_popup_and_allow_shares_the_defaults()
    {
        var a = Asked("{ friendNames = { ['Zeb#2222'] = 'Zebbe' } }");
        Assert.Equal("CHARACTERTRACKER_FRIEND_REQUEST", Str(a, "shown[1].name"));
        Assert.Equal("Zebbe (Zeb#2222)", Str(a, "shown[1].arg"));
        Answer(a, "allow");
        a.Run(1);
        Assert.True(a.Lua("return ns.shareSettings().contacts['Zeb#2222'].share").Boolean);
        Assert.True(a.Lua("return ns.shareSettings().contacts['Zeb#2222'].gear").Boolean);
        Assert.False(a.Lua("return ns.shareSettings().contacts['Zeb#2222'].gold").Boolean);
        Assert.Contains(a.Sent(), m => m.Get("text").String.Contains("char=Deadbeck"));
        Assert.Contains(a.Sent(), m => m.Get("text").String.Contains("char=Drygbeck"));
        Assert.DoesNotContain(a.Sent(), m => m.Get("text").String.Contains("money=")); // gold is off by default
    }

    [Fact]
    public void Not_now_sends_nothing_and_doesnt_ask_again_this_session()
    {
        var a = Asked();
        Answer(a, "notnow");
        a.Run(31);
        a.Receive("1:REQ:2:1:1:", BAccount);
        a.Run(1);
        Assert.Equal(1, a.Lua("return #shown").Number);
        Assert.Empty(a.Sent());
    }

    [Fact]
    public void Never_blocks_them_and_their_addon_stops_asking()
    {
        var a = new Client(A, AFriends, ADb("{ enabled = true, at = 100 }"));
        var b = new Client(B, BFriends, BDb("{ enabled = true, at = 100 }"));
        b.Call("friendsCommand", "ask JDybeck#1111");
        Talk(a, b);
        Answer(a, "never");
        Talk(a, b);
        Assert.True(a.Lua("return ns.shareSettings().blocked['Zeb#2222']").Boolean);
        Assert.False(b.Lua("return WoWCharacterTrackerDB.friends['JDybeck#1111'].refused").IsNil());
        b.Run(31);
        b.Call("friendsCommand", "ask JDybeck#1111");
        Assert.Contains(b.Said(), s => s.Contains("said no"));
        a.Run(31);
        a.Receive("1:REQ:9:1:1:", BAccount); // a blocked friend asking again: no popup, no second NO
        a.Run(1);
        Assert.Equal(1, a.Lua("return #shown").Number);
        Assert.Equal(0, Count(a, "NO"));
    }

    [Fact]
    public void Two_requests_within_30_seconds_get_one_answer()
    {
        var (a, _) = Friends();
        a.Lua("sent = {}");
        a.Receive("1:REQ:1:1:1:", BAccount);
        a.Run(10);
        a.Receive("1:REQ:2:1:1:", BAccount);
        a.Run(1);
        Assert.Equal(1, a.Sent().Count(m => m.Get("text").String.Contains("char=Deadbeck")));
    }

    [Fact]
    public void A_request_in_combat_waits_for_the_fight_to_end()
    {
        var a = new Client(A, AFriends, ADb("{ enabled = true, at = 100 }"));
        a.Lua("inCombat = true");
        a.Receive("1:REQ:1:1:1:", BAccount);
        Assert.Equal(0, a.Lua("return #shown").Number);
        a.Lua("inCombat = false");
        a.Call("showNextFriendRequest");
        Assert.Equal(1, a.Lua("return #shown").Number);
    }

    [Fact]
    public void Commands_find_the_friend_and_change_what_they_get()
    {
        var a = new Client(A, AFriends, ADb("nil"));
        a.Call("friendsCommand", "ask nobody#1");
        Assert.Contains(a.Said(), s => s.Contains("isn't on your Battle.net friends list"));
        a.Call("friendsCommand", "share zeb#2222");
        Assert.True(a.Lua("return ns.shareSettings().enabled").Boolean);
        Assert.True(a.Lua("return ns.shareSettings().contacts['Zeb#2222'].share").Boolean);
        a.Call("friendsCommand", "set Zeb#2222 gold on");
        Assert.True(a.Lua("return ns.shareSettings().contacts['Zeb#2222'].gold").Boolean);
        a.Call("friendsCommand", "hide");
        Assert.False(a.Lua("return ns.isCharShared(ns.shareSettings(), 'Player-1-0000000A')").Boolean);
        a.Lua("sent = {}");
        a.Receive("1:HI:1:1:1:0.33.0", BAccount);
        a.Run(1);
        Assert.Equal(0, Count(a, "DATA")); // hidden: not sent
        a.Lua("WoWCharacterTrackerDB.friends = { ['Zeb#2222'] = { chars = {} } }");
        a.Call("friendsCommand", "remove Zeb#2222");
        Assert.True(a.Lua("return ns.shareSettings().contacts['Zeb#2222']").IsNil());
        Assert.True(a.Lua("return WoWCharacterTrackerDB.friends['Zeb#2222']").IsNil());
        a.Call("friendsCommand", "off");
        Assert.False(a.Lua("return ns.shareSettings().enabled").Boolean);
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test companion/WoWCharacterTracker.Tests --filter "FullyQualifiedName~FriendsLuaTests"`
Expected: the new tests FAIL (`friendsCommand`, `answerFriendRequest`, `showNextFriendRequest` are nil, no popup shown).

- [ ] **Step 3: Append the requests part to Friends.lua**

Append at the end of `Friends.lua`:

```lua
-- Requests ------------------------------------------------------------------------------------
local REQUEST_COOLDOWN = 30 -- between two requests to, or from, the same contact
local DEFAULTS = { gear = true, talents = true, location = false, gold = false }
local CATEGORIES = { "gear", "talents", "location", "gold" }

local asked = {}     -- [tag] = when we last asked them
local requested = {} -- [tag] = when they last asked us
local notNow = {}    -- [tag] = true: "Not now" this session
local altsSent = {}  -- [tag] = true: the alts went out this session

-- "Zebbe (Zeb#2222)" with a nickname from the app (Settings.lua friendNames), else the BattleTag.
local function label(tag)
  local names = WoWCharacterTrackerSettings and WoWCharacterTrackerSettings.friendNames
  local name = names and names[tag]
  if name and name ~= "" then return string.format("%s (%s)", name, tag) end
  return tag
end

local function accountsOf(tag)
  local out = {}
  for _, f in ipairs(online()) do
    if f.tag == tag then out[#out + 1] = f.account end
  end
  return out
end

-- Your shared characters other than the one being played.
local function sharedAlts()
  local s, me, out = ns.shareSettings(), UnitGUID("player"), {}
  for guid in pairs(WoWCharacterTrackerDB.chars or {}) do
    if guid ~= me and ns.isCharShared(s, guid) then out[#out + 1] = guid end
  end
  table.sort(out)
  return out
end

-- Everything a contact may see, for one who asked: the character being played, and the alts once a session.
local function answer(f, contact)
  sendCurrent(f, contact, false, true)
  if altsSent[f.tag] then return end
  altsSent[f.tag] = true
  for _, guid in ipairs(sharedAlts()) do
    local text = charText(guid, contact)
    if text then ns.comms.send(f.account, "DATA", text) end
  end
end

-- Makes tag a contact whose characters we keep, and shares ours too when share is true (default
-- categories for any not set yet). Turns sharing with friends on. Returns the contact.
local function addContact(tag, share)
  ns.editShare(function(s)
    s.enabled = true
    local c = s.contacts[tag] or {}
    c.receive = true
    if share then
      c.share = true
      for k, v in pairs(DEFAULTS) do
        if c[k] == nil then c[k] = v end
      end
    end
    s.contacts[tag] = c
  end)
  return ns.shareSettings().contacts[tag]
end

-- A friend asks for our characters: answered when we share with them, else asked about in a popup.
function on.REQ(f, contact)
  local now = time()
  if requested[f.tag] and now - requested[f.tag] < REQUEST_COOLDOWN then return end
  requested[f.tag] = now
  heard[f.account] = true
  if contact and contact.share then return answer(f, contact) end
  if not notNow[f.tag] then ns.offerFriendRequest(f) end
end

-- The popup: one at a time, never in combat (PLAYER_REGEN_ENABLED shows the next one).
local waitingRequests = {}
local showing

function ns.offerFriendRequest(f)
  if showing and showing.tag == f.tag then return end
  for _, w in ipairs(waitingRequests) do
    if w.tag == f.tag then return end
  end
  table.insert(waitingRequests, f)
  ns.showNextFriendRequest()
end

function ns.showNextFriendRequest()
  if showing or #waitingRequests == 0 then return end
  if InCombatLockdown and InCombatLockdown() then return end
  showing = table.remove(waitingRequests, 1)
  ns.popup("CHARACTERTRACKER_FRIEND_REQUEST", {
    text = "%s wants to see your characters (Character Tracker).",
    button1 = "Allow", button2 = "Not now", button3 = "Never",
    OnAccept = function(_, data) ns.answerFriendRequest(data, "allow") end,
    OnCancel = function(_, data) ns.answerFriendRequest(data, "notnow") end,
    OnAlt = function(_, data) ns.answerFriendRequest(data, "never") end,
  }, label(showing.tag), showing)
end

-- Allow shares the defaults with them, Not now waits for the next session, Never blocks them and
-- tells their addon to stop asking.
function ns.answerFriendRequest(f, choice)
  showing = nil
  if f then
    if choice == "allow" then
      answer(f, addContact(f.tag, true))
      ns.say(label(f.tag) .. " can now see your shared characters. /wct friends to change what they get.")
    elseif choice == "never" then
      ns.editShare(function(s) s.blocked[f.tag] = true end)
      saidNo[f.tag] = true
      ns.comms.send(f.account, "NO", "")
    else
      notNow[f.tag] = true
    end
  end
  ns.showNextFriendRequest()
end

-- /wct friends ---------------------------------------------------------------------------------
-- The Battle.net friend's BattleTag as the game writes it, from what was typed (any case), or nil.
function ns.findFriendTag(typed)
  typed = typed and typed:lower()
  if not typed or typed == "" or not (BNGetNumFriends and C_BattleNet and C_BattleNet.GetFriendAccountInfo) then return nil end
  for i = 1, (BNGetNumFriends() or 0) do
    local info = C_BattleNet.GetFriendAccountInfo(i)
    if info and info.battleTag and info.battleTag:lower() == typed then return info.battleTag end
  end
  return nil
end

local HELP = {
  "/wct friends lists your contacts and what they get.",
  "/wct friends on or off turns sharing with Battle.net friends on or off.",
  "/wct friends ask <BattleTag> asks a friend for their characters.",
  "/wct friends share <BattleTag> shares your characters with them (gear and talents to start).",
  "/wct friends set <BattleTag> <gear|talents|location|gold> <on|off> changes what they get.",
  "/wct friends stop <BattleTag> stops sharing with them, remove <BattleTag> forgets them and what they sent.",
  "/wct friends hide or unhide keeps this character from friends, or shares it again.",
  "/wct friends unblock <BattleTag> lets someone you said Never to ask again.",
}

local IS_CATEGORY = {}
for _, k in ipairs(CATEGORIES) do IS_CATEGORY[k] = true end

local function listContacts()
  local s, me = ns.shareSettings(), UnitGUID("player")
  ns.say(string.format("Sharing with friends is %s. This character is %s.", s.enabled and "on" or "off",
    ns.isCharShared(s, me) and "shared" or "hidden"))
  local onlineTags = {}
  for _, f in ipairs(online()) do onlineTags[f.tag] = true end
  local any = false
  for tag, c in pairs(s.contacts) do
    any = true
    local what = {}
    for _, k in ipairs(CATEGORIES) do
      if c[k] then what[#what + 1] = k end
    end
    local got = WoWCharacterTrackerDB.friends and WoWCharacterTrackerDB.friends[tag]
    local count = 0
    for _ in pairs(got and got.chars or {}) do count = count + 1 end
    ns.say(string.format("%s%s: %s, %s, %d of their characters here.", label(tag), onlineTags[tag] and " (online)" or "",
      c.share and ("gets the basics" .. (#what > 0 and (", " .. table.concat(what, ", ")) or "")) or "gets nothing",
      c.receive and "theirs kept" or "theirs not kept", count))
  end
  if not any then ns.say("No contacts yet. /wct friends ask <BattleTag> or /wct friends share <BattleTag>.") end
end

local function ask(tag)
  local now = time()
  if asked[tag] and now - asked[tag] < REQUEST_COOLDOWN then return ns.say("Asked " .. label(tag) .. " a moment ago.") end
  local got = WoWCharacterTrackerDB.friends and WoWCharacterTrackerDB.friends[tag]
  if got and got.refused then return ns.say(label(tag) .. " said no to sharing.") end
  addContact(tag, false)
  local accounts = accountsOf(tag)
  if #accounts == 0 then return ns.say(label(tag) .. " isn't online in this game right now.") end
  asked[tag] = now
  for _, account in ipairs(accounts) do ns.comms.send(account, "REQ", "") end
  ns.say("Asked " .. label(tag) .. ". What they share reaches the app on your next /reload or logout.")
end

local TAG_COMMANDS = { ask = true, share = true, stop = true, remove = true, unblock = true, set = true }

function ns.friendsCommand(args)
  local cmd, rest = strtrim(args or ""):match("^(%S*)%s*(.-)$")
  cmd = (cmd or ""):lower()
  if cmd == "" then return listContacts() end
  if cmd == "on" or cmd == "off" then
    ns.editShare(function(s) s.enabled = cmd == "on" end)
    return ns.say("Sharing with friends is now " .. cmd .. ".")
  end
  if cmd == "hide" or cmd == "unhide" then
    local me = UnitGUID("player")
    if me then ns.editShare(function(s) s.chars[me] = cmd == "unhide" end) end
    return ns.say(cmd == "hide" and "Friends won't get this character anymore." or "Friends get this character again.")
  end
  if not TAG_COMMANDS[cmd] then
    for _, line in ipairs(HELP) do ns.say(line) end
    return
  end
  local typed, what, onOff = rest:match("^(%S+)%s*(%S*)%s*(%S*)$")
  local tag = ns.findFriendTag(typed)
  if not tag then return ns.say((typed or "That") .. " isn't on your Battle.net friends list.") end
  if cmd == "ask" then return ask(tag) end
  if cmd == "share" then
    addContact(tag, true)
    ns.friendsTick()
    return ns.say(label(tag) .. " gets your shared characters (gear and talents to start). They need the addon, with you as a contact.")
  end
  if cmd == "unblock" then
    ns.editShare(function(s) s.blocked[tag] = nil end)
    return ns.say(label(tag) .. " can ask again.")
  end
  local contact = ns.shareSettings().contacts[tag]
  if not contact then return ns.say(label(tag) .. " isn't a contact. /wct friends share " .. tag .. " adds them.") end
  if cmd == "stop" then
    ns.editShare(function(s) s.contacts[tag].share = false end)
    return ns.say(label(tag) .. " doesn't get your characters anymore.")
  end
  if cmd == "remove" then
    ns.editShare(function(s) s.contacts[tag] = nil end)
    if WoWCharacterTrackerDB.friends then WoWCharacterTrackerDB.friends[tag] = nil end
    return ns.say(label(tag) .. " is forgotten, with what they sent.")
  end
  what, onOff = what:lower(), onOff:lower()
  if not IS_CATEGORY[what] or (onOff ~= "on" and onOff ~= "off") then
    return ns.say("/wct friends set <BattleTag> <gear|talents|location|gold> <on|off>")
  end
  ns.editShare(function(s) s.contacts[tag][what] = onOff == "on" end)
  ns.say(string.format("%s: %s is now %s.", label(tag), what, onOff))
end
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test companion/WoWCharacterTracker.Tests --filter "FullyQualifiedName~FriendsLuaTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add addon/WoWCharacterTracker/Friends.lua companion/WoWCharacterTracker.Tests/FriendsLuaTests.cs
git commit -m "feat(addon): ask friends for their characters, with a popup to allow it" -m "Co-Authored-By: Claude <noreply@anthropic.com>" -m "Claude-Session: 5273ae34-7426-4d14-b965-12f2bb2aaef0"
```

---

### Task 6: Wiring it into the addon

**Files:**
- Modify: `addon/WoWCharacterTracker/WoWCharacterTracker.toc`
- Modify: `addon/WoWCharacterTracker/Core.lua:31-48` (`EVENTS`)
- Modify: `addon/WoWCharacterTracker/WoWCharacterTracker.lua` (`ADDON_LOADED`, `PLAYER_ENTERING_WORLD`, `PLAYER_REGEN_ENABLED`, new handlers)
- Modify: `addon/WoWCharacterTracker/Commands.lua:76-110`
- Modify: `addon/WoWCharacterTracker/Options.lua` (page height line 18, new section after `askLast`, `reloadTitle` anchor line 183)
- Test: `companion/WoWCharacterTracker.Tests/FriendsLuaTests.cs`

**Interfaces:**
- Consumes: `ns.comms.receive`, `ns.friendsTick`, `ns.startFriends`, `ns.showNextFriendRequest`, `ns.friendsCommand`, `ns.shareSettings`, `ns.editShare`, `ns.isCharShared`.
- Produces: nothing new for other tasks.

- [ ] **Step 1: Write the failing tests**

Add to `FriendsLuaTests`:

```csharp
    [Fact]
    public void The_toc_has_every_file_and_loads_the_friend_files_after_what_they_use()
    {
        var lines = File.ReadAllLines(Path.Combine(AddonLuaTests.AddonDir, "WoWCharacterTracker.toc")).Where(l => l.EndsWith(".lua")).ToList();
        Assert.Equal(Directory.GetFiles(AddonLuaTests.AddonDir, "*.lua").Select(f => Path.GetFileName(f)).Order(), lines.Order());
        Assert.True(lines.IndexOf("Shared.lua") < lines.IndexOf("FriendData.lua"));
        Assert.True(lines.IndexOf("FriendData.lua") < lines.IndexOf("Comms.lua"));
        Assert.True(lines.IndexOf("Comms.lua") < lines.IndexOf("Friends.lua"));
        Assert.True(lines.IndexOf("Friends.lua") < lines.IndexOf("Options.lua"));
    }

    [Fact]
    public void The_friends_command_gets_what_was_typed_with_its_case()
    {
        var c = new Client(A, "{}", files: ["Shared.lua"]);
        c.Lua("""
            SlashCmdList = {}
            got = nil
            ns.friendsCommand = function(args) got = args end
            """);
        c.Script.Call(c.Script.LoadString(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, "Commands.lua"))), "WoWCharacterTracker", c.Ns);
        c.Lua("SlashCmdList.CHARACTERTRACKER('Friends ask Zeb#2222')");
        Assert.Equal(" ask Zeb#2222", Str(c, "got"));
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test companion/WoWCharacterTracker.Tests --filter "FullyQualifiedName~FriendsLuaTests"`
Expected: both FAIL (toc doesn't list the new files; `/wct friends` shows level times instead).

- [ ] **Step 3: Add the files to the .toc**

In `WoWCharacterTracker.toc`, add `FriendData.lua` right after `Shared.lua`, and `Comms.lua` then `Friends.lua` right after `Ask.lua`:

```
Settings.lua
Shared.lua
FriendData.lua
Core.lua
Character.lua
Inventory.lua
LiveGrid.lua
Events.lua
PvP.lua
Announce.lua
Ask.lua
Comms.lua
Friends.lua
NoteBox.lua
Options.lua
Commands.lua
WoWCharacterTracker.lua
```

- [ ] **Step 4: Register the events**

In `Core.lua`, in `EVENTS`, add a line before the "Refresh the character snapshot" comment:

```lua
  "BN_CHAT_MSG_ADDON", "BN_FRIEND_INFO_CHANGED",                                  -- sharing with Battle.net friends
```

- [ ] **Step 5: Hook up the dispatcher**

In `WoWCharacterTracker.lua`:

In `on.ADDON_LOADED`, after `db.xpTable = ...`, add:

```lua
  db.friends = db.friends or {}           -- [battleTag] = what that friend shares (Friends.lua)
```

In `on.PLAYER_ENTERING_WORLD`, inside `if not enteredWorld then`, after `C_Timer.After(15, ns.checkIn)`, add:

```lua
    C_Timer.After(20, ns.startFriends) -- after the snapshot, which is what friends get
```

In `on.PLAYER_REGEN_ENABLED`, after `ns.showNextAsk()`, add:

```lua
  ns.showNextFriendRequest() -- a friend's request that came during the fight
```

Before the final `ns.frame:SetScript("OnEvent", ...)`, add:

```lua
-- Sharing with Battle.net friends (Comms.lua, Friends.lua). The friends list changes in bursts
-- at login, so one look a few seconds after.
function on.BN_CHAT_MSG_ADDON(prefix, text, channel, senderID) ns.comms.receive(prefix, text, channel, senderID) end
on.BN_FRIEND_INFO_CHANGED = ns.debounce(5, function() ns.friendsTick() end)
```

- [ ] **Step 6: Route /wct friends**

In `Commands.lua`, replace the first two lines of the slash handler:

```lua
SlashCmdList["CHARACTERTRACKER"] = function(msg)
  msg = strtrim(msg or ""):lower()
  if msg == "popup" then
```

with

```lua
SlashCmdList["CHARACTERTRACKER"] = function(msg)
  local typed = strtrim(msg or "")
  msg = typed:lower()
  if msg == "friends" or msg:match("^friends%s") then
    ns.friendsCommand(typed:sub(8)) -- BattleTags keep their case
  elseif msg == "popup" then
```

and in the `help` branch, after the `/wct note` line, add:

```lua
    ns.say("/wct friends shares your characters with Battle.net friends who have the addon. /wct friends help for more.")
```

- [ ] **Step 7: Add the Options section**

In `Options.lua`, change `page:SetSize(640, 1330)` to `page:SetSize(640, 1490)`. After the `askLast` block (ends at line 180), add:

```lua
-- Sharing with Battle.net friends ---------------------------------------------------
local friendsTitle = heading("Share with Battle.net friends", askLast)
local friendsHint = hint("Friends who have the addon see the characters you share. Contacts and what each one gets: /wct friends.", friendsTitle)
local shareOn = checkbox("Share with friends", "Off: nothing is sent, and nothing friends send is kept.",
  function() return ns.shareSettings().enabled == true end,
  function(on) ns.editShare(function(s) s.enabled = on end) end)
shareOn:SetPoint("TOPLEFT", friendsHint, "BOTTOMLEFT", -2, -8)
local shareThis = checkbox("Share this character", nil,
  function() return ns.isCharShared(ns.shareSettings(), UnitGUID("player")) end,
  function(on)
    local me = UnitGUID("player")
    if me then ns.editShare(function(s) s.chars[me] = on end) end
  end)
shareThis:SetPoint("TOPLEFT", shareOn, "BOTTOMLEFT", 0, -18)
local shareNew = checkbox("Share new characters", "Characters you haven't shared or hidden yet.",
  function() return ns.shareSettings().newChars ~= false end,
  function(on) ns.editShare(function(s) s.newChars = on end) end)
shareNew:SetPoint("TOPLEFT", shareThis, "BOTTOMLEFT", 0, -2)
```

and change `local reloadTitle = heading("Keeping the app up to date", askLast)` to `local reloadTitle = heading("Keeping the app up to date", shareNew)`.

- [ ] **Step 8: Run all tests**

Run: `dotnet test companion/WoWCharacterTracker.Tests`
Expected: PASS (all of `FriendsLuaTests`, every `AddonLuaTests` test, `Addon_file_compiles` for all 17 Lua files).

- [ ] **Step 9: Commit**

```bash
git add addon/WoWCharacterTracker companion/WoWCharacterTracker.Tests/FriendsLuaTests.cs
git commit -m "feat(addon): turn on friend sharing in game with /wct friends and the options page" -m "Co-Authored-By: Claude <noreply@anthropic.com>" -m "Claude-Session: 5273ae34-7426-4d14-b965-12f2bb2aaef0"
```

---

### Task 7: Docs and the in-game checklist

**Files:**
- Create: `.claude/notes/friends.md`
- Modify: `CLAUDE.md` (notes table)
- Modify: `.claude/notes/addon.md` (first bullet, the `.toc` load order)
- Modify: `docs/GUIDE.md` (`## In game` and `## In-game commands`)
- Modify: `CHANGELOG.md`

**Interfaces:** none.

- [ ] **Step 1: Write `.claude/notes/friends.md`**

```markdown
# Friends (sharing with Battle.net friends)

Spec: `docs/superpowers/specs/2026-11-06-friends-sharing-design.md`. Step 1 (addon) is built; the app's Friends page (steps 2 and 3) is not.

- Files: `FriendData.lua` (one character as `key=value;...` text, field allowlist and caps, `ns.encodeFriendChar`/`ns.decodeFriendChar`), `Comms.lua` (the only sender/receiver of `C_BattleNet.SendGameData`/`BN_CHAT_MSG_ADDON`, prefix `WCTF`, wire `protocol:kind:id:part:total:chunk`, 240-byte chunks, paced, waits out combat/encounters/`InChatMessagingLockdown`, handler errors kept in `ns.lastCommsError`), `Friends.lua` (settings, contacts, cadence, popup, `/wct friends`).
- Kinds: HI (once per session per game account), HEY (answer, never answered), REQ, DATA (one character), NO (Never).
- Settings: `WoWCharacterTrackerDB.share` vs Settings.lua `share`, newest `at` wins (own time, not `inGameAt`). Nicknames: Settings.lua `friendNames`. Received: `WoWCharacterTrackerDB.friends[battleTag]`.
- Off means off: nothing sent, nothing kept, no popups. Asking only adds a receive-only contact; Allow shares gear and talents.
- Tests: `FriendsLuaTests` runs two MoonSharp clients against each other (`Talk`, `Deliver`).
- Not yet checked in game: everything Battle.net (see the open questions in the spec).
```

- [ ] **Step 2: Link it from CLAUDE.md**

In the notes table in `CLAUDE.md`, add a row after the `streaming.md` row:

```markdown
| `.claude/notes/friends.md` | sharing with Battle.net friends: Comms.lua, Friends.lua, FriendData.lua, the Friends page |
```

- [ ] **Step 3: Update the load order in addon.md**

In the first bullet of `.claude/notes/addon.md`, the sentence "The `.toc` loads, in order:" lists the files. Insert `` `FriendData.lua` (a character as text for friends, see `friends.md`), `` after the `Shared.lua (...)` entry, and `` `Comms.lua` and `Friends.lua` (sharing with Battle.net friends, see `friends.md`), `` after the `Ask.lua`-related text (before `NoteBox.lua`). Also add `ns.copy` and `ns.popup` to the list of what `Shared.lua` has.

- [ ] **Step 4: Add the GUIDE section**

In `docs/GUIDE.md`, at the end of the `## In game` section, add:

```markdown
### Sharing with Battle.net friends (new, in game only for now)

Friends on your Battle.net friends list who have the addon can see the characters you share: name, level, class, spec and item level, plus gear and talents, and location and gold if you allow it. It's off until you turn it on, and each friend only gets what you picked for them.

- `/wct friends ask <BattleTag>` asks a friend for theirs. They get a popup: Allow, Not now or Never.
- `/wct friends share <BattleTag>` shares yours with them (gear and talents to start), `/wct friends set <BattleTag> gold on` and so on changes what they get.
- `/wct friends hide` keeps the character you're on from friends.
- Under Esc, Options, AddOns you can turn sharing on or off, and choose whether this character and new characters are shared.

It all goes through Battle.net while you're both online, nothing is shown in chat, and nothing goes out in combat. A Friends page in the app is coming next.
```

And in `## In-game commands`, add a line for `/wct friends` in the same style as the others there (read the section first and match its format).

- [ ] **Step 5: Add the CHANGELOG line**

At the top of `CHANGELOG.md`, under the coming version's section (add `## v0.33.0` with a `### New` heading above `## v0.32.0` if it isn't there yet), add:

```markdown
- **Share with Battle.net friends** (in game, new): friends with the addon can see the characters you share, with gear and talents, and location and gold if you allow it. Off until you turn it on. `/wct friends help` in game. The app's Friends page comes next.
```

- [ ] **Step 6: Run all tests once more**

Run: `dotnet test companion/WoWCharacterTracker.Tests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add .claude/notes/friends.md .claude/notes/addon.md CLAUDE.md docs/GUIDE.md CHANGELOG.md
git commit -m "docs: describe sharing with battle.net friends" -m "Co-Authored-By: Claude <noreply@anthropic.com>" -m "Claude-Session: 5273ae34-7426-4d14-b965-12f2bb2aaef0"
```

- [ ] **Step 8: Hand JDybeck the in-game checklist**

Not automatable. Report this list to JDybeck (and add the answers to `friends.md` and the spec's open questions once known):

1. Two clients that are Battle.net friends (a friend, or a second account), both on Forever, both with `/wct friends on`.
2. A: `/wct friends share <B's BattleTag>`. B: `/wct friends ask <A's BattleTag>`. Expect: no popup (A already shares), nothing in either chat, and after B's `/reload`, `WoWCharacterTrackerDB.friends` in B's SavedVariables has A's characters.
3. A fresh pair where A doesn't share yet: B asks, A sees the popup with three buttons (Allow, Not now, Never). Check each button and Esc.
4. Do it in a group, in a dungeon and in combat: sends wait, the popup waits until the fight ends, no "blocked from an action".
5. Cross-realm: A and B on different realms.
6. `/wct debug` after: `ns.lastCommsError` empty. If messages never arrive, check whether `C_BattleNet.SendGameData` exists on Forever (or only `BNSendGameData`).
7. Raise `comms.CHUNK` (Comms.lua) to 4000 in a test build and see whether long gear texts still arrive whole. That measures the real limit.

---

## Self-review notes (done while writing)

- Spec coverage for step 1: consent (Tasks 4-5), cadence (Task 4), protocol and validation (Tasks 2-3), addon files and settings (Tasks 1-6), privacy (friend data only in `WoWCharacterTrackerDB.friends`, never in `events`; the app-side privacy tests belong to step 2), failure cases (Comms tests, Review Focus), docs (Task 7). App parts are steps 2 and 3, separate plans after the in-game check.
- Names used across tasks: `ns.encodeFriendChar`/`ns.decodeFriendChar`, `ns.comms.send/receive/onReceive/flush/canSend`, `ns.shareSettings`, `ns.editShare`, `ns.isCharShared`, `ns.friendsOnline`, `ns.friendsTick`, `ns.startFriends`, `ns.offerFriendRequest`, `ns.showNextFriendRequest`, `ns.answerFriendRequest`, `ns.findFriendTag`, `ns.friendsCommand`, `ns.popup`, `ns.copy`.
