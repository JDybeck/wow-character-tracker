# wow-character-tracker

Posts your World of Warcraft moments to a Discord channel, the moment they happen: level-ups, end boss kills, rare and epic loot, new mounts, hardcore deaths, duels, Mak'gora wins and Mythic+ or PvP rating milestones. Posts can include a screenshot taken in game. The app also shows all your characters with their level, item level, gold, talents, stats, highlights and level history, and can post a character card to Discord.

Made with WoW Forever (Classic+) in mind, and works for Retail too. Each has its own tab and its own look.

```
[Night Elf portrait]   Night Elf Warrior
                       Zeb Skogsbrand just dinged level 40.
                       39 to 40 took 1h 59m.
                       Total played: 3d 5h.
                       They dinged in Scarlet Monastery.

                       Professions: ⛏️ Mining 210, 🔨 Blacksmithing 205
                       Talents: Arms 21 / Fury 10 / Protection 0
                       [screenshot]
```

## Getting started

1. Download `WoWCharacterTracker.exe` from the [Releases page](https://github.com/JDybeck/wow-character-tracker/releases/latest). Nothing else to install. Windows might show a SmartScreen warning since the exe isn't signed, click **More info** and then **Run anyway**.

   Developers can build it themselves instead: run `build.cmd` (needs the [.NET 10 SDK](https://dotnet.microsoft.com/download)), it lands in `dist\`.
2. Double-click `WoWCharacterTracker.exe`. Pick **Retail** or **Forever** at the top. Three checks show what's set up for that game:
   - **Game folder**: found automatically, with the game version it found so you can see it picked the right one. Click **Change** to pick another folder. While Forever is in beta, its folder is `_classic_beta_`.
   - **Addon**: click **Install** once to put the addon in that game's AddOns folder. After that the app keeps it up to date on its own, including the interface number after patches.
   - **Discord**: paste a webhook link in the Discord panel (it explains how to get one). It's checked with Discord before saving. **Send test** shows you a sample post.
3. That's it. With **Open with World of Warcraft** on (the default), the app waits in the system tray and opens when you start WoW, like Archon. When you close WoW it goes back to the tray, and it keeps tracking the whole time. Closing the window also sends it to the tray; right-click the tray icon to quit. Untick the option at the top to use it as a normal app instead.
4. In game, type `/wct test`. The console in the app should say instant posting works within a few seconds. The addon reminds you of `/wct help` when you log in, and the (i) icon at the top of the app lists the commands.

### What gets posted

Each game has a **What gets posted** page, in three columns:

- **General**
  - **Level-ups**: up to a level of your choice, post every few levels, then every level after that. Forever starts at every 5 levels up to 30, then every level. Reaching max level is always posted, with extra flair.
  - **Loot**: rare (blue) and epic (purple) items, separately. Legendary items are always posted, with extra flair.
  - **New mounts**: bought from a vendor or learned.
- **PvE**
  - **End boss kills**: the last boss of a dungeon or raid, with the group. You can skip the post when someone in the group matches a regex, for friends who run the tracker too, so the same kill isn't posted twice.
  - **Hardcore deaths**: what killed the character, total played and the gold they had on them.
  - **Mythic+ rating** (Retail): when your rating passes one of your steps (1000, 1500, 2000... by default).
- **PvP**
  - **Duels won**: who you beat, or who ran away.
  - **Mak'gora won** (Forever): the hardcore duel to the death.
  - **PvP rating** (Retail): when a rated bracket (2v2, 3v3, Rated Battlegrounds, Solo Shuffle, Blitz) passes one of your steps.

Every option has a **Test** button that posts a sample, using the selected character. Most have **Screenshot** (the addon takes one in game a moment after it happens) and **Post screenshot** (attach it to the Discord post). Screenshot settings reach the game on your next `/reload` or login. On Forever you can also pick the **Name in posts**: full name (Ronnie Raket), first name or surname.

Posts show the character's portrait as the sender picture, item and mount names link to Wowhead, and professions get an emoji.

### The app

- **Characters**: every character you've logged in with the addon, with class, spec, level, item level, gold and when you last played. The bottom of the list shows the total gold for the realm (Retail) or ruleset (Forever) you've filtered on. Hardcore characters are marked. Click one for guild, total played, gold, crests (Retail), professions, talents, stats, last zone, highlights and level history. On Forever, talents are split into the class's three trees and every talent you picked is shown with its icon and rank. On Retail, **Open in Wowhead talent calculator** opens your build there.
- **Tags**: make your own (like Main, Alts, Guild) and put them on characters. Webhooks can post only for some tags.
- **Post to Discord**: sends the character as a card picture, with icons and quality colors. The gear next to it picks what the card shows (item level, played, gold, crests, professions, talents, stats, guild, last seen, levels, highlights). **Remove from list** hides a character you don't play anymore; if you play it again, it comes back.
- **Filter**: chips above the list filter by realm on Retail and by ruleset (Hardcore, Self-found, Normal) on Forever.
- **Discord**: add as many webhooks as you like. Click one to turn it on or off for the game tab you're on; posts go to every webhook that's on. The gear opens its options: which games it posts for, on Forever which rulesets (so a hardcore channel only gets hardcore characters), and which character tags (none picked means every character). Click the pencil to rename a webhook, like "#leveling / My Guild". Hover a webhook to see its masked link.
- **Console**: what the app is doing, like posts sent and anything that needs your attention.
- **Sync now**: reads the game's data again and posts anything new.
- **Test characters**: the switch at the top adds three made-up Forever characters (Zeb, Fågelmannen and Goggo) so you can try everything without playing. They're never posted on their own.
- **Updates**: the app checks for a new version when it starts and every few hours. **Check for updates** next to the version number checks right away. **Update now** downloads it and restarts, and **What's new** shows the notes from [CHANGELOG.md](CHANGELOG.md).

### Making a Discord webhook

In Discord, hover the channel, click the gear (Edit Channel), then Integrations, Webhooks, New Webhook, and Copy Webhook URL. You need the Manage Webhooks permission on the server. Posts use the webhook's name and the character's portrait as the sender.

Treat the link like a password: anyone with it can post in that channel. The app only ever shows it masked, and it's only stored on your PC.

### In game

- `/wct` shows your last 10 level times for the character you're on.
- `/wct test` checks that instant posting reaches the app.
- `/wct live` turns instant posting on or off.
- `/wct help` lists these in game.
- `/wct popup` turns on a reload popup after a ding, if you'd rather not use instant posting.
- `/wct debug` saves what your client's talent, name and guild functions return (for tracking down problems on new clients).

## How it works

WoW addons can't make network calls and only save their data on `/reload` or logout. The one file an addon can make the game write right away is a screenshot. So for each event, the addon shows a small black and white grid (about 320 x 28 px, with a "WoW Character Tracker" label next to it) in the top-left corner for a single frame, takes a screenshot and hides it again. The app reads the event from the grid within a second, posts it and deletes that screenshot. While that one screenshot is taken, the screenshot format is set to PNG (your own settings come right back) and the "Screen captured" message is hidden.

Only events that will actually be posted go out this way, plus one check-in about 15 seconds after you log in, so your character's row in the app updates right away. Everything else reaches the app on your next `/reload` or logout. In-game screenshots you turned on are only kept for events that get posted. Nothing is posted twice.

Level times come from `RequestTimePlayed()`: the addon stores total played at the start of each level, and the finished level's time is the difference. Installing the addon mid-level is fine, the first level after installing is timed in full.

## Notes and limits

- Talents: Forever shows points in its three trees plus each pick. Retail shows points in the class, spec and hero trees.
- Professions and end bosses are matched by English name, so a non-English client won't show them.
- Rating milestones start counting from the first time the addon sees your rating, so ratings you already had aren't posted.
- The portrait comes from Wowhead's icon CDN. A race it doesn't have an icon for yet falls back to the class icon.
- Screenshots over Discord's 9.5 MB limit are left off the post.
- On the first run, events already saved are marked as posted so the channel isn't flooded. Start the app with `--backfill` to post them instead.
- Settings and webhooks are saved in `%LOCALAPPDATA%\WoWCharacterTracker\config.json` and survive updates.
