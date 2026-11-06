# Friends: sharing character data with Battle.net contacts

Status: design, approved in chat 2026-11-06. Not built.

## Goal

Let players share basic character data with Battle.net friends they pick, and see what those friends share on a new Friends page in the app. Opt-in on both sides, per contact, per category, per character. No server: the addons talk to each other in game and the app reads the result from the saved file.

Success: two players who are Battle.net friends, both with the addon and the feature on, can see each other's characters (gear, talents, and location and gold if allowed) in the app, at most a few minutes old while both are online, and "as of last time" otherwise.

Out of scope for v1: guild-wide sharing, guild map pins (a later feature on the same comms layer), live push to the app through the screenshot grid, a cloud relay, in-game display of friend data beyond the request popup and `/wct friends`.

## Decisions

- **Contact = Battle.net friend, keyed by BattleTag.** Players rarely add character friends now. `bnetAccountID` is believed to change between sessions, so it is never stored (to confirm in game).
- **Transport = `C_BattleNet.SendGameData(gameAccountID, prefix, data)`, received on `BN_CHAT_MSG_ADDON` (`prefix, text, channel, senderID`).** Checked in wow-ui-source `live` (Retail). Not yet checked on the Forever client.
- **We only send to friends who are online in the same game**: walking the Battle.net friends list (`GetFriendAccountInfo`, `GetFriendNumGameAccounts`, `GetFriendGameAccountInfo`), the game account has `isOnline`, `clientProgram` WoW and `wowProjectID == WOW_PROJECT_ID`.
- **Sender identity comes from the game**: `senderID` -> `C_BattleNet.GetGameAccountInfoByID` -> `playerGuid` -> `C_BattleNet.GetAccountInfoByGUID` -> `battleTag`. The game sets it, so it can't be spoofed by the friend's addon.
- **Silent by default**: addon messages never show in chat or in a Battle.net conversation. The addon prints nothing about sharing on its own; the only visible things are the request popup, `/wct friends` output and the Options page. We use `SendGameData`, not the logged variants.
- **`accountName` is never stored** (can be a Real ID real name). Only the BattleTag, plus the nickname the user gives.

## Consent

- Sharing is off by default (`share.enabled`).
- **Sending**: the user adds contacts from their Battle.net list. Each contact has categories: `gear`, `talents`, `location`, `gold`. Defaults: gear and talents on, location and gold off.
- **Receiving**: the addon only keeps data from contacts with `receive` on. Data from anyone else is dropped without a message.
- **Requests**: B asks A (`REQ`). If B is not yet a contact of A, A gets a popup: "<B> wants to see your characters" with Allow / Not now / Never. Allow adds B with the default categories (and `receive` on). Not now sends nothing back. Never adds B to `blocked` and replies `NO`, so B's addon stops asking.
- **Own characters**: each is shared or hidden (`share.hidden`, GUIDs that are never sent). `share.newChars` decides whether a character not seen before is shared (on) or hidden (off).
- **Receiver side hiding**: the app can hide a contact's characters from the Friends page (`Config.HiddenFriendCharacters`). Local only, nothing is sent back.
- Removing a contact removes their stored data in the addon and, on the next sync, in the app.

## Cadence

- Character being played, to accepted contacts:
  - in your group: at most every 2 minutes
  - online elsewhere: at most every 10 minutes
  - only when the payload for that contact changed (last sent string per contact and character), plus a heartbeat every 30 minutes
- Alts (last-known snapshots from `WoWCharacterTrackerDB.chars`/`items`): only on `REQ`, at most once per session per contact.
- "Refresh now" sends `REQ` right away, with a 30 s cooldown per contact. In game only (`/wct friends`, a button on the Options page): the app can't send anything, it only writes Settings.lua.
- Group membership: a party/raid member's GUID through `GetAccountInfoByGUID` tells if they are a contact.
- Nothing goes out in combat, in an encounter or while `InChatMessagingLockdown()` is true. Those sends wait and go on `PLAYER_REGEN_ENABLED` (same pattern as `flushAnnouncements`).

## Protocol

Prefix `WCT1` (1 = protocol version), registered with `C_ChatInfo.RegisterAddonMessagePrefix`.

Every message: `type`, message id, part, total, then the body chunk. Chunk size is one constant, starting at 240 bytes; raise it once the real `SendGameData` limit is measured in game.

| Type | Body | When |
|------|------|------|
| `HI` | addon version, protocol version | once when a contact is seen online (login, friend comes online) |
| `REQ` | nothing | "please send your data" (refresh now, first request) |
| `DATA` | one character snapshot | cadence, or answer to `REQ` |
| `NO` | nothing | the user chose Never |

A contact that never answers `HI` gets nothing else until their next login.

### Snapshot

Always: name, realm, level, class, race, spec, ilvl, `at` (time taken). Then only the allowed categories:

- gear: `slot~itemId~enchant` lines, as in `items[guid].gear`
- talents: the existing talents string
- location: zone and subzone
- gold: copper

Built from what the addon already collects (`characterInfo`, the inventory scan), using the existing `SHORT` keys where a field has one. New keys get two letters and go into `Payload.ShortKeys` at the same time (`SharedRulesTests`).

### Validating what comes in (addon and again in the app)

- Drop: unknown sender, unknown type, other protocol version (recorded so the app can show "needs to update"), reassembled message over 8 KB, half messages after 30 s.
- Keep only known fields; numbers must parse as numbers; strings capped in length.
- Strip `|` (WoW escape sequences for links and textures).
- At most 50 characters per contact.

