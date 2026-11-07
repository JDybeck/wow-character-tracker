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
-- What's listed comes from Friends.lua (ns.onlineFriends and the rest), each guarded so the tab
-- opens without them. Rows are pooled: a redraw reuses the frames and hides the spare ones.
local DAY = 86400
local ROW = 24
local drawFriends
local searched = false -- Find online friends was clicked this session

local function rowsOf(fn) return fn and fn() or {} end

local function place(w, page, x, y)
  w:ClearAllPoints()
  w:SetPoint("TOPLEFT", page, "TOPLEFT", x, -y)
  w:Show()
end

local function pooled(pool, i, make)
  pool[i] = pool[i] or make()
  return pool[i]
end

local function hideFrom(pool, first)
  for i = first, #pool do pool[i]:Hide() end
end

local function classColor(fs, classFile)
  local c = RAID_CLASS_COLORS and classFile and RAID_CLASS_COLORS[classFile]
  if c then fs:SetTextColor(c.r, c.g, c.b) else fs:SetTextColor(1, 1, 1) end
end

local function ago(at)
  local s = math.max(0, time() - (at or time()))
  if s < 3600 then return math.floor(s / 60) .. "m ago" end
  if s < DAY then return math.floor(s / 3600) .. "h ago" end
  return math.floor(s / DAY) .. "d ago"
end

-- A row's button runs whatever the last redraw gave it (self.act).
local function rowButton(row, width, x)
  local b = ui.button(row, "", width, function(self) if self.act then self.act() end end)
  b:SetPoint("LEFT", row, "LEFT", x, 0)
  return b
end

local function setButton(b, label, act)
  b:SetText(label)
  b.act = act
  if act then b:Enable() else b:Disable() end
  b:Show()
end

local function rowText(row, template, x, width)
  local fs = ui.text(row, template)
  fs:SetPoint("LEFT", row, "LEFT", x, 0)
  fs:SetWidth(width)
  return fs
end

