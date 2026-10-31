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
  maxLevel = "X", guild = "U", surname = "L", opponent = "O", fled = "F", rating = "T", step = "H", bracket = "D", talentExport = "J", talentCalc = "Q", region = "A", lost = "Y", opponentInfo = "V", keystone = "W", zoneType = "f",
  mplusScore = "j", pvpRatings = "pr", faction = "fa", spellId = "sp", keyLevel = "kl", upgrades = "ku", profession = "pf", boe = "be", mat = "mt", askedKind = "ak", midBoss = "mb", -- single letters are used up, so new keys get two
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
      if k == "guid" and type(v) == "string" then v = v:gsub("^Player%-", "") end -- the app puts it back
      local s = tostring(v):gsub("[%%;=#|]", function(c) return string.format("%%%02X", c:byte()) end)
      table.insert(parts, (SHORT[k] or k) .. "=" .. s)
    end
  end
  table.sort(parts)
  return table.concat(parts, ";")
end

-- The grid: 160 cells across, 2 pixels each, black or white, saved as a high-quality JPEG (fast for the game to write; tested to read back cleanly). Row 0
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

-- Which screen corner the grid sits in (Settings.lua gridCorner): row 0 and column 0 (the clock) are always
-- at that corner, so the grid is mirrored for the right and bottom corners. The label and the portrait sit
-- on the inner side. The app tries every corner.
local function corner()
  local c = ns.setting("gridCorner")
  if c == "TOPRIGHT" or c == "BOTTOMLEFT" or c == "BOTTOMRIGHT" then return c end
  return "TOPLEFT"
end

local grid, gridWhite, gridPortrait, gridLabel
-- Where the portrait goes, in screen pixels from the top-left corner (the app knows the same spot):
-- right of the grid's 160 columns and the 230 px label.
local PORTRAIT_X, PORTRAIT_SIZE = 160 * 2 + 4 + 230 + 4, 64 -- kept where it was (the label used to be 230 px)
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

  -- A small label next to the grid so players know what the flicker in the corner is (Settings.lua
  -- gridLabel = false hides it). It sits beside the 160 columns with a gap, outside what the app reads.
  gridLabel = CreateFrame("Frame", nil, grid)
  gridLabel:SetSize(150, 16)
  local bg = gridLabel:CreateTexture(nil, "BACKGROUND")
  bg:SetColorTexture(0.05, 0.05, 0.08, 0.8)
  bg:SetAllPoints()
  local title = gridLabel:CreateFontString(nil, "OVERLAY")
  title:SetFont(STANDARD_TEXT_FONT or "Fonts\\FRIZQT__.TTF", 10, "")
  title:SetTextColor(1, 0.82, 0)
  title:SetPoint("CENTER")
  title:SetText("Character Tracker  /wct")

  -- The character's portrait (what the unit frame shows), at a fixed spot beside the grid, only in
  -- the check-in shot after login: the app cuts it out and uses it as the character's picture.
  -- (drawGrid places it, and the label, on the inner side of the corner in use.)
  local portraitFrame = CreateFrame("Frame", nil, grid)
  portraitFrame:SetSize(PORTRAIT_SIZE, PORTRAIT_SIZE)
  gridPortrait = portraitFrame:CreateTexture(nil, "ARTWORK")
  gridPortrait:SetAllPoints()
  portraitFrame:Hide()
  gridPortrait.frame = portraitFrame
  return grid
end

