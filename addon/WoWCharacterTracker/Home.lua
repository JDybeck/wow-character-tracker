-- The window's Home tab (/wct), like the app's Home page: the character you're playing on a banner
-- in its class's color (portrait, level, XP) with its numbers in a strip under it, its level times
-- (Leveling.lua) as a small chart and a level history that folds open, today's moments,
-- the newest moments of all your characters and the friends who share with you. Redrawn every few
-- seconds while it's shown, so played time ticks.
local ADDON, ns = ...
local ui = ns.ui
local C = ui.COLOR

local WIDTH = 680  -- the page's content width
local INNER = WIDTH - 28 -- inside the card
local CHART_BARS = 40
local CHART_HEIGHT = 36
local BAR_STEP = 12 -- a bar and its gap
local HISTORY_ROWS = 20
local ROW = 16
local FEED_SIZE = 6  -- rows shown per moment column
local FEED_MAX = 50  -- kept per column, the mouse wheel scrolls through them
local FRIENDS_SHOWN = 4
local FRIEND_ROW = 34 -- a friend's row (a small card) and the gap under it
local BANNER = 124   -- the playing-now banner's height
local STRIP = 52     -- the numbers under it
local PORTRAIT = 84
local ONLINE_ROWS = 4 -- friends in the banner's right side
local CIRCLE = "Interface\\CharacterFrame\\TempPortraitAlphaMask"
local GOLD = { 1, 0.82, 0, 1 }
local TODAY_MAX = 12 -- room for every stat Today.lua has
local REDRAW_EVERY = 2 -- seconds
local SLOW_EVERY = 15  -- redraws

local function db() return WoWCharacterTrackerDB or {} end

-- Whether the level history is open, kept with the window's other choices.
local function historyOpen() return (db().window or {}).homeHistory == true end
local function setHistoryOpen(on)
  local d = db()
  d.window = d.window or {}
  d.window.homeHistory = on
end

local function todayOpen() return (db().window or {}).homeToday == true end
local function setTodayOpen(on)
  local d = db()
  d.window = d.window or {}
  d.window.homeToday = on
end

-- Live numbers for the character being played, from the game where it's cheap, else its snapshot.
local function playing()
  local guid = UnitGUID("player")
  local snap = (db().chars or {})[guid] or {}
  local name = UnitName and UnitName("player") or snap.char
  local className, classFile = UnitClass("player")
  local maxLevel = (GetMaxPlayerLevel and GetMaxPlayerLevel()) or MAX_PLAYER_LEVEL or snap.maxLevel
  return {
    guid = guid,
    name = (snap.surname and snap.surname ~= "") and (name .. " " .. snap.surname) or name,
    level = UnitLevel("player"),
    race = UnitRace and UnitRace("player") or snap.race,
    class = className or snap.class,
    classFile = classFile or snap.classFile,
    zone = (GetRealZoneText and GetRealZoneText()) or snap.zone,
    ilvl = snap.ilvl,
    money = (GetMoney and GetMoney()) or snap.money,
    played = ns.playedNow() or snap.played,
    levelTime = ns.levelTimeNow(),
    xp = UnitXP and UnitXP("player"),
    xpMax = UnitXPMax and UnitXPMax("player"),
    maxLevel = maxLevel,
  }
end

local function midnight()
  local t = date("*t")
  return time({ year = t.year, month = t.month, day = t.day, hour = 0 })
end

local function setTile(t, value, label)
  t.value:SetText(value)
  t.label:SetText(label)
end

local hideTip = ns.hideTip

-- The content width the window gives now (Window.lua; it can be resized wider than WIDTH).
local function contentWidth() return ns.pageWidth and math.max(WIDTH, ns.pageWidth() - 30) or WIDTH end

-- Where the two moment columns meet, dragged by the player and kept with the window's choices as a
-- share of the width, so it scales with the window (homeSplit: pixels of the old fixed width).
local SPLIT_MIN = 180
local function split(width)
  local w = db().window or {}
  local share = tonumber(w.homeSplitShare) or (tonumber(w.homeSplit) and w.homeSplit / WIDTH) or 0.5
  return math.max(SPLIT_MIN, math.min(width - SPLIT_MIN, math.floor(share * width)))
end
local function setSplit(x, width)
  local d = db()
  d.window = d.window or {}
  d.window.homeSplitShare = math.max(SPLIT_MIN, math.min(width - SPLIT_MIN, x)) / width
end

-- The whole line on hover, for a moment cut short by its column.
local function showRowTip(self)
  if not (GameTooltip and self.full) then return end
  GameTooltip:SetOwner(self, "ANCHOR_TOP")
  GameTooltip:SetText(self.full, 1, 1, 1, 1, true)
  GameTooltip:Show()
end

-- Loot and mounts show the item's own tooltip, the rest the whole line.
local function rowEnter(self)
  self.hover:SetShown(self.open ~= nil)
  if self.itemId then return ui.itemTip(self, self.itemId, nil, "ANCHOR_CURSOR") end
  showRowTip(self)
end
local function rowLeave(self)
  self.hover:Hide()
  hideTip()
end
local function rowClick(self) if self.open then self.open() end end

local WHO = 120 -- room for a full name in a moment line
local ICON = 22 -- the moment's icon and its gap, before the name

