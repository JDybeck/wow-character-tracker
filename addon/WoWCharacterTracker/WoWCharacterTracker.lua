-- Character Tracker: records level times and notable moments (end boss kills, rare loot,
-- new mounts, hardcore deaths), plus a snapshot of every character you play.
-- The companion app decides which of these actually get posted, and writes Settings.lua to
-- say which ones get a screenshot.
--
-- Data reaches the companion app two ways:
--  1. WoWCharacterTrackerDB (SavedVariables), written by the game on /reload and logout.
--  2. Live: each event is whispered to yourself as a hidden data line. With chat logging on,
--     the game writes it to Logs\WoWChatLog.txt right away, and the app reads it from there.
--     That's what makes posts show up without a reload.

local ADDON = "WoWCharacterTracker"
local LIVE_PREFIX = "#CT1#" -- marks our hidden whispers, both in chat filters and in the chat log

local frame = CreateFrame("Frame")
frame:RegisterEvent("ADDON_LOADED")
frame:RegisterEvent("PLAYER_LOGIN")
frame:RegisterEvent("PLAYER_LEVEL_UP")
frame:RegisterEvent("TIME_PLAYED_MSG")
frame:RegisterEvent("PLAYER_REGEN_ENABLED")
frame:RegisterEvent("PLAYER_LOGOUT")
frame:RegisterEvent("ENCOUNTER_END")
frame:RegisterEvent("CHAT_MSG_LOOT")
frame:RegisterEvent("PLAYER_DEAD")
frame:RegisterEvent("COMBAT_LOG_EVENT_UNFILTERED")
frame:RegisterEvent("MERCHANT_SHOW")
frame:RegisterEvent("MERCHANT_CLOSED")
pcall(frame.RegisterEvent, frame, "NEW_MOUNT_ADDED") -- clients with a mount journal
-- Refresh the character snapshot when gear or spec changes, while the game has the data.
frame:RegisterEvent("PLAYER_ENTERING_WORLD")
frame:RegisterEvent("PLAYER_EQUIPMENT_CHANGED")
pcall(frame.RegisterEvent, frame, "PLAYER_SPECIALIZATION_CHANGED")
pcall(frame.RegisterEvent, frame, "TRAIT_CONFIG_UPDATED")
pcall(frame.RegisterEvent, frame, "CHARACTER_POINTS_CHANGED")

local pendingLevel  -- set on level up, cleared when /played data arrives
local popupLevel    -- a level waiting for the "post now" popup until combat ends
local suppressChat = false
local lastPlayed, lastPlayedAt -- latest /played total and when it came, to estimate played later
local lastAttacker  -- who last hurt us, for hardcore death posts
local recentBosses = {} -- [name] = time, so ENCOUNTER_END and the combat log don't both record a kill
local merchantOpen = false -- a mount that shows up while a vendor is open was bought
local snapshotTicker  -- refreshes the character snapshot every few minutes

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

