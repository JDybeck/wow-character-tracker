-- A small box that offers to post a moment no setting posts: a boss kill, blue or purple gear, a
-- level-up, a mount, a win, a milestone... Which ones comes from the `ask` setting (the Sharing
-- table's Ask first column, or the addon window). Post sends
-- the app a separate "ask" event carrying the moment, so the moment itself isn't counted twice in the
-- app's history. Where guild chat needs a click (ns.GUILD_CHAT_NEEDS_CLICK), the box also offers the
-- guild line, in the same box when a moment has both. The box is our own frame and takes no action
-- Blizzard protects, so no taint. It never pops up in combat, and goes away by itself.
local ADDON, ns = ...

-- How long the box stays, counted down on the Skip button: the In game setting askSeconds, 10 by default.
local function showSeconds()
  local n = tonumber(ns.setting("askSeconds"))
  if not n then return 10 end
  return math.max(3, math.min(120, math.floor(n)))
end
local MAX_WAITING = 5

-- Entries: { e = the moment, discord = true when offered for Discord, guild = the guild chat line }.
local waiting = {}
local current
local shownAt = 0 -- which showing a timer belongs to
local box

-- The kinds offered as themselves (wins only: a lost duel or battleground is never offered).
local ASKED_AS_KIND = { mount = true, keyrun = true, death = true, profession = true, mplus = true, pvp = true,
  duel = true, makgora = true, bg = true }

-- The `ask` switch that covers this moment, or nil when it's never offered.
local function askKind(e)
  local k = e.kind
  if k == "boss" then return "boss" end
  if k == "level" then return e.maxLevel and "maxlevel" or "level" end
  if k == "loot" then
    if e.mat then return nil end
    if (e.quality or 0) == 3 then return "rare" end
    if (e.quality or 0) == 4 then return "epic" end
    return nil
  end
  if e.lost then return nil end
  return ASKED_AS_KIND[k] and k or nil
end

-- Only with the app and a webhook on the other end: otherwise a post can't go anywhere.
local function offered(e)
  local ask = ns.setting("ask")
  if not ask or not ns.hasApp() or not ns.setting("discord") then return false end
  local kind = askKind(e)
  return kind ~= nil and ask[kind] == true
end

local function describe(e)
  if e.kind == "boss" then
    return string.format("Killed %s%s", e.boss, (e.instance and e.instance ~= "") and (" in " .. e.instance) or "")
  end
  if e.kind == "loot" then return "Looted " .. ns.itemLink(e.itemId, e.item) end
  return (ns.momentLine and ns.momentLine(e)) or e.kind -- the same words as the chat line (Announce.lua)
end

local function buildBox()
  if box then return box end
  box = CreateFrame("Frame", nil, UIParent)
  box:SetSize(320, 74)
  box:SetFrameStrata("DIALOG")
  box:SetClampedToScreen(true)
  box:EnableMouse(true)
  box:SetMovable(true)
  box:RegisterForDrag("LeftButton")
  box:SetScript("OnDragStart", box.StartMoving)
  box:SetScript("OnDragStop", function(self)
    self:StopMovingOrSizing()
    local point, _, relative, x, y = self:GetPoint()
    WoWCharacterTrackerDB.askBox = { point = point, relative = relative, x = x, y = y }
  end)
  local saved = WoWCharacterTrackerDB.askBox
  if saved then box:SetPoint(saved.point, UIParent, saved.relative, saved.x, saved.y)
  else box:SetPoint("TOP", UIParent, "TOP", 0, -140) end

  local bg = box:CreateTexture(nil, "BACKGROUND")
  bg:SetColorTexture(0.05, 0.05, 0.08, 0.92)
  bg:SetAllPoints()
  box.title = box:CreateFontString(nil, "OVERLAY", "GameFontNormal")
  box.title:SetPoint("TOPLEFT", 10, -8)
  box.line = box:CreateFontString(nil, "OVERLAY", "GameFontHighlight")
  box.line:SetPoint("TOPLEFT", box.title, "BOTTOMLEFT", 0, -5)
  box.line:SetPoint("RIGHT", box, "RIGHT", -10, 0)
  box.line:SetJustifyH("LEFT")
  box.line:SetWordWrap(false)

  local function button(width, discord, guild)
    local b = CreateFrame("Button", nil, box, "UIPanelButtonTemplate")
    b:SetSize(width, 22)
    b:SetScript("OnClick", function() ns.answerAsk(discord, guild) end)
    return b
  end
  box.skip = button(80, false, false) -- room for "Skip (10)"
  box.skip:SetPoint("BOTTOMRIGHT", box, "BOTTOMRIGHT", -8, 7)
  box.both = button(64, true, true)
  box.both:SetText("Both")
  box.guild = button(64, false, true)
  box.discord = button(64, true, false)
  box:Hide()
  return box
end

-- Title, line and buttons for what this moment is offered for: Discord, guild chat or both.
local function fill(entry)
  local f = buildBox()
  local both = entry.discord and entry.guild
  f.title:SetText(both and "Share this?" or entry.guild and "Tell your guild?" or "Post to Discord?")
  f.line:SetText(entry.guild or describe(entry.e))
  f.discord:SetText(both and "Discord" or "Post")
  f.guild:SetText(both and "Guild" or "Send")
  local shown = both and { f.both, f.guild, f.discord } or { entry.guild and f.guild or f.discord }
  f.both:Hide()
  f.guild:Hide()
  f.discord:Hide()
  local anchor = f.skip
  for _, b in ipairs(shown) do
    b:ClearAllPoints()
    b:SetPoint("RIGHT", anchor, "LEFT", -6, 0)
    b:Show()
    anchor = b
  end
  return f
end

function ns.showNextAsk()
  if current or #waiting == 0 then return end
  if InCombatLockdown and InCombatLockdown() then return end -- PLAYER_REGEN_ENABLED comes back here
  current = table.remove(waiting, 1)
  local f = fill(current)
  f:Show()
  shownAt = shownAt + 1
  local showing = shownAt
  -- "Skip (10)", "Skip (9)"... then it goes away by itself. A newer showing stops this one.
  local function countdown(left)
    if showing ~= shownAt or not current then return end
    if left <= 0 then return ns.answerAsk(false) end
    f.skip:SetText(string.format("Skip (%d)", left))
    C_Timer.After(1, function() countdown(left - 1) end)
  end
  countdown(showSeconds())
end

-- The player's answer to the moment in the box (or running out of time: neither).
function ns.answerAsk(discord, guild)
  local entry = current
  current = nil
  if box then box:Hide() end
  if entry and guild and entry.guild then ns.sayInGuild(entry.guild) end -- in the click, or the game blocks it
  if entry and discord and entry.discord then
    local e = entry.e
    local fields = {}
    for k, v in pairs(e) do fields[k] = v end
    fields.id, fields.kind, fields.shot = nil, nil, nil
    fields.askedKind = e.kind
    ns.record("ask", fields, string.format("ask:%s:%d", tostring(e.boss or e.itemId or e.kind), e.at or time()))
    ns.say(WoWCharacterTrackerDB.live == false and "It posts on your next /reload or logout." or "Sent to the app to post.")
  end
  ns.showNextAsk()
end

-- One box per moment: what it's offered for is added to its entry, shown or waiting.
local function offer(e, field, value)
  if current and current.e == e then
    current[field] = value
    fill(current)
    return
  end
  for _, entry in ipairs(waiting) do
    if entry.e == e then
      entry[field] = value
      return
    end
  end
  if #waiting >= MAX_WAITING then table.remove(waiting, 1) end
  table.insert(waiting, { e = e, [field] = value })
  ns.showNextAsk()
end

-- A moment that won't be posted by itself: offer it, when that's switched on.
function ns.offerAsk(e)
  if ns.isIgnored(e.guid) or ns.incognitoActive() or not offered(e) then return end
  offer(e, "discord", true)
end

-- A guild chat line (Announce.lua) where the game only sends it on a click.
function ns.offerGuildLine(e, msg)
  offer(e, "guild", msg)
end