-- A moment line: who, what (cut to the column) and when. A friend's opens their details.
local function newMomentRow(parent)
  local row = CreateFrame("Frame", nil, parent)
  row:SetHeight(18)
  row:EnableMouse(true)
  row.hover = row:CreateTexture(nil, "BACKGROUND")
  ui.fill(row.hover, C.hover)
  row.hover:SetAllPoints()
  row.hover:Hide()
  row.icon = row:CreateTexture(nil, "ARTWORK")
  row.icon:SetSize(16, 16)
  row.icon:SetPoint("LEFT", row, "LEFT", 0, 0)
  row.who = ui.text(row, "GameFontNormalSmall")
  row.who:SetPoint("LEFT", row, "LEFT", ICON, 0)
  row.who:SetWidth(WHO)
  row.who:SetWordWrap(false)
  row.what = ui.text(row, "GameFontHighlightSmall")
  row.what:SetPoint("LEFT", row, "LEFT", ICON + WHO + 4, 0)
  row.what:SetWordWrap(false)
  row.when = ui.text(row, "GameFontDisableSmall")
  row.when:SetPoint("RIGHT", row, "RIGHT", 0, 0)
  row:SetScript("OnEnter", rowEnter)
  row:SetScript("OnLeave", rowLeave)
  row:SetScript("OnMouseUp", rowClick)
  return row
end

-- The row's icon: the item's for loot and mounts, else the class's (a friend's row has a portrait instead).
local function setRowIcon(row, classFile, itemId)
  if row.portrait then return row.icon:Hide() end
  local icon = itemId and ns.itemIcon(itemId)
  if icon then
    row.icon:SetTexture(icon)
    row.icon:SetTexCoord(0.08, 0.92, 0.08, 0.92)
    row.icon:Show()
  else
    row.icon:SetShown(ui.classIcon(row.icon, classFile))
  end
end

-- open: what a click does (nil: nothing, no hover). itemId: the item the moment is about, for its tooltip.
local function setMomentRow(row, who, classFile, what, at, open, itemId)
  setRowIcon(row, classFile, itemId)
  row.who:SetText(who)
  row.who:SetTextColor(ui.classRgb(classFile))
  row.what:SetText(what)
  row.when:SetText(ns.ago(at))
  row.full, row.open, row.itemId = who .. ": " .. what, open, itemId
  row:Show()
end

local ITEM_KINDS = { loot = true, mount = true }
local function momentItem(e) return ITEM_KINDS[e.kind] and tonumber(e.itemId) or nil end

