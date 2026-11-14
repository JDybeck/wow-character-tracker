-- Friends' pins on the world map: drawing only, FriendMap.lua knows the positions. Our own data
-- provider and pin template on WorldMapFrame, nothing of Blizzard's replaced. The map's AcquirePin
-- sets right-click pass-through on every pin (SetPassThroughButtons, a known taint source), so our
-- pins skip that and take mouse-over only: clicks reach the map on their own.
local ADDON, ns = ...

local TEMPLATE = "WoWCharacterTrackerFriendPinTemplate"
local SIZE = 18
local RING = 2    -- the class colored ring around the icon
local REFRESH = 5 -- seconds between redraws while the map is open, so pins fade and go
local CIRCLE = "Interface\\CharacterFrame\\TempPortraitAlphaMask"
local WHITE = "Interface\\Buttons\\WHITE8X8"

local provider
local redrawing = false

-- Where a pin falls on the shown map: x, y in 0..1, or nil. Through world coordinates for the maps
-- above the friend's (continent, world); never on dungeon maps.
function ns.pinSpot(pin, shownMapID)
  if pin.mapID == shownMapID then return pin.x, pin.y end
  if not (C_Map and C_Map.GetMapInfo and C_Map.GetWorldPosFromMapPos and C_Map.GetMapPosFromWorldPos and CreateVector2D) then return nil end
  local info = C_Map.GetMapInfo(shownMapID)
  local dungeon = (Enum and Enum.UIMapType and Enum.UIMapType.Dungeon) or 4
  if not info or (info.mapType or dungeon) >= dungeon then return nil end
  local continentID, world = C_Map.GetWorldPosFromMapPos(pin.mapID, CreateVector2D(pin.x, pin.y))
  if not continentID or not world then return nil end
  local _, spot = C_Map.GetMapPosFromWorldPos(continentID, world, shownMapID)
  if not spot or spot.x < 0 or spot.x > 1 or spot.y < 0 or spot.y > 1 then return nil end
  return spot.x, spot.y
end

local function classColor(file)
  local c = file and RAID_CLASS_COLORS and RAID_CLASS_COLORS[file]
  if c then return c.r, c.g, c.b end
  return 0.8, 0.8, 0.8
end

-- The game's round class icon ("classicon-warrior"), or nil on a client without that atlas.
function ns.pinIcon(classFile)
  if type(classFile) ~= "string" or not (GetClassAtlas and C_Texture and C_Texture.GetAtlasInfo) then return nil end
  local atlas = GetClassAtlas(classFile:lower())
  if atlas and C_Texture.GetAtlasInfo(atlas) then return atlas end
  return nil
end

-- A round texture inset from the pin's edges, in one color.
local function circle(pin, layer, inset, r, g, b)
  local t = pin:CreateTexture(nil, layer)
  t:SetTexture(WHITE)
  t:SetVertexColor(r, g, b)
  t:SetPoint("TOPLEFT", inset, -inset)
  t:SetPoint("BOTTOMRIGHT", -inset, inset)
  local mask = pin:CreateMaskTexture()
  mask:SetTexture(CIRCLE, "CLAMPTOBLACKADDITIVE", "CLAMPTOBLACKADDITIVE")
  mask:SetAllPoints(t)
  t:AddMaskTexture(mask)
  return t
end

