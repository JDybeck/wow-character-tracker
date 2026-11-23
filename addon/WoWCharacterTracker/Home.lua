-- The window's Home tab (/wct), laid out like the app's Home page in two columns. On the left, the
-- character you're playing (portrait on a class-tinted card, XP, its numbers, its level times from
-- Leveling.lua as a small chart, a level history that folds open, and today), and under it the
-- friends who share with you, the one seen last first. On the right, your newest moments and your
-- friends'. Redrawn every few seconds while it's shown, so played time ticks.
local ADDON, ns = ...
local ui = ns.ui
local C = ui.COLOR

local WIDTH = 680  -- the page's content width at the window's first size
local GAP = 10     -- between the columns, and between the cards in a column
local PAD = 12     -- inside a card
local CHART_BARS = 30
local CHART_HEIGHT = 30
local BAR_STEP = 9 -- a bar and its gap
local HISTORY_ROWS = 20
local ROW = 16
local MOMENT_ROW = 34 -- an icon and two lines
local FEED_SIZE = 6   -- moment rows per list at least; a taller page gets more
local FEED_MAX = 50   -- kept per list, the mouse wheel scrolls through them
local FRIENDS_SHOWN = 6
local FRIEND_ROW = 32
local PORTRAIT = 60
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

-- The content width the window gives now (Window.lua; it can be resized wider than WIDTH).
local function contentWidth() return ns.pageWidth and math.max(WIDTH, ns.pageWidth() - 30) or WIDTH end
-- The left column's width for a page this wide (the right one takes the rest).
local function leftWidth(w) return math.floor((w - GAP) * 0.48) end

-- A moment: its icon (the item's, else the class's), the line, and who and when under it. A
-- friend's opens their character.
local function newMomentRow(parent)
  local row = CreateFrame("Frame", nil, parent)
  row:SetHeight(MOMENT_ROW - 2)
  row:EnableMouse(true)
  row.hover = row:CreateTexture(nil, "BACKGROUND")
  ui.fill(row.hover, C.hover)
  row.hover:SetAllPoints()
  row.hover:Hide()
  row.icon = row:CreateTexture(nil, "ARTWORK")
  row.icon:SetSize(24, 24)
  row.icon:SetPoint("LEFT", row, "LEFT", 0, 0)
  row.what = ui.text(row, "GameFontHighlightSmall")
  row.what:SetPoint("TOPLEFT", row, "TOPLEFT", 32, -3)
  row.what:SetWordWrap(false)
  row.sub = ui.text(row, "GameFontDisableSmall")
  row.sub:SetPoint("TOPLEFT", row.what, "BOTTOMLEFT", 0, -3)
  row.sub:SetWordWrap(false)
  row:SetScript("OnEnter", rowEnter)
  row:SetScript("OnLeave", rowLeave)
  row:SetScript("OnMouseUp", rowClick)
  return row
end

local function sizeMomentRow(row, width)
  row:SetWidth(width)
  row.what:SetWidth(math.max(40, width - 32))
  row.sub:SetWidth(math.max(40, width - 32))
end

local function setRowIcon(row, classFile, itemId)
  local icon = itemId and ns.itemIcon(itemId)
  if icon then
    row.icon:SetTexture(icon)
    row.icon:SetTexCoord(0.08, 0.92, 0.08, 0.92)
    row.icon:Show()
  else
    row.icon:SetShown(ui.classIcon(row.icon, classFile))
  end
end

local function hexColor(classFile)
  local r, g, b = ui.classRgb(classFile)
  return string.format("|cff%02x%02x%02x", r * 255, g * 255, b * 255)
end

