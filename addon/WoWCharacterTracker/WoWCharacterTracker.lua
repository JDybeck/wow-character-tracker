-- Character Tracker: records how long each character level takes, plus a snapshot of
-- every character you play. Data lives in WoWCharacterTrackerDB (SavedVariables),
-- which the companion app reads.

local ADDON = "WoWCharacterTracker"

local frame = CreateFrame("Frame")
frame:RegisterEvent("ADDON_LOADED")
frame:RegisterEvent("PLAYER_LOGIN")
frame:RegisterEvent("PLAYER_LEVEL_UP")
frame:RegisterEvent("TIME_PLAYED_MSG")
frame:RegisterEvent("PLAYER_REGEN_ENABLED")
frame:RegisterEvent("PLAYER_LOGOUT")

local pendingLevel  -- set on level up, cleared when /played data arrives
local popupLevel    -- a level waiting for the "post now" popup until combat ends
local suppressChat = false
local lastPlayed, lastPlayedAt -- latest /played total and when it came, to estimate played at logout

local function say(msg)
  print("|cff00ccff[Character Tracker]|r " .. msg)
end

local function formatDuration(s)
  local h = math.floor(s / 3600)
  local m = math.floor((s % 3600) / 60)
  local sec = s % 60
  if h > 0 then return string.format("%dh %dm", h, m) end
  if m > 0 then return string.format("%dm %ds", m, sec) end
  return string.format("%ds", sec)
end

-- Primary professions in Classic-style clients. Names are matched in English,
-- so on a non-English client the profession list will come back empty.
local CLASSIC_PROFESSIONS = {
  Alchemy = true, Blacksmithing = true, Enchanting = true, Engineering = true,
  Herbalism = true, Leatherworking = true, Mining = true, Skinning = true, Tailoring = true,
}

-- Returns e.g. "Mining 150, Herbalism 75", or "" when there are none (the post then skips the line).
local function getProfessions()
  local found = {}

  if type(GetProfessions) == "function" then
    -- Retail: the two main professions are the first two return values.
    local p1, p2 = GetProfessions()
    for _, idx in ipairs({ p1 or 0, p2 or 0 }) do
      if idx > 0 then
        local name, _, rank = GetProfessionInfo(idx)
        if name then table.insert(found, string.format("%s %d", name, rank or 0)) end
      end
    end
  elseif type(GetNumSkillLines) == "function" then
    -- Classic: scan the skill list for primary professions.
    for i = 1, GetNumSkillLines() do
      local name, isHeader, _, rank = GetSkillLineInfo(i)
      if name and not isHeader and CLASSIC_PROFESSIONS[name] then
        table.insert(found, string.format("%s %d", name, rank or 0))
      end
    end
  end

  return table.concat(found, ", ")
end

-- Retail point split, e.g. "Class 15 / Fury 12 / Slayer 3".
-- The class and spec trees share one trait tree with one currency each (class first),
-- and the hero tree has its own currency. Returns nil if anything is missing.
local function getRetailTalentSplit(specName)
  if not (C_ClassTalents and C_Traits) then return nil end
  local configID = C_ClassTalents.GetActiveConfigID()
  if not configID then return nil end
  local config = C_Traits.GetConfigInfo(configID)
  local treeID = config and config.treeIDs and config.treeIDs[1]
  if not treeID then return nil end

  local currencies = C_Traits.GetTreeCurrencyInfo(configID, treeID, false) or {}
  if not currencies[1] then return nil end

  local heroName, heroCurrency
  local heroID = C_ClassTalents.GetActiveHeroTalentSpec and C_ClassTalents.GetActiveHeroTalentSpec()
  if heroID then
    local sub = C_Traits.GetSubTreeInfo(configID, heroID)
    if sub then heroName, heroCurrency = sub.name, sub.traitCurrencyID end
  end

  local classSpent, specSpent, heroSpent = 0, 0, 0
  local regular = 0
  for _, c in ipairs(currencies) do
    if heroCurrency and c.traitCurrencyID == heroCurrency then
      heroSpent = c.spent or 0
    else
      regular = regular + 1
      if regular == 1 then classSpent = c.spent or 0
      elseif regular == 2 then specSpent = c.spent or 0 end
    end
  end

  if classSpent + specSpent + heroSpent == 0 then return "No talent points spent yet" end
  local text = string.format("Class %d / %s %d", classSpent, specName, specSpent)
  if heroName then text = text .. string.format(" / %s %d", heroName, heroSpent) end
  return text
