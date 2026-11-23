-- The addon window's frame around the tabs: a bar of icons down the left side (one per tab, the
-- one open lit in the theme's accent) and a header over the page with its name. Window.lua builds
-- the window and its pages and calls ns.buildRail once; selecting a tab calls ns.railSelect.
local ADDON, ns = ...
local ui = ns.ui
local C = ui.COLOR

local RAIL = 68      -- the bar's width; Window.lua leaves this much room on the left
local BUTTON = 52    -- a tab's button, square
ns.RAIL_WIDTH = RAIL

-- Game icons that every client has (retail and classic alike).
local ICONS = {
  home = "Interface\\Icons\\INV_Misc_PocketWatch_01",
  characters = "Interface\\Icons\\INV_Helmet_01",
  friends = "Interface\\Icons\\Spell_Holy_PrayerOfHealing02",
  sharing = "Interface\\Icons\\INV_Letter_15",
  settings = "Interface\\Icons\\INV_Misc_Gear_01",
}
local QUIET = { 0.55, 0.55, 0.58, 1 } -- a tab's name while it's not open
local BADGE = { 0.78, 0.16, 0.14, 1 }

local buttons = {}   -- [key] = button
local labels = {}    -- [key] = what the header says
local header         -- the page's name in the header

local function paint(b, on)
  b.on = on
  b.bg:SetShown(on)
  b.stripe:SetShown(on)
  b.icon:SetDesaturated(not on)
  b.icon:SetAlpha(on and 1 or 0.6)
  ui.tint(b.caption, on and C.accent or QUIET)
end

local function enter(self)
  if not self.on then self.icon:SetAlpha(0.9) end
end
local function leave(self)
  if not self.on then self.icon:SetAlpha(0.6) end
end

local function newButton(rail, key, label, onClick)
  local b = CreateFrame("Button", nil, rail)
  b:SetSize(BUTTON, BUTTON)
  b.bg = b:CreateTexture(nil, "BACKGROUND")
  b.bg:SetAllPoints()
  ui.fill(b.bg, C.selected)
  b.stripe = b:CreateTexture(nil, "ARTWORK")
  b.stripe:SetSize(3, BUTTON - 16)
  b.stripe:SetPoint("RIGHT", b, "LEFT", -5, 0)
  ui.fill(b.stripe, C.accent)
  local hover = b:CreateTexture(nil, "HIGHLIGHT")
  ui.fill(hover, C.hover)
  hover:SetAllPoints()
  b.icon = b:CreateTexture(nil, "ARTWORK")
  b.icon:SetSize(24, 24)
  b.icon:SetPoint("TOP", b, "TOP", 0, -7)
  b.icon:SetTexture(ICONS[key] or "Interface\\Icons\\INV_Misc_QuestionMark")
  b.icon:SetTexCoord(0.08, 0.92, 0.08, 0.92)
  b.caption = ui.text(b, "GameFontNormalSmall", label)
  b.caption:SetJustifyH("CENTER")
  b.caption:SetPoint("BOTTOM", b, "BOTTOM", 0, 5)
  b.caption:SetWidth(BUTTON + 10)
  b.caption:SetWordWrap(false)
  b:SetScript("OnClick", onClick)
  b:SetScript("OnEnter", enter)
  b:SetScript("OnLeave", leave)
  paint(b, false)
  return b
end

-- The red count on the Friends button: friend requests waiting for an answer.
local function newBadge(b)
  local badge = CreateFrame("Frame", nil, b)
  badge:SetSize(16, 14)
  badge:SetPoint("TOPRIGHT", b, "TOPRIGHT", -6, -3)
  badge:SetFrameLevel((b:GetFrameLevel() or 1) + 2)
  local bg = badge:CreateTexture(nil, "BACKGROUND")
  bg:SetAllPoints()
  bg:SetColorTexture(BADGE[1], BADGE[2], BADGE[3], 1)
  badge.text = ui.text(badge, "GameFontHighlightSmall")
  badge.text:SetJustifyH("CENTER")
  badge.text:SetPoint("CENTER", 0, 0)
  badge:Hide()
  return badge
end

local function drawBadge(badge)
  local n = ns.pendingRequests and #ns.pendingRequests() or 0
  badge:SetShown(n > 0)
  if n > 0 then
    badge.text:SetText(n > 9 and "9+" or tostring(n))
    badge:SetWidth(math.max(16, (badge.text:GetStringWidth() or 8) + 8))
  end
end

-- Builds the bar and the header on window. tabs: Window.lua's TABS ({ key, label }); select(key)
-- opens one. titleBar: the header's height. Returns the header's title, for the version beside it.
function ns.buildRail(window, tabs, select, titleBar)
  local rail = CreateFrame("Frame", nil, window)
  rail:SetPoint("TOPLEFT", 1, -1)
  rail:SetPoint("BOTTOMLEFT", 1, 1)
  rail:SetWidth(RAIL)
  ui.box(rail, C.bar)
  local edge = rail:CreateTexture(nil, "BORDER")
  ui.fill(edge, C.border)
  edge:SetWidth(1)
  edge:SetPoint("TOPRIGHT")
  edge:SetPoint("BOTTOMRIGHT")

  local logo = rail:CreateTexture(nil, "ARTWORK")
  logo:SetSize(34, 34)
  logo:SetPoint("TOP", rail, "TOP", 0, -12)
  logo:SetTexture(ns.ICON) -- Minimap.lua's

  local last
  for _, t in ipairs(tabs) do
    local key = t.key
    labels[key] = t.label
    local b = newButton(rail, key, t.short or t.label, function() select(key) end)
    if key == "settings" then
      b:SetPoint("BOTTOM", rail, "BOTTOM", 0, 26) -- apart, at the bottom (above the resize grip's row)
    else
      b:SetPoint("TOP", last or logo, "BOTTOM", 0, last and -6 or -16)
      last = b
    end
    buttons[key] = b
  end
  if buttons.friends then
    local badge = newBadge(buttons.friends)
    local waited = 1
    rail:SetScript("OnUpdate", function(_, elapsed)
      waited = waited + elapsed
      if waited < 1 then return end
      waited = 0
      drawBadge(badge)
    end)
  end

  -- The header over the page: its name, then (Window.lua) the version; a line under it.
  local line = window:CreateTexture(nil, "BORDER")
  ui.fill(line, C.border)
  line:SetHeight(1)
  line:SetPoint("TOPLEFT", window, "TOPLEFT", RAIL + 1, -titleBar)
  line:SetPoint("TOPRIGHT", window, "TOPRIGHT", -1, -titleBar)
  local head = CreateFrame("Frame", nil, window)
  head:SetPoint("TOPLEFT", window, "TOPLEFT", RAIL + 1, 0)
  head:SetPoint("TOPRIGHT", window, "TOPRIGHT", -40, 0)
  head:SetHeight(titleBar)
  header = ui.text(head, "GameFontHighlightLarge", "")
  header:SetPoint("LEFT", head, "LEFT", 17, 0)
  return header
end

-- key's tab is open now: its button lit, its name in the header.
function ns.railSelect(key)
  for k, b in pairs(buttons) do paint(b, k == key) end
  if header then header:SetText(labels[key] or "") end
end
