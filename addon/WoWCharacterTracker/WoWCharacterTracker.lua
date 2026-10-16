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

local ADDON = "WoWCharacterTracker"

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
frame:RegisterEvent("PLAYER_MONEY")
pcall(frame.RegisterEvent, frame, "CURRENCY_DISPLAY_UPDATE")
pcall(frame.RegisterEvent, frame, "PLAYER_SPECIALIZATION_CHANGED")
pcall(frame.RegisterEvent, frame, "TRAIT_CONFIG_UPDATED")
pcall(frame.RegisterEvent, frame, "CHARACTER_POINTS_CHANGED")
pcall(frame.RegisterEvent, frame, "PLAYER_GUILD_UPDATE")
frame:RegisterEvent("SCREENSHOT_SUCCEEDED")
frame:RegisterEvent("SCREENSHOT_FAILED")

local pendingLevel  -- set on level up, cleared when /played data arrives
local popupLevel    -- a level waiting for the "post now" popup until combat ends
local suppressChat = false
local lastPlayed, lastPlayedAt -- latest /played total and when it came, to estimate played later
local lastAttacker  -- who last hurt us, for hardcore death posts
local recentBosses = {} -- [name] = time, so ENCOUNTER_END and the combat log don't both record a kill
local merchantOpen = false -- a mount that shows up while a vendor is open was bought
local snapshotTicker  -- refreshes the character snapshot every few minutes
local moneyPending    -- a snapshot is already scheduled after a money/currency change

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

  if classSpent + specSpent + heroSpent == 0 then return nil end -- nothing to show yet
  local text = string.format("Class %d / %s %d", classSpent, specName, specSpent)
  if heroName then text = text .. string.format(" / %s %d", heroName, heroSpent) end
  return text
end

-- Returns e.g. "Arms 31 / Fury 20 / Protection 0", or nil when there is nothing to show yet.
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
    if total == 0 then return nil end -- under level 10: nothing to show, so nothing is stored
    return table.concat(parts, " / ")
  elseif type(GetSpecialization) == "function" then
    -- Retail: class, spec and hero trees.
    local spec = GetSpecialization()
    local specName = spec and select(2, GetSpecializationInfo(spec))
    if not specName then return nil end
    local ok, split = pcall(getRetailTalentSplit, specName)
    if ok and split then return split, specName end
    return nil, specName
  end
  return nil
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

-- Retail crests as "id~quantity~name,..." (the app shows them with their icons), or nil.
-- Read from the game's own Crests section of the currency window, so only this season's crests
-- show up (older ones stay in your currencies but under other sections). A collapsed Crests
-- section can't be read without changing your window, so then the last known crests are kept.
local function getCrests()
  if not (C_CurrencyInfo and C_CurrencyInfo.GetCurrencyListSize and C_CurrencyInfo.GetCurrencyListInfo) then return nil end
  local parts, inCrests = {}, false
  for i = 1, C_CurrencyInfo.GetCurrencyListSize() do
    local info = C_CurrencyInfo.GetCurrencyListInfo(i)
    if info then
      if info.isHeader then
        if inCrests then break end
        inCrests = info.name ~= nil and info.name:lower():find("crest") ~= nil and info.isHeaderExpanded ~= false
      elseif inCrests then
        local link = C_CurrencyInfo.GetCurrencyListLink and C_CurrencyInfo.GetCurrencyListLink(i)
        local id = link and tonumber(link:match("currency:(%d+)")) or 0
        table.insert(parts, string.format("%d~%d~%s", id, info.quantity or 0, (info.name or "?"):gsub("[~,]", " ")))
      end
    end
  end
  if #parts == 0 then return nil end
  return table.concat(parts, ",")
end

-- Best guess of total /played right now.

local function playedNow()
  if not lastPlayed then return nil end
  return lastPlayed + (time() - lastPlayedAt)
end

