# wow-character-tracker

Posts to a Discord channel every time one of your World of Warcraft characters levels up: how long the level took, total played, where you dinged, your professions and your talents, with a portrait of your character's race and class. The app also shows all your characters with their level, item level, spec, played time and level history.

```
[Undead portrait]   Undead Frost Mage
                    Zebby just dinged level 12.
                    11 to 12 took 18m 4s.
                    Total played: 10h 16m.
                    They dinged in Tirisfal Glades.

                    Professions: Tailoring 75, Enchanting 60
                    Talents: Class 4 / Frost 2
```

The professions line is left out when the character has none.

Retail for now. WoW Forever (Classic+) uses the same addon API and comes later.

## Getting started

1. Download `WoWCharacterTracker.exe` from the [Releases page](https://github.com/JDybeck/wow-character-tracker/releases/latest). Nothing else to install. Windows might show a SmartScreen warning since the exe isn't signed, click **More info** and then **Run anyway**.

   Developers can build it themselves instead: run `build.cmd` (needs the [.NET 10 SDK](https://dotnet.microsoft.com/download)), it lands in `dist\`.
2. Double-click `WoWCharacterTracker.exe`. Three checks at the top of the window show what's set up:
   - **World of Warcraft**: found automatically. If not, click Browse.
   - **Addon**: installed and kept up to date for you, including the interface number after patches.
   - **Discord**: paste a webhook link in the Discord panel (it explains how to get one). It's checked with Discord before saving. **Send test** shows you a sample post.
3. Leave the window open while you play.

### The app

- **Characters**: every character you've logged in with the addon, with class, spec, role, level, item level and when you last played. Click one for total played, professions, talents, last zone and its level history.
- **Discord**: add as many webhooks as you like and click one to make it the active one. Level-ups post to the active webhook.
- **Console**: what the app is doing, like posts sent and anything that needs your attention.
- **Sync now**: reads the game's data again and posts anything new. It also does this by itself whenever the game saves.

When you ding, the game pops up **"Post now"**. Click it (it reloads your UI) and the post shows up in Discord. Click **Later** and it posts on your next `/reload` or logout instead. WoW only saves addon data on reload or logout, which is why that step exists.

### Making a Discord webhook

In Discord, hover the channel, click the gear (Edit Channel), then Integrations, Webhooks, New Webhook, and Copy Webhook URL. You need the Manage Webhooks permission on the server. The name and picture you give the webhook are what the posts show as the sender.

Treat the link like a password: anyone with it can post in that channel. The app only ever shows it masked.

### In game

- `/ct` shows your last 10 level times for the character you're on.
- `/ct popup` turns the "Post now" popup on or off.

Characters show up in the app after the game saves the addon's data, which happens on `/reload` and logout.

## How the timing works

`RequestTimePlayed()` returns total time played and time played on the current level. Right after a level-up the "current level" part is about zero, so the addon stores `total - thisLevel` (the total at the start of the level) per character. On the next level-up, the finished level's time is the difference. It re-syncs on every login, so it corrects itself.

If you install the addon mid-level, that first level has no baseline and is skipped. Everything after that is tracked.

## Notes and limits

- Talents: retail shows points spent in the class tree, spec tree and hero tree. Classic clients show points per talent tree.
- Professions: on Classic-style clients the nine primary professions are matched by English name, so a non-English client shows `None`.
- The portrait comes from Wowhead's icon CDN. A race it doesn't have an icon for yet falls back to the class icon.
- On the first run, level-ups already in the file are marked as posted so the channel isn't flooded. Start the app with `--backfill` to post them instead.
- Settings and webhooks are saved in `%LOCALAPPDATA%\WoWCharacterTracker\config.json`.