-- The state text and up to two buttons ({ label, fn(tag) }, fn nil: shown disabled).
local function friendState(f)
  if f.blocked then return "Blocked" end
  if f.contact then
    local bits = {}
    if f.contact.share then bits[#bits + 1] = "You share" end
    if f.sharesWithYou then bits[#bits + 1] = #bits > 0 and "shares with you" or "Shares with you" end
    return #bits > 0 and table.concat(bits, ", ") or "Contact", { "Ask again", ns.askFriend }
  end
  if f.hasAddon == nil then return "Checking..." end
  if f.hasAddon then return "Has the addon", { "Ask", ns.askFriend }, { "Share", ns.shareWithFriend } end
  if f.invitedAt and time() - f.invitedAt < DAY then return "No addon", { "Invited" } end
  return "No addon", { "Invite", ns.inviteFriend }
end

-- "Zeb Stonehand" with a Forever surname, else the name.
local function fullName(c)
  local name = c.char or "?"
  if c.surname and c.surname ~= "" then return name .. " " .. c.surname end
  return name
end

-- "Zeb Stonehand (Firemaw)": the gear panel's title and the hover tooltip's first line.
local function charTitle(c)
  return c.realm and string.format("%s (%s)", fullName(c), c.realm) or fullName(c)
end

-- The item's name ("item 12345" until the game has it cached) and its quality color, white without one.
local function itemName(id)
  local name, _, quality = ns.itemInfo(id)
  local color = quality and ITEM_QUALITY_COLORS and ITEM_QUALITY_COLORS[quality]
  if color then return name or ("item " .. id), color.r, color.g, color.b end
  return name or ("item " .. id), 1, 1, 1
end

-- Gear as item names (quality colored when the game has the item) and talents.
local function showCharTip(self)
  local c = self.char
  if not (c and GameTooltip) then return end
  GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
  GameTooltip:AddLine(charTitle(c), 1, 1, 1)
  for id in string.gmatch(c.gear or "", "%d+~(%d+)") do GameTooltip:AddLine(itemName(tonumber(id))) end
  if c.talents then GameTooltip:AddLine("Talents: " .. c.talents, 0.8, 0.8, 0.8, true) end
  GameTooltip:Show()
end

local function hideCharTip() if GameTooltip then GameTooltip:Hide() end end

local function closeButton(parent, onClick)
  local close = CreateFrame("Button", nil, parent)
  close:SetSize(28, 28)
  close:SetPoint("TOPRIGHT", -4, -4)
  close:SetNormalTexture("Interface\\Buttons\\UI-Panel-MinimizeButton-Up")
  close:SetPushedTexture("Interface\\Buttons\\UI-Panel-MinimizeButton-Down")
  close:SetHighlightTexture("Interface\\Buttons\\UI-Panel-MinimizeButton-Highlight", "ADD")
  close:SetScript("OnClick", onClick)
  return close
end

-- The gear panel: a shared character's equipped items beside the window, each with the game's own
-- item tooltip (built from the ids). Built on the first click on a character line.
local GEAR_PANEL = "WoWCharacterTrackerGearPanel"
local GEAR_ROW = 30
local SLOT_NAMES = { "Head", "Neck", "Shoulder", "Shirt", "Chest", "Waist", "Legs", "Feet", "Wrist", "Hands",
  "Finger", "Finger", "Trinket", "Trinket", "Back", "Main Hand", "Off Hand", "Ranged", "Tabard" }
local gearPanel, gearTitle, noGear
local gearRows = {}
local gearKey -- which character the panel shows

local function charKey(c) return c.guid or charTitle(c) end

local function itemIcon(id)
  local f = (C_Item and C_Item.GetItemIconByID) or GetItemIcon
  if not f then return nil end
  local ok, icon = pcall(f, id)
  return ok and icon or nil
end

local function showItemTip(self)
  if not (GameTooltip and self.itemId) then return end
  GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
  GameTooltip:SetHyperlink("item:" .. self.itemId .. ":" .. (self.enchantId or ""))
  GameTooltip:Show()
end

local function newGearRow()
  local r = CreateFrame("Frame", nil, gearPanel)
  r:SetSize(256, GEAR_ROW)
  r:EnableMouse(true)
  r.icon = r:CreateTexture(nil, "ARTWORK")
  r.icon:SetSize(24, 24)
  r.icon:SetPoint("LEFT", r, "LEFT", 0, 0)
  r.slotText = ui.text(r, "GameFontDisableSmall")
  r.slotText:SetPoint("LEFT", r, "LEFT", 30, 0)
  r.slotText:SetWidth(62)
  r.nameText = ui.text(r, "GameFontHighlightSmall")
  r.nameText:SetPoint("TOPLEFT", r, "TOPLEFT", 96, -3)
  r.nameText:SetWidth(160)
  r.enchantText = ui.text(r, "GameFontHighlightSmall")
  r.enchantText:SetPoint("TOPLEFT", r.nameText, "BOTTOMLEFT", 0, -2)
  r.enchantText:SetWidth(160)
  r.enchantText:SetTextColor(0.1, 1, 0.1)
  r:SetScript("OnEnter", showItemTip)
  r:SetScript("OnLeave", hideCharTip)
  return r
end

local function buildGearPanel()
  gearPanel = CreateFrame("Frame", GEAR_PANEL, window)
  gearPanel:SetFrameStrata("DIALOG")
  gearPanel:SetClampedToScreen(true)
  gearPanel:EnableMouse(true)
  gearPanel:SetPoint("TOPLEFT", window, "TOPRIGHT", 4, 0)
  if UISpecialFrames then table.insert(UISpecialFrames, GEAR_PANEL) end -- Esc closes it
  local bg = gearPanel:CreateTexture(nil, "BACKGROUND")
  bg:SetColorTexture(0.05, 0.05, 0.08, 0.95)
  bg:SetAllPoints()
  gearTitle = ui.text(gearPanel, "GameFontNormal")
  gearTitle:SetPoint("TOPLEFT", 12, -12)
  gearTitle:SetWidth(220)
  closeButton(gearPanel, function() gearPanel:Hide() end)
  noGear = ui.text(gearPanel, "GameFontDisableSmall", "No gear shared")
  noGear:SetPoint("TOPLEFT", 12, -40)
end

local function drawGear(c)
  gearKey = charKey(c)
  gearTitle:SetText(charTitle(c))
  local items = ns.friendGear(c)
  for i, item in ipairs(items) do
    local r = pooled(gearRows, i, newGearRow)
    r.itemId, r.enchantId = item.id, item.enchantId
    r.icon:SetTexture(itemIcon(item.id))
    r.slotText:SetText(SLOT_NAMES[item.slot] or "")
    local name, red, green, blue = itemName(item.id)
    r.nameText:SetText(name)
    r.nameText:SetTextColor(red, green, blue)
    r.enchantText:SetText(item.enchant or "")
    r.enchantText:SetShown(item.enchant ~= nil)
    r:ClearAllPoints()
    r:SetPoint("TOPLEFT", gearPanel, "TOPLEFT", 12, -36 - (i - 1) * GEAR_ROW)
    r:Show()
  end
  hideFrom(gearRows, #items + 1)
  noGear:SetShown(#items == 0)
  gearPanel:SetSize(280, 48 + math.max(1, #items) * GEAR_ROW)
  gearPanel:Show()
end

-- A click on a character line: opens its gear, or closes the panel when it already shows it.
local function toggleGear(self)
  local c = self.char
  if not c then return end
  if not gearPanel then buildGearPanel() end
  if gearPanel:IsShown() and gearKey == charKey(c) then gearPanel:Hide() else drawGear(c) end
end

local function charInfo(c)
  local parts = {}
  if c.level then parts[#parts + 1] = "level " .. c.level end
  if c.spec then parts[#parts + 1] = c.spec end
  if c.ilvl then parts[#parts + 1] = "ilvl " .. math.floor(c.ilvl) end
  if c.zone then parts[#parts + 1] = c.zone end
  parts[#parts + 1] = ago(c.received or c.at)
  return table.concat(parts, ", ")
end

local function buildFriends(p)
  local width = PAGE_WIDTH - 20
  ui.heading(p, "Friends")
  local status
  local find = ui.button(p, "Find online friends", 170, function()
    searched = true
    if ns.findOnlineFriends then ns.findOnlineFriends() end
    status:SetText("")
    drawFriends()
  end)
  place(find, p, 16, 38)
  local hint = ui.text(p, "GameFontDisableSmall", "Battle.net friends online in this game. Checking who has the addon sends them nothing they can see.")
  hint:SetWidth(ui.WRAP)
  place(hint, p, 16, 66)
  status = ui.text(p, "GameFontHighlightSmall")
  status:SetWidth(ui.WRAP)
  place(status, p, 16, 84)

  local function title(s) return ui.text(p, "GameFontNormal", s) end
  local function note(s)
    local fs = ui.text(p, "GameFontDisableSmall", s)
    fs:SetWidth(ui.WRAP)
    return fs
  end
  local requestsTitle, onlineTitle, sharedTitle = title("Requests"), title("Online friends"), title("Shared with you")
  local prompt = note("Click Find online friends to see who's on.")
  local noneOnline = note("None of your Battle.net friends are online in this game.")
  local noneShared = note("Nothing shared with you yet. Ask a friend above.")
  local requestRows, friendRows, sharedHeads, charLines = {}, {}, {}, {}

  -- Runs fn(tag) and shows what it said (ask/share return a line, invite ok and a line).
  local function act(fn, tag)
    return function()
      local a, b = fn(tag)
      status:SetText(type(a) == "string" and a or (type(b) == "string" and b or ""))
      drawFriends()
    end
  end

  local function newRequestRow()
    local r = CreateFrame("Frame", nil, p)
    r:SetSize(width, ROW)
    r.line = rowText(r, "GameFontHighlight", 0, 400)
    r.buttons = { rowButton(r, 64, 410), rowButton(r, 80, 478), rowButton(r, 64, 562) }
    return r
  end

  local function newFriendRow()
    local r = CreateFrame("Frame", nil, p)
    r:SetSize(width, ROW)
    r.nameText = rowText(r, "GameFontHighlight", 0, 120)
    r.levelText = rowText(r, "GameFontHighlightSmall", 124, 30)
    r.zoneText = rowText(r, "GameFontHighlightSmall", 158, 140)
    r.tagText = rowText(r, "GameFontHighlightSmall", 302, 130)
    r.stateText = rowText(r, "GameFontDisableSmall", 436, 80)
    r.b1, r.b2 = rowButton(r, 70, 518), rowButton(r, 64, 592) -- ends at 656 (+16 indent), inside the ~678 px scroll area
    return r
  end

  local function newCharLine()
    local l = CreateFrame("Frame", nil, p)
    l:SetSize(width - 8, 18)
    l:EnableMouse(true)
    l.nameText = rowText(l, "GameFontHighlight", 0, 120)
    l.infoText = rowText(l, "GameFontHighlightSmall", 124, 500)
    l:SetScript("OnEnter", showCharTip)
    l:SetScript("OnLeave", hideCharTip)
    l:SetScript("OnMouseUp", toggleGear)
    return l
  end

  local CHOICES = { { "Allow", "allow" }, { "Not now", "notnow" }, { "Never", "never" } }

  drawFriends = function()
    local y = 106

    local requests = rowsOf(ns.pendingRequests)
    if #requests > 0 then
      place(requestsTitle, p, 16, y)
      y = y + 20
    else
      requestsTitle:Hide()
    end
    for i, req in ipairs(requests) do
      local r = pooled(requestRows, i, newRequestRow)
      local who = (req.label and req.label ~= req.tag) and string.format("%s (%s)", req.label, req.tag) or req.tag
      r.line:SetText(who .. " wants to see your characters")
      for j, choice in ipairs(CHOICES) do
        setButton(r.buttons[j], choice[1], ns.answerFriendRequest and function()
          ns.answerFriendRequest(req, choice[2])
          drawFriends()
        end)
      end
      place(r, p, 16, y)
      y = y + ROW
    end
    hideFrom(requestRows, #requests + 1)
    if #requests > 0 then y = y + 10 end

    place(onlineTitle, p, 16, y)
    y = y + 20
    local online = searched and rowsOf(ns.onlineFriends) or {}
    prompt:Hide()
    noneOnline:Hide()
    if not searched then
      place(prompt, p, 16, y)
      y = y + 20
    elseif #online == 0 then
      place(noneOnline, p, 16, y)
      y = y + 20
    end
    for i, f in ipairs(online) do
      local r = pooled(friendRows, i, newFriendRow)
      r.nameText:SetText(f.character or "?")
      classColor(r.nameText, f.classFile)
      r.levelText:SetText(f.level and tostring(f.level) or "")
      r.zoneText:SetText(f.zone or "")
      r.tagText:SetText(f.label or f.tag or "")
      local state, first, second = friendState(f)
      r.stateText:SetText(state)
      for _, pair in ipairs({ { r.b1, first }, { r.b2, second } }) do
        local b, what = pair[1], pair[2]
        if what then setButton(b, what[1], what[2] and act(what[2], f.tag)) else b:Hide() end
      end
      place(r, p, 16, y)
      y = y + ROW
    end
    hideFrom(friendRows, #online + 1)
    y = y + 10

    place(sharedTitle, p, 16, y)
    y = y + 20
    local shared = rowsOf(ns.sharedWithYou)
    noneShared:Hide()
    if #shared == 0 then
      place(noneShared, p, 16, y)
      y = y + 20
    end
    local heads, lines = 0, 0
    local gearChar -- the open panel's character in the new data
    for _, friend in ipairs(shared) do
      heads = heads + 1
      local h = pooled(sharedHeads, heads, function() return ui.text(p, "GameFontNormalSmall") end)
      h:SetText(friend.label or friend.tag or "?")
      place(h, p, 16, y)
      y = y + 18
      for _, c in ipairs(friend.chars or {}) do
        lines = lines + 1
        local l = pooled(charLines, lines, newCharLine)
        l.char = c
        if charKey(c) == gearKey then gearChar = c end
        l.nameText:SetText(fullName(c))
        classColor(l.nameText, c.classFile)
        l.infoText:SetText(charInfo(c))
        place(l, p, 24, y)
        y = y + 18
      end
      y = y + 4
    end
    hideFrom(sharedHeads, heads + 1)
    hideFrom(charLines, lines + 1)
    if gearPanel and gearPanel:IsShown() then
      if gearChar then drawGear(gearChar) else gearPanel:Hide() end
    end

    p:SetHeight(y + 10)
  end
  ui.onRefresh(p, drawFriends)
end

-- Friends.lua calls this when a friend answers, asks, or sends something: redraws the tab if it's up.
function ns.friendsChanged()
  local t = tabs.friends
  if drawFriends and ns.windowShown() and t and t.holder and t.holder:IsShown() then drawFriends() end
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
  { key = "friends", label = "Friends", height = 600, build = buildFriends }, -- grows with its lists
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

  closeButton(window, function() window:Hide() end)

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
