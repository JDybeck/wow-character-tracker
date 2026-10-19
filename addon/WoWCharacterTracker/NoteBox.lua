-- /wct note: the character's note from the app (its Notes tab), in a box on screen.
-- The app writes the notes into Settings.lua (`notes`, by GUID) with the box's background
-- opacity (`noteAlpha`, 0-1), so a new note shows after a /reload. Where the box sits, whether
-- it's locked and whether it's open are kept in WoWCharacterTrackerDB.noteBox.
local ADDON, ns = ...

local WIDTH, PAD = 280, 10
local box

local function state()
  WoWCharacterTrackerDB.noteBox = WoWCharacterTrackerDB.noteBox or {}
  return WoWCharacterTrackerDB.noteBox
end

local function noteText()
  local notes = ns.setting("notes")
  local text = notes and notes[UnitGUID("player")]
  if text and text ~= "" then return text end
  return "No note for this character yet. Write one on the character's Notes tab in the Character Tracker app, then /reload."
end

local function savePosition()
  local point, _, relPoint, x, y = box:GetPoint()
  local s = state()
  s.point, s.relPoint, s.x, s.y = point, relPoint, x, y
end

-- Locked: the box can't be dragged and clicks go through it to the game (only its buttons take them).
local function applyLock()
  local locked = state().locked == true
  box:EnableMouse(not locked)
  box.lock:SetText(locked and "Unlock" or "Lock")
end

local function fit()
  box.text:SetText(noteText())
  box:SetHeight(box.text:GetStringHeight() + PAD * 2 + 22)
end

local function build()
  box = CreateFrame("Frame", "WoWCharacterTrackerNoteBox", UIParent)
  box:SetWidth(WIDTH)
  box:SetFrameStrata("MEDIUM")
  box:SetClampedToScreen(true)
  box:SetMovable(true)
  box:RegisterForDrag("LeftButton")
  box:SetScript("OnDragStart", function(self) if not state().locked then self:StartMoving() end end)
  box:SetScript("OnDragStop", function(self) self:StopMovingOrSizing(); savePosition() end)

  local alpha = tonumber(ns.setting("noteAlpha")) or 0.6
  box.bg = box:CreateTexture(nil, "BACKGROUND")
  box.bg:SetAllPoints()
  box.bg:SetColorTexture(0, 0, 0, alpha)

  local title = box:CreateFontString(nil, "OVERLAY", "GameFontNormal")
  title:SetPoint("TOPLEFT", PAD, -PAD + 2)
  title:SetText("Notes: " .. (UnitName("player") or ""))

  local close = CreateFrame("Button", nil, box, "UIPanelCloseButton")
  close:SetSize(22, 22)
  close:SetPoint("TOPRIGHT", 2, 2)
  close:SetScript("OnClick", function() ns.toggleNoteBox(false) end)

  box.lock = CreateFrame("Button", nil, box)
  box.lock:SetSize(44, 16)
  box.lock:SetPoint("RIGHT", close, "LEFT", -2, 0)
  box.lock:SetNormalFontObject("GameFontHighlightSmall")
  box.lock:SetHighlightFontObject("GameFontNormalSmall")
  box.lock:SetScript("OnClick", function() ns.lockNoteBox(not state().locked) end)

  box.text = box:CreateFontString(nil, "OVERLAY", "GameFontHighlight")
  box.text:SetPoint("TOPLEFT", PAD, -PAD - 18)
  box.text:SetWidth(WIDTH - PAD * 2)
  box.text:SetJustifyH("LEFT")
  box.text:SetJustifyV("TOP")
  if box.text.SetWordWrap then box.text:SetWordWrap(true) end

  local s = state()
  box:ClearAllPoints()
  if s.point then box:SetPoint(s.point, UIParent, s.relPoint or s.point, s.x or 0, s.y or 0)
  else box:SetPoint("CENTER", UIParent, "CENTER", 0, 120) end
end

-- show: true/false, or nil to flip.
function ns.toggleNoteBox(show)
  if show == nil then show = not (box and box:IsShown()) end
  state().shown = show
  if not show then
    if box then box:Hide() end
    return
  end
  if not box then build() end
  fit()
  applyLock()
  box:Show()
end

function ns.lockNoteBox(locked)
  state().locked = locked
  if box then applyLock() end
  ns.say(locked and "Note box locked. /wct note unlock to move it again." or "Note box unlocked: drag it to move it.")
end

-- At login: open again if it was open when the game last saved.
function ns.restoreNoteBox()
  if state().shown then ns.toggleNoteBox(true) end
end
