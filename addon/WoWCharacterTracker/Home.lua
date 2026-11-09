-- The window's Home tab (/wct), like the app's Home page: the character you're playing with its
-- level times (Leveling.lua), today's moments, the newest moments of all your characters and the
-- friends who share with you. Redrawn every few seconds while it's shown, so played time ticks.
local ADDON, ns = ...
local ui = ns.ui
local C = ui.COLOR

local WIDTH = 640  -- the page's content width
local CHART_BARS = 40
local CHART_HEIGHT = 64
local FEED_SIZE = 6
local FRIENDS_SHOWN = 4
local REDRAW_EVERY = 2 -- seconds
local SLOW_EVERY = 15  -- redraws

local MUTED = { 0.55, 0.5, 0.45 }

local function db() return WoWCharacterTrackerDB or {} end

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
    rested = GetXPExhaustion and GetXPExhaustion(),
    maxLevel = maxLevel,
  }
end

local function midnight()
  local t = date("*t")
  return time({ year = t.year, month = t.month, day = t.day, hour = 0 })
end

local function fmtNumber(n)
  local s = tostring(math.floor(n or 0))
  local out = s:reverse():gsub("(%d%d%d)", "%1,"):reverse()
  return (out:gsub("^,", ""))
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

function ns.buildHomeTab(p)
  -- Playing now ---------------------------------------------------------------------------------
  local card = CreateFrame("Frame", nil, p)
  card:SetPoint("TOPLEFT", p, "TOPLEFT", 16, -12)
  card:SetSize(WIDTH, 286)
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
  pace:SetPoint("TOPLEFT", portrait, "BOTTOMLEFT", 0, -12)
  pace:SetWidth(WIDTH - 28)

  local tileWidth = math.floor((WIDTH - 28 - 3 * 8) / 4)
  local tiles = {}
  for i = 1, 4 do
    tiles[i] = newTile(card, tileWidth)
    tiles[i]:SetPoint("TOPLEFT", pace, "BOTTOMLEFT", (i - 1) * (tileWidth + 8), -10)
  end

  -- The XP bar: rested XP a paler shade past the filled part.
  local bar = CreateFrame("Frame", nil, card)
  bar:SetSize(WIDTH - 28, 12)
  bar:SetPoint("TOPLEFT", tiles[1], "BOTTOMLEFT", 0, -14)
  ui.box(bar, C.bar, C.border)
  local rested = bar:CreateTexture(nil, "ARTWORK")
  rested:SetColorTexture(0.25, 0.45, 0.8, 0.45)
  rested:SetPoint("TOPLEFT", bar, "TOPLEFT", 1, -1)
  rested:SetPoint("BOTTOMLEFT", bar, "BOTTOMLEFT", 1, 1)
  local fill = bar:CreateTexture(nil, "OVERLAY")
  fill:SetColorTexture(C.accent[1], C.accent[2], C.accent[3], 0.9)
  fill:SetPoint("TOPLEFT", bar, "TOPLEFT", 1, -1)
  fill:SetPoint("BOTTOMLEFT", bar, "BOTTOMLEFT", 1, 1)
  local xpText = ui.text(card, "GameFontDisableSmall")
  xpText:SetPoint("TOPLEFT", bar, "BOTTOMLEFT", 0, -5)
  local estimate = ui.text(card, "GameFontDisableSmall")
  estimate:SetPoint("TOPLEFT", xpText, "BOTTOMLEFT", 0, -6)
  estimate:SetWidth(WIDTH - 28)

  local line = card:CreateTexture(nil, "ARTWORK")
  ui.fill(line, C.border)
  line:SetHeight(1)
  line:SetPoint("BOTTOMLEFT", card, "BOTTOMLEFT", 14, 52)
  line:SetPoint("BOTTOMRIGHT", card, "BOTTOMRIGHT", -14, 52)
  local todayLabel = ui.text(card, "GameFontDisableSmall", "TODAY")
  todayLabel:SetPoint("TOPLEFT", line, "BOTTOMLEFT", 0, -8)
  local today = {}
  local todayWidth = math.floor((WIDTH - 28) / 4)
  for i = 1, 4 do
    local fs = ui.text(card, "GameFontHighlight")
    fs:SetPoint("TOPLEFT", todayLabel, "BOTTOMLEFT", (i - 1) * todayWidth, -6)
    today[i] = fs
  end

  -- Leveling: the facts and a bar per level, the fastest and slowest in the accent color.
  local levelTitle = ui.heading(p, "Leveling", card, -18)
  levelTitle:ClearAllPoints()
  levelTitle:SetPoint("TOPLEFT", card, "BOTTOMLEFT", 0, -18)
  local noLevels = ui.text(p, "GameFontDisableSmall", "No timed levels yet for this character. The next ding starts the clock.")
  noLevels:SetPoint("TOPLEFT", levelTitle, "BOTTOMLEFT", 0, -6)
  local factLines = {}
  for i = 1, 2 do
    local fs = ui.text(p, "GameFontHighlightSmall")
    fs:SetPoint("TOPLEFT", levelTitle, "BOTTOMLEFT", 0, -6 - (i - 1) * 16)
    fs:SetWidth(WIDTH)
    factLines[i] = fs
  end
  local chart = CreateFrame("Frame", nil, p)
  chart:SetSize(WIDTH, CHART_HEIGHT)
  chart:SetPoint("TOPLEFT", levelTitle, "BOTTOMLEFT", 0, -44)
  local base = chart:CreateTexture(nil, "BACKGROUND")
  ui.fill(base, C.border)
  base:SetHeight(1)
  base:SetPoint("BOTTOMLEFT")
  base:SetPoint("BOTTOMRIGHT")
  local bars = {}
  local barWidth = math.floor(WIDTH / CHART_BARS)
  for i = 1, CHART_BARS do
    local b = CreateFrame("Frame", nil, chart)
    b:SetWidth(barWidth - 3)
    b:SetPoint("BOTTOMLEFT", chart, "BOTTOMLEFT", (i - 1) * barWidth, 1)
    b:EnableMouse(true)
    b.fill = b:CreateTexture(nil, "ARTWORK")
    b.fill:SetAllPoints()
    b:SetScript("OnEnter", showBarTip)
    b:SetScript("OnLeave", hideTip)
    bars[i] = b
  end

  -- Latest moments of all your characters ------------------------------------------------------
  local feedTitle = ui.heading(p, "Latest moments")
  feedTitle:ClearAllPoints()
  feedTitle:SetPoint("TOPLEFT", chart, "BOTTOMLEFT", 0, -22)
  local noFeed = ui.text(p, "GameFontDisableSmall", "Nothing yet. Level-ups, boss kills and loot show up here.")
  noFeed:SetPoint("TOPLEFT", feedTitle, "BOTTOMLEFT", 0, -6)
  local feed = {}
  for i = 1, FEED_SIZE do
    local row = CreateFrame("Frame", nil, p)
    row:SetSize(WIDTH, 18)
    row:SetPoint("TOPLEFT", feedTitle, "BOTTOMLEFT", 0, -6 - (i - 1) * 20)
    row.who = ui.text(row, "GameFontNormalSmall")
    row.who:SetPoint("LEFT", row, "LEFT", 0, 0)
    row.who:SetWidth(130)
    row.what = ui.text(row, "GameFontHighlightSmall")
    row.what:SetPoint("LEFT", row, "LEFT", 136, 0)
    row.what:SetWidth(WIDTH - 136 - 80)
    row.what:SetWordWrap(false)
    row.when = ui.text(row, "GameFontDisableSmall")
    row.when:SetPoint("RIGHT", row, "RIGHT", 0, 0)
    feed[i] = row
  end

  -- Friends who share with you, the one seen last first -----------------------------------------
  local friendsTitle = ui.heading(p, "Friends")
  friendsTitle:ClearAllPoints()
  friendsTitle:SetPoint("TOPLEFT", feedTitle, "BOTTOMLEFT", 0, -16 - FEED_SIZE * 20)
  local openFriends = ui.button(p, "Friends tab", 110, function() ns.openWindow("friends") end)
  openFriends:SetPoint("BOTTOMRIGHT", friendsTitle, "BOTTOMLEFT", WIDTH, -4)
  local noFriends = ui.text(p, "GameFontDisableSmall", "Nobody shares with you yet. Ask a friend on the Friends tab, they see your characters and level times too when you share.")
  noFriends:SetPoint("TOPLEFT", friendsTitle, "BOTTOMLEFT", 0, -6)
  noFriends:SetWidth(WIDTH)
  local friendRows = {}
  for i = 1, FRIENDS_SHOWN do
    local row = CreateFrame("Frame", nil, p)
    row:SetSize(WIDTH, 18)
    row:SetPoint("TOPLEFT", friendsTitle, "BOTTOMLEFT", 0, -6 - (i - 1) * 20)
    row.name = ui.text(row, "GameFontHighlightSmall")
    row.name:SetPoint("LEFT", row, "LEFT", 0, 0)
    row.name:SetWidth(170)
    row.info = ui.text(row, "GameFontDisableSmall")
    row.info:SetPoint("LEFT", row, "LEFT", 176, 0)
    row.info:SetWidth(WIDTH - 176 - 70)
    row.info:SetWordWrap(false)
    row.when = ui.text(row, "GameFontDisableSmall")
    row.when:SetPoint("RIGHT", row, "RIGHT", 0, 0)
    friendRows[i] = row
  end

  -- What only changes with new events is worked out again only then (the redraw runs every few
  -- seconds, and the list holds thousands): level times, today's counts and the feed.
  local seenList, seenCount, seenGuid
  local levels, summary = {}, nil

  -- The parts that change while you play: name, tiles, the XP bar and the estimate.
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

    local barWidth = WIDTH - 30
    local xp, xpMax = me.xp or 0, me.xpMax or 0
    bar:SetShown(not atMax and xpMax > 0)
    xpText:SetShown(not atMax and xpMax > 0)
    if not atMax and xpMax > 0 then
      local share = math.min(1, xp / xpMax)
      fill:SetWidth(math.max(1, barWidth * share))
      local restedShare = math.min(1, (xp + (me.rested or 0)) / xpMax)
      rested:SetWidth(math.max(1, barWidth * restedShare))
      xpText:SetText(string.format("XP %s / %s (%d%%)%s", fmtNumber(xp), fmtNumber(xpMax), math.floor(share * 100),
        (me.rested or 0) > 0 and ("  ·  rested " .. fmtNumber(me.rested)) or ""))
    end
    local left = not atMax and not ns.IS_RETAIL and ns.timeToMax(levels, db().xpTable, me.level, xp, xpMax, me.maxLevel)
    estimate:SetText(left and string.format("About %s played to level %d, at your recent pace", ns.formatPlayed(left), me.maxLevel) or "")
  end

  -- The parts that come from the events: level times, today's counts, the chart and the feed.
  local function drawEvents(events, guid)
    levels = ns.levelTimes(events, guid)
    summary = ns.levelSummary(levels)
    local paceParts = {}
    if #levels > 0 then paceParts[#paceParts + 1] = ns.levelText(levels[#levels]) end
    if summary then paceParts[#paceParts + 1] = "average " .. ns.formatDuration(summary.average) end
    pace:SetText(#paceParts > 0 and table.concat(paceParts, "  ·  ") or "No level times yet")

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

    -- The chart and its facts.
    noLevels:SetShown(summary == nil)
    chart:SetShown(summary ~= nil)
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
        b:SetHeight(math.max(3, (CHART_HEIGHT - 2) * l.seconds / slowest))
        local mark = summary and (l == summary.fastest or l == summary.slowest)
        if mark then b.fill:SetColorTexture(C.accent[1], C.accent[2], C.accent[3], 1)
        else b.fill:SetColorTexture(MUTED[1], MUTED[2], MUTED[3], 0.6) end
        b.text = string.format("%d to %d: %s", l.level - 1, l.level, ns.formatDuration(l.seconds))
        b:Show()
      else
        b:Hide()
      end
    end

    -- The feed: the newest moments with a line (ns.momentLine), any of your characters.
    local n = 0
    for i = #events, 1, -1 do
      if n >= FEED_SIZE then break end
      local e = events[i]
      local text = type(e) == "table" and e.kind ~= "ask" and ns.momentLine and ns.momentLine(e)
      if text then
        n = n + 1
        local row = feed[n]
        row.who:SetText(e.char or "?")
        row.who:SetTextColor(ui.classRgb(e.classFile))
        row.what:SetText(text)
        row.when:SetText(ns.ago(e.at))
        row:Show()
      end
    end
    for i = n + 1, FEED_SIZE do feed[i]:Hide() end
    noFeed:SetShown(n == 0)
  end

  -- Friends: each one's newest character.
  local function drawFriends()
    local latest = {}
    for _, f in ipairs(ns.sharedWithYou and ns.sharedWithYou() or {}) do
      local c = f.chars and f.chars[1]
      if c then latest[#latest + 1] = { label = f.label or f.tag, c = c, at = c.received or c.at or 0 } end
    end
    table.sort(latest, function(a, b) return a.at > b.at end)
    for i, row in ipairs(friendRows) do
      local f = latest[i]
      if f then
        local c = f.c
        row.name:SetText(c.char or "?")
        row.name:SetTextColor(ui.classRgb(c.classFile))
        local bits = { string.format("%s %s", c.level or "?", c.class or "") }
        local fl = ns.friendLevels(c)
        if #fl > 0 then bits[#bits + 1] = ns.levelText(fl[#fl]) end
        if c.zone then bits[#bits + 1] = c.zone end
        bits[#bits + 1] = f.label
        row.info:SetText(table.concat(bits, "  ·  "))
        row.when:SetText(ns.ago(f.at))
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
    end
    if all then drawFriends() end
    drawLive(me)
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
