-- The reputation window: one character's factions, like the game's reputation list. Each row has the
-- faction, its standing (renown level, or a friendship's rank) in the game's standing colors and a bar
-- for how far through it the character is. Renown first, then the highest standings, then by name.
-- Opened from a character's unfolded row (Window.lua) and /wct rep. The factions come from the
-- character's saved `reputations` text (Reputation.lua).
local ADDON, ns = ...

local NAME = "WoWCharacterTrackerReputations"
local WIDTH, HEIGHT, ROW = 440, 480, 22
local BAR_WIDTH = 150

local win
local rows = {}

-- The game's standing colors (FACTION_BAR_COLORS) where it has them, else the same values.
local COLORS = { { 0.8, 0.13, 0.13 }, { 0.8, 0.13, 0.13 }, { 0.75, 0.27, 0 }, { 0.9, 0.7, 0 },
  { 0, 0.6, 0.1 }, { 0, 0.6, 0.1 }, { 0, 0.6, 0.1 }, { 0, 0.6, 0.1 } }
local RENOWN_COLOR = { 0, 0.6, 0.9 }

local function standingColor(r)
  if r.renown or r.label then return RENOWN_COLOR[1], RENOWN_COLOR[2], RENOWN_COLOR[3] end
  local c = FACTION_BAR_COLORS and FACTION_BAR_COLORS[r.standing]
  if type(c) == "table" and c.r then return c.r, c.g, c.b end
  local d = COLORS[r.standing] or COLORS[4]
  return d[1], d[2], d[3]
end

-- "Revered", "Renown 12" or "Best Friend".
function ns.reputationStandingText(r)
  if r.renown then return string.format("Renown %d", r.renown) end
  if r.label and r.label ~= "" then return r.label end
  return ns.standingName(r.standing)
end

-- The progress shown on the bar: "2400 / 21000", nothing at the top (Exalted, top renown).
local function progressText(r)
  local top = (r.renown and r.renownMax and r.renown >= r.renownMax) or (not r.renown and not r.label and r.standing >= 8)
  if top or not (r.cur and r.max and r.max > 0) then return "" end
  return string.format("%d / %d", r.cur, r.max)
end

-- Renown first, then the standing, highest first, then the name.
local function sorted(list)
  table.sort(list, function(a, b)
    if (a.renown ~= nil) ~= (b.renown ~= nil) then return a.renown ~= nil end
    if a.renown and b.renown and a.renown ~= b.renown then return a.renown > b.renown end
    if a.standing ~= b.standing then return a.standing > b.standing end
    return a.name < b.name
  end)
  return list
end
ns.sortedReputations = sorted -- the tests check the order

-- How many factions a character has saved (0 without any), for the button that opens them.
function ns.reputationCount(c)
  return #ns.parseReputations(c and c.reputations)
end

local function newRow(parent)
  local ui = ns.ui
  local r = CreateFrame("Frame", nil, parent)
  r:SetSize(WIDTH - 52, ROW)
  r.name = ui.text(r, "GameFontHighlightSmall")
  r.name:SetPoint("LEFT", r, "LEFT", 0, 0)
  r.name:SetWidth(WIDTH - 52 - BAR_WIDTH - 12)
  r.name:SetWordWrap(false)
  r.bar = CreateFrame("StatusBar", nil, r)
  r.bar:SetSize(BAR_WIDTH, ROW - 6)
  r.bar:SetPoint("RIGHT", r, "RIGHT", 0, 0)
  r.bar:SetStatusBarTexture("Interface\\TargetingFrame\\UI-StatusBar")
  r.bar:SetMinMaxValues(0, 1)
  r.barBg = r.bar:CreateTexture(nil, "BACKGROUND")
  r.barBg:SetAllPoints()
  ui.fill(r.barBg, ui.COLOR.bar)
  r.standing = ui.text(r.bar, "GameFontHighlightSmall")
  r.standing:SetPoint("LEFT", r.bar, "LEFT", 4, 0)
  r.progress = ui.text(r.bar, "GameFontHighlightSmall")
  r.progress:SetPoint("RIGHT", r.bar, "RIGHT", -4, 0)
  return r
end

local function build()
  local ui, C = ns.ui, ns.ui.COLOR
  win = CreateFrame("Frame", NAME, UIParent)
  win:SetSize(WIDTH, HEIGHT)
  win:SetPoint("CENTER")
  win:SetFrameStrata("DIALOG")
  win:SetToplevel(true)
  win:SetClampedToScreen(true)
  win:EnableMouse(true)
  win:SetMovable(true)
  win:RegisterForDrag("LeftButton")
  win:SetScript("OnDragStart", win.StartMoving)
  win:SetScript("OnDragStop", win.StopMovingOrSizing)
  if UISpecialFrames then table.insert(UISpecialFrames, NAME) end
  ui.box(win, C.window, C.border)

  local close = CreateFrame("Button", nil, win)
  close:SetSize(28, 28)
  close:SetPoint("TOPRIGHT", -4, -4)
  close:SetNormalTexture("Interface\\Buttons\\UI-Panel-MinimizeButton-Up")
  close:SetPushedTexture("Interface\\Buttons\\UI-Panel-MinimizeButton-Down")
  close:SetHighlightTexture("Interface\\Buttons\\UI-Panel-MinimizeButton-Highlight", "ADD")
  close:SetScript("OnClick", function() win:Hide() end)

  win.title = ui.text(win, "GameFontHighlightLarge")
  win.title:SetPoint("TOPLEFT", 14, -14)
  win.title:SetWidth(WIDTH - 50)
  win.sub = ui.text(win, "GameFontDisableSmall")
  win.sub:SetPoint("TOPLEFT", win.title, "BOTTOMLEFT", 0, -4)
  win.sub:SetWidth(WIDTH - 28)
  win.scroll = ns.ui.scrollFrame(win)
  win.scroll:SetPoint("TOPLEFT", 14, -60)
  win.scroll:SetPoint("BOTTOMRIGHT", -30, 12)
  win.list = CreateFrame("Frame", nil, win.scroll)
  win.list:SetSize(WIDTH - 52, 1)
  win.scroll:SetScrollChild(win.list)
  win.none = ui.text(win, "GameFontDisableSmall",
    "None saved yet. They're read a few seconds after logging in and when your reputation changes.")
  win.none:SetPoint("TOPLEFT", 14, -62)
  win.none:SetWidth(WIDTH - 28)
end

-- Shows c's factions (c: a saved snapshot with `reputations`).
function ns.openReputations(c)
  if not win then build() end
  local list = sorted(ns.parseReputations(c and c.reputations))
  win.title:SetText((ns.fullName and c and ns.fullName(c)) or (c and c.char) or "Reputations")
  if c and c.classFile and ns.ui.classRgb then win.title:SetTextColor(ns.ui.classRgb(c.classFile)) end
  win.sub:SetText(#list == 1 and "1 faction" or string.format("%d factions, highest first", #list))
  win.none:SetShown(#list == 0)
  for i, r in ipairs(list) do
    local row = rows[i] or newRow(win.list)
    rows[i] = row
    row.name:SetText(r.name)
    row.standing:SetText(ns.reputationStandingText(r))
    row.progress:SetText(progressText(r))
    row.bar:SetStatusBarColor(standingColor(r))
    local full = progressText(r) == ""
    row.bar:SetValue(full and 1 or math.max(0, math.min(1, (r.cur or 0) / r.max)))
    row:ClearAllPoints()
    row:SetPoint("TOPLEFT", win.list, "TOPLEFT", 0, -(i - 1) * ROW)
    row:Show()
  end
  for i = #list + 1, #rows do rows[i]:Hide() end
  win.list:SetHeight(math.max(1, #list * ROW))
  ns.ui.scrollTo(win.scroll, 0)
  win:Show()
  win:Raise()
end

-- /wct rep: the character you're on, read fresh.
function ns.openOwnReputations()
  ns.checkReputations()
  local guid = UnitGUID("player")
  local saved = guid and WoWCharacterTrackerDB and WoWCharacterTrackerDB.chars and WoWCharacterTrackerDB.chars[guid]
  local c = saved and ns.copy(saved) or { char = UnitName and UnitName("player") }
  c.reputations = ns.reputationText() or c.reputations
  ns.openReputations(c)
end
