-- Character Tracker: records level times and notable moments (end boss kills, rare loot,
-- new mounts, hardcore deaths), plus a snapshot of every character you play.
-- The companion app decides which of these actually get posted, and writes Settings.lua to
-- say which ones get a screenshot.
--
-- Data reaches the companion app two ways:
--  1. WoWCharacterTrackerDB (SavedVariables), written by the game on /reload and logout.
--  2. Live: a screenshot is the only file an addon can make the game write right away. For each
--     event the addon draws the data as a black and white grid in the top-left corner for one
--     frame, takes a screenshot and hides the grid. The app reads the grid from the picture,
--     posts the event and deletes that screenshot.

local ADDON, ns = ...

-- "Blocked from an action" reports first, with their own frame and handler, so even something
-- that goes wrong while the rest of this file loads gets caught. The game names the function.
local blockedSeen = {}
local noteBlockedLater -- set once the full recorder below exists
local blockedFrame = CreateFrame("Frame")
blockedFrame:SetScript("OnEvent", function(_, event, addon, func)
  if addon == ADDON and noteBlockedLater then noteBlockedLater(event .. " " .. tostring(func))
  elseif addon == ADDON then blockedSeen[#blockedSeen + 1] = event .. " " .. tostring(func) end
end)
pcall(blockedFrame.RegisterEvent, blockedFrame, "ADDON_ACTION_BLOCKED")
pcall(blockedFrame.RegisterEvent, blockedFrame, "ADDON_ACTION_FORBIDDEN")

-- The events the addon listens to (handled in WoWCharacterTracker.lua). Some only exist on some
-- clients, so each is registered protected.
-- No COMBAT_LOG_EVENT_UNFILTERED: newer clients (Midnight, and Forever built on it) block addons
-- from the combat log, and registering for it is itself "blocked from an action" at login.
local EVENTS = {
  "ADDON_LOADED", "PLAYER_LOGIN", "PLAYER_LOGOUT", "PLAYER_ENTERING_WORLD", "PLAYER_REGEN_ENABLED",
  "PLAYER_LEVEL_UP", "TIME_PLAYED_MSG",                                  -- level times
  "ENCOUNTER_END", "CHAT_MSG_LOOT", "PLAYER_DEAD",                       -- bosses, loot, deaths
  "MERCHANT_SHOW", "MERCHANT_CLOSED", "NEW_MOUNT_ADDED",                 -- mounts (bought or learned)
  "CHAT_MSG_SYSTEM", "DUEL_REQUESTED", "CHALLENGE_MODE_COMPLETED", "PVP_RATED_STATS_UPDATE", -- duels, Mak'gora, ratings
  "UPDATE_BATTLEFIELD_STATUS", "PVP_MATCH_COMPLETE",                     -- battleground results
  "SKILL_LINES_CHANGED", "CHAT_MSG_SKILL",                               -- profession milestones
  "CHALLENGE_MODE_START", "INSPECT_READY",                               -- party specs for Mythic+ runs
  "SCREENSHOT_SUCCEEDED", "SCREENSHOT_FAILED",                           -- the shot queue
  "BAG_UPDATE_DELAYED", "BANKFRAME_OPENED", "BANKFRAME_CLOSED", "PLAYERBANKSLOTS_CHANGED", -- gear, bags and bank
  "EQUIPMENT_SETS_CHANGED",                                             -- Equipment Manager sets saved or deleted
  "PLAYER_CONTROL_LOST", "PLAYER_CONTROL_GAINED", "TAXIMAP_CLOSED",                 -- flight paths (the reload box)
  "MAIL_CLOSED", "AUCTION_HOUSE_CLOSED",                                          -- moments for the reload reminder
  -- Refresh the character snapshot when these change, while the game has the data.
  "PLAYER_EQUIPMENT_CHANGED", "PLAYER_MONEY", "CURRENCY_DISPLAY_UPDATE", "PLAYER_SPECIALIZATION_CHANGED",
  "TRAIT_CONFIG_UPDATED", "CHARACTER_POINTS_CHANGED", "PLAYER_GUILD_UPDATE",
}
ns.frame = CreateFrame("Frame")
for _, event in ipairs(EVENTS) do pcall(ns.frame.RegisterEvent, ns.frame, event) end

-- What the addon did last, saved with any "blocked from an action" report.
local lastDoing = "nothing yet"
function ns.doing(what) lastDoing = date("%H:%M:%S") .. " " .. what end

-- "Blocked from an action" reports: said in chat right away (with the function the game names)
-- and kept (last 10) in WoWCharacterTrackerDB.blocked. Held in memory until the saved data has
-- loaded, since it can happen early.
local blockedPending = {}
function ns.flushBlocked()
  if not WoWCharacterTrackerDB or #blockedPending == 0 then return end
  local list = WoWCharacterTrackerDB.blocked or {}
  for _, l in ipairs(blockedPending) do table.insert(list, l) end
  blockedPending = {}
  while #list > 10 do table.remove(list, 1) end
  WoWCharacterTrackerDB.blocked = list
end

local function noteBlocked(what)
  local line = string.format("%s %s (in combat: %s, last thing we did: %s)", date("%H:%M:%S"), what,
    tostring(InCombatLockdown()), lastDoing)
  table.insert(blockedPending, line)
  if DEFAULT_CHAT_FRAME then DEFAULT_CHAT_FRAME:AddMessage("|cffff6060[Character Tracker] Blocked:|r " .. line) end
  ns.flushBlocked()
end
noteBlockedLater = noteBlocked
for _, s in ipairs(blockedSeen) do noteBlocked(s .. " (while loading)") end
blockedSeen = {}

-- Backup in case the events don't reach us: the game shows this popup for blocked actions.
if hooksecurefunc and StaticPopup_Show then
  hooksecurefunc("StaticPopup_Show", function(which, arg1, arg2)
    if (which == "ADDON_ACTION_FORBIDDEN" or which == "ADDON_ACTION_BLOCKED") and arg1 == ADDON then
      noteBlocked("popup " .. which .. " " .. tostring(arg2))
    end
  end)
end

-- State shared between the files:
-- ns.pendingLevel the level just reached, until the game answers its /played request
-- ns.popupLevel a level waiting for the "post now" popup until combat ends
-- ns.lastPlayed, ns.lastPlayedAt total played and when it was known, to work out played now
-- ns.snapshotTicker refreshes the character snapshot every few minutes
ns.recentBosses = {} -- [name] = time, so one kill isn't recorded twice
ns.merchantOpen = false -- a mount that shows up while a vendor is open was bought

function ns.say(msg)
  print("|cff00ccff[Character Tracker]|r " .. msg)
end
