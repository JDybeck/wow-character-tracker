# Addon window, phase 2 (Friends tab) Implementation Plan

> Built by two parallel agents in their own worktrees, merged by the main session. Read only your task, the spec, and the files it names.

**Goal:** The window's Friends tab: find your online Battle.net friends, see invisibly who has the addon, and Ask, Share or Invite with one click. Incoming requests are listed there too, and so is what friends shared with you. No BattleTag typing.

**Spec:** `docs/superpowers/specs/2026-11-06-addon-window-design.md`, section "Friends tab" and "Protocol changes" (PING/PONG only; professions are phase 3). Open question 1 answered: a friend with sharing off still answers PING.

## Global Constraints

- WoW addon Lua 5.1. Plain frames only, no Blizzard globals replaced, nothing protected, no code from other addons. No `|` in UI or chat text unless it's a real WoW code.
- Battle.net addon messages (`ns.comms.send`, kinds in Comms.lua) never show in chat. The **only** thing that may send a real chat message is Invite, and only on the player's click.
- Read `.claude/notes/friends.md` and `.claude/notes/addon.md` if present (they're git-ignored and may be missing in a worktree, that's fine).
- Tests: `dotnet test companion/WoWCharacterTracker.Tests` (xUnit + MoonSharp). `FriendsLuaTests.Client` and its `Talk`/`Deliver` helpers run two clients against each other; `AddonWindowTests` has the frame stubs (`find`, `click`, `strings`).
- Your worktree may start on `main`. First run `git log --oneline -3`: if this plan's commit isn't there, `git reset --hard feat/friends-tab` (the tree is clean at start).
- Commits: Conventional Commits scope `addon`, trailers `Co-Authored-By: Claude <noreply@anthropic.com>` and `Claude-Session: 5273ae34-7426-4d14-b965-12f2bb2aaef0`. Never push. Don't change the `.toc` `## Version`. Brief comments, no em dashes.

## Interfaces (Task A provides, Task B uses)

All in `Friends.lua`. Rows are plain tables; B only reads them.

- `ns.findOnlineFriends()`: rebuilds the online list and PINGs each listed game account not pinged yet this session; at most once every 60 s (a second call within 60 s only rebuilds the list). Returns nothing.
- `ns.onlineFriends() -> { row }`, sorted by label. `row = { tag, account, label, character, classFile, level, zone, hasAddon, contact, sharesWithYou, blocked, invitedAt }`:
  - `character`, `classFile`, `level`, `zone` from the Battle.net game account (`characterName`, `classFilename`, `characterLevel`, `areaName`; any may be nil).
  - `hasAddon`: `true` after a PONG (or any message) from that account this session, `false` once 10 s passed after our PING without an answer, `nil` before we know.
  - `contact`: the `ns.shareSettings().contacts[tag]` table or nil. `sharesWithYou`: `WoWCharacterTrackerDB.friends[tag]` has characters. `blocked`: in `blocked`. `invitedAt`: when we last invited them, or nil.
  - `label`: nickname from Settings.lua `friendNames` with the tag, else the tag (the existing `label(tag)`).
