-- The addon window (/wct, the minimap button): every in-game option in tabs. Settings the game
-- keeps (WoWCharacterTrackerDB) and the In game settings shared with the app (ns.editSettings, the
-- newest change wins) apply right away. What gets posted to Discord comes from the app
-- (Settings.lua) and is listed read-only on the App tab. Plain frames only, so it opens in combat.
-- Built on first open, each tab on its first showing.
local ADDON, ns = ...
local ui = ns.ui

local NAME = "WoWCharacterTrackerWindow"
local WIDTH, HEIGHT = 720, 560
local AREA_HEIGHT = HEIGHT - 78 -- below the title and the tab row
local PAGE_WIDTH = WIDTH - 50

local window, area, version
local tabs = {}  -- [key] = { button, holder, page }
local current

local function isOn(key) return ns.setting(key) ~= false end

local function saved()
  WoWCharacterTrackerDB.window = WoWCharacterTrackerDB.window or {}
  return WoWCharacterTrackerDB.window
end

-- General ---------------------------------------------------------------------------------------
local CORNERS = { "TOPLEFT", "TOPRIGHT", "BOTTOMRIGHT", "BOTTOMLEFT" }
local CORNER_NAMES = { TOPLEFT = "Top left", TOPRIGHT = "Top right", BOTTOMRIGHT = "Bottom right", BOTTOMLEFT = "Bottom left" }

local function currentCorner()
  local c = ns.setting("gridCorner")
  return CORNER_NAMES[c] and c or "TOPLEFT"
end

