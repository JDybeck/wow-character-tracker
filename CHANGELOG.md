# Changelog

The app shows the notes for a new version when it finds an update. Each release takes its notes from its section here.

## v0.12.3

- Clearer console: each instant post's line says how long it took to reach Discord and where the time went, e.g. "In Discord 15s after it happened: the game sent it after 13s (instant posts wait until combat ends), then 2s for the screenshot and upload."
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

- Retail characters show their actual character render (the one Raider.IO and Warcraft Logs show) in the list, on the card and as the Discord sender picture. Needs one login with the updated addon.

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
