# Changelog

The app shows the notes for a new version when it finds an update. Each release takes its notes from its section here.

## v0.11.0

- Forever talents: read from Forever's talent trees (it has no classic talent tabs), e.g. "Holy 7 / Protection 3 / Retribution 0". Talents also update again after the first save.
- Forever talents show the spec, like "Retribution 16".
- If the game ever blocks the addon ("blocked from an action"), the addon notes which function it was, so it can be fixed.
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
