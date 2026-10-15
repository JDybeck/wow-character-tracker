# wow-character-tracker

Posts WoW moments to Discord as they happen: level-ups, end boss kills, rare/epic loot, new mounts, hardcore deaths, optionally with an in-game screenshot. Owner: Jesper (JDybeck).
Main target: WoW Forever (Classic+, launches 2026-11-04; beta lives in `_classic_beta_`, product `wow_classic_beta`, client 1.60.1, exe `WowB.exe`). Retail is supported too, with quieter defaults. Each has its own tab in the app.

## Layout

- `addon/WoWCharacterTracker/`: Lua addon. `WoWCharacterTracker.lua` (everything), `Settings.lua` (written by the app: which events get a screenshot; the bundled one has all off), `.toc` loads Settings first. No build step, `/reload` in game.
- `companion/WoWCharacterTracker/`: .NET 10 WPF app (C#), builds to `WoWCharacterTracker.exe`. Look based on the owner's React mockup (dark slate, setup checks, character table, webhook list, console).
  - `MainWindow.xaml` + `App.xaml` (styles, palette, converters, dark CheckBox and chip styles). Header has the game tabs (Retail/Forever); the left side has page tabs (Characters / What gets posted).
  - `ViewModels.cs`: `MainViewModel` (webhooks, console, selected tab/page), `FlavorViewModel` (one per game: folder, addon, tracker, characters, posting settings, `ShotOption`s), `CharacterRow` (merges snapshots and events, highlights), `SetupItem`.
  - `Tracker.cs`: one per game. Reads SavedVariables (file watcher + 1 min safety net) and live events from `ScreenshotChannel`, filters by the tab's settings, finds screenshots, posts. State per game in `posted-<key>.json`.
  - `ScreenshotChannel.cs`: watches `<game>\Screenshots`, `GridDecoder` reads the addon's data grid (clock row/column give the cell size, so scaling and JPEG are fine), `Payload` parses `k=v;...`. Decoded grid shots are deleted.
  - `Wow.cs`: `Flavor`/`Flavors` (folders, defaults per game), install detection, product from `.flavor.info`, client version from the root `.build.info`, interface number (1.60.1 -> 16001).
  - `AddonInstaller.cs`: addon embedded in the exe, installed/updated per game folder, writes `Settings.lua`.
  - `Discord.cs`: webhook check, embeds per kind, multipart upload when there's a screenshot. `SavedVariables.cs`: `Entry` (all kinds), `Snapshot`, MoonSharp parsing, `Format`.
  - `Log.cs`: console messages. `Config.cs`: webhooks, per-game `FlavorSettings` (path, active webhook, level rule, toggles, screenshot dicts). Migrates v0.2/v0.3 configs.
- `build.cmd`: `dotnet publish` to `dist\WoWCharacterTracker.exe` (self-contained single file). `.github/workflows/release.yml` does the same on a `v*` tag and attaches it to a release.

## How it works

- Addon: `RequestTimePlayed()` on login and level-up; stores `total - thisLevel` per GUID (`WoWCharacterTrackerDB.start`), level time is the difference.
- Events go to `WoWCharacterTrackerDB.events` with `kind` = level | boss | loot | mount | death (+ "test" from `/ct test`, live only). Common fields: id, guid, char, realm, level, at, zone, professions, talents, spec, role, ilvl, class(File), race(File), sex, hardcore, shot. Kind fields: seconds/played; boss/instance; item/itemId/quality; mount/itemId/bought; killer/gold/played.
  - Boss: `ENCOUNTER_END` success or combat log `UNIT_DIED` in party/raid, only names in `END_BOSSES` (Classic dungeon/raid last bosses), 10 min dedupe.
  - Loot: `CHAT_MSG_LOOT` matched with patterns built from `LOOT_ITEM_SELF(_MULTIPLE)`, quality >= 3. Mounts: item class 15/5 from `LOOT_ITEM_PUSHED_SELF` or looted, or `NEW_MOUNT_ADDED`; 2 min dedupe; `bought` if a merchant was open.
  - Death: `PLAYER_DEAD` only when `C_GameRules.IsHardcoreActive()`; killer from the last damage event to the player.
- Live channel: the chat log can't be used (the game only writes it on exit, and toggling `LoggingChat` doesn't flush it; tested on retail). Instead `addEvent` queues a data screenshot: a parentless frame scaled to 1 unit = 1 pixel shows a 128-column grid of 5 px cells in the top-left corner (row 0 and column 0 alternate white/black as a clock; data = 0xC7, 2-byte length, payload, 2-byte sum), `Screenshot()` on the next frame with `screenshotFormat=jpeg`/`screenshotQuality=10` (restored after) and ActionStatus muted, grid hidden on `SCREENSHOT_SUCCEEDED`. Normal event screenshots (`Settings.lua`) are queued after, and the app matches them by write time (event time -2s to +20s).
- `WoWCharacterTrackerDB.chars[guid]`: latest snapshot per character, saved after `/played`, on death and on `PLAYER_LOGOUT`.
- App posts only what the tab's settings want; unwanted events are marked handled so toggling later doesn't flood. First run marks existing events as posted unless `--backfill`.
- Webhooks: shared list, active one per game tab. Never commit them.
- Testing: `CHARACTERTRACKER_DATA_DIR` moves config/state; `CHARACTERTRACKER_SCREENSHOT=<png>` renders the window and quits, `CHARACTERTRACKER_SCREENSHOT_VIEW=forever:posting` picks tab and page. Put both game paths in the test config, otherwise detection finds the real game folders and installs the addon there.