local function showPortrait(on)
  if not gridPortrait then return end
  if on and SetPortraitTexture and pcall(SetPortraitTexture, gridPortrait, "player") then
    gridPortrait.frame:Show()
  else
    gridPortrait.frame:Hide()
  end
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
  local where = corner()
  local right, bottom = where:find("RIGHT") ~= nil, where:find("BOTTOM") ~= nil
  f:ClearAllPoints()
  f:SetPoint(where, nil, where, 0, 0)
  f:SetSize(GRID_COLS * CELL, rows * CELL)
  -- The label and the portrait on the inner side, level with the corner's edge.
  local edge = bottom and "BOTTOM" or "TOP"
  gridLabel:ClearAllPoints()
  if right then gridLabel:SetPoint(edge .. "RIGHT", f, edge .. "LEFT", -4, 0) else gridLabel:SetPoint(edge .. "LEFT", f, edge .. "RIGHT", 4, 0) end
  gridLabel:SetShown(ns.setting("gridLabel") ~= false)
  gridPortrait.frame:ClearAllPoints()
  if right then gridPortrait.frame:SetPoint(edge .. "RIGHT", f, edge .. "RIGHT", -PORTRAIT_X, 0)
  else gridPortrait.frame:SetPoint(edge .. "LEFT", f, edge .. "LEFT", PORTRAIT_X, 0) end

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
    -- mirrored for the right and bottom corners, so the clock stays at the screen corner
    local px = right and (GRID_COLS - x - cells) or x
    local py = bottom and (rows - 1 - y) or y
    t:SetPoint("TOPLEFT", f, "TOPLEFT", px * CELL, -py * CELL)
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
-- The game names screenshots by the second (WoWScrnShot_MMDDYY_HHMMSS), so two in the same second
-- share a file and the second one overwrites the first. A ding shot in combat followed by the
-- grid right as combat ended did exactly that. The next shot waits for the clock to move on.
local lastShotSecond = 0

local getCVar = (C_CVar and C_CVar.GetCVar) or GetCVar
local setCVar = (C_CVar and C_CVar.SetCVar) or SetCVar

-- JPEG while the grid is captured: much faster for the game to write than PNG. screenshotQuality
-- 10 is the game's best quality (about JPEG 100, ~4 MB at 2568x1360); 3 is its default (about
-- JPEG 81, ~0.7 MB), and the black and white 2 px cells read back cleanly far below that (tested
-- down to JPEG 20). TGA can't be read by the app. The player's own settings come back right after.
local function useGridShotSettings()
  savedCVars = { format = getCVar("screenshotFormat"), quality = getCVar("screenshotQuality") }
  pcall(setCVar, "screenshotFormat", "jpeg")
  pcall(setCVar, "screenshotQuality", "3")
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

-- The last few shots and how they went (kind, ok, in combat, retried), for /wct debug.
local function logShot(item, ok)
  local log = WoWCharacterTrackerDB.shotLog or {}
  WoWCharacterTrackerDB.shotLog = log
  table.insert(log, string.format("%s %s %s%s%s", date("%H:%M:%S"), item.bits and "grid" or (item.kind or "event"),
    ok and "ok" or "FAILED", item.inCombat and " in combat" or "", item.retried and " (retry)" or ""))
  while #log > 20 do table.remove(log, 1) end
end

-- ok = false when the game said the screenshot failed or never answered. An event's own picture
-- that failed in combat is tried once more, first thing after combat (some clients may refuse
-- screenshots mid-fight); the app waits for it.
function ns.shotDone(ok)
  if not shotBusy then return end
  lastShotSecond = math.max(lastShotSecond, time())
  local item = shotItem
  if item and item.bits then
    if grid then grid:Hide() end
    restoreShotSettings()
  else
    showChat()
  end
  if item then
    logShot(item, ok ~= false)
    if ok == false and not item.bits and not item.retried then
      item.retried, item.afterCombat, item.delay = true, true, 0.3
      table.insert(shotQueue, 1, item)
    end
  end
  shotItem = nil
  shotBusy = false
  C_Timer.After(0.1, ns.processShots)
end

-- In combat only normal screenshots are taken (the moment itself); data grids wait until combat
-- ends, so screenshot settings are never changed mid-fight. A retried event shot waits too.
local function nextShot()
  if not InCombatLockdown() then return table.remove(shotQueue, 1) end
  for i, item in ipairs(shotQueue) do
    if not item.bits and not item.afterCombat then return table.remove(shotQueue, i) end
  end
  return nil
end

