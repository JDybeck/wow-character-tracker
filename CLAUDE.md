# wow-character-tracker

Posts a Discord message every time a WoW character levels up. Owner: Jesper (JDybeck).
Current focus: retail. WoW Forever (Classic+, launches 2026-11-04) comes later and uses the same addon API.

## Layout

- `addon/WoWCharacterTracker/`: Lua addon (`WoWCharacterTracker.lua`, `WoWCharacterTracker.toc`). No build step, `/reload` in game.
- `companion/WoWCharacterTracker/`: .NET 10 console app (C#), builds to `WoWCharacterTracker.exe`.
  - `Program.cs`: the guided startup (WoW folder, addon, webhook, data) and the file watcher.
  - `Wow.cs`: finds the install, reads the client version from `.build.info`.
  - `AddonInstaller.cs`: the addon is embedded in the exe (see the .csproj) and installed/updated from there.
  - `Discord.cs`: webhook check and the embed. `SavedVariables.cs`: parses the Lua file with MoonSharp.
  - `Ui.cs`, `Config.cs`: console helpers, settings in `%LOCALAPPDATA%\WoWCharacterTracker\config.json`.
- `build.cmd`: `dotnet publish` to `dist\WoWCharacterTracker.exe` (self-contained single file).

WoW addons cannot make network calls, which is why the companion exists. The game only writes SavedVariables on `/reload` or logout, so the addon shows a "Post now" popup (ReloadUI) after a ding, deferred until combat ends.

## How it works

- Addon calls `RequestTimePlayed()` on login and on `PLAYER_LEVEL_UP`. It stores `total - thisLevel` per character GUID (`WoWCharacterTrackerDB.start[guid]`), and on level-up the finished level's time is the new value minus the stored one.
- Each level-up appends to `WoWCharacterTrackerDB.events`: id (`guid:level`), char, realm, level, seconds, at, zone, professions, talents, spec, class, classFile, race, raceFile, sex.
- Companion watches every account's `WTF\Account\*\SavedVariables\WoWCharacterTracker.lua`, dedupes by `id` (`posted.json` next to config), and posts. First run marks existing entries as posted unless `--backfill`.
- Webhook: asked for on first run and saved in config. `DISCORD_WEBHOOK_URL` overrides it. Never commit it.
- Embed: class color, author "Race Spec Class" with the class icon, thumbnail `race_<raceFile>_<sex>.jpg` from wow.zamimg.com (HEAD-checked, falls back to the class icon).
- `CHARACTERTRACKER_DATA_DIR` moves config/state, for testing without touching real settings.

## Message format (requested by the owner)

```
{Char} just dinged level {Level}.
{Level-1} to {Level} took {time}.
They dinged in {Zone}.
Professions: Mining 150, Herbalism 75
Talents: Class 15 / Fury 12 / Slayer 3
```

Faction and guild were deliberately left out.

## Status

- Companion builds and was tested end to end against a fake WoW folder and a mock webhook: addon install, interface number fix, webhook prompt (real Discord rejects a fake link), backfill, live file watching, embed JSON.
- Addon tested in MoonSharp with a stubbed WoW API (timing, retail talent split and fallbacks, popup and combat deferral, `/ct popup`).
- Not yet tested in the real game or with a real webhook. The retail talent split assumes `C_Traits.GetTreeCurrencyInfo` lists the class currency before the spec currency; if posts show them swapped, flip them in `getRetailTalentSplit`.
- Retail interface is 120105 (client 12.1.5, confirmed from `.build.info`). The companion rewrites `## Interface:` to match the installed client.

## Known limits

- Profession names are matched in English on Classic-style clients.
- If the addon is installed mid-level, that first level has no baseline and is skipped.

## Conventions

- Jesper prefers casual, direct writing and does not like em dashes (use commas).
- Commit messages end with the Co-Authored-By and Claude-Session trailers.
