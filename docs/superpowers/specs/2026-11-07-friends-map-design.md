# Friends on the world map

Status: design, 2026-11-07. Builds on the friends sharing layer (`2026-11-06-friends-sharing-design.md`, `.claude/notes/friends.md`).

## Goal

Battle.net friends who both run the addon can see each other as pins on the world map, live while the map is open. Opt-in, off by default, per friend, like the rest of friends sharing. World map first, the minimap later on the same messages.

Success: two friends in the same game, both with Map position allowed for each other, open the world map and see the other's pin in the right spot within a few seconds, moving while they move. With the setting off, nothing about position goes out at all.

Out of scope for v1: minimap pins, the app showing positions, clicking a pin (whisper, invite, waypoint), guild or party members who aren't Battle.net friends, per-friend update rate.

## What the player sees

- One pin per friend character that's sending: a dot in the class color with a thin border. Built from our own texture or a plain color texture, no Blizzard art in the repo.
- Hover tooltip: nickname (Settings.lua `friendNames`) or BattleTag, character name, level and class, zone name (`C_Map.GetMapInfo(mapID).name`), realm when it differs from ours, and the age ("a few seconds ago", "3 min ago").
- A friend on another realm still gets a pin, drawn as a hollow ring instead of a dot (faded stays reserved for stale), with "other realm" in the tooltip. Battle.net can't tell us the layer, so we never claim "same layer".
- Pins show on the map the friend is on and on the maps above it (continent, world) when the position translates onto them (`C_Map.GetWorldPosFromMapPos` then `C_Map.GetMapPosFromWorldPos` for the shown map, kept only when inside 0..1). Never on dungeon maps.
- Freshness depends on the sender's rate: a pin fades after `max(30, 2 x rate)` seconds without an update and is removed after `max(120, 4 x rate)`. Live (5 s): fades at 30 s, gone at 2 min. Every 5 min: fades at 10 min, gone at 20 min.
- No pin when the friend is in an instance (dungeon, raid, battleground, arena), playing another game (`ns.friendsOnline` already only lists `wowProjectID == WOW_PROJECT_ID`), or hidden by the faction rule. No "hidden" hint in any of these cases.

## Settings

All in `share` (Friends.lua), same newest-`at`-wins sync with the app as today.

- **Map position**, key `map`: `share.defaults.map = false`. A contact's own `map` (true/false) overrides it, nil follows it. Works through `ns.allowedFor` like the other categories, and is a category in CATEGORIES (it's data friends get).
- **Show to the other faction**, key `otherFaction`: `share.defaults.otherFaction = false`, which means hidden from a friend playing the other faction. A contact's own `otherFaction` overrides it. It isn't data, so it goes in a second key list (`RULES = { "otherFaction" }`) that `ns.allowedFor` also walks. Data checkboxes keep listing CATEGORIES only.
- **Update rate**, key `mapRate`: 5 (live, default), 60 or 300 seconds. General only, no per-friend value. Anything else reads as 5.
- Commands: `/wct friends set all map on/off`, `/wct friends set <BattleTag> map on/off/default`, same for `otherfaction`, and `/wct friends maprate live/1m/5m`.

## Messages

Two new kinds through `Comms.lua` (the only sender and receiver). Both fit in one chunk. Older addons ignore them: `ns.comms.onReceive` only calls `on[kind]` when there's a handler.

- `WATCH` (empty body): "I have the world map open". Sent while the world map is shown, to every online contact with `receive` on, renewed every 20 s. A watch expires 30 s after the last one. Closing the map sends nothing, the watch just expires.
- `POS` body `mapID,x,y,rate,age`: x and y in 0..1 rounded to 3 decimals, `rate` the sender's `mapRate`, `age` how many seconds old the reading is as it goes out. An age instead of a time, so the two clocks never need to agree. An empty body means "no position now" and removes the pin.

### Sender

On each tick (every 5 s while anyone watches, nothing at all while nobody does), for each watcher whose watch hasn't expired:

1. Allowed: sharing on, the character being played is shared (`ns.isCharShared`), the contact has `share` on, `ns.allowedFor(s, contact).map`, and the faction rule passes: `otherFaction` allowed, or the friend's `factionName` (Battle.net game account info) equals `UnitFactionGroup("player")`. Unknown faction on either side = not allowed.
2. Position: `C_Map.GetBestMapForUnit("player")` and `C_Map.GetPlayerMapPosition(mapID, "player")`. None, or `IsInInstance()`, = no position.
3. Not allowed or no position: one empty `POS` (so the pin goes), only to a watcher who got a position from us this session and only once until they get one again. Someone who never got one gets nothing, not even the empty one.
4. Rate: a new reading at most every `mapRate` seconds. Between readings, a new watch (first WATCH, or one after its watch expired) gets the last reading as it was, with its real age, so opening and closing the map never gets around the rate. A reading goes to a watcher when it's new to them and it moved, or 20 s after the last one they got (so on slow rates every new reading goes).