-- open: what a click does (nil: nothing, no hover). itemId: the item the moment is about, for its
-- tooltip. label: whose friend it is, beside the time.
local function setMomentRow(row, who, classFile, what, at, open, itemId, label)
  setRowIcon(row, classFile, itemId)
  row.what:SetText(what)
  local parts = { hexColor(classFile) .. (who or "?") .. "|r" }
  if label and label ~= "" then parts[#parts + 1] = label end
  parts[#parts + 1] = ns.ago(at)
  row.sub:SetText(table.concat(parts, "  ·  "))
  row.full, row.open, row.itemId = (who or "?") .. ": " .. what, open, itemId
  row:Show()
end

local ITEM_KINDS = { loot = true, mount = true }
local function momentItem(e) return ITEM_KINDS[e.kind] and tonumber(e.itemId) or nil end

-- Shows col.size rows of col.list from col.offset (the mouse wheel moves it), and "1-6 of 50" when
-- there's more. col.size grows with a taller page.
local function showColumn(col)
  local list, size = col.list, col.size
  col.offset = math.max(0, math.min(col.offset or 0, #list - size))
  for i, row in ipairs(col.rows) do
    local item = i <= size and list[col.offset + i]
    if item then
      local e = item.e
      local open = item.c and function() ns.toggleFriendDetails(item.c, item.label) end or nil
      setMomentRow(row, ns.fullName(e), e.classFile, ns.momentLine(e), e.at, open, momentItem(e), item.label)
    else
      row:Hide()
    end
  end
  col.empty:SetShown(#list == 0)
  col.more:SetShown(#list > size)
  col.more:SetText(string.format("%d-%d of %d", col.offset + 1, math.min(col.offset + size, #list), #list))
end

-- A list of moments from list ({ e, c, label }: c and label for a friend's, whose click opens it).
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


-- A card: the theme's card color with a thin edge.
local function newCard(p)
  local card = CreateFrame("Frame", nil, p)
  card:SetSize(WIDTH, 100)
  ui.box(card, C.card, C.border)
  return card
end

-- A small grey heading in a card, at y below its top (right: on the right edge instead).
local function cardLabel(card, s, y, right)
  local fs = ui.text(card, "GameFontDisableSmall", s)
  if right then fs:SetPoint("TOPRIGHT", card, "TOPRIGHT", -PAD, -y)
  else fs:SetPoint("TOPLEFT", card, "TOPLEFT", PAD, -y) end
  return fs
end

-- A friend who shares with you: portrait, name and what they're on, whose friend and when seen.
local function newFriendRow(parent)
  local row = CreateFrame("Frame", nil, parent)
  row:SetHeight(FRIEND_ROW - 2)
  row:EnableMouse(true)
  row.hover = row:CreateTexture(nil, "BACKGROUND")
  ui.fill(row.hover, C.hover)
  row.hover:SetAllPoints()
  row.hover:Hide()
  row.portrait = ui.portrait(row, 24)
  row.portrait:SetPoint("LEFT", row, "LEFT", 0, 0)
  row.who = ui.text(row, "GameFontHighlightSmall")
  row.who:SetPoint("TOPLEFT", row, "TOPLEFT", 32, -2)
  row.who:SetWordWrap(false)
  row.what = ui.text(row, "GameFontDisableSmall")
  row.what:SetPoint("TOPLEFT", row.who, "BOTTOMLEFT", 0, -3)
  row.what:SetWordWrap(false)
  row.label = ui.text(row, "GameFontHighlightSmall")
  row.label:SetJustifyH("RIGHT")
  row.label:SetPoint("TOPRIGHT", row, "TOPRIGHT", 0, -2)
  row.when = ui.text(row, "GameFontDisableSmall")
  row.when:SetJustifyH("RIGHT")
  row.when:SetPoint("TOPRIGHT", row.label, "BOTTOMRIGHT", 0, -3)
  row:SetScript("OnEnter", rowEnter)
  row:SetScript("OnLeave", rowLeave)
  row:SetScript("OnMouseUp", rowClick)
  return row
end

function ns.buildHomeTab(p)
  local hint = buildAppHint(p)
  local hintHeight = 0
  local width = WIDTH -- what the page is laid out for now (fit)

  -- Playing now: the left column's first card ------------------------------------------------------
  local card = newCard(p)
  local tint = card:CreateTexture(nil, "BORDER")
  tint:SetPoint("TOPLEFT", 1, -1)
  tint:SetPoint("TOPRIGHT", -1, -1)
  tint:SetHeight(96)

  local dot = card:CreateTexture(nil, "ARTWORK")
  dot:SetSize(6, 6)
  dot:SetColorTexture(0.3, 0.85, 0.4)
  dot:SetPoint("TOPLEFT", card, "TOPLEFT", PAD, -15)
  local nowLabel = ui.text(card, "GameFontDisableSmall", "PLAYING NOW")
  nowLabel:SetPoint("LEFT", dot, "RIGHT", 6, 0)

  local ring = card:CreateTexture(nil, "ARTWORK")
  ring:SetTexture(CIRCLE)
  ring:SetSize(PORTRAIT + 6, PORTRAIT + 6)
  ring:SetPoint("TOPLEFT", card, "TOPLEFT", PAD, -30)
  local portrait = ui.portrait(card, PORTRAIT)
  portrait:SetPoint("CENTER", ring, "CENTER")
  local classColor = { 1, 1, 1, 1 } -- the level badge's edge, the class's color
  local levelBadge = CreateFrame("Frame", nil, card)
  levelBadge:SetSize(26, 15)
  levelBadge:SetPoint("BOTTOMRIGHT", ring, "BOTTOMRIGHT", 4, 0)
  levelBadge:SetFrameLevel((portrait:GetFrameLevel() or 1) + 2)
  ui.box(levelBadge, C.bar, classColor)
  local levelText = ui.text(levelBadge, "GameFontHighlightSmall")
  levelText:SetJustifyH("CENTER")
  levelText:SetPoint("CENTER", 0, 0)

  local name = ui.text(card, "GameFontNormalHuge")
  name:SetPoint("TOPLEFT", ring, "TOPRIGHT", 12, -8)
  name:SetWordWrap(false)
  local sub = ui.text(card, "GameFontHighlight")
  sub:SetPoint("TOPLEFT", name, "BOTTOMLEFT", 0, -4)
  local zone = ui.text(card, "GameFontDisableSmall")
  zone:SetPoint("TOPLEFT", sub, "BOTTOMLEFT", 0, -3)

  -- XP to the next level, with this level's time; the time to the top at your pace on the right.
  local xpBar = CreateFrame("StatusBar", nil, card)
  xpBar:SetHeight(6)
  xpBar:SetStatusBarTexture("Interface\\Buttons\\WHITE8X8")
  xpBar:SetStatusBarColor(0.58, 0.38, 0.96)
  local track = xpBar:CreateTexture(nil, "BACKGROUND")
  track:SetAllPoints()
  track:SetColorTexture(1, 1, 1, 0.08)
  local xpText = ui.text(card, "GameFontDisableSmall")
  local estimate = ui.text(card, "GameFontDisableSmall")
  estimate:SetJustifyH("RIGHT")

  local pace = ui.text(card, "GameFontHighlightSmall")
  local tiles = {}
  for i = 1, 3 do
    local t = CreateFrame("Frame", nil, card)
    t:SetHeight(40)
    ui.box(t, C.bar, C.border)
    t.value = ui.text(t, "GameFontHighlightLarge")
    t.value:SetPoint("TOPLEFT", t, "TOPLEFT", 8, -6)
    t.label = ui.text(t, "GameFontDisableSmall")
    t.label:SetPoint("TOPLEFT", t.value, "BOTTOMLEFT", 0, -2)
    tiles[i] = t
  end
  ui.tint(tiles[2].value, GOLD)

  -- The mini chart (Ui.lua): a bar per level, the last CHART_BARS.
  local chart = ui.levelChart(card, CHART_BARS, CHART_HEIGHT, BAR_STEP)

  -- The level history, folded until opened: the numbers, then the newest levels.
  local layout -- below
  local toggle = ui.button(card, "", 150, function()
    setHistoryOpen(not historyOpen())
    layout()
  end)
  local factLines = {}
  for i = 1, 2 do factLines[i] = ui.text(card, "GameFontHighlightSmall") end
  local history = {}
  for i = 1, HISTORY_ROWS do
    local row = CreateFrame("Frame", nil, card)
    row:SetHeight(ROW)
    row.level = ui.text(row, "GameFontHighlightSmall")
    row.level:SetPoint("LEFT", row, "LEFT", 0, 0)
    row.took = ui.text(row, "GameFontHighlightSmall")
    row.took:SetPoint("LEFT", row, "LEFT", 70, 0)
    row.took:SetTextColor(0.45, 0.85, 0.5)
    row.zone = ui.text(row, "GameFontDisableSmall")
    row.zone:SetPoint("LEFT", row, "LEFT", 140, 0)
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
  for i = 1, TODAY_MAX do today[i] = ui.text(card, "GameFontHighlight") end
  local todayToggle = ui.button(card, "", 90, function()
    setTodayOpen(not todayOpen())
    layout()
  end)

  -- Friends who share with you, the one seen last first: the left column's second card ------------
  local friendsCard = newCard(p)
  local friendsTitle = cardLabel(friendsCard, "FRIENDS", PAD)
  cardLabel(friendsCard, "LAST SEEN", PAD, true)
  local noFriends = ui.text(friendsCard, "GameFontDisableSmall", "Nobody shares with you yet. Ask a friend on the Friends tab, they see your characters and level times too when you share.")
  noFriends:SetPoint("TOPLEFT", friendsTitle, "BOTTOMLEFT", 0, -8)
  local friendRows = {}
  for i = 1, FRIENDS_SHOWN do
    local row = newFriendRow(friendsCard)
    row:SetPoint("TOPLEFT", friendsCard, "TOPLEFT", PAD, -(PAD + 18 + (i - 1) * FRIEND_ROW))
    friendRows[i] = row
  end
  local openFriends = ui.button(friendsCard, "Friends tab", 110, function() ns.openWindow("friends") end)

  -- The newest moments: yours, then your friends', in the right column's card ----------------------
  local momentsCard = newCard(p)
  local function column(title, empty)
    local col = { rows = {}, list = {}, offset = 0, size = FEED_SIZE }
    -- Each list takes the wheel itself: past its ends, or without more, the page scrolls.
    col.frame = CreateFrame("Frame", nil, momentsCard)
    col.frame:EnableMouseWheel(true)
    col.frame:SetScript("OnMouseWheel", function(_, delta)
      if scrollColumn(col, delta) then return end
      local page = p:GetParent()
      if page and page.GetVerticalScrollRange then ui.scrollBy(page, delta) end
    end)
    col.title = ui.text(col.frame, "GameFontNormal", title)
    ui.tint(col.title, C.accent)
    col.title:SetPoint("TOPLEFT", col.frame, "TOPLEFT", 0, 0)
    col.more = ui.text(col.frame, "GameFontDisableSmall")
    col.more:SetPoint("TOPRIGHT", col.frame, "TOPRIGHT", 0, -2)
    col.empty = ui.text(col.frame, "GameFontDisableSmall", empty)
    col.empty:SetPoint("TOPLEFT", col.frame, "TOPLEFT", 0, -22)
    for i = 1, FEED_SIZE do col.rows[i] = newMomentRow(col.frame) end
    return col
  end
  -- Friends' first: the newest list made takes the wheel's tests' place (yours, on top).
  local theirs = column("Friends' moments", "Nothing from friends yet.")
  local mine = column("Latest moments", "Nothing yet. Level-ups, boss kills and loot show up here.")
  local divider = momentsCard:CreateTexture(nil, "ARTWORK")
  ui.fill(divider, C.border)
  divider:SetHeight(1)

  -- n rows per list, built as they're needed, the lists placed one over the other.
  local feedRows, listWidth = FEED_SIZE, 300
  local function placeLists()
    local listHeight = 24 + feedRows * MOMENT_ROW
    for i, col in ipairs({ mine, theirs }) do
      col.size = feedRows
      for r = #col.rows + 1, feedRows do col.rows[r] = newMomentRow(col.frame) end
      col.frame:ClearAllPoints()
      col.frame:SetPoint("TOPLEFT", momentsCard, "TOPLEFT", PAD, -(PAD + (i - 1) * (listHeight + 14)))
      col.frame:SetSize(listWidth, listHeight)
      col.empty:SetWidth(listWidth)
      for r, row in ipairs(col.rows) do
        row:ClearAllPoints()
        row:SetPoint("TOPLEFT", col.frame, "TOPLEFT", 0, -22 - (r - 1) * MOMENT_ROW)
        sizeMomentRow(row, listWidth)
      end
    end
    divider:ClearAllPoints()
    divider:SetPoint("TOPLEFT", momentsCard, "TOPLEFT", PAD, -(PAD + listHeight + 6))
    divider:SetPoint("TOPRIGHT", momentsCard, "TOPRIGHT", -PAD, -(PAD + listHeight + 6))
    return PAD + 2 * listHeight + 14 + PAD
  end

  -- Everything sized for the page's width (the window can be resized).
  local function fit()
    local w = contentWidth()
    width = w
    local left = leftWidth(w)
    local inner = left - 2 * PAD
    hint:SetWidth(w)
    hint.text:SetWidth(w - 24)
    card:SetWidth(left)
    friendsCard:SetWidth(left)
    momentsCard:SetWidth(w - left - GAP)
    listWidth = w - left - GAP - 2 * PAD
    name:SetWidth(inner - PORTRAIT - 18)
    xpBar:SetWidth(inner)
    for _, fs in ipairs({ pace, factLines[1], factLines[2] }) do fs:SetWidth(inner) end
    local tileWidth = math.floor((inner - 2 * 6) / 3)
    for _, t in ipairs(tiles) do t:SetWidth(tileWidth) end
    for _, row in ipairs(history) do
      row:SetWidth(inner)
      row.zone:SetWidth(math.max(0, inner - 140 - 60))
    end
    noFriends:SetWidth(inner)
    for _, row in ipairs(friendRows) do
      row:SetWidth(inner)
      row.who:SetWidth(inner - 32 - 90)
      row.what:SetWidth(inner - 32 - 90)
    end
  end

  -- What only changes with new events is worked out again only then (the redraw runs every few
  -- seconds, and the list holds thousands): level times, today's counts and the feed.
  local seenList, seenCount, seenGuid
  local levels, summary = {}, nil
  local shownFriends = 0

  -- Places what's shown from the top down, and sizes the cards and the page.
  layout = function()
    fit()
    local shown = appHintShown()
    hint:SetShown(shown)
    hintHeight = shown and APP_HINT or 0
    local top = 12 + hintHeight
    card:ClearAllPoints()
    card:SetPoint("TOPLEFT", p, "TOPLEFT", 16, -top)
    local left = leftWidth(width)
    momentsCard:ClearAllPoints()
    momentsCard:SetPoint("TOPLEFT", p, "TOPLEFT", 16 + left + GAP, -top)

    local y = 30 + PORTRAIT + 6 + 10
    xpBar:ClearAllPoints()
    xpText:ClearAllPoints()
    estimate:ClearAllPoints()
    if xpBar:IsShown() then
      at(xpBar, card, y, -2)
      xpText:SetPoint("TOPLEFT", xpBar, "BOTTOMLEFT", 0, -4)
      estimate:SetPoint("TOPRIGHT", xpBar, "BOTTOMRIGHT", 0, -4)
      y = y + 6 + 18
    else
      at(xpText, card, y, -2)
      estimate:SetPoint("TOPRIGHT", card, "TOPRIGHT", -PAD, -y)
      y = y + ((xpText:GetText() or "") ~= "" and 16 or 0)
    end
    at(pace, card, y + 4, -2)
    y = y + 4 + 18
    local tileWidth = math.floor((left - 2 * PAD - 12) / 3)
    for i, t in ipairs(tiles) do at(t, card, y, -2 + (i - 1) * (tileWidth + 6)) end
    y = y + 40 + 10

    local open = historyOpen()
    chart:SetShown(summary ~= nil)
    toggle:SetShown(summary ~= nil)
    if summary then
      at(chart, card, y, -2)
      y = y + CHART_HEIGHT + 8
      toggle:SetText(open and "Level history  ^" or "Level history  v")
      at(toggle, card, y, -2)
      y = y + 22 + 8
    end
    local shownRows = 0
    for _, fs in ipairs(factLines) do
      local show = summary ~= nil and open and (fs:GetText() or "") ~= ""
      fs:SetShown(show)
      if show then
        at(fs, card, y, -2)
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
          at(row, card, y, -2)
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
    line:SetPoint("TOPLEFT", card, "TOPLEFT", PAD, -(y + 4))
    line:SetPoint("TOPRIGHT", card, "TOPRIGHT", -PAD, -(y + 4))
    at(todayLabel, card, y + 12, -2)
    local more = #todayStats - ns.TODAY_SHOWN
    todayToggle:SetShown(more > 0)
    if more > 0 then
      todayToggle:SetText(todayOpen() and "Less  ^" or string.format("+%d more  v", more))
      todayToggle:ClearAllPoints()
      todayToggle:SetPoint("TOPRIGHT", card, "TOPRIGHT", -PAD, -(y + 8))
    end
    -- Two to a row in a narrow column, four in a wide one.
    local cols = (left - 2 * PAD) >= 400 and ns.TODAY_SHOWN or 2
    local colWidth = math.floor((left - 2 * PAD) / cols)
    local count = todayOpen() and #todayStats or math.min(#todayStats, ns.TODAY_SHOWN)
    for i, fs in ipairs(today) do
      fs:SetShown(i <= count)
      if i <= count then
        local col, row = (i - 1) % cols, math.floor((i - 1) / cols)
        at(fs, card, y + 30 + row * 20, -2 + col * colWidth)
      end
    end
    local cardHeight = y + 30 + math.ceil(math.max(count, 1) / cols) * 20 + 8
    card:SetHeight(cardHeight)

    -- The friends card under it.
    friendsCard:ClearAllPoints()
    friendsCard:SetPoint("TOPLEFT", card, "BOTTOMLEFT", 0, -GAP)
    local friendsHeight = PAD + 18 + math.max(shownFriends * FRIEND_ROW, shownFriends == 0 and 40 or 0) + 6
    openFriends:ClearAllPoints()
    openFriends:SetPoint("TOPRIGHT", friendsCard, "TOPRIGHT", -PAD, -friendsHeight)
    friendsHeight = friendsHeight + 22 + PAD
    friendsCard:SetHeight(friendsHeight)
    local leftHeight = cardHeight + GAP + friendsHeight

    -- The moments take the left column's height, or the window's if that's taller: more rows each.
    local room = math.max(leftHeight, (ns.pageHeight and ns.pageHeight() or 0) - top - 20)
    local rows = math.max(FEED_SIZE, math.min(FEED_MAX, math.floor((room - 2 * PAD - 14 - 48) / (2 * MOMENT_ROW))))
    local changed = rows ~= feedRows
    feedRows = rows
    local rightHeight = placeLists()
    if changed then
      showColumn(mine)
      showColumn(theirs)
    end
    momentsCard:SetHeight(math.max(rightHeight, leftHeight))
    p:SetHeight(top + math.max(rightHeight, leftHeight) + 16)
  end
  hint.onHide = layout

  -- The parts that change while you play: portrait, name, XP and the numbers.
  local function drawLive(me)
    local r, g, b = ui.classRgb(me.classFile)
    ui.gradient(tint, r, g, b, 0.28, 0.02)
    ring:SetVertexColor(r, g, b)
    classColor[1], classColor[2], classColor[3] = r, g, b
    ui.box(levelBadge, C.bar, classColor)
    ui.setPortrait(portrait, { guid = me.guid, classFile = me.classFile })
    levelText:SetText(me.level or "?")
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
      local text = string.format("%d%% to %d", math.floor(100 * (me.xp or 0) / me.xpMax), me.level + 1)
      if me.levelTime then text = text .. "  ·  " .. ns.formatDuration(me.levelTime) .. " on this level" end
      xpText:SetText(text)
    else
      xpText:SetText("")
    end

    setTile(tiles[1], me.ilvl and tostring(math.floor(me.ilvl)) or "?", "Item level")
    setTile(tiles[2], me.money and ui.gold(me.money) or "?", "Gold")
    setTile(tiles[3], me.played and ns.formatPlayed(me.played) or "?", "Played")

    -- The chart, with the level being played as a paler last bar.
    chart.draw(levels, not atMax and me.levelTime and me.level and { level = me.level + 1, seconds = me.levelTime } or nil)
    local left = not atMax and not ns.IS_RETAIL and ns.timeToMax(levels, db().xpTable, me.level, me.xp, me.xpMax, me.maxLevel)
    estimate:SetText(left and string.format("~%s to %d", ns.formatPlayed(left), me.maxLevel) or "")
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
    shownFriends = math.min(#latest, FRIENDS_SHOWN)
    for i, row in ipairs(friendRows) do
      local f = latest[i]
      if f then
        local c = f.c
        row.who:SetText(ns.fullName(c))
        row.who:SetTextColor(ui.classRgb(c.classFile))
        local bits = { string.format("%s %s", c.level or "?", c.class or "") }
        if c.zone and c.zone ~= "" then bits[#bits + 1] = c.zone end
        row.what:SetText(table.concat(bits, "  ·  "))
        row.label:SetText(f.label or "")
        row.when:SetText(ns.ago(f.at))
        ui.setPortrait(row.portrait, c)
        local fl = ns.friendLevels(c)
        row.full = ns.fullName(c) .. (#fl > 0 and (": " .. ns.levelText(fl[#fl])) or "")
        row.open = function() ns.toggleFriendDetails(c, f.label) end
        row:Show()
      else
        row:Hide()
      end
    end
    noFriends:SetShown(#latest == 0)
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