- `ns.askFriend(tag) -> message`, `ns.shareWithFriend(tag) -> message`: the same as `/wct friends ask` / `share` (reuse the existing `ask` and the `share` branch, don't copy them), returning the line they'd `ns.say` (and still saying it).
- `ns.inviteFriend(tag) -> ok, message`: one real Battle.net whisper `"I use WoW Character Tracker to share characters with friends: https://github.com/JDybeck/wow-character-tracker"` via `C_BattleNet.SendWhisper(bnetAccountID, text)` (else `BNSendWhisper`), at most once a day per tag (`WoWCharacterTrackerDB.invitedAt[tag]`). Refuses (`false`, reason) when the friend has the addon, isn't online, or was invited in the last 24 h.
- `ns.pendingRequests() -> { { tag, account, label, at } }`: requests waiting for an answer this session (the popup queue plus the one showing), oldest first. Answering stays `ns.answerFriendRequest(f, choice)` (`"allow"`, `"notnow"`, `"never"`), which must also remove it from this list and hide the popup if it's the one showing (`StaticPopup_Hide("CHARACTERTRACKER_FRIEND_REQUEST")` guarded).
- `ns.sharedWithYou() -> { { tag, label, chars = { char, ... } } }`: what's in `WoWCharacterTrackerDB.friends` for contacts you keep (`receive` and not blocked), friends by label, characters newest `at` first. `char` is the saved table (`char`, `realm`, `level`, `classFile`, `spec`, `ilvl`, `zone`, `gear`, `at`, `received`, ...).
- `ns.friendsChanged`: an optional function B sets; A calls `if ns.friendsChanged then ns.friendsChanged() end` after a PONG, a new request, an answer, or new DATA, so the open tab refreshes.

---

### Task A: Friends.lua (agent A)

**Files:** `addon/WoWCharacterTracker/Friends.lua`, `addon/WoWCharacterTracker/Comms.lua` only if a kind list needs it (it shouldn't: kinds are free text), tests in `companion/WoWCharacterTracker.Tests/FriendsLuaTests.cs`. Update `/wct friends help` with nothing new (the window is the way now), but keep the commands working.

- `PING` (no body) and `PONG` (addon version) kinds. A PING is answered with PONG **before** the "sharing off" check in `ns.comms.onReceive` (a friend with sharing off still says they have the addon), at most once per 60 s per account, and never answered with anything else. Any message from an account marks it `hasAddon = true`.
- The no-answer timeout uses `C_Timer.After(10, ...)` and calls `ns.friendsChanged`.
- Requests: today `waitingRequests` + `showing` are file locals in the Requests section. Make `ns.pendingRequests()` read them (with an `at` per request), and make answering from the list work the same as from the popup.
- Tests first (two clients where it's about the exchange): PING answered with PONG once (rate limited), also with sharing off; `hasAddon` true after PONG, false after 10 s silence; `findOnlineFriends` twice within 60 s pings once; `onlineFriends` row fields (contact, sharesWithYou, blocked, label with nickname); `askFriend`/`shareWithFriend` do what the commands do; `inviteFriend` sends one whisper (stub `C_BattleNet.SendWhisper` and `GetFriendAccountInfo` returning `bnetAccountID`), refuses within 24 h, refuses a friend who has the addon, and never runs otherwise (no test path sends a whisper without the call); `pendingRequests` lists a request and answering it from the list removes it and hides the popup; `sharedWithYou` leaves out non-receive and blocked friends.

**Commit:** `feat(addon): find online friends and who has the addon, invite, and list requests`.

### Task B: the Friends tab (agent B)

**Files:** `addon/WoWCharacterTracker/Window.lua` (`buildFriends`, the tab's height), tests in `companion/WoWCharacterTracker.Tests/AddonWindowTests.cs`. Use only the interface above; in tests, stub those `ns.*` functions with fixed rows.

Layout, top to bottom:
1. **Find online friends** button (calls `ns.findOnlineFriends()` then redraws) and a hint: "Battle.net friends online in this game. Checking who has the addon sends them nothing they can see."
2. **Requests** (only when `ns.pendingRequests()` has any): one line per request, "Zebbe (Zeb#2222) wants to see your characters" with **Allow**, **Not now**, **Never**.
3. **Online friends** list, one row each: class-colored character name (`RAID_CLASS_COLORS[classFile]` read-only, else white), level, zone, the label, and a state with buttons:
   - `hasAddon == nil`: "Checking..." (no buttons)
   - has the addon, no contact: **Ask** and **Share**
   - contact: "You share" / "Shares with you" bits as text, plus **Ask again**
   - `hasAddon == false`: "No addon" and **Invite** (disabled with "Invited" when `invitedAt` is within 24 h)
   - blocked: "Blocked" (no buttons)
   Rows are pooled (reuse frames on redraw, never create new ones per redraw) and the list scrolls if longer than the tab.
4. **Shared with you**: per friend from `ns.sharedWithYou()`, a heading with the label, then one line per character: name (class colored), level, spec, ilvl, zone, "2h ago". Hovering a character line shows a tooltip (`GameTooltip`, owner the line, `SetOwner`/`AddLine`/`Show`, `Hide` on leave) with its gear as item names (from `ns.itemInfo(itemId)`, quality colored when known, "item 12345" when not cached yet) and talents.
5. Empty states: no friends online: "None of your Battle.net friends are online in this game."; nothing shared: "Nothing shared with you yet. Ask a friend above."
- Set `ns.friendsChanged` to redraw the tab when it's shown (and do nothing when hidden).
- Tests first: the tab builds with the stubbed rows; each row state shows the right buttons and clicking calls the right `ns.*` function with the tag; requests' three buttons call `ns.answerFriendRequest` with the right choice; redrawing twice doesn't create new row frames (count `frames` before/after); shared-with-you lines show the character names; empty states show their text; `ns.friendsChanged` redraws.

**Commit:** `feat(addon): the friends tab with online friends, requests and what friends share`.

---

### After the merge (main session)

Run the full suite, whole-branch review, fix, bump the addon `.toc`, GUIDE (Friends tab), CHANGELOG (with the next version at release), rebuild dist. In-game check by JDybeck: the online list, Checking... to Has the addon / No addon, Ask/Share/Invite, a request in the list and in the popup, the shared-with-you hover, the ping invisible on both sides.
