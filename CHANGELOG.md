# Changelog

The app shows the notes for a new version when it finds an update. Each release takes its notes from its section here.

## v0.26.0

- **Alliance and Horde colors**: each character row gets a thin blue or red edge and a small faction crest on its portrait, and the open details glow faintly in the faction's color. The addon now records the faction; older characters get it from their race.
- **Faction filter** on the Characters page (Alliance / Horde chips), shown when you have characters on both sides.

## v0.25.4

- Discord posts show **WoW Character Tracker** small in the footer, with the app's logo, next to the time.
- The posts' sender picture now shows the new logo (Discord had kept the old one cached).

## v0.25.3

- **Clearer app icon at small sizes**: the taskbar, tray and title bar show a simpler WCT with the ring that's readable at those sizes; the full framed logo stays for large icons.
- Discord posts end with a small **WoW Character Tracker** line that links to the app's GitHub page.
- After an update Windows is told the icon changed, so the taskbar and Start menu don't keep showing the old one.

## v0.25.2

- **New logo**: the WCT logo in the app's header, a new icon for the app (taskbar, tray, Start menu) and a new picture for Discord posts.

## v0.25.1

- **Fixed the note box erroring in game**: showing the box (or having it open at login) gave an "attempt to call a nil value" error every moment on newer clients, and its buttons never showed. Comes with the updated addon, so `/reload` once.

## v0.25.0

- **Character notes, now in game too**: every character has a Notes tab in the app for anything you want to keep track of (gear to farm, quests, plans). Type `/wct note` in game to see it in a box on screen, and click **Edit** in the box to change it right there.
  - Write it wherever is handy: the newest edit wins. The game gets the app's changes on `/reload`, and the app gets the game's when you `/reload` or log out.
  - Drag the box where you want it, then **Lock** it (its button, or `/wct note lock`). A locked box lets your clicks through to the game; `/wct note unlock` to move it again. It stays open across reloads until you close it.
  - Edit, Lock and the X only show while your mouse is over the box, so it's just your note the rest of the time (turn "Buttons only on mouseover" off in the addon's options to always see them).
  - Set how see-through its background is in the addon's options (below). Lost the box? Reset position there, or `/wct note reset`.
- **The addon has its own page in the game's options**: Esc, Options, AddOns, WoW Character Tracker (or `/wct options`). Show, lock or reset the note box, pick whether its buttons only show on mouseover, set its background opacity (it changes as you drag), and turn instant posting and the reload popup after a ding on or off. What the app decides (what gets posted, screenshots, chat announcements, reminders) is listed there too, read-only.
- **Played time fits**: the last seen column is wider, so long played times (100+ days) aren't cut off.

Comes with the updated addon, so `/reload` once after the app updates.

## v0.24.5

- **Played time in the character list**: each row shows the character's total time played in small text under the last seen time.
- **Notes tab for every character**: a notepad in the character details for anything you want to keep track of. Saved as you type, kept in the app's settings.
- **Column names line up with the rows**: the headers (class, level, item level, gold, last seen) sat a few pixels to the right of what's below them, because the list reserves room for its scrollbar. They now match.

## v0.24.4

- **Only the first boss kill per character**: an end boss is posted the first time each character kills it, not every run after (farming Stratholme or Molten Core no longer fills the channel). On by default; turn off "First kill only" under End boss kills on the What gets posted page to post every kill again. Every kill still shows in the app's highlights.

## v0.24.3

- **Forever's new dungeons count as boss kills**: killing the last boss of The Hall of Thanes (Durgen Dirgehammer), Ruins of Lordaeron (Rath'mael), Excavation Site: Wetlands (Relic Guardian), City of Dalaran (Shade of the Archmage), Alcaz Prison (Blazeroar) or Shaper's Terrace (Nanaya) is now posted like any other end boss. More are added as Forever's dungeons and raids come out. Comes with the updated addon, so `/reload` once.
- **Stratholme's two sides are told apart**: Balnazzar ends the Living side and Baron Rivendare the Undead side.

## v0.24.2

- **Guild and party announcements fixed**: with announcements on, every level-up was called out as "Reached max level"! Now only the real one is. The addon also stops taking screenshots for moments that won't be posted. Comes with the updated addon, so `/reload` once.
- **Under the hood**: the app and the addon were cleaned up so each rule lives in one place (which webhooks take a character, what each game has, event texts, how events are recorded), with tests that keep the addon and the app in step. Nothing else should look different.

## v0.24.1

- **Enchant tooltips on Forever too**: Forever only shows an enchant's stats ("Stamina +2"), so the addon now also saves the enchant's id and the app knows which enchanting spell it is. Hover it for Wowhead's tooltip, click for its page, on Retail and Forever. Comes with the updated addon, so `/reload` once.
- **Game tabs stay put**: the Retail and Forever buttons always sit in the middle of the header, a longer status line under the title no longer moves them.