-- The tooltip of a pin (a row of ns.friendPins), as { text, r, g, b } lines after the label: the
-- character (with what they share: spec, item level, XP, professions), the zone, another layer or
-- realm, and how old the position is.
function ns.pinLines(f, zone)
  local c = f.shared or {}
  local lines = {}
  local function add(text, r, g, b) lines[#lines + 1] = { text, r, g, b } end
  local className = c.class or (LOCALIZED_CLASS_NAMES_MALE and f.classFile and LOCALIZED_CLASS_NAMES_MALE[f.classFile])
  local level = f.level or c.level
  local r, g, b = classColor(f.classFile)
  local name = c.char and ns.fullName(c) or f.character or "?"
  local what = ""
  for _, part in ipairs({ c.spec, className }) do
    if part and part ~= "" then what = what .. " " .. part end
  end
  add(string.format("%s, level %s%s", name, tostring(level or "?"), what), r, g, b)
  if zone then add(zone, 0.8, 0.8, 0.8) end
  local facts = {}
  if c.ilvl then facts[#facts + 1] = "Item level " .. math.floor(c.ilvl) end
  local percent = ns.levelPercent(c, level) -- nil when what they shared is from another level
  if percent then facts[#facts + 1] = string.format("%d%% into level %d", percent, level) end
  if #facts > 0 and c.at then facts[#facts + 1] = "updated " .. ns.ago(c.at) end
  if #facts > 0 then add(table.concat(facts, "  ·  "), 0.9, 0.9, 0.9) end
  if c.professions and c.professions ~= "" then add(c.professions, 0.9, 0.9, 0.9) end
  -- Orange where you can't meet them, green on your own layer.
  if f.realmNote then
    if f.otherRealm then add(f.realmNote, 1, 0.6, 0.2) else add(f.realmNote, 0.45, 0.85, 0.5) end
  end
  local ago = ns.ago(time() - (f.age or 0))
  add(f.faded and ("Last seen " .. ago) or ago, 0.6, 0.6, 0.6)
  return lines
end

-- Filled in once the map's own mixins exist (attach); the XML template names it.
WoWCharacterTrackerFriendPinMixin = {}
local Pin = WoWCharacterTrackerFriendPinMixin

local function buildPin()
  Mixin(Pin, MapCanvasPinMixin)

  function Pin:OnLoad()
    self:SetSize(SIZE, SIZE)
    self:UseFrameLevelType("PIN_FRAME_LEVEL_GROUP_MEMBER")
    self:SetScalingLimits(1, 1.0, 1.2)
    self.ring = circle(self, "BACKGROUND", 0, 1, 1, 1)
    self.icon = circle(self, "ARTWORK", RING, 1, 1, 1)
  end

  -- Class icon in a class colored ring (grey: another realm or layer); a class colored dot without the atlas.
  function Pin:OnAcquired(friend, x, y)
    self.friend = friend
    self:SetPosition(x, y)
    local r, g, b = classColor(friend.classFile)
    if friend.otherRealm then self.ring:SetVertexColor(0.5, 0.5, 0.5) else self.ring:SetVertexColor(r, g, b) end
    local atlas = ns.pinIcon(friend.classFile)
    if atlas then
      self.icon:SetAtlas(atlas)
      self.icon:SetVertexColor(1, 1, 1)
    else
      self.icon:SetTexture(WHITE)
      self.icon:SetTexCoord(0, 1, 0, 1) -- a reused pin may still have the atlas's
      self.icon:SetVertexColor(r, g, b)
    end
    self:SetAlpha(friend.faded and 0.45 or 1)
  end

  -- Blizzard's sets pass-through buttons (see the top); ours has no clicks to pass.
  function Pin:CheckMouseButtonPassthrough() end

  function Pin:OnMouseEnter()
    local f = self.friend
    if not (f and GameTooltip) then return end
    GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
    GameTooltip:SetText(f.label, 1, 1, 1)
    local info = C_Map.GetMapInfo and C_Map.GetMapInfo(f.mapID)
    for _, line in ipairs(ns.pinLines(f, info and info.name)) do
      GameTooltip:AddLine(line[1], line[2], line[3], line[4])
    end
    GameTooltip:Show()
  end

  function Pin:OnMouseLeave()
    if GameTooltip then GameTooltip:Hide() end
  end
end

local function buildProvider()
  local p = CreateFromMixins(MapCanvasDataProviderMixin)
  function p:RemoveAllData() self:GetMap():RemoveAllPinsByTemplate(TEMPLATE) end
  function p:RefreshAllData()
    self:RemoveAllData()
    local map = self:GetMap()
    local shown = map and map:GetMapID()
    if not shown then return end
    for _, friend in ipairs(ns.friendPins and ns.friendPins() or {}) do
      local x, y = ns.pinSpot(friend, shown)
      if x then map:AcquirePin(TEMPLATE, friend, x, y) end
    end
  end
  return p
end

local function redraw()
  if not (provider and WorldMapFrame:IsShown()) then
    redrawing = false
    return
  end
  provider:RefreshAllData()
  C_Timer.After(REFRESH, redraw)
end

local function startRedrawing()
  if redrawing then return end
  redrawing = true
  C_Timer.After(REFRESH, redraw)
end

-- FriendMap.lua: a position came or went.
function ns.friendPinsChanged()
  if provider and WorldMapFrame and WorldMapFrame:IsShown() then provider:RefreshAllData() end
end

-- Adds the pins to the world map once it exists (PLAYER_LOGIN, or when Blizzard_WorldMap loads).
function ns.attachMapPins()
  if provider or not (WorldMapFrame and WorldMapFrame.AddDataProvider and MapCanvasDataProviderMixin and MapCanvasPinMixin
    and Mixin and CreateFromMixins) then return false end
  buildPin()
  provider = buildProvider()
  WorldMapFrame:AddDataProvider(provider)
  WorldMapFrame:HookScript("OnShow", function()
    ns.setMapOpen(true)
    startRedrawing()
  end)
  WorldMapFrame:HookScript("OnHide", function() ns.setMapOpen(false) end)
  if WorldMapFrame:IsShown() then ns.setMapOpen(true) end
  return true
end
