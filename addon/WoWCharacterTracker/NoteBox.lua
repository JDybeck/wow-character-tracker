-- /wct note: the character's note in a box on screen, editable in game.
-- The note lives in two places, and the newer one wins (`at`, unix time):
--   the app's: Settings.lua `notes[guid] = { text, at }`, written from the Notes tab (read on /reload)
--   the game's: WoWCharacterTrackerDB.notes[guid] = { text, at }, from Edit in the box (the app
--   reads it back when the game writes its saved file, on /reload or logout)
-- Texts are kept as the game shows them ("||" for a plain "|"). Where the box sits, whether it's
-- locked or open and its background opacity (`alpha`, 0-1, set in the game's AddOns options,
-- Options.lua) are kept in WoWCharacterTrackerDB.noteBox.
local ADDON, ns = ...

local WIDTH, PAD = 280, 10
local EMPTY = "No note for this character yet. Click Edit to write one, or use the character's Notes tab in the Character Tracker app."
local box

local function state()
  WoWCharacterTrackerDB.noteBox = WoWCharacterTrackerDB.noteBox or {}
  return WoWCharacterTrackerDB.noteBox
end

-- The newer of the app's and the game's note for this character.
function ns.currentNote()
  local guid = UnitGUID("player")
  local fromApp = (ns.setting("notes") or {})[guid]
  if type(fromApp) == "string" then fromApp = { text = fromApp, at = 0 } end -- an older app
  local fromGame = WoWCharacterTrackerDB.notes and WoWCharacterTrackerDB.notes[guid]
  if fromGame and (not fromApp or (fromGame.at or 0) > (fromApp.at or 0)) then return fromGame.text or "" end
  return fromApp and fromApp.text or ""
end

function ns.saveNote(text)
  WoWCharacterTrackerDB.notes = WoWCharacterTrackerDB.notes or {}
  WoWCharacterTrackerDB.notes[UnitGUID("player")] = { text = text, at = time() }
end

local function savePosition()
  local point, _, relPoint, x, y = box:GetPoint()
  local s = state()
  s.point, s.relPoint, s.x, s.y = point, relPoint, x, y
end

-- Locked: the box can't be dragged and clicks go through it to the game (only its buttons and
-- the edit field take them).
local function applyLock()
  local locked = state().locked == true
  box:EnableMouse(not locked)
  box.lock:SetText(locked and "Unlock" or "Lock")
end

local function fit()
  local h = box.editing and math.max(box.edit:GetHeight(), 60) or box.text:GetStringHeight()
  box:SetHeight(h + PAD * 2 + 22)
end

local function show()
  local note = ns.currentNote()
  box.text:SetText(note ~= "" and note or EMPTY)
  fit()
end

-- Edit swaps the text for an edit field; Save keeps it, Esc leaves it as it was.
local function setEditing(on)
  box.editing = on
  box.text:SetShown(not on)
  box.edit:SetShown(on)
  box.editBg:SetShown(on)
  box.editButton:SetText(on and "Save" or "Edit")
  if on then
    box.edit:SetText(ns.currentNote())
    box.edit:SetFocus()
  else
    box.edit:ClearFocus()
  end
  show()
end

local function smallButton(label)
  local b = CreateFrame("Button", nil, box)
  b:SetSize(40, 16)
  b:SetNormalFontObject("GameFontHighlightSmall")
  b:SetHighlightFontObject("GameFontNormalSmall")
  b:SetText(label)
  return b
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

  box.bg = box:CreateTexture(nil, "BACKGROUND")
  box.bg:SetAllPoints()
  box.bg:SetColorTexture(0, 0, 0, ns.noteAlpha())

  local title = box:CreateFontString(nil, "OVERLAY", "GameFontNormal")
  title:SetPoint("TOPLEFT", PAD, -PAD + 2)
  title:SetText("Notes: " .. (UnitName("player") or ""))

  local close = CreateFrame("Button", nil, box, "UIPanelCloseButton")
  close:SetSize(22, 22)
  close:SetPoint("TOPRIGHT", 2, 2)
  close:SetScript("OnClick", function() ns.toggleNoteBox(false) end)

  box.lock = smallButton("Lock")
  box.lock:SetPoint("RIGHT", close, "LEFT", -2, 0)
  box.lock:SetScript("OnClick", function() ns.lockNoteBox(not state().locked) end)

  box.editButton = smallButton("Edit")
  box.editButton:SetPoint("RIGHT", box.lock, "LEFT", -2, 0)
  box.editButton:SetScript("OnClick", function()
    if box.editing then
      ns.saveNote(box.edit:GetText())
      ns.say("Note saved. The app gets it on your next /reload or logout.")
    end
    setEditing(not box.editing)
  end)

  box.text = box:CreateFontString(nil, "OVERLAY", "GameFontHighlight")
  box.text:SetPoint("TOPLEFT", PAD, -PAD - 18)
  box.text:SetWidth(WIDTH - PAD * 2)
  box.text:SetJustifyH("LEFT")
  box.text:SetJustifyV("TOP")
  if box.text.SetWordWrap then box.text:SetWordWrap(true) end

  -- A multi-line edit field grows with its text; the box follows it.
  box.edit = CreateFrame("EditBox", nil, box)
  box.edit:SetMultiLine(true)
  box.edit:SetAutoFocus(false)
  box.edit:SetFontObject("GameFontHighlight")
  box.edit:SetPoint("TOPLEFT", PAD, -PAD - 18)
  box.edit:SetWidth(WIDTH - PAD * 2)
  box.edit:SetHeight(60)
  box.edit:SetScript("OnEscapePressed", function() setEditing(false) end)
  box.edit:SetScript("OnTextChanged", function() if box.editing then fit() end end)
  box.edit:Hide()
  box.editBg = box:CreateTexture(nil, "BORDER")
  box.editBg:SetColorTexture(1, 1, 1, 0.08)
  box.editBg:SetPoint("TOPLEFT", box.edit, "TOPLEFT", -4, 4)
  box.editBg:SetPoint("BOTTOMRIGHT", box, "BOTTOMRIGHT", -PAD + 4, PAD - 4)
  box.editBg:Hide()

  local s = state()
  box:ClearAllPoints()
  if s.point then box:SetPoint(s.point, UIParent, s.relPoint or s.point, s.x or 0, s.y or 0)
  else box:SetPoint("CENTER", UIParent, "CENTER", 0, 120) end
end

-- show: true/false, or nil to flip.
function ns.toggleNoteBox(on)
  if on == nil then on = not (box and box:IsShown()) end
  state().shown = on
  if not on then
    if box then
      if box.editing then setEditing(false) end -- closing drops an unsaved edit
      box:Hide()
    end
    return
  end
  if not box then build() end
  show()
  applyLock()
  box:Show()
end

-- quiet: from the options panel, where the checkbox already says it.
function ns.lockNoteBox(locked, quiet)
  state().locked = locked
  if box then applyLock() end
  if not quiet then
    ns.say(locked and "Note box locked. /wct note unlock to move it again." or "Note box unlocked: drag it to move it.")
  end
end

function ns.noteBoxShown() return box ~= nil and box:IsShown() end
function ns.noteBoxLocked() return state().locked == true end

-- The background's opacity, 0 (see-through) to 1 (solid black); applied right away.
function ns.noteAlpha() return tonumber(state().alpha) or 0.6 end
function ns.setNoteAlpha(alpha)
  state().alpha = alpha
  if box then box.bg:SetColorTexture(0, 0, 0, alpha) end
end

-- At login: open again if it was open when the game last saved.
function ns.restoreNoteBox()
  if state().shown then ns.toggleNoteBox(true) end
end