## v0.24.0

- **Enchants under your gear**: each item on the Gear and bags tab shows its enchant right under its name in green, like "Nature's Fury" under a ring, instead of in a separate list. The "Enchant Ring -" part is left out, the slot already says that. Hover an enchant to see what it does (Wowhead's tooltip, like items), click it to open its Wowhead page. Works on Retail and Forever for enchants the game names; ones it only shows as stats ("+41 Agility/Strength & +115 Stamina") have nothing to look up.

## v0.23.1

- **Tidier gear effects**: the Equip: and Use: lines on the Gear and bags tab are folded away under "Effects (N)", click to open. Each item shows once with all its lines, multi-line effects keep their line breaks, and cooldowns read "1 Min 30 Sec" instead of game codes.
- **Catalyst note on Retail**: set bonuses on Retail say that catalysted items may not show properly.

## v0.23.0

- **Set bonuses, effects and enchants next to your gear**: the Gear and bags tab now lists the sets you're wearing (like "4/5") with each bonus and how many pieces it needs, the active ones lit green, every Equip: and Use: effect on your gear, and your enchants. It reads the game's own tooltips, so catalysted pieces that count for a tier set count here too, and tier bonuses show for your spec only. Works on Retail and Forever. Comes with the updated addon, so `/reload` once.
- **Gear and bags opens faster**: the gear and bags show right away and bank tabs fill in after, empty slots are a count ("12 free slots") instead of hundreds of empty boxes, and item tooltips are only built when you first hover them.
- **Crests and talents look like items**: hovering a crest, a talent or a looted item in the highlights shows the same game-style tooltip as items in your bags, and clicking it opens its Wowhead page.
- **Easier to read crest tooltips**: the description keeps its paragraphs and list lines instead of running together, and the cap line is gone.
- **Spec icons**: the class column shows your spec's icon (Blood, Fury, Devourer, Feral Combat...), or the role icon when the spec isn't known yet.
- **Click a page tab again to start it over**: clicking Characters while you're on it closes the open character, resets the filter and scrolls to the top. Items clears the search and filters. The other pages scroll back to the top.

## v0.22.0

- **Stream card and highlights no longer cut off**: as a single source (`?only=card`, `?only=highlights`) the part is scaled up to fill the source, but the page only kept its unscaled height, so everything below the first lines was cut away in OBS and Streamlabs. Now the whole part shows.
- **Fast with lots of characters**: with 60+ characters the app froze for 10 seconds or more at startup and again when opening Items. Now the Items list is only built when you open it and only draws what's on screen, a character's gear, bags and icons load when you open that character, and Wowhead lookups run in the background. Startup went from about 30 seconds of work to 2 on 64 Retail characters.
- **Realm filter under the page tabs**: with many realms the filter chips pushed the Characters, Items... tabs off screen. The chips now sit at the top of the Characters and Items pages and wrap onto more lines when needed.
- **Tell your guild or group** (In game page): the addon can write a line in guild and/or party/raid chat when you level up, hit max level, kill an end boss, loot an epic (as a clickable link), get a mount, win a duel, Mak'gora or battleground, reach a rating step, or die in hardcore. Pick which. Messages wait until you're out of combat, and boss kills don't list the group. Off until you turn it on.
- **Battlegrounds**: wins and losses are tracked (with the updated addon), shown as highlights, as a record on the stream card, and a win pops VICTORY! on the stream.
- **Stream highlights show losses too**: duel and Mak'gora losses now show in the highlights box, the same lines as in the app.
- **Loot is a highlight right away**: loot that isn't posted used to wait for /reload before it showed. Now it comes in live like everything else.
- **Sort your characters**: click CLASS, LEVEL, ITEM LEVEL, GOLD or LAST SEEN to sort, click again to flip. Race, played time and rating (Retail) sort from next to CHARACTER. Remembered per game.
- **Role icons**: tanks, healers, and damage dealers split into melee, ranged (bow) and caster. Forever characters get one from their top talent tree.
- **Same picture everywhere**: Discord posts showed a race portrait for Retail characters while the app showed their real render. One rule now picks the picture for the app, posts and cards. The post sender is always the Character Tracker logo, with the character's picture in the post.
- **Smoother start**: the window fades in once it's ready instead of stuttering.
- **Test alert fills the highlights too**: each test (boss kill, loot, duel...) also puts its line in the highlights box for a few seconds, so you can place the highlights source without waiting for real loot.

## v0.21.1

- **Missing ding screenshots fixed**: a screenshot taken mid-fight could be overwritten by the tracker's own data shot when the fight ended in the same second (the game names screenshots by the second), so the post went out without a picture. Shots now always land in different seconds. Comes with the updated addon, so log in (or `/reload`) once.
- **In-game portrait on every post** (Forever): dings, boss kills, loot and the rest now show the character's in-game portrait as the thumbnail, like character cards and talent posts already did. Characters set to Standard keep the standard picture.

## v0.21.0

- **Your ratings** (Retail): a character's overview shows its current Mythic+ score and PvP ratings (2v2, 3v3, RBG, Solo Shuffle, Blitz), each with its icon. Only brackets you have a rating in show up, and nothing shows on Forever. The stream card lists them under the keystone too. They come with the updated addon, so log in (or `/reload`) once.

## v0.20.0

Alerts, a clearer card, and a pile of small things.

- **Alerts, not just DING!**: the stream alert now pops up for your other big moments too, each in its own colors: **MAX LEVEL!** with turning light rays, **VICTORY!** for an end boss or a duel win, **LEGENDARY!**, **EPIC LOOT!** and **RARE LOOT!**, **NEW MOUNT!**, **MAK'GORA!** in blood red, a calm gray **R.I.P.** for a hardcore death, and **MILESTONE!** for Mythic+ and PvP rating steps. Pick which ones under **Alerts** in the Stream overlay panel (rare loot is off by default). **Test alert** shows the next kind on each click. The address for just the alert is now `?only=alert` (`?only=ding` still works).
- **The alert's glow no longer ends in a box**: on its own source the alert leaves room for its burst of light, and the edges fade out softly.
- **Card and highlights each have a job**: the card is the character right now, with its stats on the left (item level, played, gold, your Mythic Keystone on Retail, duel and Mak'gora records) and its leveling on the right (last level, the ones before, today). Without level times the stats get the card. The highlights box is what happened lately. The card's top line has race and spec too.
- **What the overlay counts**: three choices at the bottom of the panel, the one in use highlighted: **Start counting now** (shows since when; click again to restart), **Today** (since midnight, starts over by itself) and **All**.
- Mythic+ and PvP rating only show up as overlay options while you're streaming a Retail character.
- **Clearer buttons** in the Stream overlay panel: they have a frame now and light up on hover.
- **Hover for info**: tooltips come up quicker (0.3 seconds) and stay longer, and the stream options all say what they do.
- **Your Mythic Keystone** (Retail) in a character's overview and on the stream card: level and dungeon.
- **Post talents to Discord**: next to the Wowhead talent calculator link, posts the build on its own (level, race and class, points per tree, the talents picked and the Wowhead link).
- **Items: leave a place out**: click **Equipped**, **Bags** or **Bank** to show only that place, click it again to leave it out instead (red and crossed out), and once more for everything.
- **Updates are hard to miss**: a filled **Update to v...** button at the top, and a bolder banner that says what Update now does.
- **Screenshots that went missing**: when the game doesn't take an event's screenshot (it seems to happen when you ding mid-fight), the addon now tries again right after combat, and the app waits for that later picture instead of posting without one. The addon keeps a short log of its last shots for troubleshooting.
- The **made by / tip** line stays in view at the bottom of the right column, the panels scroll above it.
- The Discord panel's webhook steps fold away behind **How do I make one?**.
- README: a table of contents, and fresh pictures.
- **Find it in Windows search**: the app adds itself to your Start menu, so typing "WoW Character Tracker" finds it. The Uninstall page removes the shortcut with the app.
- **Icons for stats and places**: each stat in a character's overview and on the Discord character card has its icon (Strength, Agility, Crit...), and the last zone says what kind of place it is: dungeon, raid, battleground, city, town or the open world. The place kind comes with the updated addon; capital cities show right away.
- **Your in-game portrait in Discord**: on Forever, talent posts and text character cards use the portrait you picked in the app (the one from the game) instead of the standard race picture.
- The webhook help (the round **?**) also shows while you add another webhook, not only before the first one.
- **A smaller download**: the exe is about 14 MB smaller (60 MB instead of 73). The tray icon no longer pulls in a whole second UI toolkit, and Windows' translations for languages the app doesn't use are left out.

## v0.19.0

The streaming release.

- **Stream overlay panel**: all the stream settings now have their own panel between Discord and the console (fold it away by clicking its title). Turn the overlay on there, copy its addresses, preview it, send a test ding and open the text files.
- **Place each part on its own**: copy the address of just the **card**, just the **highlights** or just the **DING! alert**, and add them as separate Browser sources. A part on its own fills its source, so resizing the source in OBS resizes it. Or one address for everything.
- The right column (Discord, Stream overlay, console) scrolls as one when it doesn't fit, and the console keeps a readable height. No scrollbar when everything fits.
- **Background**: a slider from see-through (just the text) to solid.
- **Choose what shows**: level times, today's levels, the duel record, the Mak'gora record and highlights, each on or off. For highlights, pick which kinds count: loot, boss kills, mounts, duels, Mak'gora, deaths, Mythic+ and PvP rating.
- **Duel and Mak'gora records** on the card (won and lost), and in `duels.txt` and `makgora.txt` for text sources.
- **Start from now**: the overlay only counts what happens from that moment (records, highlights, level times), handy for a fresh stream. Everything is still tracked in the app, and **All time** brings it back.
- **A WoW-style DING!**: gold lettering with a dark edge, an ornament line and the level in the game's yellow.
- A small **tip** link next to the credit in the corner.
## v0.18.0

- **Faster instant posts**: the addon's data screenshot is saved as a high-quality JPEG instead of PNG (a fraction of the size, and much quicker for the game to write), and the app notices it the moment it's written instead of checking every half second. In tests a shot was picked up 70 ms after the game finished writing it, where it used to take 0.3 to 0.8 seconds.
- **Pick the corner** for the data grid on the **In game** page: top left (as before), top right, bottom left or bottom right. The app finds it in any corner, so changing it never loses a post.
- The grid's label is now one small line ("Character Tracker /wct"), and you can hide it.
- More reliable grid reading: a busy game scene below the grid could make a shot unreadable at some resolutions; the app now measures the grid from its clock row only.
- Animations no longer replay on the open character every time the list refreshes.
- **Color themes**: pick a palette per game under Settings. Retail: Midnight (the current expansion), Void arcane and Silvermoon. Forever: Classic gold, Forever wisp, Emerald Dream and Blackrock ember.
- **Characters in frames**: each character in the list sits in its own box with a sharper edge in the theme's color, brighter on hover and when open.
- **Switch animation**: switching games sends a blue wisp flying to Forever, or a void orb to Retail. It plays once, in the tab strip.
- **Turn a game off**: only play Forever (or only Retail)? Untick **Track Retail** (or Forever) under Settings. The app then doesn't look for that game, read its data or install the addon there. Its tab stays, with a button to turn it back on, and a button to remove the addon from that game if it's still installed. Nothing is deleted otherwise.
- **Stream overlay and text files** (Settings, Stream overlay): a page for an OBS or Streamlabs Browser source with the character you're playing, its latest level times, today's levels and highlights, and a big **DING!** alert when you level. Add `?only=ding` for just the alert, `?scale=1.5` to make it bigger. Text files with the same info (level, last level time, today...) for Text sources. Only your PC can open the page.
- **Animations**: a character's details unfold when you open them, pages slide in from the side of their tab (Characters, Items, What gets posted, In game), and so does the game when you switch between Retail and Forever. Untick **Animations** under Settings to have everything switch instantly (the tab orb too).
- A gold bag next to the total gold, instead of a card.
- **Test ding** next to the stream overlay settings shows the DING! alert in OBS right away, to set it up without leveling.
- Forever portraits are replaced in place (one file per character, swapped in whole), and a check-in screenshot the app couldn't read is deleted instead of staying in your Screenshots folder.
- The test characters' surname is TESTCHAR, so their posts can't be mistaken for real ones.
- README: new pictures (a real level-up post, the four Forever themes, Items, settings, Uninstall, the stream overlay) and a Streaming section that walks through the OBS setup.
- **Picking the game folder is clearer**: Browse asks for your World of Warcraft folder (or the game's folder inside it) and starts there. Any folder inside them works too, picking the other game's folder finds the right one next to it, and when it can't, it says why (like "This World of Warcraft folder has _retail_ but no _classic_beta_. Is Forever installed?").

## v0.16.0

- **Item tooltips look like the game's and Wowhead's**: dark box, quality colors, yellow item level, green effects, class colors, the slot and type on one line ("Two-Hand ... Mace"), and coins in their colors.
- **Items by type**: the Items page has type chips next to the place chips: Gear, Consumables, Trade goods (reagents, trade goods, gems, recipes), Quest and Other. The addon tells the app each item's type, so /reload once after updating.
- Retail's fifth bag shows as **Reagent bag**.
- **Duel opponents**: duel and Mak'gora posts and highlights say who you fought, like "level 21 Orc Warrior" (when the game could see them: target, party, nameplates, or when they challenged you). Their spec isn't available to addons.
- **Wowhead talent link fixed** for Forever: it now opens with your points filled in (Wowhead's link format needed a "v2").
- A **Feedback** button at the top opens the Discussions page.
- README: pictures of where to make a webhook in Discord, and that you can share a channel with friends or make your own.
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