-- Shows col.size rows of col.list from col.offset (the mouse wheel moves it), and "1-6 of 50" when
-- there's more. col.size grows with a taller window.
local function showColumn(col)
  local list, size = col.list, col.size
  col.offset = math.max(0, math.min(col.offset or 0, #list - size))
  for i, row in ipairs(col.rows) do
    local item = i <= size and list[col.offset + i]
    if item then
      local e = item.e
      local open = item.c and function() ns.toggleFriendDetails(item.c, item.label) end or nil
      setMomentRow(row, ns.fullName(e), e.classFile, ns.momentLine(e), e.at, open, momentItem(e))
    else
      row:Hide()
    end
  end
  col.empty:SetShown(#list == 0)
  col.more:SetShown(#list > size)
  col.more:SetText(string.format("%d-%d of %d", col.offset + 1, math.min(col.offset + size, #list), #list))
end

-- A column of moments from list ({ e, c, label }: c and label for a friend's, whose click opens it).
local function fillColumn(col, list)
  col.list = list
  showColumn(col)
end

-- Scrolls col by delta (the wheel's: 1 up, -1 down); false at the end, so the page scrolls instead.
local function scrollColumn(col, delta)
  local before = col.offset or 0
  col.offset = before - delta
  showColumn(col)
  return col.offset ~= before
end

local function sizeMomentRow(row, width)
  row:SetWidth(width)
  row.what:SetWidth(math.max(40, width - ICON - WHO - 4 - 56))
end

-- events[i] as the feed shows it, or nil when it doesn't go to Discord: what LiveGrid's wanted()
-- says, a boss again only without first kills only (the app's IsRepeatKill), and a moment posted
-- from the ask box as the moment it was. Without the app every moment counts. repeats: repeatKills.
local function postedMoment(events, i, repeats)
  local e = events[i]
  if e.kind == "ask" then
    local shown = ns.copy(e)
    shown.kind = e.askedKind
    return shown
  end
  if not ns.setting("post") then return e end
  if ns.wanted and not ns.wanted(e) then return nil end
  if repeats[i] then return nil end
  return e
end

-- { [i] = true } for each boss kill a character had made before, in one pass; empty without first kills only.
local function repeatKills(events)
  local out = {}
  if not ns.setting("post") or ns.filters().firstKill == false then return out end
  local seen = {}
  for i = 1, #events do
    local e = events[i]
    if type(e) == "table" and e.kind == "boss" then
      local key = tostring(e.guid) .. "\0" .. tostring(e.boss)
      if seen[key] then out[i] = true end
      seen[key] = true
    end
  end
  return out
end

-- At y below the card's top edge.
local function at(w, card, y, x)
  w:ClearAllPoints()
  w:SetPoint("TOPLEFT", card, "TOPLEFT", 14 + (x or 0), -y)
end

-- Without the app (ns.hasApp): what it adds and where to get it, over the card, until Got it.
local APP_HINT = 62
local function appHintShown() return not ns.hasApp() and (db().window or {}).appHintHidden ~= true end

local function buildAppHint(p)
  local hint = CreateFrame("Frame", nil, p)
  hint:SetSize(WIDTH, APP_HINT - 10)
  hint:SetPoint("TOPLEFT", p, "TOPLEFT", 16, -12)
  ui.box(hint, C.request, C.requestBorder)
  local text = ui.text(hint, "GameFontHighlightSmall",
    "Discord posts, screenshots and the stream overlay need the free Character Tracker app on your PC. Get it here (click the link, then Ctrl+C):")
  text:SetPoint("TOPLEFT", hint, "TOPLEFT", 12, -8)
  text:SetWidth(WIDTH - 24)
  hint.text = text
  local link = ui.copyBox(hint, ns.APP_URL, 380)
  link:SetPoint("TOPLEFT", text, "BOTTOMLEFT", 0, -5)
  local gotIt = ui.button(hint, "Got it", 80, function()
    local d = db()
    d.window = d.window or {}
    d.window.appHintHidden = true
    hint:Hide()
    if hint.onHide then hint.onHide() end
  end)
  gotIt:SetPoint("BOTTOMRIGHT", hint, "BOTTOMRIGHT", -10, 8)
  return hint
end

function ns.buildHomeTab(p)
  local hint = buildAppHint(p)
  local hintHeight = 0
  -- Playing now: a banner in the class's color, its numbers in a strip under it ------------------
  local banner = CreateFrame("Frame", nil, p)
  banner:SetSize(WIDTH, BANNER)
  ui.box(banner, C.card, C.border)
  local tint = banner:CreateTexture(nil, "BORDER")
  tint:SetPoint("TOPLEFT", 1, -1)
  tint:SetPoint("BOTTOMRIGHT", -1, 1)
  -- The class's name, big and faint, in the banner's corner.
  local watermark = banner:CreateFontString(nil, "BORDER")
  watermark:SetFont(STANDARD_TEXT_FONT or "Fonts\\FRIZQT__.TTF", 46, "")
  watermark:SetTextColor(1, 1, 1, 0.05)
  watermark:SetPoint("BOTTOMRIGHT", banner, "BOTTOMRIGHT", -12, 6)

  local ring = banner:CreateTexture(nil, "ARTWORK")
  ring:SetTexture(CIRCLE)
  ring:SetSize(PORTRAIT + 6, PORTRAIT + 6)
  ring:SetPoint("LEFT", banner, "LEFT", 18, 0)
  local portrait = ui.portrait(banner, PORTRAIT)
  portrait:SetPoint("CENTER", ring, "CENTER")
  local classColor = { 1, 1, 1, 1 } -- the level badge's edge, the class's color
  local levelBadge = CreateFrame("Frame", nil, banner)
  levelBadge:SetSize(30, 17)
  levelBadge:SetPoint("BOTTOMRIGHT", ring, "BOTTOMRIGHT", 2, 2)
  levelBadge:SetFrameLevel((portrait:GetFrameLevel() or 1) + 2)
  ui.box(levelBadge, C.bar, classColor)
  local levelText = ui.text(levelBadge, "GameFontHighlightSmall")
  levelText:SetJustifyH("CENTER")
  levelText:SetPoint("CENTER", 0, 0)

  local TEXT_X = 18 + PORTRAIT + 6 + 18
  local dot = banner:CreateTexture(nil, "ARTWORK")
  dot:SetSize(6, 6)
  dot:SetColorTexture(0.3, 0.85, 0.4)
  dot:SetPoint("TOPLEFT", banner, "TOPLEFT", TEXT_X, -16)
  local nowLabel = ui.text(banner, "GameFontDisableSmall", "PLAYING NOW")
  nowLabel:SetPoint("LEFT", dot, "RIGHT", 6, 0)
  local name = ui.text(banner, "GameFontNormalHuge")
  name:SetPoint("TOPLEFT", banner, "TOPLEFT", TEXT_X, -28)
  local sub = ui.text(banner, "GameFontHighlight")
  sub:SetPoint("TOPLEFT", name, "BOTTOMLEFT", 0, -4)
  local zone = ui.text(banner, "GameFontDisableSmall")
  zone:SetPoint("TOPLEFT", sub, "BOTTOMLEFT", 0, -3)

  -- XP to the next level, with how long to the top at your pace beside it.
  local xpBar = CreateFrame("StatusBar", nil, banner)
  xpBar:SetSize(320, 8)
  xpBar:SetPoint("TOPLEFT", zone, "BOTTOMLEFT", 0, -9)
  xpBar:SetStatusBarTexture("Interface\\Buttons\\WHITE8X8")
  xpBar:SetStatusBarColor(0.58, 0.38, 0.96)
  local track = xpBar:CreateTexture(nil, "BACKGROUND")
  track:SetAllPoints()
  track:SetColorTexture(1, 1, 1, 0.08)
  local xpText = ui.text(banner, "GameFontDisableSmall")
  xpText:SetPoint("TOPLEFT", xpBar, "BOTTOMLEFT", 0, -4)
  local estimate = ui.text(banner, "GameFontDisableSmall")
  estimate:SetJustifyH("RIGHT")
  estimate:SetPoint("TOPRIGHT", xpBar, "BOTTOMRIGHT", 0, -4)

  local online = CreateFrame("Frame", nil, banner)
  online:SetPoint("TOPRIGHT", banner, "TOPRIGHT", -14, -14)
  online:SetSize(240, BANNER - 28)
  online.title = ui.text(online, "GameFontDisableSmall", "FRIENDS ONLINE")
  online.title:SetPoint("TOPLEFT", online, "TOPLEFT", 0, 0)
  online.rows = {}
  for i = 1, ONLINE_ROWS do
    local row = CreateFrame("Button", nil, online)
    row:SetHeight(20)
    row:SetPoint("TOPLEFT", online, "TOPLEFT", 0, -14 - (i - 1) * 22)
    local hover = row:CreateTexture(nil, "HIGHLIGHT")
    ui.fill(hover, C.hover)
    hover:SetAllPoints()
    row.portrait = ui.portrait(row, 18)
    row.portrait:SetPoint("LEFT", row, "LEFT", 0, 0)
    row.who = ui.text(row, "GameFontHighlightSmall")
    row.who:SetPoint("LEFT", row, "LEFT", 24, 0)
    row.who:SetWidth(100)
    row.who:SetWordWrap(false)
    row.what = ui.text(row, "GameFontDisableSmall")
    row.what:SetPoint("LEFT", row, "LEFT", 128, 0)
    row.what:SetWordWrap(false)
    row:SetScript("OnClick", function() ns.openWindow("friends") end)
    online.rows[i] = row
  end

  local strip = CreateFrame("Frame", nil, p)
  strip:SetSize(WIDTH, STRIP)
  strip:SetPoint("TOPLEFT", banner, "BOTTOMLEFT", 0, 1)
  ui.box(strip, C.bar, C.border)
  local tiles, dividers = {}, {}
  for i = 1, 3 do
    local t = CreateFrame("Frame", nil, strip)
    t:SetHeight(STRIP)
    t.value = ui.text(t, "GameFontHighlightLarge")
    t.value:SetPoint("TOPLEFT", t, "TOPLEFT", 18, -10)
    t.label = ui.text(t, "GameFontDisableSmall")
    t.label:SetPoint("TOPLEFT", t.value, "BOTTOMLEFT", 0, -3)
    tiles[i] = t
    if i > 1 then
      local d = strip:CreateTexture(nil, "BORDER")
      ui.fill(d, C.border)
      d:SetWidth(1)
      dividers[i] = d
    end
  end
  ui.tint(tiles[2].value, GOLD)
  local function placeStrip(w)
    local cell = math.floor(w / 3)
    for i, t in ipairs(tiles) do
      t:ClearAllPoints()
      t:SetPoint("TOPLEFT", strip, "TOPLEFT", (i - 1) * cell, 0)
      t:SetWidth(cell)
      local d = dividers[i]
      if d then
        d:ClearAllPoints()
        d:SetPoint("TOPLEFT", strip, "TOPLEFT", (i - 1) * cell, -1)
        d:SetPoint("BOTTOMLEFT", strip, "BOTTOMLEFT", (i - 1) * cell, 1)
      end
    end
    local xpWidth = math.max(160, math.min(300, w - TEXT_X - 150))
    xpBar:SetWidth(xpWidth)
    -- Friends online (or seen last) in the banner's right side, when there's room for them.
    local room = w - TEXT_X - xpWidth - 40
    online.fits = room >= 200
    online:SetWidth(math.min(300, math.max(200, room)))
    for _, row in ipairs(online.rows) do
      row:SetWidth(online:GetWidth() or 200)
      row.what:SetWidth((online:GetWidth() or 200) - 26 - 104)
    end
  end
  placeStrip(WIDTH)

  -- Under them, the card with the level times and today.
  local card = CreateFrame("Frame", nil, p)
  local function placeCard()
    local shown = appHintShown()
    hint:SetShown(shown)
    hintHeight = shown and APP_HINT or 0
    banner:ClearAllPoints()
    banner:SetPoint("TOPLEFT", p, "TOPLEFT", 16, -12 - hintHeight)
  end
  card:SetPoint("TOPLEFT", strip, "BOTTOMLEFT", 0, -10)
  placeCard()
  card:SetSize(WIDTH, 300)
  ui.box(card, C.card, C.border)

  local pace = ui.text(card, "GameFontHighlightSmall")
  at(pace, card, 14)
  pace:SetWidth(INNER)

  -- The mini chart (Ui.lua): a bar per level, the last CHART_BARS.
  local chart = ui.levelChart(card, CHART_BARS, CHART_HEIGHT, BAR_STEP)

  -- The level history, folded until opened: the numbers, then the newest levels.
  local layout -- below
  local toggle = ui.button(card, "", 150, function()
    setHistoryOpen(not historyOpen())
    layout()
  end)
  local factLines = {}
  for i = 1, 2 do
    local fs = ui.text(card, "GameFontHighlightSmall")
    fs:SetWidth(INNER)
    factLines[i] = fs
  end
  local history = {}
  for i = 1, HISTORY_ROWS do
    local row = CreateFrame("Frame", nil, card)
    row:SetSize(INNER, ROW)
    row.level = ui.text(row, "GameFontHighlightSmall")
    row.level:SetPoint("LEFT", row, "LEFT", 0, 0)
    row.took = ui.text(row, "GameFontHighlightSmall")
    row.took:SetPoint("LEFT", row, "LEFT", 80, 0)
    row.took:SetTextColor(0.45, 0.85, 0.5)
    row.zone = ui.text(row, "GameFontDisableSmall")
    row.zone:SetPoint("LEFT", row, "LEFT", 170, 0)
    row.zone:SetWidth(INNER - 170 - 80)
    row.zone:SetWordWrap(false)
    row.when = ui.text(row, "GameFontDisableSmall")
    row.when:SetPoint("RIGHT", row, "RIGHT", 0, 0)
    history[i] = row
  end

  local line = card:CreateTexture(nil, "ARTWORK")
  ui.fill(line, C.border)
  line:SetHeight(1)
  -- Today: the four that matter most (Today.lua ns.todayStats), the rest folded behind "+N more".
  local todayLabel = ui.text(card, "GameFontDisableSmall", "TODAY")
  local today, todayStats = {}, {}
  local todayWidth = math.floor(INNER / ns.TODAY_SHOWN) -- fit() changes it with the window's width
  for i = 1, TODAY_MAX do today[i] = ui.text(card, "GameFontHighlight") end
  local todayToggle = ui.button(card, "", 90, function()
    setTodayOpen(not todayOpen())
    layout()
  end)

  -- The newest moments: yours on the left, friends' on the right, a bar between them to drag ------
  local moments = CreateFrame("Frame", nil, p)
  moments:SetSize(WIDTH, 26 + FEED_SIZE * 20)
  moments:SetPoint("TOPLEFT", card, "BOTTOMLEFT", 0, -18)
  local function column(title, empty)
    local col = { title = ui.text(moments, "GameFontNormal", title), rows = {}, list = {}, offset = 0, size = FEED_SIZE }
    ui.tint(col.title, C.accent)
    col.empty = ui.text(moments, "GameFontDisableSmall", empty)
    col.more = ui.text(moments, "GameFontDisableSmall")
    for i = 1, FEED_SIZE do col.rows[i] = newMomentRow(moments) end
    return col
  end
  local mine = column("Latest moments", "Nothing yet. Level-ups, boss kills and loot show up here.")
  local theirs = column("Friends' moments", "Nothing from friends yet.")
  local divider = CreateFrame("Frame", nil, moments)
  divider:SetWidth(10)
  divider:EnableMouse(true)
  local dividerLine = divider:CreateTexture(nil, "ARTWORK")
  ui.fill(dividerLine, C.border)
  dividerLine:SetWidth(1)
  dividerLine:SetPoint("TOP")
  dividerLine:SetPoint("BOTTOM")

  local function placeColumn(col, x, w)
    col.title:ClearAllPoints()
    col.title:SetPoint("TOPLEFT", moments, "TOPLEFT", x, 0)
    col.empty:ClearAllPoints()
    col.empty:SetPoint("TOPLEFT", moments, "TOPLEFT", x, -24)
    col.empty:SetWidth(w)
    col.more:ClearAllPoints()
    col.more:SetPoint("TOPRIGHT", moments, "TOPLEFT", x + w, -2)
    for i, row in ipairs(col.rows) do
      row:ClearAllPoints()
      row:SetPoint("TOPLEFT", moments, "TOPLEFT", x, -24 - (i - 1) * 20)
      sizeMomentRow(row, w)
    end
  end
  local width = WIDTH -- what the page is laid out for now (fit)
  local function placeMoments()
    local s = split(width)
    placeColumn(mine, 0, s - 8)
    placeColumn(theirs, s + 8, width - s - 8)
    divider:ClearAllPoints()
    divider:SetPoint("TOPLEFT", moments, "TOPLEFT", s - 5, 0)
    divider:SetPoint("BOTTOMLEFT", moments, "BOTTOMLEFT", s - 5, 0)
  end
  placeMoments()
  -- A taller window gives the moments its extra room: n rows per column, built as they're needed.
  local feedRows = FEED_SIZE
  local function setFeedRows(n)
    if n == feedRows then return end
    feedRows = n
    for _, col in ipairs({ mine, theirs }) do
      col.size = n
      for i = #col.rows + 1, n do col.rows[i] = newMomentRow(moments) end
    end
    moments:SetHeight(26 + n * 20)
    placeMoments()
    showColumn(mine)
    showColumn(theirs)
  end
  -- Dragging the bar moves the split; the hand cursor and a lighter line say it can be dragged.
  divider:SetScript("OnEnter", function()
    ui.fill(dividerLine, C.accent)
    if SetCursor then SetCursor("Interface\\CURSOR\\UI-Cursor-SizeRight") end
  end)
  divider:SetScript("OnLeave", function()
    if not divider.dragging then ui.fill(dividerLine, C.border) end
    if ResetCursor then ResetCursor() end
  end)
  divider:SetScript("OnMouseDown", function(self)
    self.dragging = true
    self:SetScript("OnUpdate", function()
      local x = GetCursorPosition() / (moments:GetEffectiveScale() or 1)
      setSplit(x - (moments:GetLeft() or 0), width)
      placeMoments()
    end)
  end)
  divider:SetScript("OnMouseUp", function(self)
    self.dragging = false
    self:SetScript("OnUpdate", nil)
    ui.fill(dividerLine, C.border)
  end)
  -- The wheel scrolls the column under the cursor; past its ends, or without more, the page scrolls.
  moments:EnableMouseWheel(true)
  moments:SetScript("OnMouseWheel", function(_, delta)
    local x = GetCursorPosition() / (moments:GetEffectiveScale() or 1) - (moments:GetLeft() or 0)
    if scrollColumn(x < split(width) and mine or theirs, delta) then return end
    local page = p:GetParent()
    if page and page.GetVerticalScrollRange then ui.scrollBy(page, delta) end
  end)

  -- Friends who share with you, the one seen last first -----------------------------------------
  local friendsTitle = ui.heading(p, "Friends")
  friendsTitle:ClearAllPoints()
  friendsTitle:SetPoint("TOPLEFT", moments, "BOTTOMLEFT", 0, -16)
  local openFriends = ui.button(p, "Friends tab", 110, function() ns.openWindow("friends") end)
  openFriends:SetPoint("BOTTOMRIGHT", friendsTitle, "BOTTOMLEFT", WIDTH, -4)
  local noFriends = ui.text(p, "GameFontDisableSmall", "Nobody shares with you yet. Ask a friend on the Friends tab, they see your characters and level times too when you share.")
  noFriends:SetPoint("TOPLEFT", friendsTitle, "BOTTOMLEFT", 0, -6)
  noFriends:SetWidth(WIDTH)
  local friendRows = {}
  for i = 1, FRIENDS_SHOWN do
    -- A moment row with room for the name; a click opens the character like on the Friends tab.
    -- A small card: the class's color down its left edge, then the portrait.
    local row = newMomentRow(p)
    row:SetHeight(FRIEND_ROW - 4)
    row:SetPoint("TOPLEFT", friendsTitle, "BOTTOMLEFT", 0, -8 - (i - 1) * FRIEND_ROW)
    ui.box(row, C.card)
    row.hover:SetDrawLayer("BACKGROUND", 1)
    row.stripe = row:CreateTexture(nil, "ARTWORK")
    row.stripe:SetWidth(3)
    row.stripe:SetPoint("TOPLEFT")
    row.stripe:SetPoint("BOTTOMLEFT")
    row.portrait = ui.portrait(row, 24)
    row.portrait:SetPoint("LEFT", row, "LEFT", 12, 0)
    row.who:ClearAllPoints()
    row.who:SetPoint("LEFT", row, "LEFT", 44, 0)
    row.who:SetWidth(170)
    row.what:ClearAllPoints()
    row.what:SetPoint("LEFT", row, "LEFT", 220, 0)
    row.what:SetFontObject("GameFontDisableSmall")
    row.when:ClearAllPoints()
    row.when:SetPoint("RIGHT", row, "RIGHT", -10, 0)
    row:SetWidth(WIDTH)
    row.what:SetWidth(WIDTH - 220 - 80)
    friendRows[i] = row
  end
  local BELOW_CARD = 18 + 26 + 16 + 26 + FRIENDS_SHOWN * FRIEND_ROW + 20 -- the moments' title and the friends under the card, without the moment rows

  -- Everything sized for the window's width, again when it changed (the window can be resized).
  local function fit()
    local w = contentWidth()
    if w == width then return end
    width = w
    local inner = w - 28
    hint:SetWidth(w)
    hint.text:SetWidth(w - 24)
    banner:SetWidth(w)
    strip:SetWidth(w)
    placeStrip(w)
    card:SetWidth(w)
    for _, fs in ipairs({ pace, factLines[1], factLines[2] }) do fs:SetWidth(inner) end
    for _, row in ipairs(history) do
      row:SetWidth(inner)
      row.zone:SetWidth(inner - 170 - 80)
    end
    todayWidth = math.floor(inner / ns.TODAY_SHOWN)
    moments:SetWidth(w)
    placeMoments()
    openFriends:ClearAllPoints()
    openFriends:SetPoint("BOTTOMRIGHT", friendsTitle, "BOTTOMLEFT", w, -4)
    noFriends:SetWidth(w)
    for _, row in ipairs(friendRows) do
      row:SetWidth(w)
      row.what:SetWidth(w - 220 - 80)
    end
  end

  -- What only changes with new events is worked out again only then (the redraw runs every few
  -- seconds, and the list holds thousands): level times, today's counts and the feed.
  local seenList, seenCount, seenGuid
  local levels, summary = {}, nil

  -- Places what's shown in the card from the top down, and sizes the card and the page.
  layout = function()
    fit()
    placeCard()
    local y = 34
    local open = historyOpen()
    chart:SetShown(summary ~= nil)
    toggle:SetShown(summary ~= nil)
    if summary then
      at(chart, card, y + 4)
      y = y + 4 + CHART_HEIGHT + 10
      toggle:SetText(open and "Level history  ^" or "Level history  v")
      at(toggle, card, y)
      y = y + 22 + 8
    end
    local shownRows = 0
    for i, fs in ipairs(factLines) do
      local show = summary ~= nil and open and (fs:GetText() or "") ~= ""
      fs:SetShown(show)
      if show then
        at(fs, card, y)
        y = y + ROW
      end
    end
    if summary and open then
      y = y + 4
      for i = 1, HISTORY_ROWS do
        local l = levels[#levels - i + 1]
        local row = history[i]
        if l then
          row.level:SetText(string.format("%d to %d", l.level - 1, l.level))
          row.took:SetText(ns.formatDuration(l.seconds))
          row.zone:SetText(l.zone or "")
          row.when:SetText((l.at or 0) > 0 and ns.ago(l.at) or "")
          at(row, card, y)
          row:Show()
          y = y + ROW
          shownRows = shownRows + 1
        else
          row:Hide()
        end
      end
    end
    for i = shownRows + 1, HISTORY_ROWS do history[i]:Hide() end

    line:ClearAllPoints()
    line:SetPoint("TOPLEFT", card, "TOPLEFT", 14, -(y + 6))
    line:SetPoint("TOPRIGHT", card, "TOPRIGHT", -14, -(y + 6))
    at(todayLabel, card, y + 14)
    local more = #todayStats - ns.TODAY_SHOWN
    todayToggle:SetShown(more > 0)
    if more > 0 then
      todayToggle:SetText(todayOpen() and "Less  ^" or string.format("+%d more  v", more))
      todayToggle:ClearAllPoints()
      todayToggle:SetPoint("TOPRIGHT", card, "TOPRIGHT", -14, -(y + 10))
    end
    local shown = todayOpen() and #todayStats or math.min(#todayStats, ns.TODAY_SHOWN)
    for i, fs in ipairs(today) do
      fs:SetShown(i <= shown)
      if i <= shown then
        local col, row = (i - 1) % ns.TODAY_SHOWN, math.floor((i - 1) / ns.TODAY_SHOWN)
        at(fs, card, y + 30 + row * 22, col * todayWidth)
      end
    end
    local bottom = y + 56 + (math.ceil(math.max(shown, 1) / ns.TODAY_SHOWN) - 1) * 22
    card:SetHeight(bottom)
    -- What the window shows below the rest goes to more moment rows, so a tall window has no empty bottom.
    local rest = 12 + hintHeight + BANNER + STRIP + 9 + bottom + BELOW_CARD
    local room = (ns.pageHeight and ns.pageHeight() or 0) - rest - FEED_SIZE * 20
    setFeedRows(math.max(FEED_SIZE, math.min(FEED_MAX, FEED_SIZE + math.floor(room / 20))))
    p:SetHeight(rest + feedRows * 20)
  end
  hint.onHide = layout

  -- The parts that change while you play: name, tiles and the estimate.
  local function drawLive(me)
    local r, g, b = ui.classRgb(me.classFile)
    ui.gradient(tint, r, g, b, 0.30, 0.02)
    ring:SetVertexColor(r, g, b)
    classColor[1], classColor[2], classColor[3] = r, g, b
    ui.box(levelBadge, C.bar, classColor)
    ui.setPortrait(portrait, { guid = me.guid, classFile = me.classFile })
    levelText:SetText(me.level or "?")
    watermark:SetText(me.class and string.upper(me.class) or "")
    name:SetText(me.name or "?")
    name:SetTextColor(r, g, b)
    sub:SetText(string.format("%s %s", me.race or "", me.class or ""))
    zone:SetText(me.zone or "")
    local atMax = me.maxLevel and me.level and me.level >= me.maxLevel

    local hasXp = not atMax and me.level ~= nil and (me.xpMax or 0) > 0
    xpBar:SetShown(hasXp)
    if hasXp then
      xpBar:SetMinMaxValues(0, me.xpMax)
      xpBar:SetValue(me.xp or 0)
      local line = string.format("%d%% to %d", math.floor(100 * (me.xp or 0) / me.xpMax), me.level + 1)
      if me.levelTime then line = line .. "  ·  " .. ns.formatDuration(me.levelTime) .. " on this level" end
      xpText:SetText(line)
    else
      xpText:SetText(atMax and "Max level" or "")
    end

    setTile(tiles[1], me.ilvl and tostring(math.floor(me.ilvl)) or "?", "Item level")
    setTile(tiles[2], me.money and ui.gold(me.money) or "?", "Gold")
    setTile(tiles[3], me.played and ns.formatPlayed(me.played) or "?", "Played")

    -- The chart, with the level being played as a paler last bar.
    chart.draw(levels, not atMax and me.levelTime and me.level and { level = me.level + 1, seconds = me.levelTime } or nil)
    local left = not atMax and not ns.IS_RETAIL and ns.timeToMax(levels, db().xpTable, me.level, me.xp, me.xpMax, me.maxLevel)
    estimate:SetText(left and string.format("About %s played to %d at your pace", ns.formatPlayed(left), me.maxLevel) or "")
  end

  -- The parts that come from the events: level times, today's counts, the chart and the feed.
  local function drawEvents(events, guid)
    levels = ns.levelTimes(events, guid)
    summary = ns.levelSummary(levels)
    local paceParts = {}
    if #levels > 0 then paceParts[#paceParts + 1] = ns.levelText(levels[#levels]) end
    if summary then paceParts[#paceParts + 1] = "average " .. ns.formatDuration(summary.average) end
    pace:SetText(#paceParts > 0 and table.concat(paceParts, "  ·  ") or "No level times yet. The next ding starts the clock.")

    factLines[1]:SetText(summary and string.format("%d timed level%s, average %s", summary.count, summary.count == 1 and "" or "s",
      ns.formatDuration(summary.average)) or "")
    factLines[2]:SetText(summary and summary.count > 1 and string.format("Slowest %s (%d to %d)",
      ns.formatDuration(summary.slowest.seconds), summary.slowest.level - 1, summary.slowest.level) or "")

    -- Your newest moments that go to Discord, with a line (ns.momentLine), any of your characters.
    local list, repeats = {}, repeatKills(events)
    for i = #events, 1, -1 do
      if #list >= FEED_MAX then break end
      local e = type(events[i]) == "table" and postedMoment(events, i, repeats)
      if e and ns.momentLine and ns.momentLine(e) then list[#list + 1] = { e = e } end
    end
    fillColumn(mine, list)
  end

  -- Friends: each one's newest character, and the newest of their characters' highlights.
  local function drawFriends()
    local latest, happened = {}, {}
    for _, f in ipairs(ns.sharedWithYou and ns.sharedWithYou() or {}) do
      local label = f.label or f.tag
      local c = f.chars and f.chars[1]
      if c then latest[#latest + 1] = { label = label, c = c, at = c.received or c.at or 0 } end
      for _, char in ipairs(f.chars or {}) do
        for _, e in ipairs(ns.friendHighlights(char)) do
          e.char, e.surname, e.classFile = char.char, char.surname, char.classFile
          if ns.momentLine(e) then happened[#happened + 1] = { e = e, c = char, label = label } end
        end
      end
    end
    table.sort(happened, function(a, b) return a.e.at > b.e.at end)
    for i = #happened, FEED_MAX + 1, -1 do happened[i] = nil end
    fillColumn(theirs, happened)

    table.sort(latest, function(a, b) return a.at > b.at end)
    for i, row in ipairs(friendRows) do
      local f = latest[i]
      if f then
        local c = f.c
        local bits = { string.format("%s %s", c.level or "?", c.class or "") }
        local fl = ns.friendLevels(c)
        if #fl > 0 then bits[#bits + 1] = ns.levelText(fl[#fl]) end
        if c.zone then bits[#bits + 1] = c.zone end
        bits[#bits + 1] = f.label
        setMomentRow(row, ns.fullName(c), c.classFile, table.concat(bits, "  ·  "), f.at, function() ns.toggleFriendDetails(c, f.label) end)
        row.stripe:SetColorTexture(ui.classRgb(c.classFile))
        ui.setPortrait(row.portrait, c)
      else
        row:Hide()
      end
    end
    noFriends:SetShown(#latest == 0)

    -- The banner's friends: who's online now, else the ones seen last.
    local shown, title = {}, "FRIENDS ONLINE"
    for _, f in ipairs(ns.onlineFriends and ns.onlineFriends() or {}) do
      if f.character and f.character ~= "" then
        shown[#shown + 1] = { name = f.character, classFile = f.classFile, guid = f.guid, level = f.level, where = f.zone }
      end
    end
    if #shown == 0 then
      title = "SEEN LAST"
      for _, f in ipairs(latest) do
        shown[#shown + 1] = { name = ns.fullName(f.c), classFile = f.c.classFile, guid = f.c.guid, level = f.c.level, where = ns.ago(f.at) }
      end
    end
    online.title:SetText(title)
    online:SetShown(online.fits and #shown > 0)
    watermark:SetShown(not online:IsShown())
    for i, row in ipairs(online.rows) do
      local f = shown[i]
      row:SetShown(f ~= nil)
      if f then
        ui.setPortrait(row.portrait, f)
        row.who:SetText(f.name or "?")
        row.who:SetTextColor(ui.classRgb(f.classFile))
        local bits = {}
        if f.level then bits[#bits + 1] = tostring(f.level) end
        if f.where and f.where ~= "" then bits[#bits + 1] = f.where end
        row.what:SetText(table.concat(bits, "  ·  "))
      end
    end
  end
  -- all: everything (opening the tab, and every SLOW_EVERY redraws for the "ago" times and friends).
  local function draw(all)
    local me = playing()
    local events = db().events or {}
    if all or events ~= seenList or #events ~= seenCount or me.guid ~= seenGuid then
      seenList, seenCount, seenGuid = events, #events, me.guid
      drawEvents(events, me.guid)
      -- Today: with new events, and every slow redraw for quests, gold and gathering.
      todayStats = ns.todayStats(events, db().today, date("%Y-%m-%d"), midnight(), ns.IS_RETAIL)
      for i, fs in ipairs(today) do
        local s = todayStats[i]
        fs:SetText(s and string.format("%s |cff9a9a9a%s|r", ns.todayValue(s), s.name) or "")
      end
    end
    if all then drawFriends() end
    drawLive(me)
    layout()
  end

  ui.onRefresh(p, function() draw(true) end)
  local waited, redraws = 0, 0
  p:SetScript("OnUpdate", function(_, elapsed)
    waited = waited + elapsed
    if waited < REDRAW_EVERY then return end
    waited, redraws = 0, redraws + 1
    draw(redraws % SLOW_EVERY == 0)
  end)
end
