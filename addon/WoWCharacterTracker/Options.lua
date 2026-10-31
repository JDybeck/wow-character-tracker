-- The addon's page in the game's options (Esc, Options, AddOns, WoW Character Tracker; or /wct options).
-- Settings the game keeps (WoWCharacterTrackerDB) and the In game settings shared with the app
-- (ns.editSettings, the newest change wins) can be changed here and apply right away. What gets
-- posted to Discord comes from the app (Settings.lua): it's listed read-only.
local ADDON, ns = ...

local NAME = "WoW Character Tracker"
local panel = CreateFrame("Frame")
panel.name = NAME
local category
local refreshers = {}

-- The page is longer than the window, so it scrolls.
local scroll = CreateFrame("ScrollFrame", nil, panel, "UIPanelScrollFrameTemplate")
scroll:SetPoint("TOPLEFT", 0, -4)
scroll:SetPoint("BOTTOMRIGHT", -28, 4)
local page = CreateFrame("Frame", nil, scroll)
page:SetSize(640, 1180)
scroll:SetScrollChild(page)

local function text(parent, template, s)
  local fs = parent:CreateFontString(nil, "ARTWORK", template)
  fs:SetJustifyH("LEFT")
  fs:SetText(s or "")
  return fs
end

local function checkbox(label, tip, get, set)
  local cb = CreateFrame("CheckButton", nil, page, "UICheckButtonTemplate")
  cb:SetSize(26, 26)
  local l = text(cb, "GameFontHighlight", label)
  l:SetPoint("LEFT", cb, "RIGHT", 4, 1)
  if tip then
    local t = text(cb, "GameFontDisableSmall", tip)
    t:SetPoint("TOPLEFT", l, "BOTTOMLEFT", 0, -2)
  end
  cb:SetScript("OnClick", function(self) set(self:GetChecked() and true or false) end)
  refreshers[#refreshers + 1] = function() cb:SetChecked(get()) end
  return cb
end

-- A plain slider (the game's slider templates differ between clients).
local function slider(label, get, set)
  local s = CreateFrame("Slider", nil, page)
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

local function heading(s, anchor, gap)
  local h = text(page, "GameFontNormal", s)
  h:SetPoint("TOPLEFT", anchor, "BOTTOMLEFT", 2, gap or -26)
  return h
end

local function hint(s, anchor)
  local h = text(page, "GameFontDisableSmall", s)
  h:SetPoint("TOPLEFT", anchor, "BOTTOMLEFT", 0, -4)
  return h
end

-- Checkboxes in two columns, under anchor. get(key) and set(key, on). Returns the last row's box.
local function checkGrid(items, anchor, get, set)
  local lastLeft, last = anchor, anchor
  for i, item in ipairs(items) do
    local key = item[1]
    local cb = checkbox(item[2], nil, function() return get(key) end, function(on) set(key, on) end)
    if i % 2 == 1 then
      cb:SetPoint("TOPLEFT", lastLeft, "BOTTOMLEFT", i == 1 and -2 or 0, i == 1 and -6 or -2)
      lastLeft = cb
    else
      cb:SetPoint("LEFT", lastLeft, "LEFT", 270, 0)
    end
    last = lastLeft
  end
  return last
end

local function isOn(key) return ns.setting(key) ~= false end

local title = text(page, "GameFontNormalLarge", NAME)
title:SetPoint("TOPLEFT", 16, -16)
local version = text(page, "GameFontDisableSmall")
version:SetPoint("LEFT", title, "RIGHT", 8, -1)

local live = checkbox("Instant posting", "Tell the app about events right away (a one-frame pattern and screenshot). Off: they arrive on /reload or logout.",
  function() return WoWCharacterTrackerDB.live ~= false end,
  function(on) WoWCharacterTrackerDB.live = on end)
live:SetPoint("TOPLEFT", title, "BOTTOMLEFT", -2, -16)

local popup = checkbox("Reload popup after a ding", "Offer a /reload right after you level, so the app gets everything.",
  function() return WoWCharacterTrackerDB.popup == true end,
  function(on) WoWCharacterTrackerDB.popup = on end)
popup:SetPoint("TOPLEFT", live, "BOTTOMLEFT", 0, -18)

-- Telling the guild or group ----------------------------------------------------
local chatTitle = heading("Tell your guild or group", popup)
local chatHint = hint("One line in chat as you when something happens. Lines wait until you're out of combat.", chatTitle)

local function announceGet(key)
  local a = ns.setting("announce") or {}
  return a[key] == true
end

local whereLast = checkGrid({ { "guild", "Guild chat" }, { "party", "Party / raid chat" } }, chatHint, announceGet,
  function(key, on) ns.editSettings(function(s) s.announce[key] = on end) end)

-- The same kinds and names as the app's In game page (FlavorViewModel.AnnounceNames).
local ANNOUNCE_KINDS = { { "level", "Level-ups" }, { "maxlevel", "Max level" }, { "loot", "Epic loot" }, { "followed", "Followed items" },
  { "mount", "Mounts" }, { "duel", "Duel wins" }, { "bg", "Battleground wins" }, { "profession", "Professions" } }
for _, kind in ipairs(ns.IS_RETAIL
  and { { "keyrun", "Mythic+ runs" }, { "mplus", "Mythic+ rating" }, { "pvp", "PvP rating" } }
  or { { "boss", "Boss kills" }, { "makgora", "Mak'gora wins" }, { "death", "Deaths" } }) do
  ANNOUNCE_KINDS[#ANNOUNCE_KINDS + 1] = kind
end

local kindsLast = checkGrid(ANNOUNCE_KINDS, whereLast,
  function(key)
    local a = ns.setting("announce") or {}
    return a.kinds ~= nil and a.kinds[key] == true
  end,
  function(key, on) ns.editSettings(function(s) s.announce.kinds[key] = on or nil end) end)

-- Offering to post ----------------------------------------------------------------
local askTitle = heading("Ask before a highlight goes unposted", kindsLast)
local askHint = hint("A small box offers to post it to Discord, for what What gets posted in the app doesn't post by itself. Needs the app and a webhook.", askTitle)
local askLast = checkGrid({ { "boss", "Boss kills" }, { "rare", "Rare gear" }, { "epic", "Epic gear" } }, askHint,
  function(key)
    local ask = ns.setting("ask") or {}
    return ask[key] == true
  end,
  function(key, on) ns.editSettings(function(s) s.ask[key] = on or nil end) end)

-- Keeping the app up to date ------------------------------------------------------
local reloadTitle = heading("Keeping the app up to date", askLast)
local flightReload = checkbox("Offer a reload on flight paths", nil,
  function() return isOn("flightReload") end,
  function(on) ns.editSettings(function(s) s.flightReload = on end) end)
flightReload:SetPoint("TOPLEFT", reloadTitle, "BOTTOMLEFT", -2, -6)
local chatReminder = checkbox("Remind me in chat to /reload", nil,
  function() return isOn("chatReminder") end,
  function(on) ns.editSettings(function(s) s.chatReminder = on end) end)
chatReminder:SetPoint("TOPLEFT", flightReload, "BOTTOMLEFT", 0, -2)

-- Instant posting's pattern ---------------------------------------------------------
local gridTitle = heading("Instant post pattern", chatReminder)
local CORNERS = { "TOPLEFT", "TOPRIGHT", "BOTTOMRIGHT", "BOTTOMLEFT" }
local CORNER_NAMES = { TOPLEFT = "Top left", TOPRIGHT = "Top right", BOTTOMRIGHT = "Bottom right", BOTTOMLEFT = "Bottom left" }
local function currentCorner()
  local c = ns.setting("gridCorner")
  return CORNER_NAMES[c] and c or "TOPLEFT"
end
local cornerButton = CreateFrame("Button", nil, page, "UIPanelButtonTemplate")
cornerButton:SetSize(170, 22)
cornerButton:SetPoint("TOPLEFT", gridTitle, "BOTTOMLEFT", 2, -8)
local function showCorner() cornerButton:SetText("Corner: " .. CORNER_NAMES[currentCorner()]) end
refreshers[#refreshers + 1] = showCorner
cornerButton:SetScript("OnClick", function()
  local now = currentCorner()
  for i, c in ipairs(CORNERS) do
    if c == now then
      local nextCorner = CORNERS[i % #CORNERS + 1]
      ns.editSettings(function(s) s.gridCorner = nextCorner end)
      break
    end
  end
  showCorner()
end)
local gridLabel = checkbox("Show a small label next to it", nil,
  function() return isOn("gridLabel") end,
  function(on) ns.editSettings(function(s) s.gridLabel = on end) end)
gridLabel:SetPoint("TOPLEFT", cornerButton, "BOTTOMLEFT", -4, -6)

local keepAll = checkbox("Keep all history", "On: every loot, duel and battleground stays in the saved file, which keeps growing. Applies on your next /reload.",
  function() return ns.setting("keepAllHistory") == true end,
  function(on) ns.editSettings(function(s) s.keepAllHistory = on end) end)
keepAll:SetPoint("TOPLEFT", gridLabel, "BOTTOMLEFT", 0, -10)

-- The note box ------------------------------------------------------------------------
local noteTitle = heading("Note box (/wct note)", keepAll)
local noteHint = hint("Edit it right in the box, or on the character's Notes tab in the app. The newest edit wins; each side gets the other's on /reload.", noteTitle)

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

local reset = CreateFrame("Button", nil, page, "UIPanelButtonTemplate")
reset:SetSize(150, 22)
reset:SetText("Reset position")
reset:SetPoint("TOPLEFT", hoverNote, "BOTTOMLEFT", 4, -20)
reset:SetScript("OnClick", function() ns.resetNoteBox() end)

local opacity = slider("Background opacity",
  function() return math.floor(ns.noteAlpha() * 100 + 0.5) end,
  function(v) ns.setNoteAlpha(v / 100) end)
opacity:SetPoint("TOPLEFT", reset, "BOTTOMLEFT", 0, -30)

-- From the app -----------------------------------------------------------------------
local appTitle = text(page, "GameFontNormal", "Set in the Character Tracker app")
appTitle:SetPoint("TOPLEFT", opacity, "BOTTOMLEFT", -4, -28)
local appText = text(page, "GameFontHighlightSmall")
appText:SetPoint("TOPLEFT", appTitle, "BOTTOMLEFT", 0, -6)
appText:SetWidth(560)
appText:SetJustifyV("TOP")

local function picked(t, names)
  local out = {}
  for _, n in ipairs(names) do if t and t[n[1]] then out[#out + 1] = n[2] end end
  return #out > 0 and table.concat(out, ", ") or "none"
end
local KINDS = { { "level", "level-ups" }, { "maxLevel", "max level" }, { "rare", "rare loot" }, { "epic", "epic loot" },
  { "mount", "mounts" }, { "boss", "boss kills" }, { "death", "deaths" }, { "duel", "duels" }, { "makgora", "Mak'gora" },
  { "bg", "battlegrounds" }, { "profession", "profession milestones" }, { "keyrun", "Mythic+ runs" }, { "mplus", "Mythic+ rating" }, { "pvp", "PvP rating" } }

local function appSummary()
  if not ns.hasApp() then
    return "No app found. Discord posts, screenshots and the character overview need the free Character Tracker app. Guild and group lines work without it."
  end
  local post = ns.setting("post") or {}
  local postKinds = {}
  for k, v in pairs(post) do postKinds[k] = (type(v) == "table" and v.on) or v == true end
  return table.concat({
    "Posted to Discord: " .. picked(postKinds, KINDS),
    "Screenshots: " .. picked(ns.setting("screenshot"), KINDS),
    " ",
    "Change these in the app's What gets posted page. They reach the game on your next /reload.",
    "What you change on this page reaches the app on your next /reload or logout. The newest change wins.",
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
