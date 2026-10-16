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
  - `AddonInstaller.cs`: addon embedded in the exe, installed/updated per game folder, writes `Settings.lua`. First install waits for the user's click on the Addon card (`SetUpAddon(force: true)`); an installed addon older than the bundled one is updated automatically. The app also bumps the installed toc's interface number and writes Settings.lua.
  - `Discord.cs`: webhook check, embeds per kind, multipart upload when there's a screenshot. Every post sets `avatar_url` to the character's portrait (logo from the repo's raw URL as fallback). Item/mount names link to Wowhead, professions get emoji (`Format.ProfessionsWithEmoji`).
  - `CardImage.cs`: draws the character card (Post to Discord) as a PNG in code, icons preloaded, current theme brushes, 560 px at 2x. Discord embeds can't show icons in text, so the card is the embed image; text fields are the fallback. `Config.CardHidden` (gear next to the button, `MainViewModel.CardParts`) picks what it shows; guild is off by default. `SavedVariables.cs`: `Entry` (all kinds), `Snapshot`, MoonSharp parsing, `Format`.
  - `App.xaml.cs`: single instance (a second launch signals the first to show its window), tray mode, `GameWatcher` (opens the window when a WoW process starts, hides it to the tray when the last one closes). `TrayIcon.cs`: WinForms NotifyIcon (WinForms only for this). `Autostart.cs`: HKCU Run entry with `--background` when Open with World of Warcraft is on.
  - `Wowhead.cs`: names/icons/tooltips for items and currencies from `nether.wowhead.com/[classic/]tooltip/{item|currency}/{id}`, cached in `wowhead.json`; profession icon names. `Updater.cs`: GitHub latest release check, self-update (download, rename running exe to .old, swap, restart with `--updated`), release notes from the GitHub release (written by release.cmd from `CHANGELOG.md`). Reads `JDybeck/wow-character-tracker-releases`.
- `TestCharacters.cs`: made-up Forever characters (Zeb, Fågelmannen, Goggo; GUIDs start with `Test-`) shown when the header switch is on. Only merged into the list in `FlavorViewModel.ShowData`, never into the Tracker, so they're never posted on their own.
- `Log.cs`: console messages. `Config.cs`: webhooks, per-game `FlavorSettings` (path, active webhook, level rule, toggles, screenshot dicts). Migrates v0.2/v0.3 configs.
- `build.cmd`: `dotnet publish` to `dist\WoWCharacterTracker.exe` (self-contained single file).
- Releases: this repo is public (history uses the GitHub noreply email, keep it that way; never commit webhooks or tokens). Add a `## v0.9.0` section to CHANGELOG.md, commit, then `release.cmd 0.9.0` checks it, tags and pushes. `.github/workflows/release.yml` builds the exe with that version and creates the release here with the CHANGELOG section as notes. The updater and the README download link point at this repo's releases. (v0.8.0 installs still check the old `JDybeck/wow-character-tracker-releases` repo, so they need one manual download.)

## How it works

- Addon: `RequestTimePlayed()` on login and level-up; stores `total - thisLevel` per GUID (`WoWCharacterTrackerDB.start`), level time is the difference.
- Events go to `WoWCharacterTrackerDB.events` with `kind` = level | boss | loot | mount | death (+ "test" from `/wct test` and "snapshot" check-ins, live only). Common fields: id, guid, char, realm, level, at, zone, professions, talents, spec, role, ilvl, class(File), race(File), sex, hardcore, shot. Kind fields: seconds/played; boss/instance/group ("Name~level~CLASSFILE~Class~Race~Realm,...")/groupSize; item/itemId/quality; mount/itemId/bought; killer/gold/played.
  - Boss: `ENCOUNTER_END` success or combat log `UNIT_DIED` in party/raid, only names in `END_BOSSES` (Classic dungeon/raid last bosses), 10 min dedupe.
  - Loot: `CHAT_MSG_LOOT` matched with patterns built from `LOOT_ITEM_SELF(_MULTIPLE)`, quality >= 3. Mounts: item class 15/5 from `LOOT_ITEM_PUSHED_SELF` or looted, or `NEW_MOUNT_ADDED`; 2 min dedupe; `bought` if a merchant was open.
  - Death: `PLAYER_DEAD` only when `C_GameRules.IsHardcoreActive()`; killer from the last damage event to the player.
- Live channel: the chat log can't be used (the game only writes it on exit, and toggling `LoggingChat` doesn't flush it; tested on retail). Instead `addEvent` queues a data screenshot: a parentless frame scaled to 1 unit = 1 pixel shows a 160-column grid of 2 px cells in the top-left corner (row 0 and column 0 alternate white/black as a clock; data = 0xC8, 2-byte length, payload with one-letter keys (`SHORT` in the addon, `Payload.ShortKeys` in the app; id without the GUID prefix), 2-byte sum), `Screenshot()` on the next frame with `screenshotFormat=png`/`screenshotQuality=10`. The app still reads the old 0xC7 layout (128 x 5 px, long keys) (restored after) and ActionStatus muted, grid hidden on `SCREENSHOT_SUCCEEDED`. Only events the app will post go live (`Settings.lua` carries `post` rules: level rule, rare/epic, mount, boss, death, and `hideChat`); plus a `snapshot` check-in 15s after login. Live test/snapshot events update the character row (`Tracker.presence`) but are never posted. Normal event screenshots (`Settings.lua` `screenshot`) are queued after, unposted events get theirs deleted, and the app matches them by write time (event time -2s to +20s).
- Duels: `CHAT_MSG_SYSTEM` matched against patterns built from `DUEL_WINNER_KNOCKOUT`/`DUEL_WINNER_RETREAT` (`toPattern`, argument order aware); Mak'gora patterns come from any global string mentioning Mak'gora/to the death. Kinds `duel`/`makgora` (opponent, fled). Ratings: `checkRatings` reads `C_ChallengeMode.GetOverallDungeonScore` and `GetPersonalRatedInfo` (brackets 1,2,4,7,9), baseline per character in `WoWCharacterTrackerDB.ratings`, kinds `mplus`/`pvp` (rating, step, bracket) when a step from Settings.lua `mplusSteps`/`pvpSteps` is passed.
- Saved-file-only snapshot fields (`SAVED_ONLY`, too long for the grid): `talentPicks` (Forever `spellID~rank~maxRank~tree`), `talentExport` (retail loadout string, opens Wowhead's `talent-calc/blizzard/`), `stats` (`Name~value,...`).
- Forever talents: one trait tree with the classic three side by side; nodes grouped by `posX` (gap > 1200 starts a tree), named from `CLASSIC_TREES`; top tree is stored as `spec`.
- Grid label: a "WoW Character Tracker" frame sits 4 px right of the grid's 160 columns (outside what the decoder reads) so players know what the corner flicker is.
- `WoWCharacterTrackerDB.chars[guid]`: latest snapshot per character (includes `guild` ("" = none, nil = not known yet, which keeps the old value; live key `U`), `money` in copper and retail `crests` as `id~qty~name,...` from the currency window's Crests section), saved after `/played`, on death, on money/currency changes (3s debounce) and on `PLAYER_LOGOUT`. The app shows gold per character and a total for the realm/ruleset filter.
- Max level (`GetMaxPlayerLevel`, event field `maxLevel`) and legendary loot (quality 5) are always posted, with their own flashier embeds.
- App posts only what the tab's settings want; unwanted events are marked handled so toggling later doesn't flood. First run marks existing events as posted unless `--backfill`.
- Webhooks: shared list, on/off per game tab, each with options: games, Forever rulesets, character tags (`Config.Tags`, `Config.CharacterTags` by GUID; none picked = all). Boss kills are skipped when a group member matches the tab's `SkipBossGroupRegex`. Never commit webhooks.
- Testing: `CHARACTERTRACKER_DATA_DIR` moves config/state and gives test runs their own single-instance name (so they never touch the copy Jesper is running; don't stop processes by name, only ones started from the test build); `CHARACTERTRACKER_SCREENSHOT=<png>` renders the window and quits, `CHARACTERTRACKER_SCREENSHOT_VIEW=forever:posting` picks tab and page. Put both game paths in the test config as the flavor folders themselves (`...\_retail_`, `...\_classic_beta_`, not the WoW root), otherwise detection finds the real game folders and installs the addon and Settings.lua there.
- Themes: `Theme.cs` recolors the palette per tab (Retail = Midnight violet, Forever = gold). Styles freeze brushes, so themed keys (Bg, Card, Line, Blue*, Indigo*...) must be used with `DynamicResource`; Theme swaps in new brushes.

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
- Addon tested in MoonSharp with a stubbed WoW API: timing, snapshots, boss/loot/mount/death detection and dedupe, data grid drawing (rendered to JPEG, also scaled 0.8 at quality 75, and decoded by the app end to end), screenshot calls, `/wct test`.
- Retail in-game: snapshots, professions, ilvl and spec work. The chat log route failed (only written on exit), replaced by data screenshots, which are not yet tested in game. Other unknowns: whether `ENCOUNTER_END` fires for Forever dungeons, whether Forever mount items are class 15/5, whether `C_GameRules.IsHardcoreActive` exists on Forever. `/wct test` is the quick check for the live channel; `/wct live` turns it off, the reload popup (`/wct popup`) is the fallback.
- Forever (1.60.1): no `GetNumTalentTabs`/`GetSpecialization`; talents come from the trait tree (`C_ClassTalents.GetActiveConfigID`, `C_Traits`), points summed per sub-tree (`getTraitTalents`; sub-tree layout still to be confirmed in game with `/wct debug`). Surnames: `UnitName` returns the surname as its second value (where other clients put the realm); `C_PlayerInfo.ShouldDisplaySurname` marks such clients. Stored as `surname` (live key `L`), `FlavorSettings.NameStyle` (full/first/last) picks the name in posts. `/wct debug` dumps talent/name/guild API results into `WoWCharacterTrackerDB.debug`.
- Retail talent split assumes `C_Traits.GetTreeCurrencyInfo` lists class before spec currency; flip in `getRetailTalentSplit` if swapped.

## Later (ideas, not started)

- Minimap button for the addon (with the commands and a test button).
- Smaller data grid: 2 px cells, PNG for the grid shot, one-letter keys, no repeated GUID in the id, new magic byte so the app reads old and new grids.
- Read boss kills and deaths from the combat log (WoWCombatLog.txt is written live when combat logging is on; it has ENCOUNTER_END and the group's COMBATANT_INFO). Addons can't add their own lines to it, so it can't replace the grid for level-ups, loot or mounts.
- Per-character posting rules (overriding the tab's What gets posted).
- Baganator integration: read its SavedVariables (bags, bank, warband) so the app can show what each character carries. Check its data format and whether it's stable across versions first.

## Taint rules (learned the hard way)

- Never replace Blizzard globals (we once replaced `ChatFrame_DisplayTimePlayed`: on Classic clients that tainted the chat and Esc stopped working, with "blocked from an action" popups). Mute things by unregistering events or changing alpha instead.
- Data grid shots wait for `PLAYER_REGEN_ENABLED` (no CVar changes in combat). Normal event screenshots go right away; "Hide chat" sets chat frames' alpha to 0 for the shot and restores it.

## Conventions

- Jesper prefers casual, direct writing and does not like em dashes (use commas).
- Use [Conventional Commits](https://www.conventionalcommits.org/) for every commit: `type(scope): summary`, with types like `feat`, `fix`, `docs`, `refactor`, `chore`, `ci`, `build`, `test`. Scopes in use: `addon`, `companion`, `ci`. Imperative, lowercase summary, no trailing period. Breaking changes get `!` and a `BREAKING CHANGE:` footer.
- Commit messages end with the Co-Authored-By and Claude-Session trailers.
