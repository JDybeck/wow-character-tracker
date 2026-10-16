# wow-character-tracker

Posts your World of Warcraft moments to a Discord channel, the moment they happen: level-ups, end boss kills, rare and epic loot, new mounts and hardcore deaths. Posts can include a screenshot taken in game. The app also shows all your characters with their level, item level, played time, highlights and level history.

Made with WoW Forever (Classic+) in mind, and works for Retail too. Each has its own tab.

```
[Undead portrait]   Undead Frost Mage
                    Zebby just dinged level 12.
                    11 to 12 took 18m 4s.
                    Total played: 10h 16m.
                    They dinged in Tirisfal Glades.

                    Professions: Tailoring 75, Enchanting 60
                    Talents: Class 4 / Frost 2
                    [screenshot]
```

## Getting started

1. Download `WoWCharacterTracker.exe` from the [Releases page](https://github.com/JDybeck/wow-character-tracker/releases/latest). Nothing else to install. Windows might show a SmartScreen warning since the exe isn't signed, click **More info** and then **Run anyway**.

   Developers can build it themselves instead: run `build.cmd` (needs the [.NET 10 SDK](https://dotnet.microsoft.com/download)), it lands in `dist\`.
2. Double-click `WoWCharacterTracker.exe`. Pick **Retail** or **Forever** at the top. Three checks show what's set up for that game:
   - **Game folder**: found automatically, with the game version it found so you can see it picked the right one. Click **Change** to pick another folder. While Forever is in beta, its folder is `_classic_beta_`.
   - **Addon**: installed and kept up to date for you, including the interface number after patches.
   - **Discord**: paste a webhook link in the Discord panel (it explains how to get one). It's checked with Discord before saving. **Send test** shows you a sample post.
3. That's it. With **Open with World of Warcraft** on (the default), the app waits in the system tray and opens when you start WoW, like Archon. When you close WoW it goes back to the tray, and it keeps tracking the whole time. Closing the window also sends it to the tray; right-click the tray icon to quit. Untick the option at the top to use it as a normal app instead.
4. In game, type `/wct test`. The addon also reminds you of `/wct help` when you log in, and the (i) icon at the top of the app lists the commands. The console in the app should say instant posting works within a few seconds.

### What gets posted

Each game has a **What gets posted** page:

- **Level-ups**: up to a level of your choice, post every few levels, then every level after that. Forever starts at every 5 levels up to 30, then every level.
- **Loot**: rare (blue) items, and epic and legendary items, separately.
- **New mounts**: bought from a vendor or learned.
- **End boss kills**: the last boss of a dungeon or raid, with the group (name, level, race, class). You can skip the post when someone in the group matches a regex, for friends who run the tracker too, so the same kill isn't posted several times.
- **Hardcore deaths**: what killed the character, total played and the gold they had on them.

Each of these has **Screenshot** (the addon takes one in game a moment after it happens) and **Post screenshot** (attach it to the Discord post). Screenshot settings reach the game on your next `/reload` or login.

### The app

- **Characters**: every character you've logged in with the addon, with class, spec, level, item level and when you last played. Hardcore characters are marked. Click one for total played, professions, talents, last zone, highlights (bosses, loot, mounts, death) and level history. **Tags**: make your own (like Main, Alts, Guild) and put them on characters. **Post to Discord** sends a card with the character's info. **Remove from list** hides a character you don't play anymore; if you play it again, it comes back.
- **Filter**: chips above the list filter by realm on Retail and by ruleset (Hardcore, Self-found, Normal) on Forever.
- **Discord**: add as many webhooks as you like. Click one to turn it on or off for the game tab you're on; posts go to every webhook that's on. The gear opens its options: which games it posts for, and on Forever which rulesets, so a hardcore channel only gets hardcore characters, and which character tags (none picked means every character). A new webhook is named after the name you gave it in Discord; click the pencil to rename it, like "#leveling / My Guild" (Discord doesn't tell webhooks their channel or server name). Hover a webhook to see its masked link.
- **Console**: what the app is doing, like posts sent and anything that needs your attention.
- **Sync now**: reads the game's data again and posts anything new.

### Making a Discord webhook

In Discord, hover the channel, click the gear (Edit Channel), then Integrations, Webhooks, New Webhook, and Copy Webhook URL. You need the Manage Webhooks permission on the server. The name and picture you give the webhook are what the posts show as the sender.

Treat the link like a password: anyone with it can post in that channel. The app only ever shows it masked.

### In game

- `/wct` shows your last 10 level times for the character you're on.
- `/wct test` checks that instant posting reaches the app.
- `/wct live` turns instant posting on or off.
- `/wct popup` turns on a reload popup after a ding, if you'd rather not use instant posting (`/wct live`).

## How it works

WoW addons can't make network calls and only save their data on `/reload` or logout. The one file an addon can make the game write right away is a screenshot. So for each event, the addon shows a small black and white grid in the top-left corner for a single frame, takes a screenshot and hides the grid again. The app reads the event from the grid within a second, posts it and deletes that screenshot. You might notice a brief flicker in the corner. While that one screenshot is taken, the screenshot format is set to JPEG at full quality (your own settings come right back) and the "Screen captured" message is hidden.

Only events that will actually be posted (by your "What gets posted" settings) go out this way, plus one check-in about 15 seconds after you log in, so your character's row in the app updates right away. Everything else, like a level that isn't posted, reaches the app on your next `/reload` or logout, without any screenshots. In-game screenshots you turned on are only kept for events that get posted.

The saved data is the backup: anything that didn't come through live gets posted on your next `/reload` or logout, and nothing is posted twice.

Level times come from `RequestTimePlayed()`, which returns total time played and time played on the current level. Right after a level-up the "current level" part is about zero, so the addon stores the total at the start of each level, and the finished level's time is the difference. If you install the addon mid-level, that first level has no baseline and is skipped.

## Notes and limits

- Talents: Classic clients show points per talent tree. Retail shows points in the class, spec and hero trees.
- Professions and end bosses are matched by English name, so a non-English client won't show them.
- The portrait comes from Wowhead's icon CDN. A race it doesn't have an icon for yet falls back to the class icon.
- Screenshots over Discord's 9.5 MB limit are left off the post.
- On the first run, events already saved are marked as posted so the channel isn't flooded. Start the app with `--backfill` to post them instead.
- Settings and webhooks are saved in `%LOCALAPPDATA%\WoWCharacterTracker\config.json`.
