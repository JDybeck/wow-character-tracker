-- The live channel: events go to the app through a small data grid screenshot (see Core.lua's header).
local ADDON, ns = ...

-- Live channel ---------------------------------------------------------------

-- One-letter keys keep the grid small. The app has the same table (Payload.ShortKeys).
local SHORT = {
  id = "i", kind = "k", guid = "g", char = "n", realm = "r", level = "l", at = "a", zone = "z",
  professions = "p", talents = "t", spec = "s", role = "o", ilvl = "v", class = "c", classFile = "C",
  race = "e", raceFile = "E", sex = "x", hardcore = "h", ruleset = "u", seconds = "d", played = "y",
  boss = "b", instance = "I", item = "m", itemId = "M", quality = "q", gold = "G", killer = "K",
  mount = "w", bought = "B", shot = "S", group = "P", groupSize = "Z", money = "N", crests = "R",
  maxLevel = "X", guild = "U", surname = "L", opponent = "O", fled = "F", rating = "T", step = "H", bracket = "D", talentExport = "J", talentCalc = "Q", region = "A",
}

-- Too long for the data grid; these only reach the app through the saved file. (The talent
-- code also rides along with the check-in after login, so the Wowhead link shows right away.)
local SAVED_ONLY = { talentPicks = true, talentExport = true, stats = true, xp = true, xpMax = true, maxLevel = true }

-- "k=value;k=value": short keys, empty values left out, true as 1, and the id without the
-- character's GUID in front (it's in g already). Characters that would break it are %XX.
local function encode(t)
  local parts = {}
  for k, v in pairs(t) do
    if type(v) ~= "table" and v ~= "" and v ~= false and (not SAVED_ONLY[k] or (k == "talentExport" and t.kind == "snapshot")) then
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

-- Game settings the app manages (Settings.lua). Only touched when the app set them (nil = leave
-- the player's own choice alone), and only out of combat.
function ns.applyGameSettings()
  local s = WoWCharacterTrackerSettings
  if not s or s.advancedCombatLog == nil or InCombatLockdown() then return end
  local want = s.advancedCombatLog and "1" or "0"
  if getCVar("advancedCombatLogging") ~= want then
    ns.doing("advancedCombatLogging")
    pcall(setCVar, "advancedCombatLogging", want)
  end
end

local nextFrame = CreateFrame("Frame")
local function onNextFrame(fn)
  nextFrame:SetScript("OnUpdate", function(self)
    self:SetScript("OnUpdate", nil)
    fn()
  end)
end

-- ns.processShots

-- "Hide chat in screenshots": the chat windows, tabs and buttons are made see-through for the
-- shot and put back after. Only the alpha changes (no Hide/Show on Blizzard frames), which keeps
-- the default UI untouched.
local CHAT_EXTRAS = { "GeneralDockManager", "ChatFrameMenuButton", "ChatFrameChannelButton",
  "ChatFrameToggleVoiceDeafenButton", "ChatFrameToggleVoiceMuteButton", "QuickJoinToastButton",
  "ChatAlertFrame", "FriendsMicroButton" }
local hiddenChat

local function hideChat()
  local s = WoWCharacterTrackerSettings
  if not (s and (s.hideChat or s.hideUI)) or hiddenChat then return end
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
  -- The whole interface, like Alt+Z: everything Blizzard draws hangs off UIParent. Alpha only, so
  -- this is fine in combat too (nameplates belong to the world and stay).
  if s.hideUI then fade(UIParent) end
end

local function showChat()
  if not hiddenChat then return end
  for f, alpha in pairs(hiddenChat) do f:SetAlpha(alpha) end
  hiddenChat = nil
end

function ns.shotDone()
  if not shotBusy then return end
  if shotItem and shotItem.bits then
    if grid then grid:Hide() end
    restoreShotSettings()
  else
    showChat()
  end
  shotItem = nil
  shotBusy = false
  C_Timer.After(0.1, ns.processShots)
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

ns.processShots = function()
  if shotBusy or #shotQueue == 0 then return end
  local item = nextShot()
  if not item then return end -- only grids left, PLAYER_REGEN_ENABLED picks them up
  shotItem = item
  shotBusy = true
  -- Never wait forever on a screenshot event that doesn't come.
  C_Timer.After(5, function() if shotItem == item then ns.shotDone() end end)

  if item.bits then
    ns.doing("data grid screenshot")
    drawGrid(item.bits)
    useGridShotSettings()
    grid:Show()
    onNextFrame(function() Screenshot() end) -- let the grid be drawn once first
  else
    C_Timer.After(item.delay or 0.5, function()
      ns.doing("event screenshot")
      hideChat()
      onNextFrame(function() Screenshot() end)
    end)
  end
end

function ns.sendLive(e)
  if WoWCharacterTrackerDB.live == false or type(Screenshot) ~= "function" then return end
  table.insert(shotQueue, { bits = toBits(encode(e)) })
  ns.processShots()
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
  if e.kind == "loot" and (e.quality or 0) >= 5 then return true end -- legendary: always worth a post
  if e.maxLevel and post.maxLevel ~= false then return true end -- max level has its own switch
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

-- Stores an event in SavedVariables and sends it live, so the app's level history and highlights
-- stay current while you play. Only events that will be posted get a screenshot (if the app
-- asked for one). Loot that won't be posted stays off the live channel, since blue items drop
-- often and each live event is a flicker in the corner; it reaches the app on /reload or logout.
function ns.addEvent(e)
  table.insert(WoWCharacterTrackerDB.events, e)
  local posted = wanted(e)
  if not posted and e.kind == "loot" then return end
  local shot = posted and wantsScreenshot(e.kind)
  if shot then e.shot = true end
  ns.sendLive(e)
  if shot then
    table.insert(shotQueue, { delay = SHOT_DELAY[e.kind] or 0.5 })
    ns.processShots()
  end
end

-- One live check-in per login, so the app shows the character (level, gear, zone) right away.
local checkedIn = false
function ns.checkIn()
  if checkedIn or not WoWCharacterTrackerDB or WoWCharacterTrackerDB.live == false then return end
  checkedIn = true
  local e = ns.characterInfo()
  e.kind = "snapshot"
  e.played = ns.playedNow()
  e.at = time()
  e.id = string.format("%s:snapshot:%d", e.guid, e.at)
  ns.sendLive(e)
end