local function buildGeneral(p)
  local live = ui.checkbox(p, "Instant posting", "Tell the app about events right away (a one-frame pattern and screenshot). Off: they arrive on /reload or logout.",
    function() return WoWCharacterTrackerDB.live ~= false end,
    function(on) WoWCharacterTrackerDB.live = on end)
  live:SetPoint("TOPLEFT", 14, -12)

  local popup = ui.checkbox(p, "Reload popup after a ding", "Offer a /reload right after you level, so the app gets everything.",
    function() return WoWCharacterTrackerDB.popup == true end,
    function(on) WoWCharacterTrackerDB.popup = on end)
  popup:SetPoint("TOPLEFT", live, "BOTTOMLEFT", 0, -18)

  local reloadTitle = ui.heading(p, "Keeping the app up to date", popup)
  local flightReload = ui.checkbox(p, "Offer a reload on flight paths", nil,
    function() return isOn("flightReload") end,
    function(on) ns.editSettings(function(s) s.flightReload = on end) end)
  flightReload:SetPoint("TOPLEFT", reloadTitle, "BOTTOMLEFT", -2, -6)
  local chatReminder = ui.checkbox(p, "Remind me in chat to /reload", nil,
    function() return isOn("chatReminder") end,
    function(on) ns.editSettings(function(s) s.chatReminder = on end) end)
  chatReminder:SetPoint("TOPLEFT", flightReload, "BOTTOMLEFT", 0, -2)

  local gridTitle = ui.heading(p, "Instant post pattern", chatReminder)
  local cornerButton
  local function showCorner() cornerButton:SetText("Corner: " .. CORNER_NAMES[currentCorner()]) end
  cornerButton = ui.button(p, "", 170, function()
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
  cornerButton:SetPoint("TOPLEFT", gridTitle, "BOTTOMLEFT", 2, -8)
  ui.onRefresh(p, showCorner)
  local gridLabel = ui.checkbox(p, "Show a small label next to it", nil,
    function() return isOn("gridLabel") end,
    function(on) ns.editSettings(function(s) s.gridLabel = on end) end)
  gridLabel:SetPoint("TOPLEFT", cornerButton, "BOTTOMLEFT", -4, -6)

  local keepAll = ui.checkbox(p, "Keep all history", "On: every loot, duel and battleground stays in the saved file, which keeps growing. Applies on your next /reload.",
    function() return ns.setting("keepAllHistory") == true end,
    function(on) ns.editSettings(function(s) s.keepAllHistory = on end) end)
  keepAll:SetPoint("TOPLEFT", gridLabel, "BOTTOMLEFT", 0, -10)

  -- Minimap.lua's, loaded after this file.
  local minimap = ui.checkbox(p, "Show the minimap button", "Hidden, /wct still opens this window.",
    function() return not ns.minimapButtonShown or ns.minimapButtonShown() end,
    function(on) if ns.setMinimapButtonShown then ns.setMinimapButtonShown(on) end end)
  minimap:SetPoint("TOPLEFT", keepAll, "BOTTOMLEFT", 0, -26)
end

-- Chat ------------------------------------------------------------------------------------------
-- The same kinds and names as the app's In game page (FlavorViewModel.AnnounceNames).
local ANNOUNCE_KINDS = { { "level", "Level-ups" }, { "maxlevel", "Max level" }, { "loot", "Epic loot" }, { "followed", "Followed items" },
  { "mount", "Mounts" }, { "duel", "Duel wins" }, { "bg", "Battleground wins" }, { "profession", "Professions" } }
for _, kind in ipairs(ns.IS_RETAIL
  and { { "keyrun", "Mythic+ runs" }, { "mplus", "Mythic+ rating" }, { "pvp", "PvP rating" } }
  or { { "boss", "Boss kills" }, { "makgora", "Mak'gora wins" }, { "death", "Deaths" } }) do
  ANNOUNCE_KINDS[#ANNOUNCE_KINDS + 1] = kind
end

-- Guild chat's kinds are `kinds`, the group's `partyKinds` (missing from before it had its own).
local function announceKinds(a, column)
  return column == "guild" and a.kinds or (a.partyKinds or a.kinds)
end

local function buildChat(p)
  local chatTitle = ui.heading(p, "Tell your guild or group")
  local chatHint = ui.hint(p, "One line in chat as you when something happens. Lines wait until you're out of combat.", chatTitle)
  local whereLast = ui.checkGrid(p, { { "guild", "Guild chat" }, { "party", "Party / raid chat" } }, chatHint,
    function(key)
      local a = ns.setting("announce") or {}
      return a[key] == true
    end,
    function(key, on) ns.editSettings(function(s) s.announce[key] = on end) end)
  ui.checkTable(p, ANNOUNCE_KINDS, { { "guild", "Guild chat" }, { "party", "Group chat" } }, whereLast,
    function(column, key)
      local kinds = announceKinds(ns.setting("announce") or {}, column)
      return kinds ~= nil and kinds[key] == true
    end,
    function(column, key, on) ns.editSettings(function(s) announceKinds(s.announce, column)[key] = on or nil end) end)
end

-- Highlights ------------------------------------------------------------------------------------
local function buildHighlights(p)
  local askTitle = ui.heading(p, "Ask before a highlight goes unposted")
  local askHint = ui.hint(p, "A small box offers to post it to Discord, for what What gets posted in the app doesn't post by itself. Needs the app and a webhook.", askTitle)
  local kindsLast = ui.checkGrid(p, { { "boss", "Boss kills" }, { "rare", "Rare gear" }, { "epic", "Epic gear" } }, askHint,
    function(key)
      local ask = ns.setting("ask") or {}
      return ask[key] == true
    end,
    function(key, on) ns.editSettings(function(s) s.ask[key] = on or nil end) end)

  -- How long the box stays (askSeconds, the same choices as the app's In game page): click for the next.
  local CHOICES = { 5, 10, 15, 30, 60 }
  local function current() return tonumber(ns.setting("askSeconds")) or 10 end
  local secondsButton
  local function showSeconds() secondsButton:SetText(string.format("Box stays: %d seconds", current())) end
  secondsButton = ui.button(p, "Box stays: 10 seconds", 190, function()
    local now, nextChoice = current(), CHOICES[1]
    for i, n in ipairs(CHOICES) do
      if n == now then nextChoice = CHOICES[i % #CHOICES + 1] end
    end
    ns.editSettings(function(s) s.askSeconds = nextChoice end)
    showSeconds()
  end)
  secondsButton:SetPoint("TOPLEFT", kindsLast, "BOTTOMLEFT", 2, -14)
  ui.onRefresh(p, showSeconds)
end

-- Note ------------------------------------------------------------------------------------------
local function buildNote(p)
  local noteTitle = ui.heading(p, "Note box (/wct note)")
  local noteHint = ui.hint(p, "Edit it right in the box, or on the character's Notes tab in the app. The newest edit wins; each side gets the other's on /reload.", noteTitle)

  local showNote = ui.checkbox(p, "Show the note box", "The character's note in a box on screen.",
    function() return ns.noteBoxShown() end,
    function(on) ns.toggleNoteBox(on) end)
  showNote:SetPoint("TOPLEFT", noteHint, "BOTTOMLEFT", -2, -8)

  local lockNote = ui.checkbox(p, "Lock it in place", "Can't be dragged, and clicks go through it to the game.",
    function() return ns.noteBoxLocked() end,
    function(on) ns.lockNoteBox(on, true) end)
  lockNote:SetPoint("TOPLEFT", showNote, "BOTTOMLEFT", 0, -18)

  local hoverNote = ui.checkbox(p, "Buttons only on mouseover", "Edit, Lock and the X show while your mouse is over the box.",
    function() return ns.noteButtonsOnHover() end,
    function(on) ns.setNoteButtonsOnHover(on) end)
  hoverNote:SetPoint("TOPLEFT", lockNote, "BOTTOMLEFT", 0, -18)

  local reset = ui.button(p, "Reset position", 150, function() ns.resetNoteBox() end)
  reset:SetPoint("TOPLEFT", hoverNote, "BOTTOMLEFT", 4, -20)

  local opacity = ui.slider(p, "Background opacity",
    function() return math.floor(ns.noteAlpha() * 100 + 0.5) end,
    function(v) ns.setNoteAlpha(v / 100) end)
  opacity:SetPoint("TOPLEFT", reset, "BOTTOMLEFT", 0, -30)
end

-- Friends ---------------------------------------------------------------------------------------
local function buildFriends(p)
  local title = ui.heading(p, "Friends")
  local line = ui.hint(p, "Find and ask friends here soon. Until then: /wct friends help.", title)
  local list = ui.button(p, "List my contacts in chat", 200, function() ns.friendsCommand("") end)
  list:SetPoint("TOPLEFT", line, "BOTTOMLEFT", 0, -10)
end

-- Sharing ---------------------------------------------------------------------------------------
local function buildSharing(p)
  local friendsTitle = ui.heading(p, "Share with Battle.net friends")
  local friendsHint = ui.hint(p, "Friends who have the addon can ask to see your characters; nothing is shared until you allow it. Contacts: /wct friends.", friendsTitle)
  local shareOn = ui.checkbox(p, "Share with friends", "Off: nothing is sent, and nothing friends send is kept.",
    function() return ns.shareSettings().enabled == true end,
    function(on) ns.editShare(function(s) s.enabled = on end) end)
  shareOn:SetPoint("TOPLEFT", friendsHint, "BOTTOMLEFT", -2, -8)
  local shareThis = ui.checkbox(p, "Share this character", nil,
    function() return ns.isCharShared(ns.shareSettings(), UnitGUID("player")) end,
    function(on)
      local me = UnitGUID("player")
      if me then ns.editShare(function(s) s.chars[me] = on end) end
    end)
  shareThis:SetPoint("TOPLEFT", shareOn, "BOTTOMLEFT", 0, -18)
  local shareNew = ui.checkbox(p, "Share new characters", "Characters you haven't shared or hidden yet.",
    function() return ns.shareSettings().newChars ~= false end,
    function(on) ns.editShare(function(s) s.newChars = on end) end)
  shareNew:SetPoint("TOPLEFT", shareThis, "BOTTOMLEFT", 0, -2)
  local whatTitle = ui.text(p, "GameFontNormalSmall", "What friends get (name, level, class and item level always)")
  whatTitle:SetPoint("TOPLEFT", shareNew, "BOTTOMLEFT", 4, -24)
  ui.checkGrid(p, { { "gear", "Gear" }, { "talents", "Talents" }, { "location", "Location" }, { "gold", "Gold" } }, whatTitle,
    function(key) return ns.shareSettings().defaults[key] == true end,
    function(key, on) ns.editShare(function(s) s.defaults[key] = on end) end)
end

-- App -------------------------------------------------------------------------------------------
local KINDS = { { "level", "level-ups" }, { "maxLevel", "max level" }, { "rare", "rare loot" }, { "epic", "epic loot" },
  { "mount", "mounts" }, { "boss", "boss kills" }, { "death", "deaths" }, { "duel", "duels" }, { "makgora", "Mak'gora" },
  { "bg", "battlegrounds" }, { "profession", "profession milestones" }, { "keyrun", "Mythic+ runs" }, { "mplus", "Mythic+ rating" }, { "pvp", "PvP rating" } }

local function picked(t, names)
  local out = {}
  for _, n in ipairs(names) do if t and t[n[1]] then out[#out + 1] = n[2] end end
  return #out > 0 and table.concat(out, ", ") or "none"
end

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
    "What you change in this window reaches the app on your next /reload or logout. The newest change wins.",
  }, "\n")
end

local function buildApp(p)
  local appTitle = ui.heading(p, "Set in the Character Tracker app")
  local appText = ui.text(p, "GameFontHighlightSmall")
  appText:SetPoint("TOPLEFT", appTitle, "BOTTOMLEFT", 0, -6)
  appText:SetWidth(PAGE_WIDTH - 40)
  appText:SetJustifyV("TOP")
  ui.onRefresh(p, function() appText:SetText(appSummary()) end)
end

-- The window ------------------------------------------------------------------------------------
-- height: how tall the tab's content is; taller than the window gets a scroll frame.
local TABS = {
  { key = "general", label = "General", height = 470, build = buildGeneral },
  { key = "chat", label = "Chat", height = 440, build = buildChat },
  { key = "highlights", label = "Highlights", height = 200, build = buildHighlights },
  { key = "note", label = "Note", height = 330, build = buildNote },
  { key = "friends", label = "Friends", height = 120, build = buildFriends },
  { key = "sharing", label = "Sharing", height = 300, build = buildSharing },
  { key = "app", label = "App", height = 200, build = buildApp },
}
local BY_KEY = {}
for _, t in ipairs(TABS) do BY_KEY[t.key] = t end

local function buildTab(tab)
  local holder, page
  if tab.height > AREA_HEIGHT then
    holder = CreateFrame("ScrollFrame", nil, area, "UIPanelScrollFrameTemplate")
    holder:SetPoint("TOPLEFT", 0, 0)
    holder:SetPoint("BOTTOMRIGHT", -26, 0)
    page = CreateFrame("Frame", nil, holder)
    page:SetSize(PAGE_WIDTH, tab.height)
    holder:SetScrollChild(page)
  else
    page = CreateFrame("Frame", nil, area)
    page:SetAllPoints()
    holder = page
  end
  tab.build(page)
  tabs[tab.key].holder, tabs[tab.key].page = holder, page
end

local function selectTab(key)
  if not BY_KEY[key] then key = "general" end
  current = key
  saved().tab = key
  for _, t in ipairs(TABS) do
    local state = tabs[t.key]
    if t.key == key then
      if not state.page then buildTab(t) end
      state.holder:Show()
      state.button:LockHighlight()
    else
      if state.holder then state.holder:Hide() end
      state.button:UnlockHighlight()
    end
  end
  ui.refresh(tabs[key].page)
end

local function savePosition(self)
  self:StopMovingOrSizing()
  local point, _, relative, x, y = self:GetPoint()
  local s = saved()
  s.point, s.relative, s.x, s.y = point, relative, x, y
end

local function build()
  window = CreateFrame("Frame", NAME, UIParent)
  window:SetSize(WIDTH, HEIGHT)
  window:SetFrameStrata("DIALOG")
  window:SetToplevel(true)
  window:SetClampedToScreen(true)
  window:EnableMouse(true)
  window:SetMovable(true)
  window:RegisterForDrag("LeftButton")
  window:SetScript("OnDragStart", window.StartMoving)
  window:SetScript("OnDragStop", savePosition)
  local s = saved()
  if s.point then window:SetPoint(s.point, UIParent, s.relative or s.point, s.x or 0, s.y or 0)
  else window:SetPoint("CENTER", UIParent, "CENTER", 0, 40) end
  if UISpecialFrames then table.insert(UISpecialFrames, NAME) end -- Esc closes it

  local bg = window:CreateTexture(nil, "BACKGROUND")
  bg:SetColorTexture(0.05, 0.05, 0.08, 0.95)
  bg:SetAllPoints()

  local title = ui.text(window, "GameFontNormalLarge", "WoW Character Tracker")
  title:SetPoint("TOPLEFT", 14, -12)
  version = ui.text(window, "GameFontDisableSmall")
  version:SetPoint("LEFT", title, "RIGHT", 8, -1)

  local close = CreateFrame("Button", nil, window)
  close:SetSize(28, 28)
  close:SetPoint("TOPRIGHT", -4, -4)
  close:SetNormalTexture("Interface\\Buttons\\UI-Panel-MinimizeButton-Up")
  close:SetPushedTexture("Interface\\Buttons\\UI-Panel-MinimizeButton-Down")
  close:SetHighlightTexture("Interface\\Buttons\\UI-Panel-MinimizeButton-Highlight", "ADD")
  close:SetScript("OnClick", function() window:Hide() end)

  local last
  for _, t in ipairs(TABS) do
    local key = t.key
    local b = ui.button(window, t.label, 92, function() selectTab(key) end)
    if last then b:SetPoint("LEFT", last, "RIGHT", 4, 0)
    else b:SetPoint("TOPLEFT", 12, -40) end
    tabs[key] = { button = b }
    last = b
  end

  area = CreateFrame("Frame", nil, window)
  area:SetPoint("TOPLEFT", 8, -70)
  area:SetPoint("BOTTOMRIGHT", -8, 8)
end

-- Opens the window on a tab (a key of TABS), or the last one open.
function ns.openWindow(tab)
  if not window then build() end
  version:SetText(ui.version())
  selectTab(tab or current or saved().tab)
  window:Show()
end

function ns.windowShown() return window ~= nil and window:IsShown() end

function ns.toggleWindow()
  if ns.windowShown() then window:Hide() else ns.openWindow() end
end