-- Last boss of each Classic dungeon and raid (English names, as ENCOUNTER_END and the
-- combat log report them). Killing one of these is posted as an end boss kill.
local END_BOSSES = {
  ["Bazzalan"] = "Ragefire Chasm",
  ["Mutanus the Devourer"] = "Wailing Caverns",
  ["Edwin VanCleef"] = "The Deadmines",
  ["Archmage Arugal"] = "Shadowfang Keep",
  ["Aku'mai"] = "Blackfathom Deeps",
  ["Bazil Thredd"] = "The Stockade",
  ["Mekgineer Thermaplugg"] = "Gnomeregan",
  ["Charlga Razorflank"] = "Razorfen Kraul",
  ["Bloodmage Thalnos"] = "Scarlet Monastery Graveyard",
  ["Arcanist Doan"] = "Scarlet Monastery Library",
  ["Herod"] = "Scarlet Monastery Armory",
  ["High Inquisitor Whitemane"] = "Scarlet Monastery Cathedral",
  ["Amnennar the Coldbringer"] = "Razorfen Downs",
  ["Archaedas"] = "Uldaman",
  ["Chief Ukorz Sandscalp"] = "Zul'Farrak",
  ["Princess Theradras"] = "Maraudon",
  ["Shade of Eranikus"] = "Sunken Temple",
  ["Emperor Dagran Thaurissan"] = "Blackrock Depths",
  ["Overlord Wyrmthalak"] = "Lower Blackrock Spire",
  ["General Drakkisath"] = "Upper Blackrock Spire",
  ["Alzzin the Wildshaper"] = "Dire Maul East",
  ["Prince Tortheldrin"] = "Dire Maul West",
  ["King Gordok"] = "Dire Maul North",
  ["Darkmaster Gandling"] = "Scholomance",
  ["Balnazzar"] = "Stratholme",
  ["Baron Rivendare"] = "Stratholme",
  ["Onyxia"] = "Onyxia's Lair",
  ["Ragnaros"] = "Molten Core",
  ["Nefarian"] = "Blackwing Lair",
  ["Hakkar"] = "Zul'Gurub",
  ["Ossirian the Unscarred"] = "Ruins of Ahn'Qiraj",
  ["C'Thun"] = "Temple of Ahn'Qiraj",
  ["Kel'Thuzad"] = "Naxxramas",
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

local function isHardcore()
  return C_GameRules and C_GameRules.IsHardcoreActive and C_GameRules.IsHardcoreActive() or false
end

-- Self-found shows as an aura on the player (spell 431567 in Classic Era).
local function isSelfFound()
  if C_UnitAuras and C_UnitAuras.GetPlayerAuraBySpellID then
    return C_UnitAuras.GetPlayerAuraBySpellID(431567) ~= nil
  end
  return false
end

-- "Hardcore", "Self-found", "Hardcore, Self-found" or "Normal", for grouping characters in the app.
local function getRuleset()
  local parts = {}
  if isHardcore() then table.insert(parts, "Hardcore") end
  if isSelfFound() then table.insert(parts, "Self-found") end
  if #parts == 0 then return "Normal" end
  return table.concat(parts, ", ")
end

-- Best guess of total /played right now.
local function playedNow()
  if not lastPlayed then return nil end
  return lastPlayed + (time() - lastPlayedAt)
end

-- Everything we show about the character, both in posts and in the companion app.
local function characterInfo()
  local className, classFile = UnitClass("player")
  local raceName, raceFile = UnitRace("player")
  local sex = UnitSex("player") -- 2 = male, 3 = female
  local talents, spec = getTalents()
  return {
    guid = UnitGUID("player"),
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
    hardcore = isHardcore() or nil,
    ruleset = getRuleset(),
  }
end

-- Keeps one up-to-date entry per character in WoWCharacterTrackerDB.chars.
-- Right after login and during logout the game can report no gear, spec or professions yet.
-- Values that come back empty keep what was saved before instead of wiping it.
local KEEP_IF_EMPTY = { "ilvl", "spec", "role", "talents", "professions", "zone", "played" }

local function saveSnapshot()
  local guid = UnitGUID("player")
  if not guid then return end
  local info = characterInfo()
  info.played = playedNow()
  info.lastSeen = time()
  local old = WoWCharacterTrackerDB.chars[guid]
  if old then
    -- No spec right now usually means the game isn't ready, not that the spec is gone,
    -- so the talent text that goes with it is kept too.
    if info.spec == nil and old.spec then info.talents = old.talents end
    for _, k in ipairs(KEEP_IF_EMPTY) do
      local v = info[k]
      if v == nil or v == "" or v == 0 or v == "an unknown place" or v == "Unknown" then info[k] = old[k] end
    end
  end
  WoWCharacterTrackerDB.chars[guid] = info
end

-- Live channel ---------------------------------------------------------------

-- "key=value;key=value", with the few characters that would break it escaped as %XX.
-- Pipes are escaped too, since the chat system treats them as formatting codes.
local function encode(t)
  local parts = {}
  for k, v in pairs(t) do
    if type(v) ~= "table" then
      local s = tostring(v):gsub("[%%;=#|]", function(c) return string.format("%%%02X", c:byte()) end)
      table.insert(parts, k .. "=" .. s)
    end
  end
  table.sort(parts) -- stable order makes the log easier to read
  return table.concat(parts, ";")
end

local liveCounter = 0

-- The game buffers the chat log and only writes it to disk when the file is closed (on exit).
-- Turning logging off and on again closes and reopens the file, which writes out what's
-- buffered. Done a moment after sending, once the whispers have come back from the server,
-- and once more a bit later in case the server was slow.
local flushPending = false
-- The game applies the change a frame later, so off and on in the same frame does nothing.
-- Turn it off, give it a moment to close the file, then turn it back on.
local function flushChatLog()
  if type(LoggingChat) == "function" and LoggingChat() then
    LoggingChat(false)
    C_Timer.After(0.2, function() LoggingChat(true) end)
  end
end
local function flushSoon()
  if flushPending then return end
  flushPending = true
  C_Timer.After(1.5, function()
    flushChatLog()
    C_Timer.After(3, function()
      flushChatLog()
      flushPending = false
    end)
  end)
end

-- Whispers the event to ourselves in chunks: #CT1#<msgid>#<part>/<total>#<data>.
-- Chat messages max out at 255 characters, so long events take a few whispers.
local function sendLive(e)
  if WoWCharacterTrackerDB.live == false then return end
  local me = UnitName("player")
  if not me or type(SendChatMessage) ~= "function" then return end

  liveCounter = liveCounter + 1
  local msgid = string.format("%x%x", time() % 65536, liveCounter)
  local data = encode(e)
  local size = 200
  local total = math.max(1, math.ceil(#data / size))
  for i = 1, total do
    local chunk = data:sub((i - 1) * size + 1, i * size)
    SendChatMessage(string.format("%s%s#%d/%d#%s", LIVE_PREFIX, msgid, i, total, chunk), "WHISPER", nil, me)
  end
  flushSoon()
end

-- Keep our data whispers out of the chat window. The chat log still records them.
local function hideLive(_, _, msg)
  return type(msg) == "string" and msg:sub(1, #LIVE_PREFIX) == LIVE_PREFIX
end
if type(ChatFrame_AddMessageEventFilter) == "function" then
  ChatFrame_AddMessageEventFilter("CHAT_MSG_WHISPER", hideLive)
  ChatFrame_AddMessageEventFilter("CHAT_MSG_WHISPER_INFORM", hideLive)
end

-- How long to wait before the screenshot, so the moment is on screen (the ding glow, the boss
-- corpse, the death screen).
local SHOT_DELAY = { level = 1.0, boss = 1.5, loot = 0.3, mount = 0.5, death = 0.2 }

local function wantsScreenshot(kind)
  local s = WoWCharacterTrackerSettings and WoWCharacterTrackerSettings.screenshot
  return s and s[kind] and type(Screenshot) == "function"
end

-- Stores an event in SavedVariables, sends it live and takes a screenshot if the app asked for
-- one for this kind of event. The app finds the picture in the Screenshots folder by its time.
local function addEvent(e)
  if wantsScreenshot(e.kind) then
    e.shot = true
    C_Timer.After(SHOT_DELAY[e.kind] or 0.5, function() Screenshot() end)
  end
  table.insert(WoWCharacterTrackerDB.events, e)
  sendLive(e)
end

-- Events ---------------------------------------------------------------------

-- Our own /played requests would print "Total time played" in chat. While one is out, the chat
-- windows stop listening for the answer; they listen again once it has arrived.
local mutedFrames = {}

local function unmutePlayed()
  for _, f in ipairs(mutedFrames) do f:RegisterEvent("TIME_PLAYED_MSG") end
  mutedFrames = {}
  suppressChat = false
end

local function mutePlayed()
  for i = 1, (NUM_CHAT_WINDOWS or 10) do
    local f = _G["ChatFrame" .. i]
    if f and f.IsEventRegistered and f:IsEventRegistered("TIME_PLAYED_MSG") then
      f:UnregisterEvent("TIME_PLAYED_MSG")
      table.insert(mutedFrames, f)
    end
  end
end

local function requestPlayed()
  suppressChat = true
  mutePlayed()
  RequestTimePlayed()
  -- Safety net so the player's own /played is never muted for long.
  C_Timer.After(5, unmutePlayed)
end

-- Older clients print it from this function instead.
if type(ChatFrame_DisplayTimePlayed) == "function" then
  local original = ChatFrame_DisplayTimePlayed
  ChatFrame_DisplayTimePlayed = function(...)
    if suppressChat then return end
    return original(...)
  end
end

-- Optional (off by default, /ct popup): offer a reload after a ding, for when the live
-- channel doesn't work on a client.
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

local function recordLevel(guid, level, seconds, played)
  local e = characterInfo()
  e.kind = "level"
  e.id = guid .. ":" .. level
  e.level = level -- UnitLevel can lag right after the ding
  e.seconds = seconds
  e.played = played -- total /played at the ding
  e.at = time()
  addEvent(e)
end

local function recordBoss(name)
  local instance = END_BOSSES[name]
  if not instance then return end
  local now = time()
  if recentBosses[name] and now - recentBosses[name] < 600 then return end
  recentBosses[name] = now

  local e = characterInfo()
  e.kind = "boss"
  e.boss = name
  e.instance = (GetInstanceInfo and GetInstanceInfo()) or instance
  e.at = now
  e.id = string.format("%s:boss:%s:%d", e.guid, name, now)
  addEvent(e)
  say(string.format("End boss down: %s.", name))
end

-- LOOT_ITEM_SELF is "You receive loot: %s." (and a "%sx%d." variant for stacks). Turned into
-- Lua patterns so this works with the client's own language.
local function toPattern(fmt)
  if type(fmt) ~= "string" then return nil end
  local p = fmt:gsub("[%(%)%.%+%-%*%?%[%]%^%$]", "%%%0")
  p = p:gsub("%%s", "(.+)"):gsub("%%d", "(%%d+)")
  return "^" .. p .. "$"
end
local function patterns(...)
  local list = {}
  for _, fmt in ipairs({ ... }) do
    local p = toPattern(fmt)
    if p then table.insert(list, p) end
  end
  return list
end
local LOOT_PATTERNS = patterns(LOOT_ITEM_SELF_MULTIPLE, LOOT_ITEM_SELF)
-- "You receive item: %s." covers vendor purchases and quest rewards.
local PUSHED_PATTERNS = patterns(LOOT_ITEM_PUSHED_SELF_MULTIPLE, LOOT_ITEM_PUSHED_SELF)

local QUALITY_BY_COLOR = { ["ff0070dd"] = 3, ["ffa335ee"] = 4, ["ffff8000"] = 5, ["ffe6cc80"] = 6 }

local function matchAny(list, text)
  for _, p in ipairs(list) do
    local link = text:match(p)
    if link then return link end
  end
  return nil
end

local lastMountAt = 0

local function recordMount(name, itemId)
  -- Buying a mount item and then learning it would count twice on clients with a mount journal.
  if time() - lastMountAt < 120 then return end
  lastMountAt = time()

  local e = characterInfo()
  e.kind = "mount"
  e.mount = name
  e.itemId = itemId
  e.bought = merchantOpen or nil
  e.at = time()
  e.id = string.format("%s:mount:%s:%d", e.guid, name, e.at)
  addEvent(e)
  say(string.format("New mount: %s.", name))
end

-- Mount items are class 15 (miscellaneous), subclass 5 (mount).
local function isMountItem(link)
  local getInstant = (C_Item and C_Item.GetItemInfoInstant) or GetItemInfoInstant
  if not getInstant then return false end
  local _, _, _, _, _, classID, subclassID = getInstant(link)
  return classID == 15 and subclassID == 5
end

local function recordLoot(text)
  local link = matchAny(LOOT_PATTERNS, text)
  local pushed = false
  if not link then
    link = matchAny(PUSHED_PATTERNS, text)
    pushed = true
  end
  if not link then return end

  local itemId = tonumber(link:match("item:(%d+)"))
  local name = link:match("%[(.-)%]")
  if not itemId or not name then return end

  if isMountItem(link) then
    recordMount(name, itemId)
    return
  end
  if pushed then return end -- rare loot means looted, not bought or handed out

  local quality = GetItemInfo and select(3, GetItemInfo(link))
  if not quality then
    local color = link:match("|c(%x%x%x%x%x%x%x%x)")
    quality = color and QUALITY_BY_COLOR[color:lower()]
  end
  if not quality or quality < 3 then return end -- rare (blue) and better only

  local e = characterInfo()
  e.kind = "loot"
  e.item = name
  e.itemId = itemId
  e.quality = quality
  e.at = time()
  e.id = string.format("%s:loot:%d:%d", e.guid, itemId, e.at)
  addEvent(e)
end

local function recordDeath()
  if not isHardcore() then return end
  local e = characterInfo()
  e.kind = "death"
  e.played = playedNow()
  e.gold = GetMoney and GetMoney() or 0 -- copper
  e.killer = lastAttacker
  e.at = time()
  e.id = string.format("%s:death:%d", e.guid, e.at)
  addEvent(e)
  saveSnapshot()
end

local DAMAGE_EVENTS = {
  SWING_DAMAGE = true, RANGE_DAMAGE = true, SPELL_DAMAGE = true,
  SPELL_PERIODIC_DAMAGE = true, ENVIRONMENTAL_DAMAGE = true,
}

local function onCombatLog()
  if not CombatLogGetCurrentEventInfo then return end
  local _, sub, _, _, sourceName, _, _, destGUID, destName, _, _, envType = CombatLogGetCurrentEventInfo()
  if DAMAGE_EVENTS[sub] and destGUID == UnitGUID("player") then
    if sub == "ENVIRONMENTAL_DAMAGE" then
      lastAttacker = envType and (envType:sub(1, 1) .. envType:sub(2):lower()) or nil -- "Falling", "Lava"...
    else
      lastAttacker = sourceName
    end
  elseif sub == "UNIT_DIED" and destName and END_BOSSES[destName] then
    local instanceType = GetInstanceInfo and select(2, GetInstanceInfo())
    if instanceType == "party" or instanceType == "raid" then recordBoss(destName) end
  end
end

frame:SetScript("OnEvent", function(_, event, ...)
  if event == "COMBAT_LOG_EVENT_UNFILTERED" then
    onCombatLog()

  elseif event == "ADDON_LOADED" then
    if (...) ~= ADDON then return end
    WoWCharacterTrackerDB = WoWCharacterTrackerDB or {}
    WoWCharacterTrackerDB.start = WoWCharacterTrackerDB.start or {}   -- [guid] = total played at the start of the current level
    WoWCharacterTrackerDB.events = WoWCharacterTrackerDB.events or {} -- level-ups and notable moments
    WoWCharacterTrackerDB.chars = WoWCharacterTrackerDB.chars or {}   -- [guid] = latest snapshot of that character
    if WoWCharacterTrackerDB.popup == nil then WoWCharacterTrackerDB.popup = false end

  elseif event == "PLAYER_LOGIN" then
    -- The live channel needs the chat log on. It stays on, the file just grows a little.
    if WoWCharacterTrackerDB.live ~= false and type(LoggingChat) == "function" and not LoggingChat() then
      LoggingChat(true)
    end
    requestPlayed() -- sets the baseline for the current level

  elseif event == "PLAYER_LOGOUT" then
    saveSnapshot() -- also fires on /reload, right before the game writes the file

  elseif event == "PLAYER_ENTERING_WORLD" then
    -- Gear and spec info can take a few seconds after login. Snapshot then, and every 5 minutes.
    C_Timer.After(10, saveSnapshot)
    if not snapshotTicker and C_Timer.NewTicker then snapshotTicker = C_Timer.NewTicker(300, saveSnapshot) end

  elseif event == "PLAYER_EQUIPMENT_CHANGED" or event == "PLAYER_SPECIALIZATION_CHANGED"
      or event == "TRAIT_CONFIG_UPDATED" or event == "CHARACTER_POINTS_CHANGED" then
    C_Timer.After(1, saveSnapshot)

  elseif event == "PLAYER_LEVEL_UP" then
    pendingLevel = (...) -- the level just reached
    requestPlayed()

  elseif event == "PLAYER_REGEN_ENABLED" then
    if popupLevel then
      local level = popupLevel
      popupLevel = nil
      offerPost(level)
    end

  elseif event == "ENCOUNTER_END" then
    local _, encounterName, _, _, success = ...
    if success == 1 or success == true then recordBoss(encounterName) end

  elseif event == "CHAT_MSG_LOOT" then
    recordLoot((...))

  elseif event == "PLAYER_DEAD" then
    recordDeath()

  elseif event == "MERCHANT_SHOW" then
    merchantOpen = true

  elseif event == "MERCHANT_CLOSED" then
    -- The bought item can arrive just after the window closes.
    C_Timer.After(2, function() merchantOpen = false end)

  elseif event == "NEW_MOUNT_ADDED" then
    local mountID = ...
    local name = C_MountJournal and C_MountJournal.GetMountInfoByID and C_MountJournal.GetMountInfoByID(mountID)
    if name then recordMount(name, nil) end

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
    -- Not right away: frames registered during this event could still get this same message.
    C_Timer.After(0.5, unmutePlayed)
  end
end)

local function showHistory()
  local guid = UnitGUID("player")
  local shown = 0
  for i = #WoWCharacterTrackerDB.events, 1, -1 do
    local e = WoWCharacterTrackerDB.events[i]
    if (e.kind == nil or e.kind == "level") and e.id == guid .. ":" .. e.level then
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
    say("Reload popup after a ding is now " .. (WoWCharacterTrackerDB.popup and "on." or "off."))
  elseif msg == "live" then
    WoWCharacterTrackerDB.live = WoWCharacterTrackerDB.live == false
    say("Instant posting is now " .. (WoWCharacterTrackerDB.live and "on." or "off. Posts arrive on /reload or logout."))
  elseif msg == "test" then
    local e = characterInfo()
    e.kind = "test"
    e.at = time()
    e.id = string.format("%s:test:%d", e.guid, e.at)
    sendLive(e)
    local logging = type(LoggingChat) == "function" and LoggingChat()
    say("Sent a test event. The companion app should show it within a few seconds."
      .. (logging and "" or " Chat logging is off, so it can't arrive until /reload. Type /chatlog to turn it on."))
  elseif msg == "help" then
    say("/ct shows your last 10 level times.")
    say("/ct test checks that instant posting reaches the companion app.")
    say("/ct live turns instant posting on or off.")
    say("/ct popup turns the reload popup after a ding on or off.")
  else
    showHistory()
  end
end
