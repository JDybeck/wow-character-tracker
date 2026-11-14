-- The minimap button: our icon on the minimap's edge. A click opens or closes the addon window,
-- dragging moves it around the edge. Where it sits (`angle`, degrees, 0 = right, 90 = up) and
-- whether it's hidden (Settings tab) are kept in WoWCharacterTrackerDB.minimap.
local ADDON, ns = ...

local ICON = "Interface\\AddOns\\WoWCharacterTracker\\Icon"
ns.ICON = ICON -- the window's title shows it too
local BORDER = "Interface\\Minimap\\MiniMap-TrackingBorder" -- the game's own ring, as on its tracking button
local HIGHLIGHT = "Interface\\Minimap\\UI-Minimap-ZoomButton-Highlight"
local DEFAULT_ANGLE = 225 -- bottom left
local button

local function state()
  WoWCharacterTrackerDB.minimap = WoWCharacterTrackerDB.minimap or {}
  return WoWCharacterTrackerDB.minimap
end

local function place()
  local angle = math.rad(tonumber(state().angle) or DEFAULT_ANGLE)
  local radius = Minimap:GetWidth() / 2 + 10
  button:ClearAllPoints()
  button:SetPoint("CENTER", Minimap, "CENTER", math.cos(angle) * radius, math.sin(angle) * radius)
end

-- From the minimap's middle to the cursor. The cursor comes in screen pixels, the middle in the
-- minimap's own scale.
local function cursorAngle()
  local mx, my = Minimap:GetCenter()
  local scale = Minimap:GetEffectiveScale()
  local cx, cy = GetCursorPosition()
  return math.deg(math.atan2(cy / scale - my, cx / scale - mx)) % 360
end

local function followCursor()
  state().angle = cursorAngle()
  place()
end

function ns.createMinimapButton()
  if button then return button end
  button = CreateFrame("Button", "WoWCharacterTrackerMinimapButton", Minimap)
  button:SetSize(31, 31)
  button:SetFrameStrata("MEDIUM")
  button:SetFrameLevel(8)
  button:RegisterForClicks("LeftButtonUp")
  button:RegisterForDrag("LeftButton")
  button:SetHighlightTexture(HIGHLIGHT)

  local icon = button:CreateTexture(nil, "BACKGROUND")
  icon:SetSize(20, 20)
  icon:SetPoint("TOPLEFT", 6, -6)
  icon:SetTexture(ICON)
  local border = button:CreateTexture(nil, "OVERLAY")
  border:SetSize(53, 53)
  border:SetPoint("TOPLEFT")
  border:SetTexture(BORDER)

  button:SetScript("OnClick", function()
    if ns.toggleWindow then ns.toggleWindow() end
  end)
  button:SetScript("OnDragStart", function(self)
    self:LockHighlight()
    self:SetScript("OnUpdate", followCursor)
  end)
  button:SetScript("OnDragStop", function(self)
    self:SetScript("OnUpdate", nil)
    self:UnlockHighlight()
  end)
  button:SetScript("OnEnter", function(self)
    GameTooltip:SetOwner(self, "ANCHOR_LEFT")
    GameTooltip:SetText("WoW Character Tracker")
    GameTooltip:AddLine("Click to open. Drag to move.", 1, 1, 1)
    GameTooltip:Show()
  end)
  button:SetScript("OnLeave", function() GameTooltip:Hide() end)

  place()
  button:SetShown(not state().hidden)
  return button
end

function ns.minimapButtonShown() return not state().hidden end

function ns.setMinimapButtonShown(on)
  state().hidden = not on
  if button then button:SetShown(on)
  elseif on then ns.createMinimapButton() end
end
