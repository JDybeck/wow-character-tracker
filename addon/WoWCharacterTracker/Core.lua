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

ns.frame = CreateFrame("Frame")
ns.frame:RegisterEvent("ADDON_LOADED")
ns.frame:RegisterEvent("PLAYER_LOGIN")
ns.frame:RegisterEvent("PLAYER_LEVEL_UP")
ns.frame:RegisterEvent("TIME_PLAYED_MSG")
ns.frame:RegisterEvent("PLAYER_REGEN_ENABLED")
ns.frame:RegisterEvent("PLAYER_LOGOUT")
ns.frame:RegisterEvent("ENCOUNTER_END")
ns.frame:RegisterEvent("CHAT_MSG_LOOT")
ns.frame:RegisterEvent("PLAYER_DEAD")
-- No COMBAT_LOG_EVENT_UNFILTERED: newer clients (Midnight, and Forever built on it) block addons
-- from the combat log, and registering for it is itself "blocked from an action" at login.
ns.frame:RegisterEvent("MERCHANT_SHOW")
ns.frame:RegisterEvent("MERCHANT_CLOSED")
pcall(ns.frame.RegisterEvent, ns.frame, "NEW_MOUNT_ADDED") -- clients with a mount journal
-- Refresh the character snapshot when gear or spec changes, while the game has the data.
ns.frame:RegisterEvent("PLAYER_ENTERING_WORLD")
ns.frame:RegisterEvent("PLAYER_EQUIPMENT_CHANGED")
ns.frame:RegisterEvent("PLAYER_MONEY")
pcall(ns.frame.RegisterEvent, ns.frame, "CURRENCY_DISPLAY_UPDATE")
pcall(ns.frame.RegisterEvent, ns.frame, "PLAYER_SPECIALIZATION_CHANGED")
pcall(ns.frame.RegisterEvent, ns.frame, "TRAIT_CONFIG_UPDATED")
pcall(ns.frame.RegisterEvent, ns.frame, "CHARACTER_POINTS_CHANGED")
pcall(ns.frame.RegisterEvent, ns.frame, "PLAYER_GUILD_UPDATE")
ns.frame:RegisterEvent("CHAT_MSG_SYSTEM") -- duels and Mak'gora results
pcall(ns.frame.RegisterEvent, ns.frame, "CHALLENGE_MODE_COMPLETED")
pcall(ns.frame.RegisterEvent, ns.frame, "PVP_RATED_STATS_UPDATE")

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
ns.frame:RegisterEvent("SCREENSHOT_SUCCEEDED")
ns.frame:RegisterEvent("SCREENSHOT_FAILED")

-- ns.pendingLevel set on level up, cleared when /played data arrives
-- ns.popupLevel a level waiting for the "post now" popup until combat ends
ns.suppressChat = false
-- ns.lastPlayed, ns.lastPlayedAt latest /played total and when it came, to estimate played later
ns.recentBosses = {} -- [name] = time, so ENCOUNTER_END and the combat log don't both record a kill
ns.merchantOpen = false -- a mount that shows up while a vendor is open was bought
-- ns.snapshotTicker refreshes the character snapshot every few minutes
-- ns.moneyPending a snapshot is already scheduled after a money/currency change

function ns.say(msg)
  print("|cff00ccff[Character Tracker]|r " .. msg)
end

function ns.formatDuration(s)
  local h = math.floor(s / 3600)
  local m = math.floor((s % 3600) / 60)
  local sec = s % 60
  if h > 0 then return string.format("%dh %dm", h, m) end
  if m > 0 then return string.format("%dm %ds", m, sec) end
  return string.format("%ds", sec)
end
