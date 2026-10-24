-- The addon's page in the game's options (Esc, Options, AddOns, WoW Character Tracker; or /wct options).
-- Settings the game keeps (WoWCharacterTrackerDB) can be changed here and apply right away. The rest
-- come from the app (Settings.lua): they're listed read-only, since only the app can change them.
local ADDON, ns = ...

local NAME = "WoW Character Tracker"
local panel = CreateFrame("Frame")
panel.name = NAME
local category
local refreshers = {}

local function text(parent, template, s)
  local fs = parent:CreateFontString(nil, "ARTWORK", template)
  fs:SetJustifyH("LEFT")
  fs:SetText(s or "")
  return fs
end

local function checkbox(label, tip, get, set)
  local cb = CreateFrame("CheckButton", nil, panel, "UICheckButtonTemplate")
  cb:SetSize(26, 26)
  local l = text(cb, "GameFontHighlight", label)
  l:SetPoint("LEFT", cb, "RIGHT", 4, 1)
  local t = text(cb, "GameFontDisableSmall", tip)
  t:SetPoint("TOPLEFT", l, "BOTTOMLEFT", 0, -2)
  cb:SetScript("OnClick", function(self) set(self:GetChecked() and true or false) end)
  refreshers[#refreshers + 1] = function() cb:SetChecked(get()) end
  return cb
end

-- A plain slider (the game's slider templates differ between clients).
local function slider(label, get, set)
  local s = CreateFrame("Slider", nil, panel)
  s:SetOrientation("HORIZONTAL")
  s:SetSize(220, 17)
  s:SetMinMaxValues(0, 100)
  s:SetValueStep(5)
  if s.SetObeyStepOnDrag then s:SetObeyStepOnDrag(true) end
  s:EnableMouse(true)
  s:SetThumbTexture("Interface\\Buttons\\UI-SliderBar-Button-Horizontal")
  local track = s:CreateTexture(nil, "BACKGROUND")
  track:SetColorTexture(1, 1, 1, 0.15)
  track:SetHeight(6)
  track:SetPoint("LEFT")
  track:SetPoint("RIGHT")
  local l = text(s, "GameFontHighlight", label)
  l:SetPoint("BOTTOMLEFT", s, "TOPLEFT", 0, 4)
  local value = text(s, "GameFontHighlightSmall")
  value:SetPoint("LEFT", s, "RIGHT", 10, 0)
  s:SetScript("OnValueChanged", function(_, v)
    v = math.floor(v / 5 + 0.5) * 5
    value:SetText(v .. "%")
    set(v)
  end)
  refreshers[#refreshers + 1] = function() s:SetValue(get()) end
  return s
end

local title = text(panel, "GameFontNormalLarge", NAME)
title:SetPoint("TOPLEFT", 16, -16)
local version = text(panel, "GameFontDisableSmall")
version:SetPoint("LEFT", title, "RIGHT", 8, -1)

local live = checkbox("Instant posting", "Tell the app about events right away (a one-frame pattern and screenshot). Off: they arrive on /reload or logout.",
  function() return WoWCharacterTrackerDB.live ~= false end,
  function(on) WoWCharacterTrackerDB.live = on end)
live:SetPoint("TOPLEFT", title, "BOTTOMLEFT", -2, -16)

local popup = checkbox("Reload popup after a ding", "Offer a /reload right after you level, so the app gets everything.",
  function() return WoWCharacterTrackerDB.popup == true end,
  function(on) WoWCharacterTrackerDB.popup = on end)
popup:SetPoint("TOPLEFT", live, "BOTTOMLEFT", 0, -18)

local noteTitle = text(panel, "GameFontNormal", "Note box (/wct note)")
noteTitle:SetPoint("TOPLEFT", popup, "BOTTOMLEFT", 2, -26)
local noteHint = text(panel, "GameFontDisableSmall", "Edit it right in the box, or on the character's Notes tab in the app. The newest edit wins; each side gets the other's on /reload.")
noteHint:SetPoint("TOPLEFT", noteTitle, "BOTTOMLEFT", 0, -4)

local showNote = checkbox("Show the note box", "The character's note in a box on screen.",
  function() return ns.noteBoxShown() end,
  function(on) ns.toggleNoteBox(on) end)
showNote:SetPoint("TOPLEFT", noteHint, "BOTTOMLEFT", -2, -8)

local lockNote = checkbox("Lock it in place", "Can't be dragged, and clicks go through it to the game.",
  function() return ns.noteBoxLocked() end,
  function(on) ns.lockNoteBox(on, true) end)
lockNote:SetPoint("TOPLEFT", showNote, "BOTTOMLEFT", 0, -18)

local hoverNote = checkbox("Buttons only on mouseover", "Edit, Lock and the X show while your mouse is over the box.",
  function() return ns.noteButtonsOnHover() end,
  function(on) ns.setNoteButtonsOnHover(on) end)
hoverNote:SetPoint("TOPLEFT", lockNote, "BOTTOMLEFT", 0, -18)

local reset = CreateFrame("Button", nil, panel, "UIPanelButtonTemplate")
reset:SetSize(150, 22)
reset:SetText("Reset position")
reset:SetPoint("TOPLEFT", hoverNote, "BOTTOMLEFT", 4, -20)
reset:SetScript("OnClick", function() ns.resetNoteBox() end)

local opacity = slider("Background opacity",
  function() return math.floor(ns.noteAlpha() * 100 + 0.5) end,
  function(v) ns.setNoteAlpha(v / 100) end)
opacity:SetPoint("TOPLEFT", reset, "BOTTOMLEFT", 0, -30)

local appTitle = text(panel, "GameFontNormal", "Set in the Character Tracker app")
appTitle:SetPoint("TOPLEFT", opacity, "BOTTOMLEFT", -4, -28)
local appText = text(panel, "GameFontHighlightSmall")
appText:SetPoint("TOPLEFT", appTitle, "BOTTOMLEFT", 0, -6)
appText:SetWidth(560)
appText:SetJustifyV("TOP")

local function onOff(v) return v and "on" or "off" end
local function picked(t, names)
  local out = {}
  for _, n in ipairs(names) do if t and t[n[1]] then out[#out + 1] = n[2] end end
  return #out > 0 and table.concat(out, ", ") or "none"
end
local KINDS = { { "level", "level-ups" }, { "maxLevel", "max level" }, { "maxlevel", "max level" }, { "rare", "rare loot" }, { "epic", "epic loot" },
  { "loot", "loot" }, { "mount", "mounts" }, { "boss", "boss kills" }, { "death", "deaths" }, { "duel", "duels" }, { "makgora", "Mak'gora" },
  { "bg", "battlegrounds" }, { "keyrun", "Mythic+ runs" }, { "mplus", "Mythic+ rating" }, { "pvp", "PvP rating" } }

local function appSummary()
  if not WoWCharacterTrackerSettings or not WoWCharacterTrackerSettings.post then
    return "The app hasn't written its settings yet. Start the Character Tracker app, then /reload."
  end
  local post = ns.setting("post") or {}
  local postKinds = {}
  for k, v in pairs(post) do postKinds[k] = (type(v) == "table" and v.on) or v == true end
  local announce = ns.setting("announce") or {}
  local where = {}
  if announce.guild then where[#where + 1] = "guild" end
  if announce.party then where[#where + 1] = "party/raid" end
  return table.concat({
    "Posted to Discord: " .. picked(postKinds, KINDS),
    "Screenshots: " .. picked(ns.setting("screenshot"), KINDS),
    "Told in chat: " .. (#where > 0 and (table.concat(where, " and ") .. " (" .. picked(announce.kinds, KINDS) .. ")") or "off"),
    "Reload offer on flight paths: " .. onOff(ns.setting("flightReload")) .. ", reminder in chat: " .. onOff(ns.setting("chatReminder")),
    "Instant post pattern: " .. tostring(ns.setting("gridCorner") or "TOPLEFT"):lower() .. " corner, label " .. onOff(ns.setting("gridLabel") ~= false),
    "Saved history: " .. (ns.setting("keepAllHistory") and "everything" or "newest 300 loot, duels and battlegrounds per character"),
    " ",
    "Change these in the app (What gets posted and In game pages). They reach the game on your next /reload.",
  }, "\n")
end

panel:SetScript("OnShow", function()
  if not WoWCharacterTrackerDB then return end
  local metadata = (C_AddOns and C_AddOns.GetAddOnMetadata) or GetAddOnMetadata
  version:SetText(metadata and ("v" .. tostring(metadata(ADDON, "Version") or "")) or "")
  for _, r in ipairs(refreshers) do r() end
  appText:SetText(appSummary())
end)

if Settings and Settings.RegisterCanvasLayoutCategory then
  category = Settings.RegisterCanvasLayoutCategory(panel, NAME)
  Settings.RegisterAddOnCategory(category)
elseif InterfaceOptions_AddCategory then
  InterfaceOptions_AddCategory(panel)
end

function ns.openOptions()
  if InCombatLockdown() then
    ns.say("The options open after combat.")
    return
  end
  if category and Settings.OpenToCategory then
    Settings.OpenToCategory(category:GetID())
  elseif InterfaceOptionsFrame_OpenToCategory then
    InterfaceOptionsFrame_OpenToCategory(panel)
    InterfaceOptionsFrame_OpenToCategory(panel) -- the old frame needs it twice the first time
  end
end
