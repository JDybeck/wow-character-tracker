-- Written by the Character Tracker app from its "What gets posted" page. Changes apply on /reload.
-- These are the addon's defaults on its own (no `post`: no app, see ns.hasApp): no screenshots, and
-- guild and group lines off until they're turned on in the game's options.
WoWCharacterTrackerSettings = {
  screenshot = { level = false, loot = false, mount = false, boss = false, death = false },
  announce = { guild = false, party = false, kinds = { level = true, maxlevel = true, boss = true, loot = true, mount = true, profession = true, mplus = true, pvp = true } },
  ask = {},
  flightReload = false, -- these two only help the app
  chatReminder = false,
}
