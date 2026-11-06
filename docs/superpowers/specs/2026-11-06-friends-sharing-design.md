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
- **Sender identity comes from the game**: `senderID` is the sender's game account, matched against our own Battle.net friends list (the same walk as above) to get the `battleTag`. The game sets it, so it can't be spoofed by the friend's addon, and only Battle.net friends can send at all.
- **Off means off**: with sharing off nothing is sent and nothing received is kept or shown, requests included.
- **Silent by default**: addon messages never show in chat or in a Battle.net conversation. The addon prints nothing about sharing on its own, except a "Did you know?" tip at most once a week while sharing is on and you share with nobody yet; otherwise the only visible things are the request popup, `/wct friends` output and the Options page. We use `SendGameData`, not the logged variants.
- **`accountName` is never stored** (can be a Real ID real name). Only the BattleTag, plus the nickname the user gives.

## Consent

- Sharing is on by default (`share.enabled`), but nothing goes to anyone without a contact: a friend asks and you allow, or you share with them yourself. Off: no requests, nothing sent or kept.
- **Sending**: the user adds contacts from their Battle.net list. What friends get is one general choice per category (`share.defaults`: `gear`, `talents`, `location`, `gold`; gear, talents and location on, gold off until turned on), and any contact can have their own value per category that overrides it (nil follows the general choice).
- **Receiving**: the addon only keeps data from contacts with `receive` on. Data from anyone else is dropped without a message.
- **Requests**: B asks A (`REQ`). Asking makes A a contact on B's side with `receive` on and nothing shared: asking for someone's data never shares your own. If A doesn't share with B yet, A gets a popup: "<B> wants to see your characters" with Allow / Not now / Never. Allow shares the default categories with B (and turns `receive` on). Not now sends nothing back and doesn't ask again this session. Never adds B to `blocked` and replies `NO`, so B's addon stops asking (until B gets data from A again).
- **Sharing first**: A can also share with B without being asked (`/wct friends share`). B only keeps it once B has A as a contact with `receive` on.
- **Own characters**: each is shared or hidden (`share.chars[guid] = true/false`). A character with no entry follows `share.newChars` (on: shared).
- **Receiver side hiding**: the app can hide a contact's characters from the Friends page (`Config.HiddenFriendCharacters`). Local only, nothing is sent back.
- Removing a contact removes their stored data in the addon and, on the next sync, in the app.

## Cadence

- Character being played, to accepted contacts:
  - in your group: at most every 2 minutes
  - online elsewhere: at most every 10 minutes
  - only when the payload for that contact changed (last sent string per contact and character), plus a heartbeat every 30 minutes
- Alts (last-known snapshots from `WoWCharacterTrackerDB.chars`/`items`): only on `REQ`, at most once per session per contact.
- "Refresh now" sends `REQ` right away, with a 30 s cooldown per contact (`/wct friends ask`). In game only: the app can't send anything, it only writes Settings.lua.
- Group membership: a contact's game account has `playerGuid`, compared with the party/raid members' GUIDs.
- Nothing goes out in combat, in an encounter (`IsEncounterInProgress`) or while `C_ChatInfo.InChatMessagingLockdown()` is true. Those sends wait (checked again every 5 s while anything is queued).

## Protocol

Prefix `WCTF`, registered with `C_ChatInfo.RegisterAddonMessagePrefix`. The prefix never changes; the protocol version is the first field of every message, so a newer addon can still tell an older one to update.

Every message: `protocol:type:id:part:total:chunk`. Chunk size is one constant, starting at 240 bytes; raise it once the real `SendGameData` limit is measured in game. Wire text has no `|` (dropped on send and on arrival).

| Type | Body | When |
|------|------|------|
| `HI` | addon version | once per session to each online contact's game account |
| `HEY` | addon version | the answer to `HI` (never answered, so no ping-pong) |
| `REQ` | nothing | "please send your data" (`/wct friends ask`) |
| `DATA` | one character snapshot | cadence, after `HI`/`HEY`, or answer to `REQ` |
| `NO` | nothing | the user chose Never |

A contact that never answers `HI` (no addon) gets nothing else that session.

### Snapshot

Always: `guid`, `char`, `realm`, `level`, `class`, `classFile`, `race`, `raceFile`, `faction`, `spec`, `ilvl`, `at` (the snapshot's `lastSeen`). Then only the allowed categories:

- gear: `gear` (`slot~itemId,...` as in `items[guid].gear`) and `enchants` (`items[guid].enchants` lines)
- talents: `talents`, `talentCalc`
- location: `zone`
- gold: `money` (copper)

Built from the saved snapshot (`WoWCharacterTrackerDB.chars[guid]`, kept fresh by `saveSnapshot`) and `items[guid]`, so the character being played and alts go through the same code. Full field names (`key=value`, `;` between fields, `\` escapes for `\`, `;` and line breaks): a few more bytes, but no coupling to the grid's one-letter `SHORT` keys. Change detection compares the text without `at`.

### Validating what comes in (addon and again in the app)

- Drop: unknown sender, unknown type, other protocol version (recorded so the app can show "needs to update"), reassembled message over 8 KB, half messages after 30 s.
- Keep only known fields; numbers must parse as numbers; strings capped in length.
- Strip `|` (WoW escape sequences for links and textures).
- At most 50 characters per contact.

## Addon

- `Comms.lua`: the only file that sends or receives addon messages. Prefix, chunking and reassembly, send queue with pacing, combat/encounter/lockdown guards, back off when `SendGameData` reports throttling, caps on half messages. Knows nothing about friends (hands whole messages to `ns.comms.onReceive`), so guild map pins can use it later.
- `FriendData.lua`: one character as text and back, with the field allowlist and caps. No game calls, so the tests load it.
- `Friends.lua`: sharing settings, contacts, Battle.net friends list walk, consent and popup (a shared `ns.popup` helper in Shared.lua that `askReload` uses too, no Blizzard globals replaced), cadence, change detection, what to keep, `/wct friends`.
- Options page: sharing on/off, share new characters, share this character, and a pointer to `/wct friends` for contacts. A full contacts list is the app's job (step 3).
- Saved data: `WoWCharacterTrackerDB.friends[battleTag] = { seen, version, protocol, refused, chars = { [guid] = { ...fields, received } } }`.
- Sharing settings: `share = { enabled, newChars, chars = { [guid] = bool }, contacts = { [battleTag] = { share, receive, gear, talents, location, gold } }, blocked = { [battleTag] = true }, at }`, in `WoWCharacterTrackerDB.share` (changed in game) and Settings.lua `share` (from the app, step 3). The newest `at` wins, like notes. Its own time, not the In game settings' one: those share one `at`, and an app that doesn't write `share` yet would otherwise wipe it the next time its In game page changes.
- Nicknames: Settings.lua `friendNames[battleTag]`, owned by the app (step 3), only read by the addon.
- Taint rules from `addon.md` apply: no Blizzard globals replaced, no CVar changes, nothing protected touched.

## App

- Models: `FriendContact` (BattleTag, nickname, seen, version, characters) and `FriendCharacter` (snapshot fields, gear, talents, location, gold, `At`).
- `Services/FriendData`: parses `WoWCharacterTrackerDB.friends` next to `SavedVariables`, validates again with the same rules as the addon.
- Config: `Sharing` (mirrors `share`: the app reads `WoWCharacterTrackerDB.share` on sync, takes it when its `at` is newer, and writes Settings.lua `share` with a newer `at` when changed in the app), `FriendNicknames[battleTag]` (written to Settings.lua `friendNames` so the addon shows the same name; empty falls back to the BattleTag), `HiddenFriendCharacters`.
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
