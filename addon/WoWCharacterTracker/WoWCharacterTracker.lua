-- The game's events, sent to the parts of the addon that handle them. Loaded last.
local ADDON, ns = ...

local on = {}

-- A chat reminder to /reload after the bank, mailbox, a vendor or the auction house, or leaving a
-- dungeon (Events.lua remindReload), once the bag scan that follows has run.
local function remindSoon() C_Timer.After(4, function() ns.remindReload(true) end) end

function on.ADDON_LOADED(name)
  if name ~= ADDON then return end
  local db = WoWCharacterTrackerDB or {}
  WoWCharacterTrackerDB = db
  db.start = db.start or {}               -- [guid] = total played at the start of the current level
  db.startLevel = db.startLevel or {}     -- [guid] = the level that start belongs to
  db.events = db.events or {}             -- level-ups and notable moments
  db.chars = db.chars or {}               -- [guid] = latest snapshot of that character
  db.logoutPlayed = db.logoutPlayed or {} -- [guid] = total played at logout
  db.items = db.items or {}               -- [guid] = gear, bags and bank (Inventory.lua)
  db.xpTable = db.xpTable or {}           -- [level] = XP that level needs, on this client
  if db.popup == nil then db.popup = false end
  ns.flushBlocked()
end

function on.PLAYER_LOGIN()
  ns.onLogin()
  ns.restoreNoteBox()
  ns.say("Loaded. Type /wct help for the commands.")
end

function on.PLAYER_LOGOUT() -- also on /reload, right before the game writes the saved file
  ns.noteSaved() -- (no inventory scan: the game has emptied the bags by now)
  ns.onLogout()
end

function on.PLAYER_ENTERING_WORLD()
  -- Gear and spec info can take a few seconds after login. Snapshot then, and every 5 minutes.
  C_Timer.After(2, ns.applyGameSettings)
  C_Timer.After(10, ns.saveSnapshot)
  C_Timer.After(10, ns.scanInventory)
  C_Timer.After(12, ns.checkRatings)
  C_Timer.After(15, ns.checkIn)
  if type(RequestRatedInfo) == "function" then RequestRatedInfo() end -- answered with PVP_RATED_STATS_UPDATE
  if not ns.snapshotTicker and C_Timer.NewTicker then ns.snapshotTicker = C_Timer.NewTicker(300, ns.saveSnapshot) end
  -- The chat reminder to /reload: every 30 minutes (checked every 5), and when leaving a dungeon.
  if not ns.reminderTicker and C_Timer.NewTicker then ns.reminderTicker = C_Timer.NewTicker(300, function() ns.remindReload(false) end) end
  local inInstance = IsInInstance and IsInInstance() or false
  if ns.wasInInstance and not inInstance then remindSoon() end
  ns.wasInInstance = inInstance
  ns.checkBattleground() -- out of a battleground: ready for the next one
end

function on.PLAYER_REGEN_ENABLED()
  ns.processShots() -- data grids held back during combat
  ns.flushAnnouncements() -- guild/group messages held back during combat
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
function on.DUEL_REQUESTED(name) ns.onDuelRequested(name) end -- remembers the challenger's class and level
function on.CHALLENGE_MODE_COMPLETED() C_Timer.After(5, ns.checkRatings) end -- the new score takes a moment
function on.PVP_RATED_STATS_UPDATE() ns.checkRatings() end
function on.UPDATE_BATTLEFIELD_STATUS() ns.checkBattleground() end
function on.PVP_MATCH_COMPLETE() ns.checkBattleground() end -- retail

-- Mounts: bought while a vendor is open, or learned
function on.MERCHANT_SHOW() ns.merchantOpen = true end
function on.MERCHANT_CLOSED()
  C_Timer.After(2, function() ns.merchantOpen = false end) -- the item can arrive just after the window closes
  remindSoon()
end
function on.NEW_MOUNT_ADDED(mountID)
  local name = C_MountJournal and C_MountJournal.GetMountInfoByID and C_MountJournal.GetMountInfoByID(mountID)
  if name then ns.recordMount(name, nil) end
end

-- The shot queue
function on.SCREENSHOT_SUCCEEDED() ns.shotDone(true) end
function on.SCREENSHOT_FAILED() ns.shotDone(false) end

-- Gear, bags and the bank (while it's open).
on.BAG_UPDATE_DELAYED = ns.scanInventorySoon
on.PLAYERBANKSLOTS_CHANGED = ns.scanInventorySoon
on.EQUIPMENT_SETS_CHANGED = ns.scanInventorySoon
function on.BANKFRAME_OPENED()
  ns.bankOpen = true
  C_Timer.After(0.5, ns.scanBank)
end
function on.BANKFRAME_CLOSED()
  ns.bankOpen = false
  remindSoon()
end
on.MAIL_CLOSED = function() remindSoon() end
on.AUCTION_HOUSE_CLOSED = function() remindSoon() end

-- Flight paths: the reload box when gear or bags changed (Events.lua). The flight has started a
-- moment after control goes or the flight map closes.
local function flightMaybe() C_Timer.After(1.5, ns.onFlightStart) end
on.PLAYER_CONTROL_LOST = flightMaybe
on.TAXIMAP_CLOSED = flightMaybe
function on.PLAYER_CONTROL_GAINED() ns.onFlightEnd() end

-- The character snapshot follows gear, spec, talent and guild changes...
local function snapshotSoon() C_Timer.After(1, ns.saveSnapshot) end
function on.PLAYER_EQUIPMENT_CHANGED()
  snapshotSoon()
  ns.scanInventorySoon()
end
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