## Message format (requested by the owner)

```
{Char} just dinged level {Level}.
{Level-1} to {Level} took {time}.
Total played: {played}.
They dinged in {Zone}.
Professions: Mining 150, Herbalism 75
Talents: Class 15 / Fury 12 / Slayer 3
```

Faction and guild were deliberately left out. No professions means no professions line. Other kinds: "{Char} killed {Boss}!", "{Char} looted [{Item}]", "{Char} bought a mount!" / "got a new mount!", "{Char} has died." (killer, played, gold lost).

## Status

- App tested end to end against fake `_retail_` + `_classic_beta_` folders and a mock webhook: live data screenshots and earlier live events (posted with the level rule applied, boss with screenshot attached), SavedVariables events, per-game addon install with interface 16001/120105, Settings.lua, config migration, all screens via screenshots.
- Addon tested in MoonSharp with a stubbed WoW API: timing, snapshots, boss/loot/mount/death detection and dedupe, data grid drawing (rendered to JPEG, also scaled 0.8 at quality 75, and decoded by the app end to end), screenshot calls, `/ct test`.
- Retail in-game: snapshots, professions, ilvl and spec work. The chat log route failed (only written on exit), replaced by data screenshots, which are not yet tested in game. Other unknowns: whether `ENCOUNTER_END` fires for Forever dungeons, whether Forever mount items are class 15/5, whether `C_GameRules.IsHardcoreActive` exists on Forever. `/ct test` is the quick check for the live channel; `/ct live` turns it off, the reload popup (`/ct popup`) is the fallback.
- Retail talent split assumes `C_Traits.GetTreeCurrencyInfo` lists class before spec currency; flip in `getRetailTalentSplit` if swapped.

## Conventions

- Jesper prefers casual, direct writing and does not like em dashes (use commas).
- Use [Conventional Commits](https://www.conventionalcommits.org/) for every commit: `type(scope): summary`, with types like `feat`, `fix`, `docs`, `refactor`, `chore`, `ci`, `build`, `test`. Scopes in use: `addon`, `companion`, `ci`. Imperative, lowercase summary, no trailing period. Breaking changes get `!` and a `BREAKING CHANGE:` footer.
- Commit messages end with the Co-Authored-By and Claude-Session trailers.