end

-- Returns e.g. "Arms 31 / Fury 20 / Protection 0", or a note if nothing is spent.
-- Also returns the spec name on retail (nil on Classic).
local function getTalents()
  if type(GetNumTalentTabs) == "function" and GetNumTalentTabs() > 0 then
    -- Classic: one entry per talent tree with points spent.
    local parts, total = {}, 0
    for i = 1, GetNumTalentTabs() do
      local a, b, c, _, e = GetTalentTabInfo(i)
      local name, points
      if type(a) == "number" then
        name, points = b, e -- newer return order: id, name, description, icon, pointsSpent
      else
        name, points = a, c -- classic era order: name, icon, pointsSpent
      end
      points = points or 0
      total = total + points
      table.insert(parts, string.format("%s %d", name or "?", points))
    end
    if total == 0 then return "No talent points spent yet" end
    return table.concat(parts, " / ")
  elseif type(GetSpecialization) == "function" then
    -- Retail: class, spec and hero trees.
    local spec = GetSpecialization()
    local specName = spec and select(2, GetSpecializationInfo(spec))
    if not specName then return "No specialization yet" end
    local ok, split = pcall(getRetailTalentSplit, specName)
    if ok and split then return split, specName end
    return specName, specName
  end
  return "Unknown"
end

local function getZone()
  local zone = (GetRealZoneText and GetRealZoneText()) or (GetZoneText and GetZoneText())
  if zone == nil or zone == "" then return "an unknown place" end
  return zone
end

local function requestPlayed()
  suppressChat = true
  RequestTimePlayed()
  -- Safety net so we never swallow the player's own /played forever.
  C_Timer.After(5, function() suppressChat = false end)
end

-- Hide the /played chat spam that our own requests would cause.
if type(ChatFrame_DisplayTimePlayed) == "function" then
  local original = ChatFrame_DisplayTimePlayed
  ChatFrame_DisplayTimePlayed = function(...)
    if suppressChat then return end
    return original(...)
  end
end

-- The game only writes SavedVariables on reload or logout, so offer a reload
-- right after a ding. That is what makes the Discord post show up.
StaticPopupDialogs["CHARACTERTRACKER_POST"] = {
  text = "Ding! Level %d.\n\nReload your UI now to post it to Discord?",
  button1 = "Post now",
  button2 = "Later",
  OnAccept = function() ReloadUI() end,
  timeout = 0,
  whileDead = true,
  hideOnEscape = true,
  preferredIndex = 3,
}

local function offerPost(level)
  if not WoWCharacterTrackerDB.popup then return end
  if InCombatLockdown() then
    popupLevel = level -- show it when combat ends
    return
  end
  StaticPopup_Show("CHARACTERTRACKER_POST", level)
end

local function getItemLevel()
  if type(GetAverageItemLevel) ~= "function" then return nil end
  local overall, equipped = GetAverageItemLevel()
  local v = equipped or overall
  if v and v > 0 then return math.floor(v * 10 + 0.5) / 10 end
  return nil
end

-- "TANK", "HEALER" or "DAMAGER" on retail, nil where there are no specs.
local function getRole()
  if type(GetSpecialization) ~= "function" or type(GetSpecializationRole) ~= "function" then return nil end
  local spec = GetSpecialization()
  if spec then return GetSpecializationRole(spec) end
  return nil
end

-- Everything we show about the character, both in posts and in the companion app.
local function characterInfo()
  local className, classFile = UnitClass("player")
  local raceName, raceFile = UnitRace("player")
  local sex = UnitSex("player") -- 2 = male, 3 = female
  local talents, spec = getTalents()
  return {
    char = UnitName("player"),
    realm = GetRealmName(),
    level = UnitLevel("player"),
    zone = getZone(),
    professions = getProfessions(),
    talents = talents,
    spec = spec,
    role = getRole(),
    ilvl = getItemLevel(),
    class = className,
    classFile = classFile,
    race = raceName,
    raceFile = raceFile,
    sex = (sex == 3) and "female" or "male",
  }
