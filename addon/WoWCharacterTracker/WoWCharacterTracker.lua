-- The game's events, sent to the parts of the addon that handle them. Loaded last.
local ADDON, ns = ...

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
    if WoWCharacterTrackerDB.popup == nil then WoWCharacterTrackerDB.popup = false end
    ns.flushBlocked()

  elseif event == "PLAYER_LOGIN" then
    ns.requestPlayed() -- sets the baseline for the current level
    ns.say("Loaded. Type /wct help for the commands.")

  elseif event == "SCREENSHOT_SUCCEEDED" or event == "SCREENSHOT_FAILED" then
    ns.shotDone()

  elseif event == "PLAYER_LOGOUT" then
    ns.saveSnapshot() -- also fires on /reload, right before the game writes the file

  elseif event == "PLAYER_ENTERING_WORLD" then
    -- Gear and spec info can take a few seconds after login. Snapshot then, and every 5 minutes.
    C_Timer.After(10, ns.saveSnapshot)
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
    ns.pendingLevel = level
    ns.requestPlayed()
    -- The answer can get lost (busy server, loading screen). Ask once more if it hasn't come.
    C_Timer.After(15, function() if ns.pendingLevel == level then ns.requestPlayed() end end)

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
    local oldStart = WoWCharacterTrackerDB.start[guid]
    ns.lastPlayed, ns.lastPlayedAt = total, time()

    -- The saved start belongs to startLevel. If that's the level before this one, a level was just
    -- finished: right after a ding, but also later (at login) when a /reload or logout came before
    -- the game answered the ding's /played request, which used to lose that level.
    local level = ns.pendingLevel or UnitLevel("player")
    local startLevel = WoWCharacterTrackerDB.startLevel[guid]
    -- Level 1 always starts at 0 played, so 1 to 2 never needs a baseline (a brand new character's
    -- login answer can go missing, and the addon may have been installed after creating it).
    if level == 2 and startLevel ~= 2 then oldStart, startLevel = 0, 1 end
    local finished = oldStart and newStart >= oldStart
      and (startLevel == level - 1 or (startLevel == nil and ns.pendingLevel ~= nil))
    local id = guid .. ":" .. level
    local known = false
    for _, e in ipairs(WoWCharacterTrackerDB.events) do
      if e.id == id then known = true; break end
    end
    if finished and not known then
      local seconds = newStart - oldStart
      ns.recordLevel(guid, level, seconds, total)
      ns.say(string.format("Level %d took %s.", level - 1, ns.formatDuration(seconds)))
      if ns.pendingLevel then ns.offerPost(level) end
    elseif ns.pendingLevel and not known then
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
