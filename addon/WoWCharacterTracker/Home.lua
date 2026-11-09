-- The window's Home tab (/wct), like the app's Home page: the character you're playing with its
-- level times (Leveling.lua) as a small chart and a level history that folds open, today's moments,
-- the newest moments of all your characters and the friends who share with you. Redrawn every few
-- seconds while it's shown, so played time ticks.
local ADDON, ns = ...
local ui = ns.ui
local C = ui.COLOR

local WIDTH = 640  -- the page's content width
local INNER = WIDTH - 28 -- inside the card
local CHART_BARS = 40
local CHART_HEIGHT = 36
local BAR_STEP = 12 -- a bar and its gap
local HISTORY_ROWS = 20
local ROW = 16
local FEED_SIZE = 6
local FRIENDS_SHOWN = 4
local REDRAW_EVERY = 2 -- seconds
local SLOW_EVERY = 15  -- redraws

local MUTED = { 0.55, 0.5, 0.45 }

local function db() return WoWCharacterTrackerDB or {} end

-- Whether the level history is open, kept with the window's other choices.
local function historyOpen() return (db().window or {}).homeHistory == true end
local function setHistoryOpen(on)
  local d = db()
  d.window = d.window or {}
  d.window.homeHistory = on
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

-- A tile: a small box with a big value over a label.
local function newTile(p, width)
  local t = CreateFrame("Frame", nil, p)
  t:SetSize(width, 46)
  ui.box(t, C.bar, C.border)
  t.value = ui.text(t, "GameFontHighlightLarge")
  t.value:SetPoint("TOPLEFT", t, "TOPLEFT", 10, -8)
  t.label = ui.text(t, "GameFontDisableSmall")
  t.label:SetPoint("TOPLEFT", t.value, "BOTTOMLEFT", 0, -3)
  return t
end

local function setTile(t, value, label)
  t.value:SetText(value)
  t.label:SetText(label)
end

local function showBarTip(self)
  if not (GameTooltip and self.text) then return end
  GameTooltip:SetOwner(self, "ANCHOR_TOP")
  GameTooltip:SetText(self.text, 1, 1, 1)
  GameTooltip:Show()
end

local function hideTip() if GameTooltip then GameTooltip:Hide() end end

-- Where the two moment columns meet, dragged by the player and kept with the window's choices.
local SPLIT_MIN, SPLIT_MAX = 180, WIDTH - 180
local function split()
  local s = tonumber((db().window or {}).homeSplit) or WIDTH / 2
  return math.max(SPLIT_MIN, math.min(SPLIT_MAX, s))
end
local function setSplit(x)
  local d = db()
  d.window = d.window or {}
  d.window.homeSplit = math.floor(math.max(SPLIT_MIN, math.min(SPLIT_MAX, x)))
end

-- The whole line on hover, for a moment cut short by its column.
local function showRowTip(self)
  if not (GameTooltip and self.full) then return end
  GameTooltip:SetOwner(self, "ANCHOR_TOP")
  GameTooltip:SetText(self.full, 1, 1, 1, 1, true)
  GameTooltip:Show()
end

local function rowEnter(self)
  self.hover:SetShown(self.open ~= nil)
  showRowTip(self)
end
local function rowLeave(self)
  self.hover:Hide()
  hideTip()
end
local function rowClick(self) if self.open then self.open() end end

-- A moment line: who, what (cut to the column) and when. A friend's opens their details.
local function newMomentRow(parent)
  local row = CreateFrame("Frame", nil, parent)
  row:SetHeight(18)
  row:EnableMouse(true)
  row.hover = row:CreateTexture(nil, "BACKGROUND")
  ui.fill(row.hover, C.hover)
  row.hover:SetAllPoints()
  row.hover:Hide()
  row.who = ui.text(row, "GameFontNormalSmall")
  row.who:SetPoint("LEFT", row, "LEFT", 0, 0)
  row.who:SetWidth(80)
  row.who:SetWordWrap(false)
  row.what = ui.text(row, "GameFontHighlightSmall")
  row.what:SetPoint("LEFT", row, "LEFT", 84, 0)
  row.what:SetWordWrap(false)
  row.when = ui.text(row, "GameFontDisableSmall")
  row.when:SetPoint("RIGHT", row, "RIGHT", 0, 0)
  row:SetScript("OnEnter", rowEnter)
  row:SetScript("OnLeave", rowLeave)
  row:SetScript("OnMouseUp", rowClick)
  return row