### Receiver

- Drops `POS` from anyone who isn't a contact with `receive` on (existing rule in `onReceive`), and from anyone we haven't sent a WATCH to in the last 30 s.
- Keeps `[account] = { mapID, x, y, rate, readAt }` in memory, `readAt` being its own `time()` minus the age. Character, class, level and realm come from `ns.friendsOnline` (Battle.net info), not from the message.
- Nothing is saved: positions never go into SavedVariables. The app never sees them, and nothing about where anyone was stays on disk. Uninstall needs no new entry.

## Files

- `FriendMap.lua` (new, after Friends.lua): the logic, no frames. Watching (sending WATCH, tracking who watches us), the sender tick, rate and last reading, received positions and their freshness (`ns.friendPins()` returns what to draw: tag, label, character, classFile, level, realm, other realm, mapID, x, y, age, faded). Testable in MoonSharp.
- `MapPins.lua` + `MapPins.xml` (new): drawing only. A data provider (`CreateFromMixins(MapCanvasDataProviderMixin)`) added to `WorldMapFrame`, and a virtual pin template whose mixin builds on `MapCanvasPinMixin` (the map's `AcquirePin` needs an XML template name). `RefreshAllData` reads `ns.friendPins()` and translates positions to the shown map. Starts and stops FriendMap's watching from the world map's OnShow/OnHide (hooked with `HookScript`, never replaced).
- `Friends.lua`: `map` in CATEGORIES/DEFAULTS, `RULES` + `otherFaction`, `mapRate` in `normalize`, a way for FriendMap to add handlers to `on` (`ns.onFriendKind(kind, fn)`), the new `/wct friends` words, `factionName` and `realmName` in `ns.friendsOnline`.
- `Window.lua`: the sharing panel gets Map position in its data grid, a Show to the other faction toggle and the rate picker.
- `.toc`: the new files, and the addon version goes up.

Taint: pins only read data and draw. Never replace Blizzard globals or map mixins, no CVars. `MapCanvasMixin:AcquirePin` calls `pin:CheckMouseButtonPassthrough("RightButton")` on every pin (checked in wow-ui-source's `forever` branch), which calls `SetPassThroughButtons`, a known world map taint source. So our own pin mixin gets an empty `CheckMouseButtonPassthrough`, and the pin takes mouse-over only, no clicks (right-click zoom-out still reaches the map). Pins use `PIN_FRAME_LEVEL_GROUP_MEMBER`, the party pins' level. Checked in Forever's API docs: `BNetGameAccountInfo` has `factionName`, `realmName` and `realmID`, and `C_Map.GetWorldPosFromMapPos`/`GetMapPosFromWorldPos` exist.

## App

Settings only, the app never sees positions.

- `Models/Friends.cs`: `GeneralCategories` gets `Map = false` and `OtherFaction = false`, `ShareContact` gets `bool? Map` and `bool? OtherFaction`, `ShareSettings` gets `int MapRate = 5`.
- `Services/FriendData` reads them with the addon's rules (unknown rate = 5), `AddonInstaller.WriteSettings` writes them.
- `FriendsViewModel`/`FriendsPage`: a Map position chip and a Show to the other faction chip (the same like all friends / on / off cycle), and a rate picker (Live, Every minute, Every 5 minutes) in the general sharing panel.
- `GUIDE.md` Friends section, `friends.md`, CHANGELOG lines.

## Testing

- FriendsLuaTests (two MoonSharp clients, `Talk`/`Deliver`), with fake `C_Map`, `UnitFactionGroup`, `IsInInstance` and `factionName` on the fake Battle.net friends:
  - Map off by default: WATCH gets no POS.
  - Map on: WATCH gets a POS, the receiver's `ns.friendPins()` has the pin.
  - The watch expires after 30 s: no more POS.
  - Faction: the other faction gets nothing, unless `otherFaction` is on (general and per friend), and unknown faction gets nothing.
  - Instance or no map position: one empty POS, the pin is gone.
  - Rate 60: a rewatch within the minute gets the old reading with its real age.
  - Freshness cutoffs per rate, and POS without a WATCH from us is dropped.
  - Settings: `map`/`otherFaction` overrides through `ns.allowedFor`, `mapRate` normalizing, the new commands.
- App: FriendData read/write round-trips for the new settings, the chips in screenshot mode (`forever:friends::sharing`).
- In game (JDybeck with a friend or second account), not automatable:
  - Is `factionName` filled in for Forever friends (and is it the same token as `UnitFactionGroup`)?
  - Does the data provider and pin template work on Forever's world map, and on Retail without taint errors (open the map in combat, open Talents after)?
  - Do pins land in the right spot on zone, continent and world maps?
  - Is BN game data fast enough for live (5 s ticks) without hitting the throttle with a few watchers?

## Later

- Minimap pins: the minimap showing a friend in the same zone counts as watching.
- Per-friend update rate (a three-step chip).
- Clicking a pin: whisper, invite, set a waypoint.