## Addon

- `Comms.lua`: the only file that sends or receives addon messages. Prefix, chunking and reassembly, send queue with pacing, combat/encounter/lockdown guards, back off when `SendGameData` reports throttling, sender check against the accepted list, validation. Built so guild map pins can use it later.
- `Friends.lua`: contacts, consent and popup (reuses what `askReload` uses, no Blizzard globals replaced), cadence, change detection, building snapshots, `/wct friends`.
- Options page: sharing on/off, contacts with categories, own characters shared/hidden, pending requests, blocked. Shows why sharing is paused when Battle.net or its friends list is unavailable (`IsBattleNetFriendsListEnabled`).
- Saved data: `WoWCharacterTrackerDB.friends[battleTag] = { seen, version, chars = { [guid] = { at, data } } }`.
- Settings.lua `share = { enabled, newChars, hidden = {...}, contacts = { [battleTag] = { gear, talents, location, gold, receive } }, blocked = {...}, nicknames = {...}, at }`. In-game edits go through `ns.editSettings`, newest `at` wins, like the In game settings.
- Taint rules from `addon.md` apply: no Blizzard globals replaced, no CVar changes, nothing protected touched.

## App

- Models: `FriendContact` (BattleTag, nickname, seen, version, characters) and `FriendCharacter` (snapshot fields, gear, talents, location, gold, `At`).
- `Services/FriendData`: parses `WoWCharacterTrackerDB.friends` next to `SavedVariables`, validates again with the same rules as the addon.
- Config: `Sharing` (mirrors Settings.lua `share`, synced newest wins through the existing In game settings path: `IN_GAME`, `ReadInGame`, `TakeInGame`), `FriendNicknames[battleTag]`, `HiddenFriendCharacters`. Nicknames are written to Settings.lua so the addon shows the same name; empty falls back to the BattleTag.
- Friends page, one per game tab (Forever friends on the Forever tab):
  - Contacts: nickname (or BattleTag), last heard from, version or "needs update", hint "ask in game to refresh".
  - Chip bar by contact: reuse `ChipFilter`, with an optional label function in `Fill` so the key stays the BattleTag and the chip shows the nickname. With All picked, the list is grouped under a header per contact.
  - Character cards from the `CharacterRow` parts: class, level, spec, ilvl, "Westfall, 2h ago", gold when shared. Hidden ones fold into "N hidden".
  - Opening a character: `CharacterOverview`, `CharacterGear` and the talents view in a read-only mode (no export, remove, notes, posting). Gear keeps tooltips through `ui:ItemTip.Info`, pictures through `ImageCache`.
  - Sharing panel: on/off, contacts with category checkboxes and nickname, own characters shared/hidden, requests, blocked.
- Nothing new is written to disk: friend data lives in the addon's SavedVariables and the app config, both already on the Uninstall page.
- Friend data shows in the app after a /reload or logout, like gear does today.

## Privacy

- Friend data never reaches Discord posts, the stream overlay, `ArmoryExport` or the console log.
- BattleTags stay on the user's PC.

## Failure cases

| Case | Behavior |
|------|----------|
| Contact without the addon | No `HI` answer, nothing more sent until their next login |
| Protocol mismatch | Data ignored, Friends page shows "needs to update" |
| Friend logs off mid-send | Half message dropped after 30 s |
| `SendGameData` throttled | Back off, retry later |
| Combat, encounter, lockdown | Queued, sent out of combat |
| Battle.net down or friends list off | Sharing paused, Options page says why |
| One Battle.net account, several WoW accounts | All characters under the same contact |
| BattleTag renamed | Shows as a new contact (rare, accepted) |

## Testing

- Lua (`AddonLuaTests`, MoonSharp) with a fake clock and fake Battle.net friends list: chunking and reassembly, size caps, `|` stripped, unknown sender dropped, hidden characters and off categories never sent, change detection, 2/10 min cadence, `REQ` cooldown, Never/blocked.
- C#: `FriendData` with broken, oversized and hostile input; sharing settings sync newest wins; `SharedRulesTests` for new keys; `ChipFilter` labels; friend data absent from Discord payloads, stream snapshot and `ArmoryExport`.
- UI: screenshot-mode render of the Friends page with fake friend data in a fake game folder.
- In game (Jesper with a friend or second account), since it can't be faked: `SendGameData` on Forever, its real size limit, whether `bnetAccountID` changes between sessions, sending in groups, instances and combat, cross-realm, the popup.

## Build order

Each step is its own PR on a branch, then a release.

1. Addon: `Comms.lua`, `Friends.lua`, `/wct friends`, Options page. Proves the in-game part first.
2. App: reading friend data, read-only Friends page.
3. App: sharing settings, nicknames, hiding friend characters.

Docs: a Friends section in `docs/GUIDE.md`, a new `.claude/notes/friends.md` (linked from the CLAUDE.md notes table), CHANGELOG lines per step.

## Open questions (answered in game)

- Does `C_BattleNet.SendGameData` exist and work between two Forever clients (and is it `BNSendGameData` there)?
- Max `SendGameData` message size.
- Is `bnetAccountID` stable between sessions? (The design doesn't depend on it.)
- Are Battle.net addon messages restricted in instances or encounters on Forever as on Retail?
- Confirm nothing shows for either player (chat, Battle.net conversation, toast), also when the friend is in a loading screen or goes offline mid-send.