end

-- Keeps one up-to-date entry per character in WoWCharacterTrackerDB.chars.
local function saveSnapshot()
  local guid = UnitGUID("player")
  if not guid then return end
  local info = characterInfo()
  if lastPlayed then info.played = lastPlayed + (time() - lastPlayedAt) end
  info.lastSeen = time()
  WoWCharacterTrackerDB.chars[guid] = info
end

local function recordLevel(guid, level, seconds, played)
  local e = characterInfo()
  e.id = guid .. ":" .. level
  e.level = level -- UnitLevel can lag right after the ding
  e.seconds = seconds
  e.played = played -- total /played at the ding
  e.at = time()
  table.insert(WoWCharacterTrackerDB.events, e)
end

frame:SetScript("OnEvent", function(_, event, ...)
  if event == "ADDON_LOADED" then
    if (...) ~= ADDON then return end
    WoWCharacterTrackerDB = WoWCharacterTrackerDB or {}
    WoWCharacterTrackerDB.start = WoWCharacterTrackerDB.start or {}   -- [guid] = total played at the start of the current level
    WoWCharacterTrackerDB.events = WoWCharacterTrackerDB.events or {} -- one entry per finished level
    WoWCharacterTrackerDB.chars = WoWCharacterTrackerDB.chars or {}   -- [guid] = latest snapshot of that character
    if WoWCharacterTrackerDB.popup == nil then WoWCharacterTrackerDB.popup = true end

  elseif event == "PLAYER_LOGOUT" then
    saveSnapshot() -- also fires on /reload, right before the game writes the file

  elseif event == "PLAYER_LOGIN" then
    requestPlayed() -- sets the baseline for the current level

  elseif event == "PLAYER_LEVEL_UP" then
    pendingLevel = (...) -- the level just reached
    requestPlayed()

  elseif event == "PLAYER_REGEN_ENABLED" then
    if popupLevel then
      local level = popupLevel
      popupLevel = nil
      offerPost(level)
    end

  elseif event == "TIME_PLAYED_MSG" then
    local total, thisLevel = ...
    local guid = UnitGUID("player")
    local newStart = total - thisLevel
    local oldStart = WoWCharacterTrackerDB.start[guid]
    lastPlayed, lastPlayedAt = total, time()

    if pendingLevel then
      if oldStart and newStart >= oldStart then
        local seconds = newStart - oldStart
        recordLevel(guid, pendingLevel, seconds, total)
        say(string.format("Level %d took %s.", pendingLevel - 1, formatDuration(seconds)))
        offerPost(pendingLevel)
      else
        say("No baseline for the previous level (addon was installed mid-level), skipping.")
      end
      pendingLevel = nil
    end

    WoWCharacterTrackerDB.start[guid] = newStart
    saveSnapshot()
    suppressChat = false
  end
end)

local function showHistory()
  local guid = UnitGUID("player")
  local shown = 0
  for i = #WoWCharacterTrackerDB.events, 1, -1 do
    local e = WoWCharacterTrackerDB.events[i]
    if e.id == guid .. ":" .. e.level then
      say(string.format("Level %d took %s", e.level - 1, formatDuration(e.seconds)))
      shown = shown + 1
      if shown >= 10 then break end
    end
  end
  if shown == 0 then say("No level times recorded yet for this character.") end
end

SLASH_CHARACTERTRACKER1 = "/charactertracker"
SLASH_CHARACTERTRACKER2 = "/ct"
SlashCmdList["CHARACTERTRACKER"] = function(msg)
  msg = strtrim(msg or ""):lower()
  if msg == "popup" then
    WoWCharacterTrackerDB.popup = not WoWCharacterTrackerDB.popup
    say("Post popup after a ding is now " .. (WoWCharacterTrackerDB.popup and "on." or "off. Your dings post on your next /reload or logout."))
  elseif msg == "help" then
    say("/ct shows your last 10 level times.")
    say("/ct popup turns the \"post now\" popup after a ding on or off.")
  else
    showHistory()
  end
end
