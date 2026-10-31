-- A small box that offers to post a highlight no setting posts: a boss kill or looted blue or purple
-- gear. Which ones comes from the `ask` setting (the In game page, or the game's options). Post sends
-- the app a separate "ask" event carrying the moment, so the moment itself isn't counted twice in the
-- app's history. The box is our own frame and takes no action Blizzard protects, so no taint. It
-- never pops up in combat, and goes away by itself.
local ADDON, ns = ...

local SHOW_SECONDS = 30
local MAX_WAITING = 5

local waiting = {}
local current
local shownAt = 0 -- which showing a timer belongs to
local box

-- The `ask` switch that covers this moment, or nil when it's never offered.
local function askKind(e)
  if e.kind == "boss" then return "boss" end
  if e.kind ~= "loot" or e.mat then return nil end
  if (e.quality or 0) == 3 then return "rare" end
  if (e.quality or 0) == 4 then return "epic" end
  return nil
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
  return "Looted " .. ns.itemLink(e.itemId, e.item)
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
  local title = box:CreateFontString(nil, "OVERLAY", "GameFontNormal")
  title:SetPoint("TOPLEFT", 10, -8)
  title:SetText("Post to Discord?")
  box.line = box:CreateFontString(nil, "OVERLAY", "GameFontHighlight")
  box.line:SetPoint("TOPLEFT", title, "BOTTOMLEFT", 0, -5)
  box.line:SetPoint("RIGHT", box, "RIGHT", -10, 0)
  box.line:SetJustifyH("LEFT")
  box.line:SetWordWrap(false)

  local function button(label, post)
    local b = CreateFrame("Button", nil, box, "UIPanelButtonTemplate")
    b:SetSize(70, 22)
    b:SetText(label)
    b:SetScript("OnClick", function() ns.answerAsk(post) end)
    return b
  end
  local skip = button("Skip", false)
  skip:SetPoint("BOTTOMRIGHT", box, "BOTTOMRIGHT", -8, 7)
  button("Post", true):SetPoint("RIGHT", skip, "LEFT", -6, 0)
  box:Hide()
  return box
end

function ns.showNextAsk()
  if current or #waiting == 0 then return end
  if InCombatLockdown and InCombatLockdown() then return end -- PLAYER_REGEN_ENABLED comes back here
  current = table.remove(waiting, 1)
  local f = buildBox()
  f.line:SetText(describe(current))
  f:Show()
  shownAt = shownAt + 1
  local showing = shownAt
  C_Timer.After(SHOW_SECONDS, function()
    if showing == shownAt and current then ns.answerAsk(false) end
  end)
end

-- The player's answer to the moment in the box (or running out of time: false).
function ns.answerAsk(post)
  local e = current
  current = nil
  if box then box:Hide() end
  if e and post then
    local fields = {}
    for k, v in pairs(e) do fields[k] = v end
    fields.id, fields.kind, fields.shot = nil, nil, nil
    fields.askedKind = e.kind
    ns.record("ask", fields, string.format("ask:%s:%d", tostring(e.boss or e.itemId), e.at or time()))
    ns.say(WoWCharacterTrackerDB.live == false and "It posts on your next /reload or logout." or "Sent to the app to post.")
  end
  ns.showNextAsk()
end

-- A moment that won't be posted by itself: offer it, when that's switched on.
function ns.offerAsk(e)
  if not offered(e) then return end
  if #waiting >= MAX_WAITING then table.remove(waiting, 1) end
  table.insert(waiting, e)
  ns.showNextAsk()
end
