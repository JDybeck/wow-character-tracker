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
  b.caption:SetWidth(RAIL - 2)
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

-- While Incognito is on: "Incognito until 21:30" at the header's right, on every tab (it's easy to
-- forget otherwise). A click ends it.
local function drawIncognitoChip(chip)
  local w = ns.incognitoActive and ns.incognitoActive()
  chip:SetShown(w ~= nil)
  if not w then return end
  chip:SetText("Incognito until " .. date("%H:%M", w["until"]))
  local fs = chip:GetFontString()
  chip:SetWidth(math.max(120, ((fs and fs:GetStringWidth()) or 120) + 20))
end

local function newIncognitoChip(head)
  local chip
  chip = ui.button(head, "", 150, function()
    if ns.goIncognito then ns.goIncognito(nil) end
    drawIncognitoChip(chip)
    if ns.refreshWindow then ns.refreshWindow() end -- the Sharing tab's own Incognito buttons
  end, "selected")
  chip:SetPoint("RIGHT", head, "RIGHT", -8, 0)
  chip:SetScript("OnEnter", function(self)
    if not GameTooltip then return end
    GameTooltip:SetOwner(self, "ANCHOR_BOTTOM")
    GameTooltip:SetText("Nothing is posted or said in chat. Click to end it now.", 1, 1, 1, 1, true)
    GameTooltip:Show()
  end)
  chip:SetScript("OnLeave", function() if GameTooltip then GameTooltip:Hide() end end)
  chip:Hide()
  return chip
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
  local badge = buttons.friends and newBadge(buttons.friends)
  local chip -- built with the header, below
  local waited = 1
  rail:SetScript("OnUpdate", function(_, elapsed)
    waited = waited + elapsed
    if waited < 1 then return end
    waited = 0
    if badge then drawBadge(badge) end
    if chip then drawIncognitoChip(chip) end
  end)

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
  chip = newIncognitoChip(head)
  drawIncognitoChip(chip)
  ns.drawIncognitoChip = function() drawIncognitoChip(chip) end -- the Sharing tab's buttons
  ns.fadeWhenAway(window)
  return header
end

-- The window and the panels beside it (details, highlights, sharing, recipes, reputations) fade a
-- little together while the mouse is away from all of them, and are solid again under the mouse,
-- while a mouse button is held (dragging, resizing) or while typing in one. Each gets its alpha set
-- itself (the side panels ignore the window's), so none is left solid.
local AWAY_ALPHA, FADE_SPEED = 0.9, 4 -- alpha per second; 0.9 keeps the grey hints readable (Ui.lua HINT_GREY)
local SIDE_PANELS = { "WoWCharacterTrackerDetailsPanel", "WoWCharacterTrackerHighlightsPanel", "WoWCharacterTrackerSharingPanel",
  "WoWCharacterTrackerRecipes", "WoWCharacterTrackerReputations" }
local function inUse(window)
  if window:IsShown() and window:IsMouseOver() then return true end
  for _, name in ipairs(SIDE_PANELS) do
    local p = _G[name]
    if p and p:IsShown() and p:IsMouseOver() then return true end
  end
  if IsMouseButtonDown and (IsMouseButtonDown("LeftButton") or IsMouseButtonDown("RightButton")) and window.alpha == 1 then return true end
  local focus = GetCurrentKeyBoardFocus and GetCurrentKeyBoardFocus()
  while focus do
    if focus == window then return true end
    for _, name in ipairs(SIDE_PANELS) do if focus == _G[name] then return true end end
    focus = focus.GetParent and focus:GetParent()
  end
  return false
end

local function differs(f, alpha) return math.abs((f:GetAlpha() or -1) - alpha) > 0.005 end
local function paintAlpha(window, alpha)
  if differs(window, alpha) then window:SetAlpha(alpha) end
  for _, name in ipairs(SIDE_PANELS) do
    local p = _G[name]
    if p and p:IsShown() and differs(p, alpha) then
      if p.SetIgnoreParentAlpha then p:SetIgnoreParentAlpha(true) end
      p:SetAlpha(alpha)
    end
  end
end

function ns.fadeWhenAway(window)
  window.alpha = 1
  -- On UIParent, so the recipes and reputations windows fade even with the main window closed.
  local ticker = CreateFrame("Frame", nil, UIParent)
  window.fadeTicker = ticker
  ticker:SetScript("OnUpdate", function(_, elapsed)
    local want = inUse(window) and 1 or AWAY_ALPHA
    if window.alpha == want then paintAlpha(window, want) return end -- a panel opened since
    local step = (elapsed or 0) * FADE_SPEED * (1 - AWAY_ALPHA)
    if window.alpha < want then window.alpha = math.min(want, window.alpha + step)
    else window.alpha = math.max(want, window.alpha - step) end
    paintAlpha(window, window.alpha)
  end)
end

-- key's tab is open now: its button lit, its name in the header.
function ns.railSelect(key)
  for k, b in pairs(buttons) do paint(b, k == key) end
  if header then header:SetText(labels[key] or "") end
end
