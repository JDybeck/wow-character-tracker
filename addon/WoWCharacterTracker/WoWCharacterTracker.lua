-- The game's events, sent to the parts of the addon that handle them. Loaded last.
local ADDON, ns = ...

local function knownEvent(id)
  for _, e in ipairs(WoWCharacterTrackerDB.events) do
    if e.id == id then return true end
  end
  return false
end

-- Times a finished level: newStart is the total played when `level` began, and the saved start
-- belongs to startLevel. If that's the level before, the level is done (also later, at login, when a
-- /reload or logout came before the game answered the ding's /played request). Returns true when
-- the level is accounted for (recorded now or before). live = right at the ding.
local function finishLevel(guid, level, newStart, total, live)
  local oldStart = WoWCharacterTrackerDB.start[guid]
  local startLevel = WoWCharacterTrackerDB.startLevel[guid]
  -- Level 1 always starts at 0 played, so 1 to 2 never needs a baseline (a brand new character's
  -- login answer can go missing, and the addon may have been installed after creating it).
  if level == 2 and startLevel ~= 2 then oldStart, startLevel = 0, 1 end
  if not (oldStart and newStart >= oldStart and (startLevel == level - 1 or (startLevel == nil and live))) then return false end
  if not knownEvent(guid .. ":" .. level) then
    local seconds = newStart - oldStart
    ns.recordLevel(guid, level, seconds, total)
    ns.say(string.format("Level %d took %s.", level - 1, ns.formatDuration(seconds)))
    if live then ns.offerPost(level) end
  end
  return true
end

