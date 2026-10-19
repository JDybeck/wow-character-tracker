-- The /wct slash commands.
local ADDON, ns = ...

local function showHistory()
  local guid = UnitGUID("player")
  local shown = 0
  for i = #WoWCharacterTrackerDB.events, 1, -1 do
    local e = WoWCharacterTrackerDB.events[i]
    if (e.kind == nil or e.kind == "level") and e.id == guid .. ":" .. e.level then
      local took = e.seconds and ns.formatDuration(e.seconds) or "an unknown time" -- logged without a starting point
      ns.say(string.format("Level %d took %s", e.level - 1, took))
      shown = shown + 1
      if shown >= 10 then break end
    end
  end
  if shown == 0 then ns.say("No level times recorded yet for this character.") end
end

local function sendTest()
  if WoWCharacterTrackerDB.live == false then
    ns.say("Instant posting is off. Type /wct live to turn it on.")
    return
  end
  local e = ns.characterInfo()
  e.kind = "test"
  e.at = time()
  e.id = string.format("%s:test:%d", e.guid, e.at)
  ns.sendLive(e)
  ns.say("Sent a test event. The companion app should show it within a few seconds.")
end

-- What this client's functions return, for tracking down problems on new clients. Saved as
-- WoWCharacterTrackerDB.debug (the SavedVariables file is written on /reload or logout).
local function saveDebug()
  local out = {}
  local function add(s) out[#out + 1] = s end
  local function dump(...)
    local t = {}
    for i = 1, select("#", ...) do t[#t + 1] = tostring((select(i, ...))) end
    return table.concat(t, ", ")
  end

  add("build: " .. dump(GetBuildInfo()))
  add("level: " .. tostring(UnitLevel("player")))
  add("played now (estimate): " .. tostring(ns.playedNow()))
  for _, line in ipairs(WoWCharacterTrackerDB.playedLog or {}) do add("played log: " .. line) end
  add("last character info error: " .. tostring(ns.lastInfoError))
  for _, line in ipairs(WoWCharacterTrackerDB.blocked or {}) do add("blocked: " .. line) end
  add("talents now: " .. dump(ns.getTalents()))
  -- The trait tree as the addon reads it: currencies, and where each node sits (posX,posY:ranks),
  -- which decides the Wowhead talent calculator code.
  pcall(function()
    local configID = C_ClassTalents.GetActiveConfigID()
    local config = C_Traits.GetConfigInfo(configID)
    local treeID = config.treeIDs[1]
    add("trait config " .. tostring(configID) .. ", trees " .. table.concat(config.treeIDs or {}, ","))
    for _, c in ipairs(C_Traits.GetTreeCurrencyInfo(configID, treeID, false) or {}) do
      add(string.format("currency %s: spent %s of %s", tostring(c.traitCurrencyID), tostring(c.spent), tostring(c.maxQuantity)))
    end
    local pos = {}
    for _, nodeID in ipairs(C_Traits.GetTreeNodes(treeID) or {}) do
      local n = C_Traits.GetNodeInfo(configID, nodeID)
      if n then pos[#pos + 1] = string.format("%s,%s:%s", tostring(n.posX), tostring(n.posY), tostring(n.ranksPurchased or 0)) end
    end
    add("nodes: " .. table.concat(pos, " "))
  end)
  add("UnitName: " .. dump(UnitName("player")) .. ", surnames: " .. tostring(ns.HAS_SURNAMES))
  add("guild: " .. dump(IsInGuild and IsInGuild(), GetGuildInfo and GetGuildInfo("player")))
  add("hardcore: " .. dump(ns.isHardcore()))

  WoWCharacterTrackerDB.debug = out
  for _, s in ipairs(out) do if #s < 250 then ns.say(s) end end
  ns.say("Saved. /reload to write it to the SavedVariables file.")
end

SLASH_CHARACTERTRACKER1 = "/charactertracker"
SLASH_CHARACTERTRACKER2 = "/wct"
SlashCmdList["CHARACTERTRACKER"] = function(msg)
  msg = strtrim(msg or ""):lower()
  if msg == "popup" then
    WoWCharacterTrackerDB.popup = not WoWCharacterTrackerDB.popup
    ns.say("Reload popup after a ding is now " .. (WoWCharacterTrackerDB.popup and "on." or "off."))
  elseif msg == "live" then
    WoWCharacterTrackerDB.live = WoWCharacterTrackerDB.live == false
    ns.say("Instant posting is now " .. (WoWCharacterTrackerDB.live and "on." or "off. Posts arrive on /reload or logout."))
  elseif msg == "test" then
    sendTest()
  elseif msg == "debug" then
    saveDebug()
  elseif msg == "options" or msg == "config" then
    ns.openOptions()
  elseif msg == "note" then
    ns.toggleNoteBox()
  elseif msg == "note lock" then
    ns.lockNoteBox(true)
  elseif msg == "note unlock" then
    ns.lockNoteBox(false)
  elseif msg == "note reset" then
    ns.resetNoteBox()
    ns.say("Note box moved back to the middle of the screen.")
  elseif msg == "help" then
    ns.say("/wct shows your last 10 level times.")
    ns.say("/wct test checks that instant posting reaches the companion app.")
    ns.say("/wct live turns instant posting on or off.")
    ns.say("/wct popup turns the reload popup after a ding on or off.")
    ns.say("/wct note shows or hides this character's note (Edit in the box to change it). /wct note lock or unlock pins it in place, /wct note reset brings it back to the middle.")
    ns.say("/wct options opens the addon's settings (also under Esc, Options, AddOns).")
    ns.say("/wct debug saves details for tracking down problems.")
  else
    showHistory()
  end
end
