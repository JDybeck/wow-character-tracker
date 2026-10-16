-- The /wct slash commands.
local ADDON, ns = ...

local function showHistory()
  local guid = UnitGUID("player")
  local shown = 0
  for i = #WoWCharacterTrackerDB.events, 1, -1 do
    local e = WoWCharacterTrackerDB.events[i]
    if (e.kind == nil or e.kind == "level") and e.id == guid .. ":" .. e.level then
      ns.say(string.format("Level %d took %s", e.level - 1, ns.formatDuration(e.seconds)))
      shown = shown + 1
      if shown >= 10 then break end
    end
  end
  if shown == 0 then ns.say("No level times recorded yet for this character.") end
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
    local e = ns.characterInfo()
    e.kind = "test"
    e.at = time()
    e.id = string.format("%s:test:%d", e.guid, e.at)
    if WoWCharacterTrackerDB.live == false then
      ns.say("Instant posting is off. Type /wct live to turn it on.")
      return
    end
    ns.sendLive(e)
    ns.say("Sent a test event. The companion app should show it within a few seconds.")
  elseif msg == "debug" then
    -- What this client's talent and guild functions return, saved for the app's developer to read
    -- (WoWCharacterTrackerDB.debug in the SavedVariables file, written on /reload or logout).
    local out = {}
    local function add(s) table.insert(out, s) end
    local function dump(...)
      local t = {}
      for i = 1, select("#", ...) do t[#t + 1] = tostring((select(i, ...))) end
      return table.concat(t, ", ")
    end
    add("build: " .. dump(GetBuildInfo()))
    add("level: " .. tostring(UnitLevel("player")))
    add("GetNumTalentTabs: " .. type(GetNumTalentTabs) .. (type(GetNumTalentTabs) == "function" and (" = " .. dump(pcall(GetNumTalentTabs))) or ""))
    if type(GetTalentTabInfo) == "function" then
      for i = 1, 4 do add("GetTalentTabInfo(" .. i .. "): " .. dump(pcall(GetTalentTabInfo, i))) end
    end
    if type(GetNumTalents) == "function" then add("GetNumTalents(1): " .. dump(pcall(GetNumTalents, 1))) end
    if type(GetTalentInfo) == "function" then add("GetTalentInfo(1,1): " .. dump(pcall(GetTalentInfo, 1, 1))) end
    if type(GetActiveTalentGroup) == "function" then add("GetActiveTalentGroup: " .. dump(pcall(GetActiveTalentGroup))) end
    if type(UnitCharacterPoints) == "function" then add("UnitCharacterPoints: " .. dump(pcall(UnitCharacterPoints, "player"))) end
    add("GetSpecialization: " .. type(GetSpecialization) .. (type(GetSpecialization) == "function" and (" = " .. dump(pcall(GetSpecialization))) or ""))
    add("C_ClassTalents: " .. type(C_ClassTalents) .. ", C_Traits: " .. type(C_Traits) .. ", C_SpecializationInfo: " .. type(C_SpecializationInfo))
    if C_ClassTalents and C_ClassTalents.GetActiveConfigID then add("GetActiveConfigID: " .. dump(pcall(C_ClassTalents.GetActiveConfigID))) end
    local function keys(t)
      local k = {}
      for name in pairs(t or {}) do k[#k + 1] = name end
      table.sort(k)
      return table.concat(k, " ")
    end
    add("C_SpecializationInfo: " .. keys(C_SpecializationInfo))
    add("C_ClassTalents: " .. keys(C_ClassTalents))
    pcall(function()
      local configID = C_ClassTalents.GetActiveConfigID()
      local config = C_Traits.GetConfigInfo(configID)
      add("config: type=" .. tostring(config.type) .. " name=" .. tostring(config.name) .. " trees=" .. table.concat(config.treeIDs or {}, ","))
      local treeID = config.treeIDs[1]
      for _, c in ipairs(C_Traits.GetTreeCurrencyInfo(configID, treeID, false) or {}) do
        add(string.format("currency %s: spent=%s quantity=%s max=%s", tostring(c.traitCurrencyID), tostring(c.spent), tostring(c.quantity), tostring(c.maxQuantity)))
      end
      local nodes, ranked, subs = C_Traits.GetTreeNodes(treeID) or {}, 0, {}
      for _, nodeID in ipairs(nodes) do
        local n = C_Traits.GetNodeInfo(configID, nodeID)
        if n then
          if (n.ranksPurchased or 0) > 0 then ranked = ranked + 1 end
          local s = tostring(n.subTreeID)
          subs[s] = (subs[s] or 0) + (n.ranksPurchased or 0)
        end
      end
      add("nodes: " .. #nodes .. ", with ranks: " .. ranked)
      -- Where the nodes sit, in case the classic three trees are side by side in one tree.
      local pos = {}
      for _, nodeID in ipairs(nodes) do
        local n = C_Traits.GetNodeInfo(configID, nodeID)
        if n then pos[#pos + 1] = string.format("%s,%s:%s", tostring(n.posX), tostring(n.posY), tostring(n.ranksPurchased or 0)) end
      end
      add("node pos x,y:ranks: " .. table.concat(pos, " "))
      if C_SpecializationInfo and C_SpecializationInfo.GetSpecializationInfo then
        add("spec info: " .. dump(pcall(C_SpecializationInfo.GetSpecializationInfo, C_SpecializationInfo.GetSpecialization() or 1)))
      end
      for s, r in pairs(subs) do
        local info = tonumber(s) and C_Traits.GetSubTreeInfo and C_Traits.GetSubTreeInfo(configID, tonumber(s))
        add("subtree " .. s .. " (" .. tostring(info and info.name) .. "): " .. r .. " ranks")
      end
      if C_SpecializationInfo and C_SpecializationInfo.GetSpecialization then
        add("C_SpecializationInfo.GetSpecialization: " .. dump(pcall(C_SpecializationInfo.GetSpecialization)))
      end
    end)
    add("guild: " .. dump(IsInGuild and IsInGuild(), GetGuildInfo and GetGuildInfo("player")))
    -- Forever has surnames ("Ronnie Raket"); which function knows them?
    add("UnitName: " .. dump(UnitName("player")))
    if UnitFullName then add("UnitFullName: " .. dump(pcall(UnitFullName, "player"))) end
    if UnitPVPName then add("UnitPVPName: " .. dump(pcall(UnitPVPName, "player"))) end
    if GetUnitName then add("GetUnitName: " .. dump(pcall(GetUnitName, "player", true))) end
    if UnitNameUnmodified then add("UnitNameUnmodified: " .. dump(pcall(UnitNameUnmodified, "player"))) end
    if C_PlayerInfo then
      local keys = {}
      for k in pairs(C_PlayerInfo) do keys[#keys + 1] = k end
      table.sort(keys)
      add("C_PlayerInfo: " .. table.concat(keys, " "))
    end
    if PlayerFrame and PlayerFrame.name and PlayerFrame.name.GetText then add("PlayerFrame name: " .. tostring(PlayerFrame.name:GetText())) end
    if PlayerName and PlayerName.GetText then add("PlayerName: " .. tostring(PlayerName:GetText())) end
    if CharacterFrameTitleText and CharacterFrameTitleText.GetText then add("CharacterFrameTitleText: " .. tostring(CharacterFrameTitleText:GetText())) end
    add("hardcore api: " .. type(C_GameRules and C_GameRules.IsHardcoreActive))
    add("talents now: " .. dump(ns.getTalents()))
    -- Every global function or table with Talent or Spec in its name, in case this client has its own.
    local names = {}
    for k, v in pairs(_G) do
      if type(k) == "string" and (type(v) == "function" or (type(v) == "table" and k:sub(1, 2) == "C_"))
          and (k:find("Talent") or k:find("Spec") or k:find("Trait") or k:find("Surname") or k:find("LastName")
            or k:find("FamilyName") or k:find("FullName")) then
        names[#names + 1] = k
      end
    end
    table.sort(names)
    add("names: " .. table.concat(names, " "))
    WoWCharacterTrackerDB.debug = out
    for _, s in ipairs(out) do if #s < 250 then ns.say(s) end end
    ns.say("Saved. /reload to write it to the SavedVariables file.")
  elseif msg == "help" then
    ns.say("/wct shows your last 10 level times.")
    ns.say("/wct test checks that instant posting reaches the companion app.")
    ns.say("/wct live turns instant posting on or off.")
    ns.say("/wct popup turns the reload popup after a ding on or off.")
  else
    showHistory()
  end
end