-- Guild name, "" when not in one, nil when the game doesn't know yet (right after login).
local function getGuild()
  if not (IsInGuild and IsInGuild()) then return "" end
  local name = GetGuildInfo and GetGuildInfo("player")
  return name
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
    money = GetMoney and GetMoney() or nil, -- copper
    crests = getCrests(),
    guild = getGuild(),
  }
end

-- Keeps one up-to-date entry per character in WoWCharacterTrackerDB.chars.
-- Right after login and during logout the game can report no gear, spec or professions yet.
-- Values that come back empty keep what was saved before instead of wiping it.
-- (Money too: during logout GetMoney() comes back as 0.)
local KEEP_IF_EMPTY = { "ilvl", "spec", "role", "talents", "professions", "zone", "played", "crests", "money" }

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
    -- Guild: "" means no guild (keep that), nil means not known yet.
    if info.guild == nil then info.guild = old.guild end
  end
  WoWCharacterTrackerDB.chars[guid] = info
end

-- Live channel ---------------------------------------------------------------

-- One-letter keys keep the grid small. The app has the same table (Payload.ShortKeys).
local SHORT = {
  id = "i", kind = "k", guid = "g", char = "n", realm = "r", level = "l", at = "a", zone = "z",
  professions = "p", talents = "t", spec = "s", role = "o", ilvl = "v", class = "c", classFile = "C",
  race = "e", raceFile = "E", sex = "x", hardcore = "h", ruleset = "u", seconds = "d", played = "y",
  boss = "b", instance = "I", item = "m", itemId = "M", quality = "q", gold = "G", killer = "K",
  mount = "w", bought = "B", shot = "S", group = "P", groupSize = "Z", money = "N", crests = "R",
  maxLevel = "X", guild = "U",
}

