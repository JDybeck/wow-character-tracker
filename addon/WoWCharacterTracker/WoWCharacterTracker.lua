-- The game's events, sent to the parts of the addon that handle them. Loaded last.
local ADDON, ns = ...

local on = {}
local professionsWaiting = false -- skill-ups during a fight, checked when it ends

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
  -- Only the newest loot, duels and battlegrounds (Shared.lua), unless the app's In game page says keep everything.
  if not ns.setting("keepAllHistory") then ns.trimEvents(db.events) end
  db.chars = db.chars or {}               -- [guid] = latest snapshot of that character
  db.logoutPlayed = db.logoutPlayed or {} -- [guid] = total played at logout
  db.items = db.items or {}               -- [guid] = gear, bags and bank (Inventory.lua)
  db.xpTable = db.xpTable or {}           -- [level] = XP that level needs, on this client
  db.friends = db.friends or {}           -- [battleTag] = what that friend shares (Friends.lua)
  if db.popup == nil then db.popup = false end
  ns.flushBlocked()
end

function on.PLAYER_LOGIN()
  -- Forever only, once per account: the stress test's characters (Cleanup.lua). Before onLogin, so
  -- nothing from them is used; the character being played always stays.
  local db = WoWCharacterTrackerDB
  if not ns.IS_RETAIL and not db.stressTestCleaned then
    local removed = ns.cleanStressTest(db, ns.STRESS_TEST_BEFORE, UnitGUID("player"))
    db.stressTestCleaned = time()
    if removed > 0 then ns.say(string.format("Removed %d characters left from the stress test (before November 4).", removed)) end
  end
  ns.onLogin()
  ns.restoreNoteBox()
  ns.createMinimapButton()
  ns.say("Loaded. Type /wct help for the commands.")
end

function on.PLAYER_LOGOUT() -- also on /reload, right before the game writes the saved file
  ns.stopShots()
  ns.noteSaved() -- (no inventory scan: the game has emptied the bags by now)
  ns.onLogout()
end

-- Snapshot every 5 minutes, but not mid-fight (the next tick or the logout catches up).
local function snapshotTick()
  if not (InCombatLockdown and InCombatLockdown()) then ns.saveSnapshot() end
end

local enteredWorld = false
function on.PLAYER_ENTERING_WORLD()
  C_Timer.After(2, ns.applyGameSettings)
  -- Once per login or /reload, not on every loading screen (bags, gear and professions don't change
  -- by zoning; their own events keep them current). Gear and spec info can take a few seconds.
  if not enteredWorld then
    enteredWorld = true
    C_Timer.After(10, ns.saveSnapshot)
    C_Timer.After(10, ns.scanInventory)
    C_Timer.After(12, ns.checkProfessions) -- the baseline, and anything learned while logged out
    C_Timer.After(15, ns.checkIn)
    C_Timer.After(20, ns.startFriends) -- after the snapshot, which is what friends get
    if ns.IS_RETAIL and type(RequestRatedInfo) == "function" then RequestRatedInfo() end -- answered with PVP_RATED_STATS_UPDATE
  end
  if not ns.snapshotTicker and C_Timer.NewTicker then ns.snapshotTicker = C_Timer.NewTicker(300, snapshotTick) end
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
  ns.showNextAsk() -- and the box offering to post a highlight
  ns.showNextFriendRequest() -- a friend's request that came during the fight
  if professionsWaiting then
    professionsWaiting = false
    ns.checkProfessions()
  end
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
function on.CHALLENGE_MODE_START() C_Timer.After(1, ns.inspectGroup) end -- the party's specs, for the run's group
function on.INSPECT_READY() ns.onInspectReady() end
function on.CHALLENGE_MODE_COMPLETED() ns.recordKeyRun() end
function on.PVP_RATED_STATS_UPDATE() ns.checkPvpRatings() end
function on.UPDATE_BATTLEFIELD_STATUS() ns.checkBattleground() end
function on.PVP_MATCH_COMPLETE() ns.checkBattleground() end -- retail
-- Skill-ups (weapon skills too, in a stream while fighting), so once per burst, and after a fight.
local professionsSoon = ns.debounce(2, function()
  if InCombatLockdown and InCombatLockdown() then
    professionsWaiting = true
    return
  end
  ns.checkProfessions()
end)
on.SKILL_LINES_CHANGED = professionsSoon
on.CHAT_MSG_SKILL = professionsSoon

-- Mounts: bought while a vendor is open, or learned
function on.MERCHANT_SHOW() ns.merchantOpen = true end
function on.MERCHANT_CLOSED()
  C_Timer.After(2, function() ns.merchantOpen = false end) -- the item can arrive just after the window closes
  remindSoon()
end
function on.NEW_MOUNT_ADDED(mountID)
  if not (C_MountJournal and C_MountJournal.GetMountInfoByID) then return end
  -- The spell is what Wowhead knows a learned mount by (name and icon for the Discord post).
  local name, spellID = C_MountJournal.GetMountInfoByID(mountID)
  if name then ns.recordMount(name, nil, spellID) end
end

-- The shot queue
function on.SCREENSHOT_SUCCEEDED() ns.onScreenshotEvent(true) end
function on.SCREENSHOT_FAILED() ns.onScreenshotEvent(false) end

-- Gear, bags and the bank (while it's open).
on.BAG_UPDATE_DELAYED = ns.scanInventorySoon
on.PLAYERBANKSLOTS_CHANGED = ns.scanInventorySoon
function on.EQUIPMENT_SETS_CHANGED()
  ns.equipSetsChanged()
  ns.scanInventorySoon()
end
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

-- The character snapshot follows gear, spec, talent and guild changes (once for a whole set swap,
-- which changes every slot)...
local snapshotSoon = ns.debounce(1, function() ns.saveSnapshot() end)
function on.PLAYER_EQUIPMENT_CHANGED()
  snapshotSoon()
  ns.equipSetsChanged()
  ns.scanInventorySoon()
end
on.PLAYER_SPECIALIZATION_CHANGED = snapshotSoon
on.TRAIT_CONFIG_UPDATED = snapshotSoon
on.CHARACTER_POINTS_CHANGED = snapshotSoon
on.PLAYER_GUILD_UPDATE = snapshotSoon

-- ...and money, which changes a lot (every loot and vendor), so at most every few seconds, and only
-- the money and currency fields.
local moneyChanged = ns.debounce(3, function() ns.saveMoney() end)
on.PLAYER_MONEY = moneyChanged
on.CURRENCY_DISPLAY_UPDATE = moneyChanged

-- Sharing with Battle.net friends (Comms.lua, Friends.lua). The friends list changes in bursts
-- at login, so one look a few seconds after.
function on.BN_CHAT_MSG_ADDON(prefix, text, channel, senderID) ns.comms.receive(prefix, text, channel, senderID) end
on.BN_FRIEND_INFO_CHANGED = ns.debounce(5, function() ns.friendsTick() end)

ns.frame:SetScript("OnEvent", function(_, event, ...)
  local handler = on[event]
  if handler then handler(...) end
end)
