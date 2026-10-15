# wow-level-tracker

Tracks how long each character level takes in World of Warcraft and posts it to a Discord channel when you level up.

Two parts:

- `addon/LevelTimes`: the WoW addon. Records the time for every finished level into its SavedVariables file.
- `companion/LevelTimesCompanion`: a small .NET console app. Watches that file and posts new entries to a Discord webhook.

WoW addons can't make network requests, which is why the companion exists.

## How the timing works

`RequestTimePlayed()` returns total time played and time played on the current level. Right after a level-up the "current level" part is about zero, so the addon stores `total - thisLevel` (the total at the start of the level) per character. On the next level-up, the finished level's time is the difference between the two stored values. It also re-syncs on every login, so it corrects itself.

If you install the addon mid-level, that first level has no baseline and is skipped. Everything after that is tracked.

## Install the addon

1. Copy `addon/LevelTimes` into your game's AddOns folder, for example `World of Warcraft\_retail_\Interface\AddOns\LevelTimes`.
2. Set `## Interface:` in `LevelTimes.toc` to match your client. Check it in game with `/dump (select(4, GetBuildInfo()))`. A wrong number just marks the addon "out of date", which you can allow in the AddOns menu.
3. Log in. `/leveltimes` prints your last 10 level times for the current character.

For WoW Forever, use the same folder layout for that client and the interface number it reports.

## Run the companion

You need the .NET 8 SDK.

1. Create a webhook in Discord: channel settings, Integrations, Webhooks, New Webhook, copy the URL.
2. Put the URL in an environment variable (never commit it):

   ```
   set DISCORD_WEBHOOK_URL=https://discord.com/api/webhooks/...
   ```

3. Run it, pointing at the SavedVariables file:

   ```
   cd companion/LevelTimesCompanion
   dotnet run -- "C:\...\World of Warcraft\_retail_\WTF\Account\<ACCOUNT>\SavedVariables\LevelTimes.lua"
   ```

On the first run it marks existing entries as already posted so it doesn't flood the channel. Pass `--backfill` to post them instead. Posted ids are kept in `%LOCALAPPDATA%\LevelTimesCompanion\posted.json`.

## Known limits

- The game only writes SavedVariables on `/reload` or logout, so the Discord post shows up then, not at the exact moment of the level-up.
- The chat-suppression for the `/played` spam hooks `ChatFrame_DisplayTimePlayed`. If a client doesn't have that function, you'll just see the normal `/played` lines.
- Only the addon logic has been tested (with a simulated WoW API). The C# app hasn't been compiled or run yet.