-- "k=value;k=value": short keys, empty values left out, true as 1, and the id without the
-- character's GUID in front (it's in g already). Characters that would break it are %XX.
local function encode(t)
  local parts = {}
  for k, v in pairs(t) do
    if type(v) ~= "table" and v ~= "" and v ~= false then
      if v == true then v = 1 end
      if k == "id" and t.guid and type(v) == "string" and v:sub(1, #t.guid) == t.guid then v = v:sub(#t.guid + 1) end
      if k == "sex" then v = (v == "female") and "f" or "m" end
      local s = tostring(v):gsub("[%%;=#|]", function(c) return string.format("%%%02X", c:byte()) end)
      table.insert(parts, (SHORT[k] or k) .. "=" .. s)
    end
  end
  table.sort(parts)
  return table.concat(parts, ";")
end

-- The grid: 160 cells across, 2 pixels each, saved as PNG so the small cells stay sharp. Row 0
-- and column 0 alternate white/black so the app can measure the cell size in the picture; the
-- other cells hold the bits (black = 1): a magic byte (0xC8: this layout and short keys), the
-- payload length (2 bytes), the payload, and a 2-byte checksum. (0xC7 was the older 128 x 5 px
-- layout with long keys; the app still reads it.)
local GRID_COLS = 160
local CELL = 2
local MAGIC = 0xC8

local function toBits(data)
  local bytes = { MAGIC, math.floor(#data / 256), #data % 256 }
  local sum = 0
  for i = 1, #data do
    local b = data:byte(i)
    table.insert(bytes, b)
    sum = (sum + b) % 65536
  end
  table.insert(bytes, math.floor(sum / 256))
  table.insert(bytes, sum % 256)

  local bits = {}
  for _, b in ipairs(bytes) do
    for i = 7, 0, -1 do bits[#bits + 1] = math.floor(b / 2 ^ i) % 2 end
  end
  return bits
end

local grid, gridWhite
local gridTextures = {}

-- A frame without a parent stays visible when the UI is hidden (Alt+Z). Its scale is set so one
-- unit is one screen pixel.
local function gridFrame()
  if grid then return grid end
  grid = CreateFrame("Frame", nil, nil)
  grid:SetFrameStrata("TOOLTIP")
  grid:SetFrameLevel(10000)
  grid:EnableMouse(false)
  grid:Hide()
  gridWhite = grid:CreateTexture(nil, "BACKGROUND")
  gridWhite:SetColorTexture(1, 1, 1, 1)
  gridWhite:SetAllPoints()

  -- A label next to the grid so players know what the flicker in the corner is. It sits to the
  -- right of the 160 columns with a gap, outside the area the app reads.
  local label = CreateFrame("Frame", nil, grid)
  label:SetPoint("TOPLEFT", grid, "TOPLEFT", GRID_COLS * CELL + 4, 0)
  label:SetSize(230, 30)
  local bg = label:CreateTexture(nil, "BACKGROUND")
  bg:SetColorTexture(0.05, 0.05, 0.08, 0.85)
  bg:SetAllPoints()
  local font = STANDARD_TEXT_FONT or "Fonts\\FRIZQT__.TTF"
  local title = label:CreateFontString(nil, "OVERLAY")
  title:SetFont(font, 12, "")
  title:SetTextColor(1, 0.82, 0)
  title:SetPoint("TOPLEFT", 6, -3)
  title:SetText("WoW Character Tracker")
  local sub = label:CreateFontString(nil, "OVERLAY")
  sub:SetFont(font, 10, "")
  sub:SetTextColor(0.85, 0.85, 0.85)
  sub:SetPoint("TOPLEFT", title, "BOTTOMLEFT", 0, -2)
  sub:SetText("Sending to the app. /wct help")
  return grid
end

local function pixelScale()
  local _, height = 0, 0
  if type(GetPhysicalScreenSize) == "function" then _, height = GetPhysicalScreenSize() end
  if not height or height <= 0 then height = 1080 end
  return 768 / height
end

-- Draws the bits. Black cells in a row are merged into one texture each, and textures are reused.
local function drawGrid(bits)
  local f = gridFrame()
  f:SetScale(pixelScale())
  local dataCols = GRID_COLS - 1
  local rows = math.ceil(#bits / dataCols) + 1
  f:ClearAllPoints()
  f:SetPoint("TOPLEFT", nil, "TOPLEFT", 0, 0)
  f:SetSize(GRID_COLS * CELL, rows * CELL)

  local used = 0
  local function black(x, y, cells)
    used = used + 1
    local t = gridTextures[used]
    if not t then
      t = f:CreateTexture(nil, "ARTWORK")
      t:SetColorTexture(0, 0, 0, 1)
      if t.SetSnapToPixelGrid then t:SetSnapToPixelGrid(false); t:SetTexelSnappingBias(0) end
      gridTextures[used] = t
    end
    t:ClearAllPoints()
    t:SetPoint("TOPLEFT", f, "TOPLEFT", x * CELL, -y * CELL)
    t:SetSize(cells * CELL, CELL)
    t:Show()
  end

  local function cell(r, c)
    if r == 0 then return c % 2 end      -- clock row: white, black, white...
    if c == 0 then return r % 2 end      -- clock column
    return bits[(r - 1) * dataCols + c] or 0
  end

  for r = 0, rows - 1 do
    local c = 0
    while c < GRID_COLS do
      if cell(r, c) == 1 then
        local start = c
        while c < GRID_COLS and cell(r, c) == 1 do c = c + 1 end
        black(start, r, c - start)
      else
        c = c + 1
      end
    end
  end
  for i = used + 1, #gridTextures do gridTextures[i]:Hide() end
end

-- Screenshots are taken one at a time from a queue: a data grid for every event, and a normal
-- screenshot after it when the app asked for one.
local shotQueue = {}
local shotBusy = false
local shotItem
local savedCVars

local getCVar = (C_CVar and C_CVar.GetCVar) or GetCVar
local setCVar = (C_CVar and C_CVar.SetCVar) or SetCVar

-- PNG while the grid is captured (lossless, so the 2 px cells stay sharp; TGA can't be read by
-- the app). The player's own settings come back right after.
local function useGridShotSettings()
  savedCVars = { format = getCVar("screenshotFormat"), quality = getCVar("screenshotQuality") }
  pcall(setCVar, "screenshotFormat", "png") -- lossless, so 2 px cells come through clean
  pcall(setCVar, "screenshotQuality", "10")
  if ActionStatus and ActionStatus.UnregisterEvent then ActionStatus:UnregisterEvent("SCREENSHOT_SUCCEEDED") end
end

local function restoreShotSettings()
  if savedCVars then
    if savedCVars.format then pcall(setCVar, "screenshotFormat", savedCVars.format) end
    if savedCVars.quality then pcall(setCVar, "screenshotQuality", savedCVars.quality) end
    savedCVars = nil
  end
  if ActionStatus and ActionStatus.RegisterEvent then ActionStatus:RegisterEvent("SCREENSHOT_SUCCEEDED") end
end

local nextFrame = CreateFrame("Frame")
local function onNextFrame(fn)
  nextFrame:SetScript("OnUpdate", function(self)
    self:SetScript("OnUpdate", nil)
    fn()
  end)
end

local processShots

-- "Hide chat in screenshots": the chat windows, tabs and buttons are made see-through for the
-- shot and put back after. Only the alpha changes (no Hide/Show on Blizzard frames), which keeps
-- the default UI untouched.
local CHAT_EXTRAS = { "GeneralDockManager", "ChatFrameMenuButton", "ChatFrameChannelButton",
  "ChatFrameToggleVoiceDeafenButton", "ChatFrameToggleVoiceMuteButton", "QuickJoinToastButton",
  "ChatAlertFrame", "FriendsMicroButton" }
local hiddenChat

local function hideChat()
  local s = WoWCharacterTrackerSettings
  if not (s and s.hideChat) or hiddenChat then return end
  hiddenChat = {}
  local function fade(f)
    if f and f.GetAlpha and f.SetAlpha then
      hiddenChat[f] = f:GetAlpha()
      f:SetAlpha(0)
    end
  end
  for i = 1, (NUM_CHAT_WINDOWS or 10) do
    fade(_G["ChatFrame" .. i]); fade(_G["ChatFrame" .. i .. "Tab"]); fade(_G["ChatFrame" .. i .. "EditBox"])
    fade(_G["ChatFrame" .. i .. "ButtonFrame"])
  end
  for _, name in ipairs(CHAT_EXTRAS) do fade(_G[name]) end
end

local function showChat()
  if not hiddenChat then return end
  for f, alpha in pairs(hiddenChat) do f:SetAlpha(alpha) end
  hiddenChat = nil
end

local function shotDone()
  if not shotBusy then return end
  if shotItem and shotItem.bits then
    if grid then grid:Hide() end
    restoreShotSettings()
  else
    showChat()
  end
  shotItem = nil
  shotBusy = false
  C_Timer.After(0.1, processShots)
end

-- In combat only normal screenshots are taken (the moment itself); data grids wait until combat
-- ends, so screenshot settings are never changed mid-fight.
local function nextShot()
  if not InCombatLockdown() then return table.remove(shotQueue, 1) end
  for i, item in ipairs(shotQueue) do
    if not item.bits then return table.remove(shotQueue, i) end
  end
  return nil
end

processShots = function()
  if shotBusy or #shotQueue == 0 then return end
  local item = nextShot()
  if not item then return end -- only grids left, PLAYER_REGEN_ENABLED picks them up
  shotItem = item
  shotBusy = true
  -- Never wait forever on a screenshot event that doesn't come.
  C_Timer.After(5, function() if shotItem == item then shotDone() end end)

  if item.bits then
    drawGrid(item.bits)
    useGridShotSettings()
    grid:Show()
    onNextFrame(function() Screenshot() end) -- let the grid be drawn once first
  else
    C_Timer.After(item.delay or 0.5, function()
      hideChat()
      onNextFrame(function() Screenshot() end)
    end)
  end
end

local function sendLive(e)
  if WoWCharacterTrackerDB.live == false or type(Screenshot) ~= "function" then return end
  table.insert(shotQueue, { bits = toBits(encode(e)) })
  processShots()
end

-- How long to wait before a normal screenshot, so the moment is on screen (the ding glow, the
-- boss corpse, the death screen).
local SHOT_DELAY = { level = 1.0, boss = 1.5, loot = 0.3, mount = 0.5, death = 0.2 }

-- Will the app post this event? Settings.lua carries the app's "What gets posted" choices, so
-- the addon only sends events live (and screenshots them) when they'll actually be posted.
-- Without those settings (an older app), everything counts as wanted.
local function wanted(e)
  local post = WoWCharacterTrackerSettings and WoWCharacterTrackerSettings.post
  if not post then return true end
  if e.maxLevel or (e.kind == "loot" and (e.quality or 0) >= 5) then return true end -- always worth a post
  if e.kind == "level" then
    local l = post.level
    if not l or not l.on then return false end
    local every = (e.level <= (l.split or 0)) and (l.before or 1) or (l.after or 1)
    return e.level % math.max(1, every) == 0
  elseif e.kind == "loot" then
    if (e.quality or 0) >= 4 then return post.epic and true or false end
    return post.rare and true or false
  end
  return post[e.kind] and true or false
end

local function wantsScreenshot(kind)
  local s = WoWCharacterTrackerSettings and WoWCharacterTrackerSettings.screenshot
  return s and s[kind] and type(Screenshot) == "function"
end

-- Stores an event in SavedVariables. Events that will be posted also go out live, and get a
-- screenshot if the app asked for one for that kind. Everything else reaches the app on the
-- next /reload or logout, without any screenshots.
local function addEvent(e)
  table.insert(WoWCharacterTrackerDB.events, e)
  if not wanted(e) then return end
  local shot = wantsScreenshot(e.kind)
  if shot then e.shot = true end
  sendLive(e)
  if shot then
    table.insert(shotQueue, { delay = SHOT_DELAY[e.kind] or 0.5 })
    processShots()
  end
end

-- One live check-in per login, so the app shows the character (level, gear, zone) right away.
local checkedIn = false
local function checkIn()
  if checkedIn or not WoWCharacterTrackerDB or WoWCharacterTrackerDB.live == false then return end
  checkedIn = true
  local e = characterInfo()
  e.kind = "snapshot"
  e.played = playedNow()
  e.at = time()
  e.id = string.format("%s:snapshot:%d", e.guid, e.at)
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

-- (Older versions also replaced ChatFrame_DisplayTimePlayed to hide it. Replacing Blizzard
-- functions taints the default UI, which on Classic clients blocked Esc and showed "blocked from an
-- action" errors, so that's gone. Muting the chat windows above does the same job safely.)

-- Optional (off by default, /wct popup): offer a reload after a ding, for when the live
-- channel doesn't work on a client.
-- The dialog is only added to Blizzard's StaticPopupDialogs when it's used: anything an addon
-- puts in Blizzard's tables can spread taint, so it stays out unless the player turned this on.
local function offerPost(level)
  if not WoWCharacterTrackerDB.popup then return end
  if InCombatLockdown() then
    popupLevel = level -- show it when combat ends
    return
  end
  if not StaticPopupDialogs["CHARACTERTRACKER_POST"] then
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
  end
  StaticPopup_Show("CHARACTERTRACKER_POST", level)
end

local function recordLevel(guid, level, seconds, played)
  local e = characterInfo()
  e.kind = "level"
  e.id = guid .. ":" .. level
  e.level = level -- UnitLevel can lag right after the ding
  e.seconds = seconds
  -- Max level gets a big post (always, whatever the level rule says).
  local maxLevel = (GetMaxPlayerLevel and GetMaxPlayerLevel()) or MAX_PLAYER_LEVEL
  if maxLevel and level >= maxLevel then
    e.maxLevel = true
    e.group, e.groupSize = getGroup()
  end
  e.played = played -- total /played at the ding
  e.at = time()
  addEvent(e)
end

-- The other people in the party: "Name~level~CLASSFILE~Class~Race~Realm" per member, joined by
-- commas. Raids list only how many there are, since 40 names don't fit a post.
local function getGroup()
  if not IsInGroup or not IsInGroup() then return nil, 1 end
  local size = GetNumGroupMembers and GetNumGroupMembers() or 1
  if IsInRaid and IsInRaid() then return nil, size end
  local members = {}
  for i = 1, 4 do
    local unit = "party" .. i
    if UnitExists(unit) then
      local name, realm = UnitName(unit)
      local className, classFile = UnitClass(unit)
      local raceName = UnitRace(unit)
      table.insert(members, table.concat({ name or "?", UnitLevel(unit) or 0, classFile or "", className or "",
        raceName or "", (realm and realm ~= "") and realm or "" }, "~"))
    end
  end
  return table.concat(members, ","), size
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
  e.group, e.groupSize = getGroup()
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
    requestPlayed() -- sets the baseline for the current level
    say("Loaded. Type /wct help for the commands.")

  elseif event == "SCREENSHOT_SUCCEEDED" or event == "SCREENSHOT_FAILED" then
    shotDone()

  elseif event == "PLAYER_LOGOUT" then
    saveSnapshot() -- also fires on /reload, right before the game writes the file

  elseif event == "PLAYER_ENTERING_WORLD" then
    -- Gear and spec info can take a few seconds after login. Snapshot then, and every 5 minutes.
    C_Timer.After(10, saveSnapshot)
    C_Timer.After(15, checkIn)
    if not snapshotTicker and C_Timer.NewTicker then snapshotTicker = C_Timer.NewTicker(300, saveSnapshot) end

  elseif event == "PLAYER_EQUIPMENT_CHANGED" or event == "PLAYER_SPECIALIZATION_CHANGED"
      or event == "TRAIT_CONFIG_UPDATED" or event == "CHARACTER_POINTS_CHANGED" or event == "PLAYER_GUILD_UPDATE" then
    C_Timer.After(1, saveSnapshot)

  elseif event == "PLAYER_MONEY" or event == "CURRENCY_DISPLAY_UPDATE" then
    -- Money changes a lot (every loot and vendor), so save at most every few seconds.
    if not moneyPending then
      moneyPending = true
      C_Timer.After(3, function() moneyPending = false; saveSnapshot() end)
    end

  elseif event == "PLAYER_LEVEL_UP" then
    pendingLevel = (...) -- the level just reached
    requestPlayed()

  elseif event == "PLAYER_REGEN_ENABLED" then
    processShots() -- data grids held back during combat
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
SLASH_CHARACTERTRACKER2 = "/wct"
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
    if WoWCharacterTrackerDB.live == false then
      say("Instant posting is off. Type /wct live to turn it on.")
      return
    end
    sendLive(e)
    say("Sent a test event. The companion app should show it within a few seconds.")
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
    add("talents now: " .. dump(getTalents()))
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
    for _, s in ipairs(out) do if #s < 250 then say(s) end end
    say("Saved. /reload to write it to the SavedVariables file.")
  elseif msg == "help" then
    say("/wct shows your last 10 level times.")
    say("/wct test checks that instant posting reaches the companion app.")
    say("/wct live turns instant posting on or off.")
    say("/wct popup turns the reload popup after a ding on or off.")
  else
    showHistory()
  end
end