ns.processShots = function()
  if shotBusy or #shotQueue == 0 then return end
  if time() <= lastShotSecond then
    C_Timer.After(0.25, ns.processShots)
    return
  end
  local item = nextShot()
  if not item then return end -- only grids left, PLAYER_REGEN_ENABLED picks them up
  shotItem = item
  shotBusy = true
  item.inCombat = InCombatLockdown() and true or nil
  -- Never wait forever on a screenshot event that doesn't come (that counts as failed).
  C_Timer.After(5 + (item.delay or 0), function() if shotItem == item then ns.shotDone(false) end end)

  if item.bits then
    ns.doing("data grid screenshot")
    drawGrid(item.bits)
    showPortrait(item.portrait)
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
  -- Without the app, nobody reads the grid and its screenshots would just pile up.
  if not ns.hasApp() or WoWCharacterTrackerDB.live == false or type(Screenshot) ~= "function" then return end
  table.insert(shotQueue, { bits = toBits(encode(e)), portrait = e.kind == "snapshot" })
  ns.processShots()
end

-- How long to wait before a normal screenshot, so the moment is on screen (the ding glow, the
-- boss corpse, the death screen).
local SHOT_DELAY = { level = 1.0, boss = 1.5, keyrun = 2.0, loot = 0.3, mount = 0.5, death = 0.2 }

-- Will the app post this event? Settings.lua carries the app's "What gets posted" choices, so
-- the addon only sends events live (and screenshots them) when they'll actually be posted.
-- Without those settings (an older app), everything counts as wanted.
local function wanted(e)
  local post = ns.setting("post")
  if not post then return true end
  if e.kind == "ask" then return true end -- the player clicked Post (Ask.lua)
  if post.followed and (e.kind == "loot" or e.kind == "mount") and ns.isFollowed(e.itemId) then return true end
  if e.kind == "loot" and (e.quality or 0) >= 5 then return not e.mat end -- legendary gear: always worth a post, materials never
  if e.maxLevel and post.maxLevel ~= false then return true end -- max level has its own switch
  if e.kind == "level" then
    local l = post.level
    if not l or not l.on then return false end
    local every = (e.level <= (l.split or 0)) and (l.before or 1) or (l.after or 1)
    return e.level % math.max(1, every) == 0
  elseif e.kind == "loot" then
    if (e.quality or 0) >= 4 then return post.epic and true or false end
    return (post.rare and (e.boe or not post.rareBoe)) and true or false -- rareBoe: Bind on Equip only
  elseif e.kind == "keyrun" then -- Mythic+ runs: from a key level, maybe only timed ones
    local k = post.keyrun
    if not k or not k.on or (e.keyLevel or 0) < (k.min or 0) then return false end
    return not k.timed or (e.upgrades or 0) > 0
  end
  if e.lost then return false end -- duels and Mak'gora: only wins are posted
  return post[e.kind] and true or false
end
ns.wanted = wanted -- the app's tests check it against FlavorSettings.Wants

local function wantsScreenshot(kind)
  local s = ns.setting("screenshot")
  return s and s[kind] and type(Screenshot) == "function"
end

-- Stores an event in SavedVariables and sends it live, so the app's level history and highlights
-- stay current while you play, whether it gets posted or not (before 0.21.2 loot that wouldn't be
-- posted waited for /reload). Only events that will be posted get a screenshot (if the app asked
-- for one).
function ns.addEvent(e)
  table.insert(WoWCharacterTrackerDB.events, e)
  if ns.announce then ns.announce(e) end -- guild/group chat (Announce.lua)
  local posted = wanted(e)
  local shot = posted and wantsScreenshot(e.kind)
  if shot then e.shot = true end
  ns.sendLive(e)
  if shot then
    table.insert(shotQueue, { delay = SHOT_DELAY[e.kind] or 0.5, kind = e.kind })
    ns.processShots()
  end
  if not posted and ns.offerAsk then ns.offerAsk(e) end -- not posted by itself: maybe on request
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
