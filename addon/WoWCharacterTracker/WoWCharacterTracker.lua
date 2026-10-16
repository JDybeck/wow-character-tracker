-- The game's events, sent to the parts of the addon that handle them. Loaded last.
local ADDON, ns = ...

local on = {}

function on.ADDON_LOADED(name)
  if name ~= ADDON then return end
  local db = WoWCharacterTrackerDB or {}
  WoWCharacterTrackerDB = db
  db.start = db.start or {}               -- [guid] = total played at the start of the current level
  db.startLevel = db.startLevel or {}     -- [guid] = the level that start belongs to
  db.events = db.events or {}             -- level-ups and notable moments
  db.chars = db.chars or {}               -- [guid] = latest snapshot of that character
  db.logoutPlayed = db.logoutPlayed or {} -- [guid] = total played at logout
  if db.popup == nil then db.popup = false end
  ns.flushBlocked()
end

function on.PLAYER_LOGIN()
  ns.onLogin()
  ns.say("Loaded. Type /wct help for the commands.")
end

function on.PLAYER_LOGOUT() ns.onLogout() end

function on.PLAYER_ENTERING_WORLD()
  -- Gear and spec info can take a few seconds after login. Snapshot then, and every 5 minutes.
  C_Timer.After(2, ns.applyGameSettings)
  C_Timer.After(10, ns.saveSnapshot)
  C_Timer.After(12, ns.checkRatings)
  C_Timer.After(15, ns.checkIn)
  if type(RequestRatedInfo) == "function" then RequestRatedInfo() end -- answered with PVP_RATED_STATS_UPDATE
  if not ns.snapshotTicker and C_Timer.NewTicker then ns.snapshotTicker = C_Timer.NewTicker(300, ns.saveSnapshot) end
end

function on.PLAYER_REGEN_ENABLED()
  ns.processShots() -- data grids held back during combat
  if ns.popupLevel then
    local level = ns.popupLevel
    ns.popupLevel = nil
    ns.offerPost(level)
  end
end

-- Level times
function on.PLAYER_LEVEL_UP(level) ns.onLevelUp(level) end
function on.TIME_PLAYED_MSG(total, thisLevel) ns.onTimePlayed(total, thisLevel) end

-- Moments
function on.ENCOUNTER_END(_, encounterName, _, _, success)
  if success == 1 or success == true then ns.recordBoss(encounterName) end
end
function on.CHAT_MSG_LOOT(text) ns.recordLoot(text) end
function on.PLAYER_DEAD() ns.recordDeath() end
function on.CHAT_MSG_SYSTEM(text) ns.onSystemMessage(text) end -- duels and Mak'gora
function on.CHALLENGE_MODE_COMPLETED() C_Timer.After(5, ns.checkRatings) end -- the new score takes a moment
function on.PVP_RATED_STATS_UPDATE() ns.checkRatings() end

-- Mounts: bought while a vendor is open, or learned
function on.MERCHANT_SHOW() ns.merchantOpen = true end
function on.MERCHANT_CLOSED()
  C_Timer.After(2, function() ns.merchantOpen = false end) -- the item can arrive just after the window closes
end
function on.NEW_MOUNT_ADDED(mountID)
  local name = C_MountJournal and C_MountJournal.GetMountInfoByID and C_MountJournal.GetMountInfoByID(mountID)
  if name then ns.recordMount(name, nil) end
end

-- The shot queue
function on.SCREENSHOT_SUCCEEDED() ns.shotDone() end
on.SCREENSHOT_FAILED = on.SCREENSHOT_SUCCEEDED

-- The character snapshot follows gear, spec, talent and guild changes...
local function snapshotSoon() C_Timer.After(1, ns.saveSnapshot) end
on.PLAYER_EQUIPMENT_CHANGED = snapshotSoon
on.PLAYER_SPECIALIZATION_CHANGED = snapshotSoon
on.TRAIT_CONFIG_UPDATED = snapshotSoon
on.CHARACTER_POINTS_CHANGED = snapshotSoon
on.PLAYER_GUILD_UPDATE = snapshotSoon

-- ...and money, which changes a lot (every loot and vendor), so at most every few seconds.
local function moneyChanged()
  if ns.moneyPending then return end
  ns.moneyPending = true
  C_Timer.After(3, function() ns.moneyPending = false; ns.saveSnapshot() end)
end
on.PLAYER_MONEY = moneyChanged
on.CURRENCY_DISPLAY_UPDATE = moneyChanged

ns.frame:SetScript("OnEvent", function(_, event, ...)
  local handler = on[event]
  if handler then handler(...) end
end)
