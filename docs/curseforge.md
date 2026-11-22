# WoW Character Tracker

**Your characters, your level times and your friends' characters in one window, plus a line in group chat when something big happens.**

Made for WoW Forever, and works on Retail too. Everything on this page works with the addon alone. A free Windows app adds Discord posts and a stream overlay on top, see the end of the page.

## What it does

### A window in game

Type `/wct` or click the WCT button on the minimap.

- **Home**: the character you're on, with level, item level, gold, played time and time on this level. A chart of your level times, with your average, fastest and slowest level. On Forever, how much played time is left to max level at your recent pace.
- **Today**: your biggest numbers since midnight (levels, boss kills, epics, mounts, gold earned, herbs, veins and more), and the newest moments from you and your friends.
- **Characters**: every character on this WoW account, with realm, item level, zone, gold, professions, talents, level times and gear.
- **Recipes**: browse any character's known recipes like the profession window, and **Who knows it?** finds a recipe across your characters and your friends'.
- **Note**: a note per character, in a box you can lock anywhere on screen.
- **Looks**: four themes, and the window can be resized.

### Group lines

One line in party or raid chat, as you, when something happens. You pick which:

- Dings, with how long the level took: "Ding! Level 23. 22 to 23 took 41m 12s."
- End boss kills, and Mythic+ runs on Retail
- Epic and legendary loot, as a clickable link
- New mounts
- Duel, Mak'gora and battleground wins
- Profession milestones and hardcore deaths
- Reputation milestones, like reaching Exalted with Argent Dawn
- Legacy achievements (Forever), like Conqueror of the Lair or Master Spelunker
- What came out of the weekly In Demand satchel from Booty Bay (Forever), like a Gold Ring that sells for 100g

The lines wait until you're out of combat and are rate limited, so they never spam. Off until you turn them on.

### Share with Battle.net friends

Friends who also have the addon can see the characters you share with them: level, class, spec, item level, gear, talents, professions, level times and highlights. Gold and map position are off unless you turn them on.

- **Nothing is shared until you say so.** A friend asks, you get a popup with **Allow**, **Not now** or **Never**. You pick per friend what they get.
- **Friends on the map**: friends who share their position show up on your world map with their class icon. On Forever it also tells you if they're on your layer. Never from inside a dungeon, raid or battleground.
- **Friends' moments in chat**: "Kitty Pryde (Rasmus): Ding! Level 40." when a friend levels, kills a boss or gets a new mount.
- **Find online friends** checks which of your Battle.net friends have the addon.
- **Incognito** (`/wct incognito 1h`) keeps a stretch of play to yourself.

Everything goes through Battle.net's own addon messages while you're both online. Nothing goes out in combat except map positions, and positions are never saved.

## Commands

- `/wct` opens the window
- `/wct times` shows your last 10 level times
- `/wct note` shows or hides the note box
- `/wct friends` lists who you share with, `/wct friends help` has the rest
- `/wct recipes <name>` finds who knows a recipe
- `/wct incognito 1h` (or 30m, 2h, 4h), `/wct incognito off`
- `/wct options` opens the options
- `/wct help` lists them all in game

## Privacy

The addon only talks to your group chat and to Battle.net friends you've chosen to share with. It writes only to the game's own SavedVariables and Screenshots folders.

## Want more? The free app (Windows)

The addon is complete on its own. If you also want these, get **WoW Character Tracker** for Windows from [GitHub](https://github.com/JDybeck/wow-character-tracker/releases/latest):

- **Discord posts** of your level-ups, boss kills, epic loot, mounts and hardcore deaths the moment they happen, with an in-game screenshot if you like
- **Stream overlay** and **DING!** alerts for OBS or Streamlabs
- **Every character on every WoW account**, every item in bags and bank searchable, and every profession side by side
- **Keeps the addon up to date** for you

The app is free and open source, and nothing leaves your PC unless you set it up.

## Feedback

Still young, and testers are welcome, especially on Forever. Bugs and ideas go in [Issues](https://github.com/JDybeck/wow-character-tracker/issues) or [Discussions](https://github.com/JDybeck/wow-character-tracker/discussions). The [full guide](https://github.com/JDybeck/wow-character-tracker/blob/main/docs/GUIDE.md) covers every option.

Open source under GPL-3.0. Not made or endorsed by Blizzard Entertainment.
