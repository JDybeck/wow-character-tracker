# Addon window: tabs, minimap button, friends without typing BattleTags

Status: design, from JDybeck's feedback on 0.33.0 (2026-11-06). Not built.

## Why

Sharing with friends works, but it's hard to use: you have to know a friend's BattleTag and type slash commands, and the help text is hard to follow. The in-game options also live on one long Options page. This adds one addon window, opened from a minimap button, that holds everything: the old options in clear tabs, and a Friends tab where you find and ask friends with a click.

## Decisions

- **One window, plain frames** (like the Ask box and note box): no Blizzard globals replaced, nothing protected, so it opens in combat too. Movable, position saved, Esc closes it (added to `UISpecialFrames`, the documented way; check in game that it doesn't taint).
- **Minimap button** with the WCT icon (the app's tray icon, `companion/WoWCharacterTracker/Assets/logo.png`), written by us, no library code copied. Left click: open or close the window. Drag: move it around the minimap edge (angle saved). Tooltip: name and "Click to open". It can be hidden (General tab), and `/wct` brings the window back.
- **The Options page shrinks** to a short page with an "Open the Character Tracker window" button and one line about `/wct`. Every option moves into the window, built once (no second copy of the controls).
- `/wct` with nothing after it opens the window. The level-times list it shows today moves to `/wct times`. All other slash commands stay.
- **Location is on by default** for friends (gear, talents, location on; gold off). **Professions** become a shared category, on by default: each profession with its current skill (`ns.professionRanks()`: name, rank, max).

## Tabs

| Tab | What's on it (today's Options page in brackets) |
|-----|------|
| General | Instant posting, reload popup after a ding, offer a reload on flight paths, chat reminder, the instant post pattern (corner, label), keep all history, show the minimap button |
| Chat | Tell your guild or group: the two chats and the table of kinds |
| Highlights | Ask before a highlight goes unposted (boss, rare, epic) |
| Note | Show, lock, buttons on mouseover, reset position, background opacity |
| Friends | Find online friends, ask, share, requests, what friends shared with you |
| Sharing | Share with friends, this character, new characters, what friends get, per friend, blocked |
| App | What the app set (posted kinds, screenshots), read-only, and "no app found" when there's none |

Each tab is its own small builder function, using shared control helpers (checkbox, grid, table, slider, button, list row) that move out of Options.lua into one `Ui.lua`, so the window and the short Options page use the same parts.

## Friends tab

- **Find online friends** button: lists your Battle.net friends who are online in the same game right now (`ns.friendsOnline`, no messages needed), with their character name, class, level and zone from Battle.net.
- **Who has the addon**, found invisibly: the button also sends each listed friend a `PING` (a Battle.net addon message, never shown in chat or as a toast; only friends on your list, once per session per game account, at most every 60 s per click). Addons that are on answer `PONG` (their sharing switch only decides whether they answer). Rows then say "Has the addon" or "No addon".
- Per row, depending on state:
  - has the addon, not a contact: **Ask** (sends `REQ`, the popup on their side) and **Share** (makes them a contact you share with)
  - a contact: what they get (opens Sharing for them), **Ask again** (refresh), and whether they share with you
  - no addon: **Invite** (only on click: one real Battle.net whisper, "I use WoW Character Tracker to share characters with friends: https://github.com/JDybeck/wow-character-tracker", at most once a day per friend). Nothing else ever sends a chat message.
- **Requests**: incoming ones also listed here with Allow / Not now / Never (the popup stays), so a missed popup isn't lost for the session.
- **Shared with you**: each friend's characters (name, level, class, spec, ilvl, zone, professions, age), hover for gear as item links. Same data the app's Friends page shows.

## Protocol changes

- New kinds `PING` (no body) and `PONG` (addon version). `PONG` is the only answer to a `PING`, and a `PING` from someone not on your Battle.net friends list never gets this far (the game only delivers between friends).
- `DATA` gets `professions` (`name~rank~max,...`, max 256) in category `professions`. `FriendData.lua` and the app's `FriendData` add the field; the app shows professions for friends.
- Protocol stays 1: older addons ignore the unknown kinds and field.

## Privacy

- Answering a `PING` tells a Battle.net friend you run the addon, nothing else. With sharing off you still answer (so friends can see why nothing comes); turning off the minimap button changes nothing here. If JDybeck prefers, "off" can mean no answer either (open question 1).
- No character data goes anywhere without a contact, as today.

## Testing

- Lua (MoonSharp): PING/PONG between two clients (answered once, rate limited, never from a non-friend), the Friends tab state per row (has addon, contact, no addon), Invite cooldown, professions in DATA both ways, the location default, the moved options writing the same settings as before (`ns.editSettings`, DB fields).
- App: `FriendData` reads professions; the Friends page shows them.
- In game (JDybeck): the window and every tab, the minimap button (drag, hide, show), Esc, combat, the PING invisible on both sides, Invite's whisper, the icon loading.

## Build order

1. Window, tabs and minimap button with the existing options moved in (no friends changes). Short Options page. Location default on.
2. Friends tab: online list, PING/PONG, Ask/Share/Invite, requests.
3. Professions (addon, app).

## Open questions

1. Should a friend with sharing off still answer `PING` (shows "has the addon, sharing off") or stay silent?
2. Icon format: modern clients load `.png` addon textures? If not, ship a `.tga` made from `logo.png` (a small tool script, kept out of the build).
3. Keep the old Options page entry at all, or only the window? (Spec says: keep a short page with the button, since players look there first.)