end

-- open: what a click does (nil: nothing, no hover).
local function setMomentRow(row, who, classFile, what, at, open)
  row.who:SetText(who)
  row.who:SetTextColor(ui.classRgb(classFile))
  row.what:SetText(what)
  row.when:SetText(ns.ago(at))
  row.full, row.open = who .. ": " .. what, open
  row:Show()
end

-- A column of moments from list ({ e, c, label }: c and label for a friend's, whose click opens it).
local function fillColumn(col, list)
  for i, row in ipairs(col.rows) do
    local item = list[i]
    if item then
      local e = item.e
      local open = item.c and function() ns.toggleFriendDetails(item.c, item.label) end or nil
      setMomentRow(row, e.char or "?", e.classFile, ns.momentLine(e), e.at, open)
    else
      row:Hide()
    end
  end
  col.empty:SetShown(#list == 0)
end

local function sizeMomentRow(row, width)
  row:SetWidth(width)
  row.what:SetWidth(math.max(40, width - 84 - 56))
end

-- At y below the card's top edge.
local function at(w, card, y, x)
  w:ClearAllPoints()
  w:SetPoint("TOPLEFT", card, "TOPLEFT", 14 + (x or 0), -y)
end

function ns.buildHomeTab(p)
  -- Playing now ---------------------------------------------------------------------------------
  local card = CreateFrame("Frame", nil, p)
  card:SetPoint("TOPLEFT", p, "TOPLEFT", 16, -12)
  card:SetSize(WIDTH, 300)
  ui.box(card, C.card, C.border)

  local dot = card:CreateTexture(nil, "ARTWORK")
  dot:SetSize(6, 6)
  dot:SetColorTexture(0.3, 0.85, 0.4)
  dot:SetPoint("TOPLEFT", card, "TOPLEFT", 14, -16)
  local nowLabel = ui.text(card, "GameFontDisableSmall", "PLAYING NOW")
  nowLabel:SetPoint("LEFT", dot, "RIGHT", 6, 0)

  local portrait = card:CreateTexture(nil, "ARTWORK")
  portrait:SetSize(64, 64)
  portrait:SetPoint("TOPLEFT", card, "TOPLEFT", 14, -32)
  local name = ui.text(card, "GameFontNormalHuge")
  name:SetPoint("TOPLEFT", portrait, "TOPRIGHT", 14, -4)
  local sub = ui.text(card, "GameFontHighlight")
  sub:SetPoint("TOPLEFT", name, "BOTTOMLEFT", 0, -4)
  local zone = ui.text(card, "GameFontDisableSmall")
  zone:SetPoint("TOPLEFT", sub, "BOTTOMLEFT", 0, -3)

  local pace = ui.text(card, "GameFontHighlightSmall")
  at(pace, card, 108)
  pace:SetWidth(INNER)

  local tileWidth = math.floor((INNER - 3 * 8) / 4)
  local tiles = {}
  for i = 1, 4 do
    tiles[i] = newTile(card, tileWidth)
    at(tiles[i], card, 128, (i - 1) * (tileWidth + 8))
  end
  local TILES_END = 128 + 46

  local estimate = ui.text(card, "GameFontDisableSmall")
  estimate:SetWidth(INNER)

  -- The mini chart: a bar per level (the last CHART_BARS), the fastest and slowest in the accent color.
  local chart = CreateFrame("Frame", nil, card)
  chart:SetSize(CHART_BARS * BAR_STEP, CHART_HEIGHT)
  local base = chart:CreateTexture(nil, "BACKGROUND")
  ui.fill(base, C.border)
  base:SetHeight(1)
  base:SetPoint("BOTTOMLEFT")
  base:SetPoint("BOTTOMRIGHT")
  local bars = {}
  for i = 1, CHART_BARS do
    local b = CreateFrame("Frame", nil, chart)
    b:SetWidth(BAR_STEP - 3)
    b:SetPoint("BOTTOMLEFT", chart, "BOTTOMLEFT", (i - 1) * BAR_STEP, 1)
    b:EnableMouse(true)
    b.fill = b:CreateTexture(nil, "ARTWORK")
    b.fill:SetAllPoints()
    b:SetScript("OnEnter", showBarTip)
    b:SetScript("OnLeave", hideTip)
    bars[i] = b
  end

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
  local todayLabel = ui.text(card, "GameFontDisableSmall", "TODAY")
  local today = {}
  local todayWidth = math.floor(INNER / 4)
  for i = 1, 4 do today[i] = ui.text(card, "GameFontHighlight") end

  -- The newest moments: yours on the left, friends' on the right, a bar between them to drag ------
  local moments = CreateFrame("Frame", nil, p)
  moments:SetSize(WIDTH, 26 + FEED_SIZE * 20)
  moments:SetPoint("TOPLEFT", card, "BOTTOMLEFT", 0, -18)
  local function column(title, empty)
    local col = { title = ui.text(moments, "GameFontNormal", title), rows = {} }
    ui.tint(col.title, C.accent)
    col.empty = ui.text(moments, "GameFontDisableSmall", empty)
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
    for i, row in ipairs(col.rows) do
      row:ClearAllPoints()
      row:SetPoint("TOPLEFT", moments, "TOPLEFT", x, -24 - (i - 1) * 20)
      sizeMomentRow(row, w)
    end
  end
  local function placeMoments()
    local s = split()
    placeColumn(mine, 0, s - 8)
    placeColumn(theirs, s + 8, WIDTH - s - 8)
    divider:ClearAllPoints()
    divider:SetPoint("TOPLEFT", moments, "TOPLEFT", s - 5, 0)
    divider:SetPoint("BOTTOMLEFT", moments, "BOTTOMLEFT", s - 5, 0)
  end
  placeMoments()
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
      setSplit(x - (moments:GetLeft() or 0))
      placeMoments()
    end)
  end)
  divider:SetScript("OnMouseUp", function(self)
    self.dragging = false
    self:SetScript("OnUpdate", nil)
    ui.fill(dividerLine, C.border)
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
    local row = newMomentRow(p)
    row:SetPoint("TOPLEFT", friendsTitle, "BOTTOMLEFT", 0, -6 - (i - 1) * 20)
    row.who:SetWidth(170)
    row.what:ClearAllPoints()
    row.what:SetPoint("LEFT", row, "LEFT", 176, 0)
    row.what:SetFontObject("GameFontDisableSmall")
    row:SetWidth(WIDTH)
    row.what:SetWidth(WIDTH - 176 - 70)
    friendRows[i] = row
  end
  local BELOW_CARD = 18 + 26 + FEED_SIZE * 20 + 16 + 26 + FRIENDS_SHOWN * 20 + 20 -- the moments and friends under the card

  -- What only changes with new events is worked out again only then (the redraw runs every few
  -- seconds, and the list holds thousands): level times, today's counts and the feed.
  local seenList, seenCount, seenGuid
  local levels, summary = {}, nil

  -- Places what's shown in the card from the top down, and sizes the card and the page.
  layout = function()
    local y = TILES_END + 12
    local hasEstimate = (estimate:GetText() or "") ~= ""
    estimate:SetShown(hasEstimate)
    if hasEstimate then
      at(estimate, card, y)
      y = y + 18
    end
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
    for i, fs in ipairs(today) do at(fs, card, y + 30, (i - 1) * todayWidth) end
    card:SetHeight(y + 56)
    p:SetHeight(12 + y + 56 + BELOW_CARD)
  end

  -- The parts that change while you play: name, tiles and the estimate.
  local function drawLive(me)
    if SetPortraitTexture then SetPortraitTexture(portrait, "player") end
    name:SetText(me.name or "?")
    name:SetTextColor(ui.classRgb(me.classFile))
    sub:SetText(string.format("Level %d %s %s", me.level or 0, me.race or "", me.class or ""))
    zone:SetText(me.zone or "")
    local atMax = me.maxLevel and me.level and me.level >= me.maxLevel

    setTile(tiles[1], me.ilvl and tostring(math.floor(me.ilvl)) or "?", "Item level")
    setTile(tiles[2], me.money and ui.gold(me.money) or "?", "Gold")
    setTile(tiles[3], me.played and ns.formatPlayed(me.played) or "?", "Played")
    if atMax then setTile(tiles[4], "Max", "Level " .. me.level)
    else setTile(tiles[4], me.levelTime and ns.formatDuration(me.levelTime) or "?", "This level") end

    local left = not atMax and not ns.IS_RETAIL and ns.timeToMax(levels, db().xpTable, me.level, me.xp, me.xpMax, me.maxLevel)
    estimate:SetText(left and string.format("About %s played to level %d, at your recent pace", ns.formatPlayed(left), me.maxLevel) or "")
  end

  -- The parts that come from the events: level times, today's counts, the chart and the feed.
  local function drawEvents(events, guid)
    levels = ns.levelTimes(events, guid)
    summary = ns.levelSummary(levels)
    local paceParts = {}
    if #levels > 0 then paceParts[#paceParts + 1] = ns.levelText(levels[#levels]) end
    if summary then paceParts[#paceParts + 1] = "average " .. ns.formatDuration(summary.average) end
    pace:SetText(#paceParts > 0 and table.concat(paceParts, "  ·  ") or "No level times yet. The next ding starts the clock.")

    local since = midnight()
    local counts = { 0, 0, 0, 0 }
    for _, e in ipairs(events) do
      if type(e) == "table" and (e.at or 0) >= since then
        if e.kind == "level" or e.kind == nil then counts[1] = counts[1] + 1
        elseif e.kind == "boss" or e.kind == "keyrun" then counts[2] = counts[2] + 1
        elseif e.kind == "loot" and (e.quality or 0) >= 4 then counts[3] = counts[3] + 1
        elseif e.kind == "mount" then counts[4] = counts[4] + 1 end
      end
    end
    local names = { "levels", ns.IS_RETAIL and "keys" or "bosses", "epics", "mounts" }
    for i, fs in ipairs(today) do fs:SetText(string.format("%d |cff9a9a9a%s|r", counts[i], names[i])) end

    factLines[1]:SetText(summary and string.format("%d timed level%s, average %s", summary.count, summary.count == 1 and "" or "s",
      ns.formatDuration(summary.average)) or "")
    factLines[2]:SetText(summary and summary.count > 1 and string.format("Fastest %s (%d to %d)   Slowest %s (%d to %d)",
      ns.formatDuration(summary.fastest.seconds), summary.fastest.level - 1, summary.fastest.level,
      ns.formatDuration(summary.slowest.seconds), summary.slowest.level - 1, summary.slowest.level) or "")
    local shown = {}
    for i = math.max(1, #levels - CHART_BARS + 1), #levels do shown[#shown + 1] = levels[i] end
    local slowest = 1
    for _, l in ipairs(shown) do slowest = math.max(slowest, l.seconds) end
    for i, b in ipairs(bars) do
      local l = shown[i]
      if l then
        b:SetHeight(math.max(2, (CHART_HEIGHT - 2) * l.seconds / slowest))
        local mark = summary and (l == summary.fastest or l == summary.slowest)
        if mark then b.fill:SetColorTexture(C.accent[1], C.accent[2], C.accent[3], 1)
        else b.fill:SetColorTexture(MUTED[1], MUTED[2], MUTED[3], 0.6) end
        b.text = string.format("%d to %d: %s", l.level - 1, l.level, ns.formatDuration(l.seconds))
        b:Show()
      else
        b:Hide()
      end
    end

    -- Your newest moments with a line (ns.momentLine), any of your characters.
    local list = {}
    for i = #events, 1, -1 do
      if #list >= FEED_SIZE then break end
      local e = events[i]
      if type(e) == "table" and e.kind ~= "ask" and ns.momentLine and ns.momentLine(e) then list[#list + 1] = { e = e } end
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
          e.char, e.classFile = char.char, char.classFile
          if ns.momentLine(e) then happened[#happened + 1] = { e = e, c = char, label = label } end
        end
      end
    end
    table.sort(happened, function(a, b) return a.e.at > b.e.at end)
    for i = #happened, FEED_SIZE + 1, -1 do happened[i] = nil end
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
        setMomentRow(row, c.char or "?", c.classFile, table.concat(bits, "  ·  "), f.at, function() ns.toggleFriendDetails(c, f.label) end)
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
