# wow-character-tracker

Posts a Discord message every time a WoW character levels up. Owner: Jesper (JDybeck).
Current focus: retail. WoW Forever (Classic+, launches 2026-11-04) comes later and uses the same addon API.

## Layout

- `addon/WoWCharacterTracker/`: Lua addon (`WoWCharacterTracker.lua`, `WoWCharacterTracker.toc`). No build step, `/reload` in game.
- `companion/WoWCharacterTracker/`: .NET 10 WPF app (C#), builds to `WoWCharacterTracker.exe`. Look based on the owner's React mockup (dark slate, setup checks, character table, webhook list, console).
  - `MainWindow.xaml` + `App.xaml` (styles, palette, converters). `ViewModels.cs`: `MainViewModel` (startup checks, webhooks), `CharacterRow` (merges snapshots and level-ups), `SetupItem`.
  - `Tracker.cs`: watches the SavedVariables files, raises `DataChanged`, posts new level-ups to the active webhook.
  - `Wow.cs`: finds the install, reads the client version from `.build.info`.
  - `AddonInstaller.cs`: the addon is embedded in the exe (see the .csproj) and installed/updated automatically on startup.
  - `Discord.cs`: webhook check, the embed, portrait/class icon URLs. `SavedVariables.cs`: parses events and character snapshots with MoonSharp.
  - `Log.cs`: messages for the console panel. `Config.cs`: settings and the webhook list in `%LOCALAPPDATA%\WoWCharacterTracker\config.json` (migrates the old single `WebhookUrl`).
- `build.cmd`: `dotnet publish` to `dist\WoWCharacterTracker.exe` (self-contained single file).

WoW addons cannot make network calls, which is why the companion exists. The game only writes SavedVariables on `/reload` or logout, so the addon shows a "Post now" popup (ReloadUI) after a ding, deferred until combat ends.

## How it works

- Addon calls `RequestTimePlayed()` on login and on `PLAYER_LEVEL_UP`. It stores `total - thisLevel` per character GUID (`WoWCharacterTrackerDB.start[guid]`), and on level-up the finished level's time is the new value minus the stored one.
- Each level-up appends to `WoWCharacterTrackerDB.events`: id (`guid:level`), char, realm, level, seconds, played (total), at, zone, professions (`""` when none), talents, spec, role, ilvl, class, classFile, race, raceFile, sex.
- `WoWCharacterTrackerDB.chars[guid]` holds the latest snapshot of each character (same fields plus lastSeen), saved after every `/played` and on `PLAYER_LOGOUT` (which also fires on reload). This is what the app's character table shows.
- Companion watches every account's `WTF\Account\*\SavedVariables\WoWCharacterTracker.lua`, dedupes by `id` (`posted.json` next to config), and posts to the active webhook. First run marks existing entries as posted unless `--backfill`.
- Webhooks: added in the app, checked against Discord before saving, several allowed, one active. Never commit them.
- Embed: class color, author "Race Spec Class" with the class icon, thumbnail `race_<raceFile>_<sex>.jpg` from wow.zamimg.com (HEAD-checked, falls back to the class icon).
- Testing: `CHARACTERTRACKER_DATA_DIR` moves config/state, `CHARACTERTRACKER_SCREENSHOT=<png>` renders the window (first character selected) to a PNG after ~6s and quits. Point config `WowPath` at a fake folder so the real game folder isn't touched.

## Message format (requested by the owner)

```
{Char} just dinged level {Level}.
{Level-1} to {Level} took {time}.
Total played: {played}.
They dinged in {Zone}.
Professions: Mining 150, Herbalism 75
Talents: Class 15 / Fury 12 / Slayer 3
```

Faction and guild were deliberately left out. No professions means no professions line.

## Status

- WPF app tested against a fake WoW folder and a mock webhook (screenshots via the hook above): auto addon install, character table and details, webhook list, first-run state, posting with total played and no empty professions line.
- Addon tested in MoonSharp with a stubbed WoW API (timing, snapshots and played at logout, ilvl/role, empty professions, retail talent split and fallbacks, popup and combat deferral).
- Next planned: post without a reload. Idea is `LoggingChat(true)` plus a hidden self-whisper data line that the app tails from `Logs\WoWChatLog.txt`. Needs an in-game check first (is self-whisper allowed on retail, how fast does the chat log flush). The reload popup stays until then.
- Not yet tested in the real game or with a real webhook. The retail talent split assumes `C_Traits.GetTreeCurrencyInfo` lists the class currency before the spec currency; if posts show them swapped, flip them in `getRetailTalentSplit`.
- Retail interface is 120105 (client 12.1.5, confirmed from `.build.info`). The companion rewrites `## Interface:` to match the installed client.

## Known limits

- Profession names are matched in English on Classic-style clients.
- If the addon is installed mid-level, that first level has no baseline and is skipped.

## Conventions

- Jesper prefers casual, direct writing and does not like em dashes (use commas).
- Use [Conventional Commits](https://www.conventionalcommits.org/) for every commit: `type(scope): summary`, with types like `feat`, `fix`, `docs`, `refactor`, `chore`, `ci`, `build`, `test`. Scopes in use: `addon`, `companion`, `ci`. Imperative, lowercase summary, no trailing period. Breaking changes get `!` and a `BREAKING CHANGE:` footer.
- Commit messages end with the Co-Authored-By and Claude-Session trailers.