ns.frame:SetScript("OnEvent", function(_, event, ...)
  if event == "CHAT_MSG_SYSTEM" then
    ns.onSystemMessage((...))

  elseif event == "CHALLENGE_MODE_COMPLETED" then
    C_Timer.After(5, ns.checkRatings) -- the new score takes a moment

  elseif event == "PVP_RATED_STATS_UPDATE" then
    ns.checkRatings()

  elseif event == "ADDON_LOADED" then
    if (...) ~= ADDON then return end
    WoWCharacterTrackerDB = WoWCharacterTrackerDB or {}
    WoWCharacterTrackerDB.start = WoWCharacterTrackerDB.start or {}   -- [guid] = total played at the start of the current level
    WoWCharacterTrackerDB.startLevel = WoWCharacterTrackerDB.startLevel or {} -- [guid] = the level that start belongs to
    WoWCharacterTrackerDB.events = WoWCharacterTrackerDB.events or {} -- level-ups and notable moments
    WoWCharacterTrackerDB.chars = WoWCharacterTrackerDB.chars or {}   -- [guid] = latest snapshot of that character
    WoWCharacterTrackerDB.logoutPlayed = WoWCharacterTrackerDB.logoutPlayed or {} -- [guid] = total played at logout
    if WoWCharacterTrackerDB.popup == nil then WoWCharacterTrackerDB.popup = false end
    ns.flushBlocked()

  elseif event == "PLAYER_LOGIN" then
    -- Played time doesn't move while logged out, so where it was at logout is where this session
    -- starts. Level times then don't depend on the game answering /played requests, which some
    -- clients (Forever) seem to ignore unless the player typed it.
    local guid = UnitGUID("player")
    local atLogout = WoWCharacterTrackerDB.logoutPlayed[guid]
    WoWCharacterTrackerDB.logoutPlayed[guid] = nil -- only good for the login right after it
    if atLogout then
      ns.lastPlayed, ns.lastPlayedAt = atLogout, time()
    elseif UnitLevel("player") == 1 and not WoWCharacterTrackerDB.chars[guid] then
      ns.lastPlayed, ns.lastPlayedAt = 0, time() -- a brand new character
    end
    C_Timer.After(3, function() ns.requestPlayed("login") end) -- once the world has loaded
    ns.say("Loaded. Type /wct help for the commands.")

  elseif event == "SCREENSHOT_SUCCEEDED" or event == "SCREENSHOT_FAILED" then
    ns.shotDone()

  elseif event == "PLAYER_LOGOUT" then
    local played = ns.playedNow()
    if played then WoWCharacterTrackerDB.logoutPlayed[UnitGUID("player")] = played end
    ns.saveSnapshot() -- also fires on /reload, right before the game writes the file

  elseif event == "PLAYER_ENTERING_WORLD" then
    -- Gear and spec info can take a few seconds after login. Snapshot then, and every 5 minutes.
    C_Timer.After(10, ns.saveSnapshot)
    C_Timer.After(2, ns.applyGameSettings)
    C_Timer.After(15, ns.checkIn)
    if type(RequestRatedInfo) == "function" then RequestRatedInfo() end -- answered with PVP_RATED_STATS_UPDATE
    C_Timer.After(12, ns.checkRatings)
    if not ns.snapshotTicker and C_Timer.NewTicker then ns.snapshotTicker = C_Timer.NewTicker(300, ns.saveSnapshot) end

  elseif event == "PLAYER_EQUIPMENT_CHANGED" or event == "PLAYER_SPECIALIZATION_CHANGED"
      or event == "TRAIT_CONFIG_UPDATED" or event == "CHARACTER_POINTS_CHANGED" or event == "PLAYER_GUILD_UPDATE" then
    C_Timer.After(1, ns.saveSnapshot)

  elseif event == "PLAYER_MONEY" or event == "CURRENCY_DISPLAY_UPDATE" then
    -- Money changes a lot (every loot and vendor), so save at most every few seconds.
    if not ns.moneyPending then
      ns.moneyPending = true
      C_Timer.After(3, function() ns.moneyPending = false; ns.saveSnapshot() end)
    end

  elseif event == "PLAYER_LEVEL_UP" then
    local level = (...) -- the level just reached
    local guid = UnitGUID("player")
    ns.pendingLevel = level
    -- Right away from the played time we already know (the login /played plus the time since), so
    -- the post doesn't wait on the server. The /played answer then makes the new level's start exact.
    local total = ns.playedNow()
    if total and finishLevel(guid, level, total, total, true) then
      WoWCharacterTrackerDB.start[guid] = total
      WoWCharacterTrackerDB.startLevel[guid] = level
    end
    -- Ask the server too, for the exact start of the new level. The answer can be slow or not come at
    -- all, so ask a moment later and again after a while.
    for _, delay in ipairs({ 1, 15, 45 }) do
      C_Timer.After(delay, function() if ns.pendingLevel == level then ns.requestPlayed("level " .. level) end end)
    end

  elseif event == "PLAYER_REGEN_ENABLED" then
    ns.processShots() -- data grids held back during combat
    if ns.popupLevel then
      local level = ns.popupLevel
      ns.popupLevel = nil
      ns.offerPost(level)
    end

  elseif event == "ENCOUNTER_END" then
    local _, encounterName, _, _, success = ...
    if success == 1 or success == true then ns.recordBoss(encounterName) end

  elseif event == "CHAT_MSG_LOOT" then
    ns.recordLoot((...))

  elseif event == "PLAYER_DEAD" then
    ns.recordDeath()

  elseif event == "MERCHANT_SHOW" then
    ns.merchantOpen = true

  elseif event == "MERCHANT_CLOSED" then
    -- The bought item can arrive just after the window closes.
    C_Timer.After(2, function() ns.merchantOpen = false end)

  elseif event == "NEW_MOUNT_ADDED" then
    local mountID = ...
    local name = C_MountJournal and C_MountJournal.GetMountInfoByID and C_MountJournal.GetMountInfoByID(mountID)
    if name then ns.recordMount(name, nil) end

  elseif event == "TIME_PLAYED_MSG" then
    local total, thisLevel = ...
    local guid = UnitGUID("player")
    local newStart = total - thisLevel
    ns.lastPlayed, ns.lastPlayedAt = total, time()
    ns.notePlayed(string.format("answer: total %d, this level %d", total, thisLevel))

    local level = ns.pendingLevel or UnitLevel("player") -- UnitLevel can lag right after a ding
    local live = ns.pendingLevel ~= nil
    if not finishLevel(guid, level, newStart, total, live) and live and not knownEvent(guid .. ":" .. level) then
      -- No starting point for the level (the addon came in mid-level): still log the ding, without a time.
      ns.recordLevel(guid, level, nil, total)
      ns.say(string.format("Level %d logged. Its time is unknown (no starting point for level %d yet).", level, level - 1))
      ns.offerPost(level)
    end
    ns.pendingLevel = nil

    WoWCharacterTrackerDB.start[guid] = newStart
    WoWCharacterTrackerDB.startLevel[guid] = level
    ns.saveSnapshot()
    -- Not right away: frames registered during this event could still get this same message.
    C_Timer.After(0.5, ns.unmutePlayed)
  end
end)
