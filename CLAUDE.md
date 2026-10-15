# wow-level-tracker

Posts a Discord message every time a WoW character levels up. Owner: Jesper (JDybeck).
Target game: World of Warcraft Forever (Classic+, launches 2026-11-04, beta until 2026-10-21).
Being tested on retail first because the APIs used are the same.

## Layout

- `addon/LevelTimes/`: Lua addon (`LevelTimes.lua`, `LevelTimes.toc`). No build step, `/reload` in game.
- `companion/LevelTimesCompanion/`: .NET 8 console app (C#). Watches the addon's SavedVariables file, posts new entries to a Discord webhook.

WoW addons cannot make network calls, which is why the companion exists. The game only writes SavedVariables on `/reload` or logout, so posts arrive then, not at the exact ding.

## How it works

- Addon calls `RequestTimePlayed()` on login and on `PLAYER_LEVEL_UP`. It stores `total - thisLevel` per character GUID (`LevelTimesDB.start[guid]`), and on level-up the finished level's time is the new value minus the stored one.
- Each level-up appends an entry to `LevelTimesDB.events`: id (`guid:level`), char, realm, level, seconds, at, zone, professions, talents.
- Companion parses the Lua file with MoonSharp, dedupes by `id` (kept in `%LOCALAPPDATA%\LevelTimesCompanion\posted.json`), and posts. First run marks existing entries as posted unless `--backfill`.
- Webhook URL comes from the `DISCORD_WEBHOOK_URL` environment variable. Never commit it.

## Message format (requested by the owner)

```
**{Char}** just dinged level {Level}.
{Level-1} to {Level} took {time}.
They dinged in {Zone}.
Professions: Mining 150, Herbalism 75
Talents: Arms 31 / Fury 20 / Protection 0
```

Faction and guild were deliberately left out.

## Status

- Addon logic tested against a simulated WoW API (Lua via `lupa`), including Classic and Retail variants of the profession and talent calls.
- C# companion has NOT been compiled or run. The cloud sandbox had no .NET SDK and no route to download one. First job: `dotnet build` in `companion/LevelTimesCompanion` and fix whatever it reports.
- `## Interface: 120105` in the TOC is the retail number from the Warcraft wiki, which warns it may be outdated. Confirm in game with `/dump (select(4, GetBuildInfo()))`. No Forever number is known yet.

## Known limits

- Profession names are matched in English on Classic-style clients (non-English clients show `None`).
- Retail has no talent trees, so talents shows the specialization name there.
- If the addon is installed mid-level, that first level has no baseline and is skipped.

## Conventions

- Jesper prefers casual, direct writing and does not like em dashes (use commas).
- Commit messages end with the Co-Authored-By and Claude-Session trailers.
