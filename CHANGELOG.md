# Changelog

The app shows the notes for a new version when it finds an update. Each release takes its notes from its section here.

## v0.15.2

- Switching between Retail and Forever is much lighter: each game keeps its Characters and Items pages instead of building them again, so a switch takes a few hundredths of a second instead of up to 3 seconds of heavy CPU (Retail was the worst). Memory no longer grows with every switch.
- Icons and portraits are loaded once and shared, instead of downloaded and decoded again every time a page shows them.
- The tab colors change in one go instead of fading color by color (the fade redrew the whole window many times).
- Posts from test characters are marked: the name gets [TESTCHAR] and the post says it's a test character, so nobody in the channel takes it for real.

## v0.15.1

- Item tooltips in the app read properly again: lines that ran together ("Binds when picked upTwo-HandMace") are split, the damage and speed share a line, and the sell price shows its g/s/c.

## v0.15.0

- **Uninstall**, at the bottom of Settings: shows everything the app and the addon created on your PC (the app, the addon in each game, your character data in each game, settings and caches), with every path and size, and you pick what goes.
- Presets for the usual choices: **Keep my data** (removes the app and the addon, keeps your characters and settings), **Remove everything**, and **Only the app**.
- Afterwards it lists what was removed (and why, if something couldn't be), then closes and removes itself. It also takes the app out of Windows startup.
- It waits for the game to be closed, since the game writes the addon's saved file again when it exits.

## v0.14.0

- **Items**: a new page with everything your characters carry (worn, in bags and in the bank), with icons, in one list per server. Search by name to see who has what, and where, or show only what's equipped, in bags or in the bank.
- Items update when you /reload or log out (the game only saves then). The app says so, shows when and where each character was last saved, and the console notes each logout or /reload.
- The character details are in tabs now: **Overview**, **Gear and bags** and **Leveling**.
- **Gear and bags**: what the character wears, laid out like the character window, its bags slot by slot, and its bank as of the last time you opened it. With icons, quality colors and Wowhead tooltips. Needs the updated addon and a /reload (or logout).
- **Leveling**: average, fastest and slowest level, a chart of your level times, how your item level and professions grew while leveling, and your duel and Mak'gora record.
- Forever: the played time left to max level at your recent pace. It counts that later levels need more XP, and that monsters give more XP the higher you get.
- Boss kills in the highlights say who was in the group; hardcore deaths say how much played time and gold went with them.
- Total played for the characters you've filtered on, next to the total gold.
- Click any item (gear, bags, bank, Items page) to open it on Wowhead.
- The Items page shows its items in as many columns as the window has room for.
- Updating the app shows each step in the banner: the download with its progress, checking it, putting it in place, restarting.
- On a flight path, when your bags or gear changed since the last save, a small box in game offers a /reload so the app's items get up to date (you keep flying). Once per flight; switch it off under What gets posted.
- A reminder in chat to /reload when your bags or gear changed since the last save: after the bank, mailbox, a vendor or the auction house, when you leave a dungeon (at most every 10 minutes), and every 30 minutes. Switch it off under What gets posted.
- The slot under the weapons says Relic for Paladins, Druids and Shamans, Ranged for everyone else.
- Lost duels and Mak'goras are recorded too, and running away from a duel. They show in the highlights and the duel record ("Duels: 5 won, 2 lost"), but only wins are posted.
- The realm shows its region when it's known, like "EU · Tarren Mill".
- Opening Test characters (and the list in general) is quicker: the details are only built for the open character and tab.
- The README shows the Gear and bags tab.
- A new **In game** page holds what the addon does in game besides posting: the reload box on flight paths, the chat reminder and advanced combat logging.
- The page tabs and the character details tabs are boxes with icons, the open one filled in the tab's colors.
- The (i) next to the settings says what reaches the app right away and what waits for a /reload or logout (loading screens don't count).
- Forever characters get their own portrait in the app (the picture your unit frame shows), taken from the addon's check-in right after you log in. Unit frame addons don't change it, since the addon asks the game for the portrait itself. Each character can switch between In-game and Standard (Overview tab). Discord posts keep the race portrait.
- Retail's character window has no Ranged slot, so the app doesn't show one there.

## v0.13.0

The app and the addon now share one version number, starting here.

- Under the hood: a cleanup pass through the app and the addon, so they're easier to keep working. Nothing changes in what gets posted.
- Removing a webhook asks right in its row instead of a Windows dialog, like removing a character. Deleting a tag (right-click it in a webhook's options) no longer asks in a Windows dialog either.
- A ding the addon can't time (no starting point for the level) is logged right away without a time, instead of waiting for the game to answer /played.
- /wct no longer stops at a level that was logged without a time.
- /wct debug is shorter and saves what's useful now: played time, /played requests, errors, talents and names.
- The epic loot Test posts Sul'thraze the Lasher (Arcanite Reaper is only rare).
- Items Wowhead's Forever database doesn't have yet (like raid gear) get their tooltip picture from its Classic database instead.

## v0.12.4

- Fixed level-ups (and other moments) not being recorded at the ding on Forever: the game hides some numbers like your stats from addons at moments like a ding, and reading them made the addon give up on the whole event. Those numbers are now skipped, and one detail that can't be read never stops an event again.
- Level times no longer depend on the game answering the addon's /played requests: the played time at logout carries over to the next login, and a brand new character starts at 0.
- The Addon card no longer offers to "update" to an older addon when a newer one is installed.
- /wct debug also saves the addon's /played requests and answers.
- Loot posts show the item like its Wowhead tooltip, as a picture: icon, name in its quality color, item level, damage and speed, stats, green Equip lines and the sell price in coins. A screenshot, if any, comes right under it in the same post.
- New switch on What gets posted: **Advanced combat logging** turns the game's own setting on (or off again) at your next login or /reload.

## v0.12.3

- Level-ups post right at the ding. The addon used to wait for the game's answer to its /played request, and when that answer went missing it asked again 15 seconds later, so some posts (and their screenshots) came late. Now it times the level from the played time it already knows, and the answer only fine-tunes the next level's start.
- Clearer console: each instant post's line says how long it took to reach Discord and where the time went, e.g. "In Discord 3s after it happened: the game sent it after 1s, then 2s for the screenshot and upload."
- Level-ups read "dinged level 12 in 21m 40s" in the console and status line.

## v0.12.2

- Changing settings while the game runs shows a banner at the top: type /reload in game to use them. It goes away by itself once the game has loaded them (after a /reload, a new login or closing the game).
- The Game folder, Addon and Discord cards use two lines in narrow windows instead of cutting the text off (the full text is in the tooltip).
- The console says how long an instant post took to reach the app, to see where the time goes.

## v0.12.1

- Level-ups that aren't posted still reach the app right away, so the level history stays current without a /reload (same for boss kills, mounts, duels and rating steps). Loot that isn't posted still waits for /reload, to keep the corner flicker down.
- Post any highlight (loot, boss kill, mount...) to Discord from the character details, with the small button at the end of its row.
- Looted items link to Wowhead in the post's text too.
- With the game closed, the app stops its once-a-minute check of the saved files and keeps showing the last known state.
- Every level gets logged:
  - Level 1 to 2 is always timed (level 1 starts at 0 played), even on a brand new character or when the addon was installed after level 1.
  - A /reload or logout right after a ding no longer loses that level; the addon catches it up at the next login.
  - If the game doesn't answer the ding's /played request, the addon asks again.
  - A ding with no starting point (addon installed mid-level) is still logged, with the time shown as "?".
- Fixed reaching max level: the addon hit an error and didn't log it.
- New screenshot option: hide the whole interface for the shot (like Alt+Z), so it's just your character and the world.
- Sync now shows that it's working (the icon spins) and the console says what it read and posted.
- A settings menu (the gear at the top) with Open with World of Warcraft, Test characters and a new switch to keep the window open when the game closes.
- Remove from list asks right in the character details instead of a Windows dialog.
- Narrow windows: the top bar no longer puts text behind the buttons, and the character list keeps room for names.

## v0.12.0

- What gets posted is laid out more cleanly: the level rule lines up in one column, Test buttons look like the other chips, and Post screenshot only shows once Screenshot is on.
- Max level has its own switch instead of always being posted.
- Copy settings / Paste settings: share your What gets posted settings as a code, so friends can use the same ones.
- New default rating steps: Mythic+ 2000, 2500, 3000, 3500, 4000 and PvP 1800, 2100, 2400, 2700 (old defaults are updated). A step posts once per season, only when you climb past it.
- The page and the Discord panel start at the same height.

## v0.11.0

- Screenshots go out at full size first, so boosted servers get the full picture. If Discord says one is too big, a smaller copy is sent instead of none.
- Long names no longer run into the Class column; the TEST/HARDCORE tag sits next to the realm.
- The README has screenshots.

- Retail characters show their actual character render in the list, on the card and as the Discord sender picture. Needs one login with the updated addon.

- Under the hood: the app and the addon are reorganized into smaller parts with one job each. Nothing changes in how they work.

- Logging in on a game switches the app to that game's tab, with the character picked.

- Fixed the "blocked from an action" error at login on Forever: the addon no longer listens to the combat log, which newer clients keep from addons. Hardcore death posts don't name the killer anymore because of that.
- Fixed the class showing twice ("Warrior Warrior") for characters without a spec.
- The Wowhead talent link shows right after login. Forever's opens Wowhead's Forever calculator with your build filled in.
- Forever uses Wowhead's Forever database, so Forever-only talents get their names and icons.
- Stats and talent picks show after a /reload, not only after logging out.

- The game tabs show the game's logo: the current expansion (Midnight) for Retail and Forever's own. They're downloaded from Warcraft Wiki the first time and kept on your PC.

- What gets posted is laid out in three columns: General, PvE and PvP.
- New posts: duels won, Mak'gora won (Forever), Mythic+ rating and PvP rating milestones (Retail, steps you pick). Duels and Mak'gora can have screenshots.
- Character stats (strength, armor, crit and so on) in the details and on the card.
- Retail: **Open in Wowhead talent calculator** opens the character's build.

- Forever talents: read from Forever's talent trees (it has no classic talent tabs), e.g. "Holy 7 / Protection 3 / Retribution 0". Talents also update again after the first save.
- Forever talents are split into the class's three trees, like "Holy 0 / Protection 0 / Retribution 16", and the tree with the most points shows as the spec. Each talent you picked is saved too and shown with its icon and rank in the character details and on the card.
- If the game ever blocks the addon ("blocked from an action"), the addon says in chat which function the game blocked and notes it, so it can be fixed.
- Forever surnames: the full name (Ronnie Raket) is stored and shown. What gets posted has "Name in posts": full name, first name or surname, for posts and character cards. Party members in boss posts get their surname too.
- The app updates the addon in your game folders on its own when the app is newer. Installing it the first time still waits for your click.

- "Check for updates" next to the version under the title, and it says what it found (up to date, or the new version). The app also checks again every 6 hours while it runs.
- /wct debug saves what this client's talent, guild and name functions return, to sort out talents and surnames on Forever.
- The reload popup is only set up when it's used (less risk of "blocked from an action" errors).

## v0.10.0

- Post to Discord sends the character card as a picture, with crest, profession and item icons and quality colors, in the tab's theme.
- The gear next to Post to Discord picks what the card shows: item level, played, gold, crests, professions, talents, guild, last seen, levels, highlights.
- Guilds are stored and shown in the character details (and on the card if you turn it on). Needs the updated addon.
- Posts use the character's portrait as the webhook picture.
- Item and mount names in posts link to Wowhead, and professions get an emoji.
- The small data grid in the corner now has a "WoW Character Tracker" label next to it, so it's clear what it is.
- The app asks before installing or updating the addon: the Addon card shows Install or Update, and nothing is written to the game folder until you click it.

## v0.9.1

- Fixed updating: the new version now waits for the old one to close instead of quitting right away, and the old one no longer shows a .NET error while closing.

## v0.9.0

- Each game tab has its own look: Retail is Midnight violet, Forever is classic gold.
- Highlights: "Looted" is plain white and the item has its quality color (mounts too, once Wowhead answers).
- The (i) commands icon is centered.
- Releases and updates now come from the main repo (github.com/JDybeck/wow-character-tracker).

## v0.8.0

- Icons: crests and looted items use their real icons from Wowhead, with tooltips. Professions have their icons too.
- Gold shows as gold, silver and copper coins. Under 100g the list also shows silver, under 1g silver and copper.
- Crests come from the game's Crests section, so only this season's crests show up.
- Gold is kept when logging out (the game reports 0 gold during logout).
- The live data grid is much smaller (about 320 x 28 px instead of 640 x 120).
- Clicking the open character's row closes its details.
- Test characters: a switch at the top adds three made-up Forever characters to try the app without playing.
- The app checks for updates on start, and there's a button for it. Updating downloads the new version and restarts.

## v0.7.0

- Gold per character, and the total for the realm (Retail) or ruleset (Forever) you've filtered on.
- Retail crests in the character details.
- Reaching max level and looting a legendary are always posted, with extra flair.

## v0.6.1

- Fixed: on Classic-based clients like Forever, the addon could block Esc and show "blocked from an action" after a ding.
- Data grids wait until combat ends. Your screenshot of the moment is still taken right away.
- New "Hide chat in screenshots" option.
- Test buttons for every kind of post.
- Talents aren't shown until there's something to show (level 10).
- The addon is called WoW Character Tracker in the AddOns list.

## v0.6.0

- Boss kill posts list the group (name, level, race, class).
- Skip boss posts when someone in the group matches a pattern, so friends don't all post the same kill.
- Your own tags on characters, and webhooks that only post for some tags.
- Post a character card to Discord from the character details.
- In-game command is now /wct. The (i) icon in the app lists the commands.

## v0.5.0

- Instant posting through a small data screenshot (the chat log only gets written when the game closes).
- The app waits in the tray and opens when you start WoW.
- Webhooks can be switched on and off per game, and limited to Forever rulesets.
- Filter characters by realm or ruleset. Remove characters from the list, rename webhooks.
- New logo.

## v0.4.0

- Retail and Forever tabs, each with its own game folder and settings.
- "What gets posted" page: level rule, rare and epic loot, mounts, end boss kills, hardcore deaths, screenshots.

## v0.3.0

- The companion became a window app with your characters, a webhook list and a console.

## v0.2.0

- First release: posts level-ups to Discord.
