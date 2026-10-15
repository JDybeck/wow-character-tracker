# wow-level-tracker

Posts to a Discord channel every time one of your World of Warcraft characters levels up: how long the level took, where you dinged, your professions and your talents, with a portrait of your character's race and class.

```
[Undead portrait]   Undead Frost Mage
                    Zebby just dinged level 12.
                    11 to 12 took 18m 4s.
                    They dinged in Tirisfal Glades.

                    Professions: Tailoring 75, Enchanting 60
                    Talents: Class 4 / Frost 2
```

Retail for now. WoW Forever (Classic+) uses the same addon API and comes later.

## Getting started

1. Get `LevelTimes.exe`: run `build.cmd` (needs the [.NET 10 SDK](https://dotnet.microsoft.com/download)), it lands in `dist\`.
2. Double-click `LevelTimes.exe`. It walks you through everything:
   - finds your WoW folder,
   - installs the addon (and keeps it updated, including the interface number after patches),
   - asks for a Discord webhook link and tells you how to make one, then checks it works and can send a test post.
3. Leave the window open while you play.

When you ding, the game pops up **"Post now"**. Click it (it reloads your UI) and the post shows up in Discord. Click **Later** and it posts on your next `/reload` or logout instead. WoW only saves addon data on reload or logout, which is why that step exists.

### Making a Discord webhook

In Discord, hover the channel, click the gear (Edit Channel), then Integrations, Webhooks, New Webhook, and Copy Webhook URL. You need the Manage Webhooks permission on the server. The name and picture you give the webhook are what the posts show as the sender.

The link is saved in `%LOCALAPPDATA%\LevelTimesCompanion\config.json`. Treat it like a password: anyone with it can post in that channel.

### In game

- `/lt` shows your last 10 level times for the character you're on.
- `/lt popup` turns the "Post now" popup on or off.

### Companion options

```
LevelTimes.exe --setup      ask for the webhook and WoW folder again
LevelTimes.exe --test       send a test post and quit
LevelTimes.exe --backfill   on the first run, post old level-ups instead of skipping them
```

## How the timing works

`RequestTimePlayed()` returns total time played and time played on the current level. Right after a level-up the "current level" part is about zero, so the addon stores `total - thisLevel` (the total at the start of the level) per character. On the next level-up, the finished level's time is the difference. It re-syncs on every login, so it corrects itself.

If you install the addon mid-level, that first level has no baseline and is skipped. Everything after that is tracked.

## Notes and limits

- Talents: retail shows points spent in the class tree, spec tree and hero tree. Classic clients show points per talent tree.
- Professions: on Classic-style clients the nine primary professions are matched by English name, so a non-English client shows `None`.
- The portrait comes from Wowhead's icon CDN. A race it doesn't have an icon for yet falls back to the class icon.
- On the first run, level-ups already in the file are marked as posted so the channel isn't flooded. `--backfill` posts them instead.
