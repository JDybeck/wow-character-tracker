-- The addon window (/wct, the minimap button): every in-game option in tabs. Settings the game
-- keeps (WoWCharacterTrackerDB) and the In game settings shared with the app (ns.editSettings, the
-- newest change wins) apply right away. What gets posted to Discord comes from the app
-- (Settings.lua) and is listed read-only on the Settings tab. Plain frames only, so it opens in combat.
-- Built on first open, each tab on its first showing.
local ADDON, ns = ...
local ui = ns.ui

local NAME = "WoWCharacterTrackerWindow"
local WIDTH, HEIGHT = 760, 590
local TITLE_BAR, TAB_BAR = 44, 40
local AREA_HEIGHT = HEIGHT - TITLE_BAR - TAB_BAR - 8 -- below the title and the tab row
local PAGE_WIDTH = WIDTH - 50

local window, area, version

-- The width tab pages have now: the window can be made wider than WIDTH (the grip, bottom right).
local function pageWidth() return ((window and window:GetWidth()) or WIDTH) - 50 end
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

-- Reload reminders: one choice over the three settings that ask for a /reload (the ding popup, the
-- flight path box and the chat line). Gentle is how they start; any other mix is "custom".
local RELOAD_CHOICES = { { "off", "Off" }, { "gentle", "Gentle" }, { "all", "Every chance" } }
local RELOAD_SETS = { off = { false, false, false }, gentle = { false, true, true }, all = { true, true, true } }

local function reloadChoice()
  local now = { WoWCharacterTrackerDB.popup == true, isOn("flightReload"), isOn("chatReminder") }
  for key, set in pairs(RELOAD_SETS) do
    if set[1] == now[1] and set[2] == now[2] and set[3] == now[3] then return key end
  end
  return "custom"
end
ns.reloadChoice = reloadChoice -- for the tests

local function setReloadChoice(key)
  local set = RELOAD_SETS[key]
  if not set then return end
  WoWCharacterTrackerDB.popup = set[1]
  ns.editSettings(function(s) s.flightReload, s.chatReminder = set[2], set[3] end)
end

-- Chat ------------------------------------------------------------------------------------------
-- The same kinds and names as the app's In game page (FlavorViewModel.AnnounceNames).
local ANNOUNCE_KINDS = { { "level", "Level-ups" }, { "maxlevel", "Max level" }, { "loot", "Epic loot" }, { "rare", "Rare loot" }, { "followed", "Followed items" },
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

-- The Sharing tab: how much gets shared (the app's preset, which the app applies on your next
-- /reload), the guild and group lines, and the ask box. The per-moment table is the app's; without
-- the app the chat table is here instead, since nothing else sets it.
local PRESETS_SHOWN = { { "quiet", "Quiet" }, { "normal", "Normal" }, { "everything", "Everything" } }
local ASK_KINDS = { "boss", "rare", "epic" }

-- Incognito (Shared.lua, Commands.lua): a while without posts or chat lines, never caught up later.
local INCOGNITO_CHOICES = { { "30m", "30 minutes" }, { "1h", "1 hour" }, { "2h", "2 hours" }, { "4h", "4 hours" } }

local function buildIncognito(p)
  local title = ui.heading(p, "Incognito")
  local hint = ui.hint(p, "For a while nothing is posted to Discord or said in guild or group chat, and none of it is posted later. Your own history keeps it.", title)
  local friendsToo = false
  local last
  for i, choice in ipairs(INCOGNITO_CHOICES) do
    local b = ui.button(p, choice[2], 100, function()
      ns.goIncognito(ns.INCOGNITO_TIMES[choice[1]], friendsToo)
      ui.refresh(p)
    end)
    if last then b:SetPoint("LEFT", last, "RIGHT", 6, 0) else b:SetPoint("TOPLEFT", hint, "BOTTOMLEFT", 0, -10) end
    last = b
  end
  local off = ui.button(p, "End now", 100, function()
    ns.goIncognito(nil)
    ui.refresh(p)
  end, "primary")
  off:SetPoint("LEFT", last, "RIGHT", 12, 0)
  local status = ui.text(p, "GameFontHighlightSmall")
  status:SetPoint("LEFT", off, "RIGHT", 10, 0)
  local friends = ui.checkbox(p, "Keep highlights from that time from friends too", nil,
    function()
      local w = ns.incognitoActive()
      if w then return w.friends == true end
      return friendsToo
    end,
    function(on)
      friendsToo = on
      local w = ns.incognitoActive()
      if w then w.friends = on end
    end)
  friends:SetPoint("TOPLEFT", hint, "BOTTOMLEFT", -2, -40)
  ui.onRefresh(p, function()
    local w = ns.incognitoActive()
    off:SetShown(w ~= nil)
    status:SetText(w and ("Until " .. date("%H:%M", w["until"])) or "")
  end)
  return friends
end

local function buildSharingTab(p)
  local withApp = ns.hasApp()
  local top = buildIncognito(p)
  if withApp then
    local presetTitle = ui.heading(p, "How much gets shared", top, -30)
    local presetHint = ui.hint(p, "What goes to Discord, chat and the ask box. The app picks it up on your next /reload; choose per moment on the app's Sharing page.", presetTitle)
    local presets = ui.choice(p, PRESETS_SHOWN, function() return ns.setting("preset") end,
      function(key) ns.editSettings(function(s) s.preset = key end) end)
    presets:SetPoint("TOPLEFT", presetHint, "BOTTOMLEFT", 0, -10)
    local custom = ui.text(p, "GameFontDisableSmall", "Custom: your own picks from the app.")
    custom:SetPoint("LEFT", presets, "RIGHT", 6, 0)
    ui.onRefresh(p, function() custom:SetShown(ns.setting("preset") == "custom") end)
    top = presets
  end

  local chatTitle = ui.heading(p, "Tell in chat", top, top and -24 or nil)
  local chatHint = ui.hint(p, "One line in chat as you when something happens. Lines wait until you're out of combat.", chatTitle)
  local whereLast = ui.checkGrid(p, { { "guild", "Guild chat" }, { "party", "Party / raid chat" } }, chatHint,
    function(key)
      local a = ns.setting("announce") or {}
      return a[key] == true
    end,
    function(key, on) ns.editSettings(function(s) s.announce[key] = on end) end)
  local last = whereLast
  if not withApp then
    last = ui.checkTable(p, ANNOUNCE_KINDS, { { "guild", "Guild chat" }, { "party", "Group chat" } }, whereLast,
      function(column, key)
        local kinds = announceKinds(ns.setting("announce") or {}, column)
        return kinds ~= nil and kinds[key] == true
      end,
      function(column, key, on) ns.editSettings(function(s) announceKinds(s.announce, column)[key] = on or nil end) end)
  end

  if not withApp then return end
  local askTitle = ui.heading(p, "Ask before skipping", last)
  -- On: boss kills and gear, the usual three (the app's Sharing table picks any moment). Off: none at all.
  local ask = ui.checkbox(p, "Offer to post what Discord skips", "Boss kills and gear the app doesn't post by itself: a small Post or Skip box, out of combat. More moments in the app's Sharing table.",
    function() return next(ns.setting("ask") or {}) ~= nil end,
    function(on)
      ns.editSettings(function(s)
        s.ask = {}
        if on then for _, k in ipairs(ASK_KINDS) do s.ask[k] = true end end
      end)
    end)
  ask:SetPoint("TOPLEFT", askTitle, "BOTTOMLEFT", -2, -6)

  -- How long the box stays (askSeconds, the same choices as the app): click for the next.
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
  secondsButton:SetPoint("TOPLEFT", ask, "BOTTOMLEFT", 32, -22)
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
-- opens without them. Cards are pooled: a redraw reuses the frames and hides the spare ones.
-- A click on a friend's card opens that character's details beside the window.
local C = ui.COLOR
local DAY = 86400
local CARD = 40     -- a friend or character card
local REQUEST = 34  -- a request banner
local drawFriends
local openSharing -- opens what you share with one contact beside the window (set further down)
local buildShareDefaults -- what all friends get, under the Friends tab's lists (set further down)
local SHARE_DEFAULTS_HEIGHT = 400
local searched = false -- Find online friends was clicked this session
local friendsSaid = "" -- what the last button said, shown under the hint
local openFriendTag    -- the friend whose characters are unfolded (one at a time, this session)
local revealKey        -- a character Home asked to show: its friend unfolds on the next draw

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

local classRgb, gold = ui.classRgb, ui.gold

local function classColor(fs, classFile) fs:SetTextColor(classRgb(classFile)) end

local ago = ns.ago

-- Friends.lua's label ("Zebbe (Zeb#2222)" with a nickname), else the BattleTag.
local function who(label, tag) return label or tag end

-- A card's button runs whatever the last redraw gave it (self.act).
local function cardButton(card, width)
  return ui.button(card, "", width, function(self) if self.act then self.act() end end)
end

-- style: "primary" or nil. act nil: shown disabled.
local function setButton(b, label, act, style)
  b:SetText(label)
  b.act = act
  ui.styleButton(b, style)
  if act then b:Enable() else b:Disable() end
  b:Show()
end

-- The shown buttons from the right edge in, the first of them leftmost.
local function alignButtons(card, buttons)
  local right = -10
  for i = #buttons, 1, -1 do
    local b = buttons[i]
    if b:IsShown() then
      b:ClearAllPoints()
      b:SetPoint("RIGHT", card, "RIGHT", right, 0)
      right = right - (b:GetWidth() or 0) - 6
    end
  end
end

-- Ask opens the ask box; what asking said shows on the Friends tab.
local function askAction(tag)
  if ns.openAskBox then
    ns.openAskBox(tag, function(s)
      friendsSaid = s or ""
      if drawFriends then drawFriends() end
    end)
  end
  return nil
end

-- A friend's state text, its look (good, bad or nil) and up to two buttons ({ label, fn(tag),
-- style }, fn nil: shown disabled). p: a row of the Friends list (friendRows), online or not.
local function friendState(p)
  if p.blocked then return "Blocked", "bad" end
  if p.contact then
    local bits = {}
    if p.contact.share then bits[#bits + 1] = "You share" end
    if p.sharesWithYou then bits[#bits + 1] = #bits > 0 and "shares with you" or "Shares with you" end
    local again = p.online and { p.sharesWithYou and "Ask for more" or "Ask again", askAction } or nil
    return #bits > 0 and table.concat(bits, ", ") or "Contact", #bits > 0 and "good" or nil, { "Sharing", openSharing }, again
  end
  if p.hasAddon == nil then return "Checking..." end
  if p.hasAddon then return "Has the addon", nil, { "Ask", askAction, "primary" }, { "Share", ns.shareWithFriend } end
  if p.invitedAt and time() - p.invitedAt < DAY then return "No addon", nil, { "Invited" } end
  return "No addon", nil, { "Invite", ns.inviteFriend, "primary" }
end

-- "Zeb Stonehand" with a Forever surname, else the name.
local fullName = ns.fullName

-- "Zeb Stonehand (Firemaw)": tells characters apart when there's no guid.
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

local function hideTip() if GameTooltip then GameTooltip:Hide() end end

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

-- The details panel: everything known about one friend character beside the window, its gear with
-- the game's own item tooltips (built from the ids). Built on the first click on a card.
local DETAILS_PANEL = "WoWCharacterTrackerDetailsPanel"
local DETAILS_WIDTH = 320
local GEAR_ROW = 30
local SLOT_NAMES = { "Head", "Neck", "Shoulder", "Shirt", "Chest", "Waist", "Legs", "Feet", "Wrist", "Hands",
  "Finger", "Finger", "Trinket", "Trinket", "Back", "Main Hand", "Off Hand", "Ranged", "Tabard" }
local details, detailsTitle, detailsSub, detailsLine, gearTitle, noGear, notShared
local gearRows = {}
local detailsKey -- which character the panel shows

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

-- A label and its value, the value wrapping in what's left of width.
local function newFactRow(parent, width)
  local r = CreateFrame("Frame", nil, parent)
  r:SetSize(width, 16)
  r.labelText = ui.text(r, "GameFontDisableSmall")
  r.labelText:SetPoint("TOPLEFT", r, "TOPLEFT", 0, 0)
  r.labelText:SetWidth(86)
  r.valueText = ui.text(r, "GameFontHighlightSmall")
  r.valueText:SetPoint("TOPLEFT", r, "TOPLEFT", 92, 0)
  r.valueText:SetWidth(width - 92)
  return r
end

local function newGearRow()
  local r = CreateFrame("Frame", nil, details)
  r:SetSize(DETAILS_WIDTH - 24, GEAR_ROW)
  r:EnableMouse(true)
  r.icon = r:CreateTexture(nil, "ARTWORK")
  r.icon:SetSize(24, 24)
  r.icon:SetPoint("LEFT", r, "LEFT", 0, 0)
  r.slotText = ui.text(r, "GameFontDisableSmall")
  r.slotText:SetPoint("LEFT", r, "LEFT", 30, 0)
  r.slotText:SetWidth(62)
  r.nameText = ui.text(r, "GameFontHighlightSmall")
  r.nameText:SetPoint("LEFT", r, "LEFT", 96, 0)
  r.nameText:SetWidth(DETAILS_WIDTH - 24 - 96)
  r.nameText:SetWordWrap(false)
  r.enchantText = ui.text(r, "GameFontHighlightSmall")
  r.enchantText:SetPoint("TOPLEFT", r.nameText, "BOTTOMLEFT", 0, -2)
  r.enchantText:SetWidth(DETAILS_WIDTH - 24 - 96)
  r.enchantText:SetWordWrap(false)
  r.enchantText:SetTextColor(0.1, 1, 0.1)
  r:SetScript("OnEnter", showItemTip)
  r:SetScript("OnLeave", hideTip)
  return r
end

local sharing -- the sharing panel, built further down

-- The side panels (details, sharing) go beside the window on the side with room: clamped to the
-- screen on the full side, they would slide over the window.
local function placeBeside(panel)
  if not panel then return end
  panel:ClearAllPoints()
  local right, screen = window:GetRight(), UIParent:GetRight()
  if right and screen and right + 4 + DETAILS_WIDTH > screen then
    panel:SetPoint("TOPRIGHT", window, "TOPLEFT", -4, 0)
  else
    panel:SetPoint("TOPLEFT", window, "TOPRIGHT", 4, 0)
  end
end

local function placePanels()
  placeBeside(details)
  placeBeside(sharing)
end

-- A side panel: the window's look, a close button, Esc closes it.
local function sidePanel(name)
  local p = CreateFrame("Frame", name, window)
  p:SetFrameStrata("DIALOG")
  p:SetClampedToScreen(true)
  p:EnableMouse(true)
  p:SetWidth(DETAILS_WIDTH)
  if UISpecialFrames then table.insert(UISpecialFrames, name) end
  ui.box(p, C.window, C.border)
  closeButton(p, function() p:Hide() end)
  return p
end

local function buildDetails()
  details = sidePanel(DETAILS_PANEL)
  detailsTitle = ui.text(details, "GameFontHighlightLarge")
  detailsTitle:SetPoint("TOPLEFT", 14, -14)
  detailsTitle:SetWidth(DETAILS_WIDTH - 50)
  detailsSub = ui.text(details, "GameFontHighlight")
  detailsSub:SetWidth(DETAILS_WIDTH - 28)
  detailsLine = details:CreateTexture(nil, "ARTWORK")
  ui.fill(detailsLine, C.border)
  detailsLine:SetHeight(1)
  notShared =ui.text(details, "GameFontDisableSmall", "Not shared with you. Ask them on the Friends tab to see their gear and more.")
  notShared:SetWidth(DETAILS_WIDTH - 28)
  gearTitle = ui.heading(details, "Gear", nil)
  noGear = ui.text(details, "GameFontDisableSmall", "No gear shared")
end

local function textHeight(fs, fallback)
  local h = fs.GetStringHeight and fs:GetStringHeight()
  return (h and h > 0) and h or fallback
end

-- "Level 60 Frost Mage", from what's known.
local function headline(c)
  local parts = {}
  if c.level then parts[#parts + 1] = "Level " .. c.level end
  if c.spec then parts[#parts + 1] = c.spec end
  if c.class then parts[#parts + 1] = c.class end
  return table.concat(parts, " ")
end

-- The label and value lines a character's info lists, each only when known.
local function facts(c, friend)
  local out = {}
  local function add(label, value) if value and value ~= "" then out[#out + 1] = { label, value } end end
  add("Friend", friend)
  -- Forever's realm names are a type and a layer ("CAMWOW EU5 Realm 6 PvP" -> PvP, layer 6). The
  -- ruleset (Hardcore, Self-found) only when it isn't the usual one.
  local realmType, layer
  if not ns.IS_RETAIL then realmType, layer = ns.realmType(c.realm) end
  add("Realm", realmType or c.realm)
  add("Layer", layer and tostring(layer))
  if c.ruleset ~= "Normal" then add("Ruleset", c.ruleset) end
  local race = c.race and (c.sex == "female" and "Female " or (c.sex == "male" and "Male " or "")) .. c.race
  add("Race", race and (c.faction and string.format("%s, %s", race, c.faction) or race))
  add("Item level", c.ilvl and tostring(math.floor(c.ilvl)))
  add("Zone", c.zone)
  add("Gold", c.money and gold(c.money))
  add("Professions", c.professions)
  add("Talents", c.talents)
  add("Updated", (c.received or c.at) and ago(c.received or c.at))
  return out
end

-- A character's level times: its own (c.levelList) or what a friend shared.
local function levelsOf(c) return c.levelList or ns.friendLevels(c) end

local drawDetails
local detailsChar, detailsFriend -- what the panel shows, for a redraw once item names arrive
local redrawSoon = false

-- An item the game hasn't cached yet shows as "item 12345": ask for it, and redraw the panel (once
-- for a batch) when it arrives.
local function whenLoaded(id)
  if not (Item and Item.CreateFromItemID) then return end
  local ok, item = pcall(Item.CreateFromItemID, Item, id)
  if not (ok and item and item.ContinueOnItemLoad) then return end
  item:ContinueOnItemLoad(function()
    if redrawSoon then return end
    redrawSoon = true
    C_Timer.After(0.2, function()
      redrawSoon = false
      if details and details:IsShown() and detailsChar then drawDetails(detailsChar, detailsFriend) end
    end)
  end)
end

-- c: a shared character, or what Battle.net says about an online one (c.notShared).
drawDetails = function(c, friend)
  detailsChar, detailsFriend = c, friend
  detailsKey = charKey(c)
  detailsTitle:SetText(fullName(c))
  classColor(detailsTitle, c.classFile)
  local y = 14 + textHeight(detailsTitle, 16) + 4
  local sub = headline(c)
  detailsSub:SetText(sub)
  detailsSub:SetShown(sub ~= "")
  if sub ~= "" then
    place(detailsSub, details, 14, y)
    y = y + textHeight(detailsSub, 14)
  end
  y = y + 10
  detailsLine:ClearAllPoints()
  detailsLine:SetPoint("TOPLEFT", details, "TOPLEFT", 14, -y)
  detailsLine:SetPoint("TOPRIGHT", details, "TOPRIGHT", -14, -y)
  y = y + 2

  notShared:SetShown(c.notShared == true)
  local items = c.notShared and {} or ns.friendGear(c)
  gearTitle:SetShown(not c.notShared)
  noGear:SetShown(not c.notShared and #items == 0)
  if c.notShared then
    place(notShared, details, 14, y + 6)
    y = y + 6 + textHeight(notShared, 28)
  else
    place(gearTitle, details, 14, y + 10)
    y = y + 32
    if #items == 0 then
      place(noGear, details, 14, y)
      y = y + 16
    end
  end
  for i, item in ipairs(items) do
    local r = pooled(gearRows, i, newGearRow)
    r.itemId, r.enchantId = item.id, item.enchantId
    r.icon:SetTexture(itemIcon(item.id))
    r.slotText:SetText(SLOT_NAMES[item.slot] or "")
    local name, red, green, blue = itemName(item.id)
    if not ns.itemInfo(item.id) then whenLoaded(item.id) end
    r.nameText:SetText(name)
    r.nameText:SetTextColor(red, green, blue)
    r.enchantText:SetText(item.enchant or "")
    r.enchantText:SetShown(item.enchant ~= nil)
    -- One line sits level with the slot name; with an enchant the two lines center on it together.
    r.nameText:ClearAllPoints()
    if item.enchant then r.nameText:SetPoint("BOTTOMLEFT", r, "LEFT", 96, 1)
    else r.nameText:SetPoint("LEFT", r, "LEFT", 96, 0) end
    place(r, details, 14, y)
    y = y + GEAR_ROW
  end
  hideFrom(gearRows, #items + 1)
  details:SetHeight(y + 14)
  placeBeside(details)
  if sharing then sharing:Hide() end -- one side panel at a time
  details:Show()
end

-- The character whose info is unfolded under its row (one at a time, on the Friends and
-- Characters tabs); its gear is in the side panel, which also closes on its own.
local expandedKey

local function redrawTab() if current and tabs[current] and tabs[current].page then ui.refresh(tabs[current].page) end end

-- A click on a card: unfolds its info and opens its gear beside the window, or folds both away
-- when it's the one open.
local function toggleDetails(self)
  local c = self.char
  if not c then return end
  if not details then buildDetails() end
  if expandedKey == charKey(c) then
    expandedKey = nil
    details:Hide()
  else
    expandedKey = charKey(c)
    drawDetails(c, self.friend)
  end
  redrawTab()
end

-- Home's friend rows: the Friends tab with that character unfolded.
local selectTab -- the window's, further down
function ns.toggleFriendDetails(c, friend)
  if not details then buildDetails() end
  expandedKey = charKey(c)
  revealKey = expandedKey
  selectTab("friends")
  drawDetails(c, friend)
end

-- The unfolded info under a row: the facts on the left, the level times and their chart on the
-- right. One per tab page, placed under whichever row is open.
local EXPAND_CHART = 30
local function newExpansion(p, width)
  local ex = CreateFrame("Frame", nil, p)
  ex:SetWidth(width)
  ex.width = width
  ui.box(ex, C.bar, C.border)
  ex.half = math.floor((width - 36) / 2)
  ex.left, ex.right = {}, {}
  ex.levelTitle = ui.text(ex, "GameFontNormalSmall", "Level times")
  ui.tint(ex.levelTitle, C.accent)
  ex.noLevels = ui.text(ex, "GameFontDisableSmall", "No level times shared.")
  ex.chart = ui.levelChart(ex, 30, EXPAND_CHART, math.floor(ex.half / 30))
  return ex
end

-- Lists rows of { label, value } in a column from y; returns the y under them.
local function factColumn(ex, pool, list, x, y)
  for i, fact in ipairs(list) do
    local r = pooled(pool, i, function() return newFactRow(ex, ex.half) end)
    r.labelText:SetText(fact[1])
    r.valueText:SetText(fact[2])
    local h = math.max(14, textHeight(r.valueText, 14))
    r:SetHeight(h)
    place(r, ex, x, y)
    y = y + h + 5
  end
  hideFrom(pool, #list + 1)
  return y
end

-- Hidden characters (yours or friends'), by charKey, kept with the window's choices. Shown again,
-- dimmed, while "show them" is on (this session).
local showHidden = false
local function isHidden(c) return (saved().hiddenChars or {})[charKey(c)] == true end
local function setHidden(c, on)
  local s = saved()
  s.hiddenChars = s.hiddenChars or {}
  s.hiddenChars[charKey(c)] = on or nil
end

-- "2 hidden characters, show them" under a list (nil without any hidden): a button that flips it.
local function hiddenLink(p, count)
  p.hiddenLink = p.hiddenLink or ui.button(p, "", 260, function()
    showHidden = not showHidden
    redrawTab()
  end)
  p.hiddenLink:SetText(string.format("%d hidden character%s, %s", count, count == 1 and "" or "s", showHidden and "hide them" or "show them"))
  p.hiddenLink:SetShown(count > 0)
  return count > 0 and p.hiddenLink or nil
end

-- Fills the info for c and returns its height.
local function drawExpansion(ex, c, friend)
  local left = factColumn(ex, ex.left, facts(c, friend), 12, 10)
  local x = 24 + ex.half
  place(ex.levelTitle, ex, x, 10)
  local levels = levelsOf(c)
  local right = factColumn(ex, ex.right, ns.levelingFacts(levels, c), x, 28)
  ex.noLevels:SetShown(#levels == 0)
  if #levels == 0 then
    place(ex.noLevels, ex, x, right)
    right = right + 16
  end
  ex.chart:SetShown(#levels > 0)
  if #levels > 0 then
    place(ex.chart, ex, x, right + 4)
    local playing = (c.levelTime or 0) > 0 and c.level and not (c.maxLevel and c.level >= c.maxLevel)
    ex.chart.draw(levels, playing and { level = c.level + 1, seconds = c.levelTime } or nil)
    right = right + 4 + EXPAND_CHART
  end
  -- Hide (or show again): out of the list, and the info folds away.
  ex.hide = ex.hide or cardButton(ex, 110)
  local hidden = isHidden(c)
  setButton(ex.hide, hidden and "Show again" or "Hide", function()
    setHidden(c, not hidden)
    expandedKey = nil
    if details then details:Hide() end
    redrawTab()
  end)
  local h = math.max(left, right) + 10
  place(ex.hide, ex, ex.width - 122, h)
  h = h + 30
  ex:SetHeight(h)
  ex:Show()
  return h
end

-- The sharing panel: what one contact gets from you, beside the window, with the same choices as
-- the app's Friends page. Changes apply right away (Friends.lua's ns.setContact*).
local SHARING_PANEL = "WoWCharacterTrackerSharingPanel"
local SHARING_HEIGHT = 630
local CATEGORY_NAMES = { gear = "Gear", talents = "Talents", location = "Location", professions = "Professions", highlights = "Highlights", leveling = "Level times",
  gold = "Gold", map = "Map position", otherFaction = "Map for the other faction" }
local sharingTag, sharingTitle, sharingSummary, askSaid, nickname
local categoryButtons, askButtons = {}, {}
local askWants = {} -- [category] = true: picked to ask them for

-- "Gear: like all friends (on)", "Gear: on" or "Gear: off".
local function categoryText(cat)
  local name = CATEGORY_NAMES[cat.key] or cat.key
  if cat.own == nil then return string.format("%s: like all friends (%s)", name, cat.general and "on" or "off") end
  return string.format("%s: %s", name, cat.own and "on" or "off")
end

local function sharingNow() return sharingTag and ns.contactSharing and ns.contactSharing(sharingTag) end

local function drawSharing()
  local o = sharingNow()
  if not o then
    sharing:Hide()
    return
  end
  sharingTitle:SetText(o.label)
  if not nickname:HasFocus() then
    nickname.saved = o.nickname or ""
    nickname:SetText(nickname.saved)
  end
  for i, cat in ipairs(o.categories) do
    local b = categoryButtons[i]
    if b then
      b.key = cat.key
      b:SetText(categoryText(cat))
      ui.styleButton(b, cat.own ~= nil and "selected" or nil) -- their own choice stands out
    end
  end
  sharingSummary:SetText(o.share and ("They get " .. o.summary .. ".") or "They get nothing while you don't share with them.")
  for _, b in ipairs(askButtons) do ui.styleButton(b, askWants[b.key] and "selected" or nil) end
  ui.refresh(sharing) -- the checkboxes
end

local function buildSharing()
  sharing = sidePanel(SHARING_PANEL)
  sharing:SetHeight(SHARING_HEIGHT)
  sharingTitle = ui.text(sharing, "GameFontHighlightLarge")
  sharingTitle:SetPoint("TOPLEFT", 14, -14)
  sharingTitle:SetWidth(DETAILS_WIDTH - 50)
  local sub = ui.text(sharing, "GameFontDisableSmall", "What they get from you. Changes apply right away.")
  sub:SetPoint("TOPLEFT", sharingTitle, "BOTTOMLEFT", 0, -6)
  sub:SetWidth(DETAILS_WIDTH - 28)
  -- Their nickname, here and in the app (Enter saves, empty clears).
  local nickLabel = ui.text(sharing, "GameFontNormalSmall", "Nickname")
  nickLabel:SetPoint("TOPLEFT", sub, "BOTTOMLEFT", 0, -12)
  nickname = ui.inputBox(sharing, DETAILS_WIDTH - 28 - 70, 32, function(text)
    if sharingTag and ns.setFriendNickname then ns.setFriendNickname(sharingTag, text) end
    drawSharing()
  end)
  nickname:SetPoint("LEFT", nickLabel, "LEFT", 66, 0)
  local shareBox = ui.checkbox(sharing, "Share your characters", nil,
    function() local o = sharingNow(); return o ~= nil and o.share end,
    function(on)
      if sharingTag then ns.setContactShare(sharingTag, on) end
      drawSharing()
    end)
  shareBox:SetPoint("TOPLEFT", nickLabel, "BOTTOMLEFT", -4, -14)
  local receiveBox = ui.checkbox(sharing, "Keep what they share with you", nil,
    function() local o = sharingNow(); return o ~= nil and o.receive end,
    function(on)
      if sharingTag then ns.setContactReceive(sharingTag, on) end
      drawSharing()
    end)
  receiveBox:SetPoint("TOPLEFT", shareBox, "BOTTOMLEFT", 0, -2)
  local always = ui.text(sharing, "GameFontNormalSmall", "Name, level, class and item level always, and:")
  always:SetPoint("TOPLEFT", receiveBox, "BOTTOMLEFT", 4, -12)
  local last = always
  for i = 1, #(ns.SHARE_CATEGORIES or {}) + #(ns.SHARE_RULES or {}) do
    local b = ui.button(sharing, "", DETAILS_WIDTH - 28, function(self)
      if sharingTag and self.key then ns.cycleContactCategory(sharingTag, self.key) end
      drawSharing()
    end)
    b:SetPoint("TOPLEFT", last, "BOTTOMLEFT", 0, i == 1 and -8 or -4)
    categoryButtons[i] = b
    last = b
  end
  local cycleHint = ui.text(sharing, "GameFontDisableSmall", "Click one to switch between like all friends, on and off.")
  cycleHint:SetPoint("TOPLEFT", last, "BOTTOMLEFT", 0, -6)
  cycleHint:SetWidth(DETAILS_WIDTH - 28)
  sharingSummary = ui.text(sharing, "GameFontHighlightSmall")
  sharingSummary:SetPoint("TOPLEFT", cycleHint, "BOTTOMLEFT", 0, -12)
  sharingSummary:SetWidth(DETAILS_WIDTH - 28)

  -- Asking them for more of theirs: pick what, then Ask. Their box asks them with those ticked.
  local askTitle = ui.text(sharing, "GameFontNormalSmall", "Ask them to share:")
  askTitle:SetPoint("TOPLEFT", sharingSummary, "BOTTOMLEFT", 0, -16)
  local chip = math.floor((DETAILS_WIDTH - 28 - 12) / 3)
  local categories = ns.SHARE_CATEGORIES or {}
  local function slot(b, i)
    local row, column = math.floor((i - 1) / 3), (i - 1) % 3
    b:SetPoint("TOPLEFT", askTitle, "BOTTOMLEFT", column * (chip + 6), -6 - row * 26)
  end
  for i, key in ipairs(categories) do
    local b = ui.button(sharing, CATEGORY_NAMES[key] or key, chip, function()
      askWants[key] = not askWants[key] or nil
      drawSharing()
    end)
    b.key = key
    slot(b, i)
    askButtons[i] = b
  end
  local askButton = ui.button(sharing, "Ask", chip, function()
    if not (sharingTag and ns.askFriend) then return end
    local list = {}
    for _, key in ipairs(categories) do
      if askWants[key] then list[#list + 1] = key end
    end
    askSaid:SetText(ns.askFriend(sharingTag, list) or "")
    askWants = {}
    drawSharing()
  end, "primary")
  slot(askButton, #categories + 1)
  askSaid = ui.text(sharing, "GameFontDisableSmall")
  askSaid:SetPoint("TOPLEFT", askTitle, "BOTTOMLEFT", 0, -12 - math.ceil((#categories + 1) / 3) * 26)
  askSaid:SetWidth(DETAILS_WIDTH - 28)
end

-- Opens what you share with tag (a contact), or closes it when it already shows them. Returns nil
-- (the Friends tab shows a button's answer; this one has none).
openSharing = function(tag)
  if not (ns.contactSharing and ns.contactSharing(tag)) then return nil end
  if not sharing then buildSharing() end
  if sharing:IsShown() and sharingTag == tag then
    sharing:Hide()
    return nil
  end
  if sharingTag ~= tag then
    askWants = {}
    askSaid:SetText("")
  end
  sharingTag = tag
  if details then details:Hide() end -- one side panel at a time
  placeBeside(sharing)
  drawSharing()
  sharing:Show()
  return nil
end

-- The request box: a friend asks for your characters. It says what Allow gives them, and "Choose
-- what they get" unfolds the categories with three shortcuts, to set them up before allowing.
-- Friends.lua queues the requests and shows one at a time, never in combat. Closing it is Not now.
local REQUEST_BOX = "WoWCharacterTrackerRequestBox"
local REQUEST_WIDTH = 440
local REQUEST_SHORT, REQUEST_TALL = 150, 288
local PRESETS = {
  { "Like all friends" },
  { "Everything", { gear = true, talents = true, location = true, professions = true, highlights = true, leveling = true, gold = true, map = true } },
  { "Basics only", { gear = false, talents = false, location = false, professions = false, highlights = false, leveling = false, gold = false, map = false } },
}
local requestBox

-- "Allow gives them 3 of your characters: name, level, class and item level, plus gear and talents."
-- and, when they sent theirs along, "They sent 2 of theirs, kept when you allow."
local function requestText(box)
  local gifts = (box.gifts or 0) > 0
    and string.format(" They sent %d of theirs, kept when you allow.", box.gifts) or ""
  if box.chars == 0 then return "You don't share any characters right now, so Allow gives them nothing until you do (Friends tab, What you share)." .. gifts end
  local what = ns.shareSummary and ns.shareSummary(box.choices) or ""
  return string.format("Allow gives them %s: name, level, class and item level%s.",
    box.chars == 1 and "1 of your characters" or (box.chars .. " of your characters"),
    what == "only the basics" and "" or (", plus " .. what)) .. gifts
end

local function drawRequest(box)
  box.info:SetText(requestText(box))
  box.toggle:SetText(box.open and "Choose what they get  ^" or "Choose what they get  v")
  box.choicesFrame:SetShown(box.open)
  box:SetHeight(box.open and REQUEST_TALL or REQUEST_SHORT)
  ui.refresh(box.choicesFrame) -- the checkboxes
end

local function answerRequest(box, choice)
  local f = box.request
  box.answered, box.request = true, nil
  box:Hide()
  if f then ns.answerFriendRequest(f, choice, choice == "allow" and box.choices or nil) end
end

local function buildRequestBox()
  local box = CreateFrame("Frame", REQUEST_BOX, UIParent)
  box:SetFrameStrata("DIALOG")
  box:SetClampedToScreen(true)
  box:EnableMouse(true)
  box:SetWidth(REQUEST_WIDTH)
  box:SetPoint("TOP", UIParent, "TOP", 0, -135)
  if UISpecialFrames then table.insert(UISpecialFrames, REQUEST_BOX) end
  ui.box(box, C.window, C.accent)
  local title = ui.heading(box, "Friend request")
  box.line = ui.text(box, "GameFontHighlight")
  box.line:SetPoint("TOPLEFT", title, "BOTTOMLEFT", 0, -8)
  box.line:SetWidth(REQUEST_WIDTH - 32)
  box.info = ui.text(box, "GameFontDisableSmall")
  box.info:SetPoint("TOPLEFT", box.line, "BOTTOMLEFT", 0, -6)
  box.info:SetWidth(REQUEST_WIDTH - 32)
  box.toggle = ui.button(box, "", 170, function()
    box.open = not box.open
    drawRequest(box)
  end)
  box.toggle:SetPoint("TOPLEFT", box.info, "BOTTOMLEFT", 0, -10)

  -- The unfolded part: the shortcuts, then a checkbox per category in two columns.
  local choices = CreateFrame("Frame", nil, box)
  choices:SetPoint("TOPLEFT", box.toggle, "BOTTOMLEFT", 0, -8)
  choices:SetSize(REQUEST_WIDTH - 32, 136)
  box.choicesFrame = choices
  local last
  for i, preset in ipairs(PRESETS) do
    local b = ui.button(choices, preset[1], 120, function()
      box.choices = ns.copy(preset[2] or box.general)
      drawRequest(box)
    end)
    if last then b:SetPoint("LEFT", last, "RIGHT", 6, 0) else b:SetPoint("TOPLEFT", choices, "TOPLEFT", 0, 0) end
    last = b
    if i == 1 then box.firstPreset = b end
  end
  for i, key in ipairs(ns.SHARE_CATEGORIES or {}) do
    local cb = ui.checkbox(choices, CATEGORY_NAMES[key] or key, nil,
      function() return box.choices and box.choices[key] == true end,
      function(on)
        box.choices[key] = on
        drawRequest(box)
      end)
    local row, column = math.floor((i - 1) / 2), (i - 1) % 2
    cb:SetPoint("TOPLEFT", box.firstPreset, "BOTTOMLEFT", column * 200 - 2, -8 - row * 26)
  end

  local allow = ui.button(box, "Allow", 90, function() answerRequest(box, "allow") end, "primary")
  local notNow = ui.button(box, "Not now", 90, function() answerRequest(box, "notnow") end)
  local never = ui.button(box, "Never", 90, function() answerRequest(box, "never") end)
  never:SetPoint("BOTTOMRIGHT", box, "BOTTOMRIGHT", -14, 14)
  notNow:SetPoint("RIGHT", never, "LEFT", -6, 0)
  allow:SetPoint("RIGHT", notNow, "LEFT", -6, 0)
  box.never = never

  -- Closed with Esc: Not now, a moment later so the next request doesn't open while this one closes.
  box:SetScript("OnHide", function(self)
    local f = self.request
    if self.answered or not f then return end
    self.request = nil
    C_Timer.After(0, function() ns.answerFriendRequest(f, "notnow") end)
  end)
  requestBox = box
end

-- Friends.lua's: shows f's request. Returns the box (a popup that couldn't show returns nil).
function ns.showRequestBox(f)
  if not ns.requestInfo then return nil end
  if not requestBox then buildRequestBox() end
  local info = ns.requestInfo(f)
  local box = requestBox
  box.request, box.answered, box.open = f, false, false
  box.chars, box.general, box.choices, box.gifts = info.chars, info.general, ns.copy(info.choices), info.gifts
  box.line:SetText(info.line or (info.label .. " wants to see your characters."))
  box.never:SetShown(not info.more) -- asking for more of what you share doesn't need a block
  drawRequest(box)
  box:Show()
  return box
end

-- The request was answered somewhere else (the Friends tab's list).
function ns.hideRequestBox()
  if not (requestBox and requestBox:IsShown()) then return end
  requestBox.answered, requestBox.request = true, nil
  requestBox:Hide()
end

-- More of the asker's characters came along while their request shows.
function ns.refreshRequestBox(f)
  local box = requestBox
  if not (box and box:IsShown() and box.request and box.request.tag == f.tag and ns.requestInfo) then return end
  box.gifts = ns.requestInfo(box.request).gifts
  drawRequest(box)
end

-- The ask box: asking a friend to share, with what you send them right along (You share) and what
-- you ask them for (You ask for). Their request box says you share and ticks what you asked for.
local ASK_BOX = "WoWCharacterTrackerAskBox"
local ASK_WIDTH = 420
local askBox

local function buildAskBox()
  local box = CreateFrame("Frame", ASK_BOX, UIParent)
  box:SetFrameStrata("DIALOG")
  box:SetClampedToScreen(true)
  box:EnableMouse(true)
  box:SetWidth(ASK_WIDTH)
  box:SetPoint("TOP", UIParent, "TOP", 0, -135)
  if UISpecialFrames then table.insert(UISpecialFrames, ASK_BOX) end
  ui.box(box, C.window, C.accent)
  local title = ui.heading(box, "Ask to share")
  box.line = ui.text(box, "GameFontHighlight")
  box.line:SetPoint("TOPLEFT", title, "BOTTOMLEFT", 0, -8)
  box.line:SetWidth(ASK_WIDTH - 32)
  local send = ui.checkbox(box, "Send them mine too", nil,
    function() return box.sendMine == true end,
    function(on) box.sendMine = on end)
  send:SetPoint("TOPLEFT", box.line, "BOTTOMLEFT", -4, -8)
  local items = {}
  for _, key in ipairs(ns.SHARE_CATEGORIES or {}) do items[#items + 1] = { key, CATEGORY_NAMES[key] or key } end
  local last = ui.checkTable(box, items, { { "give", "You share" }, { "ask", "You ask for" } }, send,
    function(column, key) return box[column] and box[column][key] == true end,
    function(column, key, on) box[column][key] = on end)
  local note = ui.text(box, "GameFontDisableSmall",
    "Name, level, class and item level always go. You share only goes while Send them mine too is on.")
  note:SetPoint("TOPLEFT", last, "BOTTOMLEFT", 0, -16)
  note:SetWidth(ASK_WIDTH - 32)
  local cancel = ui.button(box, "Cancel", 90, function() box:Hide() end)
  cancel:SetPoint("BOTTOMRIGHT", box, "BOTTOMRIGHT", -14, 14)
  local go = ui.button(box, "Ask", 90, function()
    local wants = {}
    for _, key in ipairs(ns.SHARE_CATEGORIES or {}) do
      if box.ask[key] then wants[#wants + 1] = key end
    end
    local said = ns.askFriend(box.tag, wants, box.sendMine and box.give or nil)
    box:Hide()
    if box.onSaid then box.onSaid(said) end
  end, "primary")
  go:SetPoint("RIGHT", cancel, "LEFT", -6, 0)
  box:SetHeight(440)
  askBox = box
end

-- Opens the ask box for tag. onSaid(message) gets what asking said.
function ns.openAskBox(tag, onSaid)
  if not (ns.askInfo and ns.askFriend) then return nil end
  if not askBox then buildAskBox() end
  local info = ns.askInfo(tag)
  askBox.tag, askBox.onSaid, askBox.sendMine = tag, onSaid, true
  askBox.give, askBox.ask = ns.copy(info.give), ns.copy(info.give) -- a swap of the same things to start with
  askBox.line:SetText("Ask " .. info.label .. " to share their characters with you.")
  ui.refresh(askBox)
  askBox:Show()
  return nil
end

-- "Night Elf · Darkshore" under a shared character's name.
local function charInfo(c)
  local parts = {}
  if c.race then parts[#parts + 1] = c.race end
  if c.zone then parts[#parts + 1] = c.zone end
  return table.concat(parts, "  ·  ")
end

-- The character an online friend plays: what they share of it, else what Battle.net tells.
local function onlineChar(f, chars)
  for _, c in ipairs(chars) do
    if f.guid and c.guid == f.guid then return c end
  end
  return { guid = f.guid, char = f.character, classFile = f.classFile, level = f.level, zone = f.zone, notShared = true }
end

local function lastSeenAt(p)
  local c = p.chars[1]
  return c and (c.received or c.at) or 0
end

-- One row per friend: contacts, whoever shares with you, and (after Find online friends) everyone
-- online here. { tag, label, online (their first game account's row of ns.onlineFriends, or nil),
-- contact, blocked, hasAddon, invitedAt, sharesWithYou, chars (shared, newest first), current (the
-- one being played, else the one seen last, else nil) }. In the order you dragged them into, the
-- others after it: online first by label, then by when they were last seen.
local function friendRows(online, shared)
  local s = ns.shareSettings and ns.shareSettings() or { contacts = {}, blocked = {} }
  local byTag, list = {}, {}
  local function row(tag, label)
    local p = byTag[tag]
    if not p then
      p = { tag = tag, label = label, chars = {}, contact = s.contacts[tag], blocked = s.blocked[tag] == true }
      byTag[tag], list[#list + 1] = p, p
    end
    return p
  end
  for _, f in ipairs(online) do
    if searched or f.contact or s.contacts[f.tag] or f.sharesWithYou then
      local p = row(f.tag, f.label)
      if not p.online then
        p.online, p.hasAddon, p.invitedAt = f, f.hasAddon, f.invitedAt
        p.contact, p.blocked = f.contact or p.contact, f.blocked or p.blocked
        p.sharesWithYou = f.sharesWithYou
      end
    end
  end
  for _, friend in ipairs(shared) do
    local p = row(friend.tag, friend.label)
    p.chars = friend.chars or {}
    p.sharesWithYou = p.sharesWithYou or #p.chars > 0
  end
  for tag in pairs(s.contacts) do
    if not s.blocked[tag] then row(tag, ns.friendLabel and ns.friendLabel(tag) or tag) end
  end
  for _, p in ipairs(list) do
    if p.online then p.current = onlineChar(p.online, p.chars) else p.current = p.chars[1] end
  end
  -- The order you dragged them into (ns.friendOrder) first, then the rest as before.
  local rank = {}
  for i, tag in ipairs(ns.friendOrder and ns.friendOrder() or {}) do rank[tag] = rank[tag] or i end
  table.sort(list, function(x, y)
    local rx, ry = rank[x.tag], rank[y.tag]
    if rx or ry then
      if rx and ry then return rx < ry end
      return rx ~= nil
    end
    if (x.online ~= nil) ~= (y.online ~= nil) then return x.online ~= nil end
    if not x.online and lastSeenAt(x) ~= lastSeenAt(y) then return lastSeenAt(x) > lastSeenAt(y) end
    return (x.label or x.tag):lower() < (y.label or y.tag):lower()
  end)
  return list
end

-- "Level 54 Arms Warrior · Felwood · Zebbe (Zeb#2222) · online, updated 2m ago" under a friend's
-- character (updated: when what they share of it last arrived), "... · last seen 2h ago" offline.
local function friendInfo(p)
  local parts, c = {}, p.current
  if c then
    local line = headline(c)
    if line ~= "" then parts[#parts + 1] = line end
    if c.zone and c.zone ~= "" then parts[#parts + 1] = c.zone end
  end
  if c then parts[#parts + 1] = who(p.label, p.tag) end
  if p.online then
    parts[#parts + 1] = (c and c.received) and ("online, updated " .. ago(c.received)) or "online"
  elseif lastSeenAt(p) > 0 then
    parts[#parts + 1] = "last seen " .. ago(lastSeenAt(p))
  else
    parts[#parts + 1] = "offline"
  end
  return table.concat(parts, "  ·  ")
end

local function showHover(self) self.hover:Show() end
local function hideHover(self) self.hover:Hide() end

-- A card: a dark box with a class-colored stripe, a name line and an info line. Clicking it opens
-- the details.
local function newCard(p, width)
  local card = CreateFrame("Frame", nil, p)
  card:SetSize(width, CARD)
  card:EnableMouse(true)
  ui.box(card, C.card, C.border)
  card.hover = card:CreateTexture(nil, "ARTWORK")
  ui.fill(card.hover, C.hover)
  card.hover:SetAllPoints()
  card.hover:Hide()
  card.stripe = card:CreateTexture(nil, "ARTWORK")
  card.stripe:SetSize(3, CARD - 14)
  card.stripe:SetPoint("LEFT", card, "LEFT", 7, 0)
  card.nameText = ui.text(card, "GameFontHighlight")
  card.nameText:SetPoint("TOPLEFT", card, "TOPLEFT", 18, -6)
  card.infoText = ui.text(card, "GameFontDisableSmall")
  card.infoText:SetPoint("TOPLEFT", card, "TOPLEFT", 18, -23)
  card.infoText:SetWidth(width - 200)
  card:SetScript("OnEnter", showHover)
  card:SetScript("OnLeave", hideHover)
  card:SetScript("OnMouseUp", toggleDetails)
  return card
end

local function setCard(card, name, classFile, info)
  card.nameText:SetText(name)
  classColor(card.nameText, classFile)
  card.stripe:SetColorTexture(classRgb(classFile))
  card.infoText:SetText(info)
end

-- A small box with a number in it (level, item level), like the app's row pills.
local function newPill(parent, width)
  local pill = CreateFrame("Frame", nil, parent)
  pill:SetSize(width, 20)
  ui.box(pill, C.bar, C.border)
  pill.text = ui.text(pill, "GameFontHighlightSmall")
  pill.text:SetPoint("CENTER", pill, "CENTER", 0, 0)
  return pill
end

-- A shared character's row, like the app's: name and race/zone, then class and spec, level and
-- item level in pills, and when it was last seen. Clicking it opens the details.
local function newCharRow(p, width)
  local row = newCard(p, width)
  row.infoText:SetWidth(width - 380)
  row.classText = ui.text(row, "GameFontHighlightSmall")
  row.classText:SetPoint("LEFT", row, "RIGHT", -360, 0)
  row.classText:SetWidth(150)
  row.classText:SetWordWrap(false)
  row.level = newPill(row, 36)
  row.level:SetPoint("LEFT", row, "RIGHT", -200, 0)
  row.ilvl = newPill(row, 36)
  row.ilvl:SetPoint("LEFT", row, "RIGHT", -158, 0)
  row.when = ui.text(row, "GameFontDisableSmall")
  row.when:SetPoint("RIGHT", row, "RIGHT", -12, 0)
  row.when:SetJustifyH("RIGHT")
  return row
end

local function setCharRow(row, c)
  setCard(row, fullName(c), c.classFile, charInfo(c))
  local class = c.class or ""
  row.classText:SetText(c.spec and (c.spec .. " " .. class) or class)
  classColor(row.classText, c.classFile)
  row.level.text:SetText(c.level and tostring(c.level) or "-")
  row.ilvl.text:SetText(c.ilvl and tostring(math.floor(c.ilvl)) or "-")
  row.when:SetText(ago(c.received or c.at))
end

-- The Friends tab: a grid of friends (as many columns as the window's width fits) in the order you drag them into (shared with
-- the app through ns.setFriendOrder), each showing the character they play or played last. A click
-- opens the friend's section under their row: nickname, state and buttons, and their characters.
local GRID_MIN_CARD, GRID_GAP, GRID_CARD = 210, 8, 64
local SECTION_TOP = 74 -- the friend section's header, above their characters

local function buildFriends(p)
  -- The room the window gives now (it can be resized): the grid's columns and card width follow it.
  local width, columns, cardWidth
  local function measure()
    width = pageWidth() - 30
    columns = math.max(1, math.floor((width + GRID_GAP) / (GRID_MIN_CARD + GRID_GAP)))
    cardWidth = math.floor((width - (columns - 1) * GRID_GAP) / columns)
  end
  measure()
  local friendsTitle = ui.heading(p, "Friends")
  local find = ui.button(p, "Find online friends", 150, function()
    searched = true
    if ns.findOnlineFriends then ns.findOnlineFriends() end
    friendsSaid = ""
    drawFriends()
  end, "primary")
  local hint = ui.text(p, "GameFontDisableSmall", "Click a friend for their characters and nickname. Drag a friend to put them where you want them, the app shows the same order. Find online friends checks who has the addon, it sends them nothing they can see.")
  hint:SetWidth(width - 170)
  local status = ui.text(p, "GameFontHighlightSmall")
  status:SetWidth(ui.WRAP)

  local function note(s)
    local fs = ui.text(p, "GameFontDisableSmall", s)
    fs:SetWidth(ui.WRAP)
    return fs
  end
  local requestsTitle = ui.heading(p, "Requests")
  -- What all friends get, after the list (it moves down as it grows).
  local yours = CreateFrame("Frame", nil, p)
  yours:SetSize(width + 16, SHARE_DEFAULTS_HEIGHT)
  buildShareDefaults(yours)
  ui.onRefresh(p, function() ui.refresh(yours) end)
  local none = note("No friends here yet. Battle.net friends online in this game show up here, and so does everyone you share with.")
  local nothingShared = note("Nothing shared with you yet.")
  local requestRows, friendCards, charCards = {}, {}, {}

  -- Runs fn(tag) and shows what it said (ask/share return a line, invite ok and a line).
  local function act(fn, tag)
    return function()
      local a, b = fn(tag)
      friendsSaid = type(a) == "string" and a or (type(b) == "string" and b or friendsSaid)
      drawFriends()
    end
  end

  local function newRequestRow()
    local r = CreateFrame("Frame", nil, p)
    r:SetSize(width, REQUEST)
    ui.box(r, C.request, C.requestBorder)
    r.line = ui.text(r, "GameFontHighlight")
    r.line:SetPoint("LEFT", r, "LEFT", 12, 0)
    r.line:SetWidth(width - 250)
    r.buttons = { cardButton(r, 64), cardButton(r, 70), cardButton(r, 60) }
    return r
  end

  -- Grid placement: the top of the grid and the friend section's height after the open card's row.
  local gridTop, sectionRow, sectionHeight = 0, nil, 0
  local function slot(i)
    local row, col = math.floor((i - 1) / columns), (i - 1) % columns
    local y = gridTop + row * (GRID_CARD + GRID_GAP)
    if sectionRow and row > sectionRow then y = y + sectionHeight end
    return 16 + col * (cardWidth + GRID_GAP), y
  end

  -- Dragging: the card follows the cursor and the others move aside live; letting go keeps the order.
  local dragging -- { card, order = { tags } }
  local cardByTag = {}

  local function placeDragged()
    for i, tag in ipairs(dragging.order) do
      local card = cardByTag[tag]
      if card and card ~= dragging.card then place(card, p, slot(i)) end
    end
  end

  -- The slot under the cursor, 1 to n.
  local function slotUnderCursor(n)
    local scale = (p.GetEffectiveScale and p:GetEffectiveScale()) or 1
    local cx, cy = GetCursorPosition()
    local x = (cx or 0) / scale - (p:GetLeft() or 0) - 16
    local y = (p:GetTop() or 0) - (cy or 0) / scale - gridTop
    local col = math.max(0, math.min(columns - 1, math.floor(x / (cardWidth + GRID_GAP))))
    local row = math.max(0, math.floor(y / (GRID_CARD + GRID_GAP)))
    return math.max(1, math.min(n, row * columns + col + 1))
  end

  local function dragUpdate(card)
    if not dragging then return end
    local order = dragging.order
    local to = slotUnderCursor(#order)
    for i, tag in ipairs(order) do
      if tag == card.tag and i ~= to then
        table.remove(order, i)
        table.insert(order, to, tag)
        placeDragged()
        return
      end
    end
  end

  local function newFriendCard()
    local card = CreateFrame("Frame", nil, p)
    card:SetSize(cardWidth, GRID_CARD)
    card:EnableMouse(true)
    card:SetMovable(true)
    card:RegisterForDrag("LeftButton")
    ui.box(card, C.card, C.border)
    card.hover = card:CreateTexture(nil, "ARTWORK")
    ui.fill(card.hover, C.hover)
    card.hover:SetAllPoints()
    card.hover:Hide()
    card.stripe = card:CreateTexture(nil, "ARTWORK")
    card.stripe:SetSize(3, GRID_CARD - 14)
    card.stripe:SetPoint("LEFT", card, "LEFT", 7, 0)
    card.dot = card:CreateTexture(nil, "ARTWORK")
    card.dot:SetSize(7, 7)
    card.dot:SetPoint("TOPRIGHT", card, "TOPRIGHT", -9, -10)
    card.nameText = ui.text(card, "GameFontHighlight")
    card.nameText:SetPoint("TOPLEFT", card, "TOPLEFT", 16, -8)
    card.nameText:SetWidth(cardWidth - 40)
    card.nameText:SetWordWrap(false)
    card.whoText = ui.text(card, "GameFontNormalSmall")
    card.whoText:SetPoint("TOPLEFT", card, "TOPLEFT", 16, -26)
    card.whoText:SetWidth(cardWidth - 24)
    card.whoText:SetWordWrap(false)
    card.infoText = ui.text(card, "GameFontDisableSmall")
    card.infoText:SetPoint("TOPLEFT", card, "TOPLEFT", 16, -43)
    card.infoText:SetWidth(cardWidth - 24)
    card.infoText:SetWordWrap(false)
    card:SetScript("OnEnter", showHover)
    card:SetScript("OnLeave", hideHover)
    card:SetScript("OnMouseUp", function(self)
      if self.dragged then
        self.dragged = nil
        return
      end
      if not self.tag then return end
      openFriendTag = openFriendTag ~= self.tag and self.tag or nil
      drawFriends()
    end)
    card:SetScript("OnDragStart", function(self)
      if not self.tag then return end
      openFriendTag = nil
      drawFriends()
      dragging = { card = self, order = ns.copy(self.order or {}) }
      self.dragged = true
      self:SetFrameLevel((p:GetFrameLevel() or 1) + 20)
      self:StartMoving()
      self:SetScript("OnUpdate", dragUpdate)
    end)
    card:SetScript("OnDragStop", function(self)
      self:StopMovingOrSizing()
      if self.SetUserPlaced then self:SetUserPlaced(false) end
      self:SetScript("OnUpdate", nil)
      self:SetFrameLevel((p:GetFrameLevel() or 1) + 1)
      if dragging and ns.setFriendOrder then ns.setFriendOrder(dragging.order) end
      dragging = nil
      drawFriends()
    end)
    return card
  end

  -- The open friend's section: who, their state and buttons, the nickname box, then their characters.
  local section = CreateFrame("Frame", nil, p)
  section:SetWidth(width)
  ui.box(section, C.bar, C.accent)
  section.title = ui.text(section, "GameFontNormal")
  section.title:SetPoint("TOPLEFT", section, "TOPLEFT", 12, -10)
  section.pill = CreateFrame("Frame", nil, section)
  section.pill:SetHeight(16)
  section.pill:SetPoint("LEFT", section.title, "RIGHT", 8, 0)
  section.stateText = ui.text(section.pill, "GameFontHighlightSmall")
  section.stateText:SetPoint("CENTER", section.pill, "CENTER", 0, 0)
  section.b1, section.b2 = cardButton(section, 90), cardButton(section, 90)
  section.b1:SetPoint("TOPRIGHT", section, "TOPRIGHT", -106, -8)
  section.b2:SetPoint("TOPRIGHT", section, "TOPRIGHT", -10, -8)
  local nickLabel = ui.text(section, "GameFontNormalSmall", "Nickname")
  nickLabel:SetPoint("TOPLEFT", section, "TOPLEFT", 12, -42)
  section.nickname = ui.inputBox(section, 220, 32, function(text)
    if section.tag and ns.setFriendNickname then ns.setFriendNickname(section.tag, text) end
    drawFriends()
  end)
  section.nickname:SetPoint("LEFT", nickLabel, "LEFT", 66, 0)
  local nickHint = ui.text(section, "GameFontDisableSmall", "Enter saves, here and in the app. Empty clears it.")
  nickHint:SetPoint("LEFT", section.nickname, "RIGHT", 10, 0)

  local function newCharCard() return newCharRow(p, width - 24) end
  local expansion -- the open row's info (newExpansion), built on the first click

  local CHOICES = { { "Allow", "allow", "primary" }, { "Not now", "notnow" }, { "Never", "never" } }

  drawFriends = function()
    if dragging then return end -- the drag places the cards; letting go redraws
    measure()
    section:SetWidth(width)
    local list = friendRows(rowsOf(ns.onlineFriends), rowsOf(ns.sharedWithYou))
    if revealKey then
      for _, person in ipairs(list) do
        for _, c in ipairs(person.chars) do
          if charKey(c) == revealKey then openFriendTag = person.tag end
        end
      end
      revealKey = nil
    end
    local open -- the open panel's character in the new data, and its friend
    local openFriend
    -- The open row's info goes under it, once.
    local unfolded = false
    local function unfold(c, friend, y)
      if unfolded or expandedKey ~= charKey(c) then return y end
      unfolded = true
      expansion = expansion or newExpansion(p, width - 24)
      local h = drawExpansion(expansion, c, friend)
      place(expansion, p, 28, y - 2)
      return y - 2 + h + 8
    end

    local y = 14
    local requests = rowsOf(ns.pendingRequests)
    if #requests > 0 then
      place(requestsTitle, p, 16, y)
      y = y + 22
    else
      requestsTitle:Hide()
    end
    for i, req in ipairs(requests) do
      local r = pooled(requestRows, i, newRequestRow)
      r:SetWidth(width)
      r.line:SetText(who(req.label, req.tag) .. " " .. (req.asks or "wants to see your characters"))
      for j, choice in ipairs(CHOICES) do
        if req.more and choice[2] == "never" then
          r.buttons[j]:Hide()
        else
          setButton(r.buttons[j], choice[1], ns.answerFriendRequest and function()
            ns.answerFriendRequest(req, choice[2])
            drawFriends()
          end, choice[3])
        end
      end
      alignButtons(r, r.buttons)
      place(r, p, 16, y)
      y = y + REQUEST + 6
    end
    hideFrom(requestRows, #requests + 1)
    if #requests > 0 then y = y + 10 end

    place(friendsTitle, p, 16, y)
    place(find, p, 16 + width - 150, y - 4)
    hint:SetWidth(width - 170)
    y = y + 20
    place(hint, p, 16, y)
    y = y + textHeight(hint, 24) + 6
    if friendsSaid ~= "" then
      status:SetText(friendsSaid)
      place(status, p, 16, y)
      y = y + textHeight(status, 14) + 4
    else
      status:Hide()
    end
    none:Hide()
    nothingShared:Hide()
    if #list == 0 then
      place(none, p, 16, y)
      y = y + 20
    end

    -- The friend section goes under the open card's row: worked out first, so the rows below move down.
    local order, openIndex, openPerson = {}, nil, nil
    for i, person in ipairs(list) do
      order[i] = person.tag
      if person.tag == openFriendTag then openIndex, openPerson = i, person end
    end
    if openFriendTag and not openPerson then openFriendTag = nil end
    gridTop, sectionRow, sectionHeight = y, nil, 0
    local rows = math.ceil(#list / columns)

    -- The section's content, placed from sectionY; returns its height.
    local cards, hiddenCount = 0, 0
    local function drawSection(sectionY)
      local person, c = openPerson, openPerson.current
      local label = who(person.label, person.tag)
      section.tag = person.tag
      section.title:SetText(label)
      local state, look, first, second = friendState(person)
      section.stateText:SetText(state)
      section.pill:SetWidth((section.stateText:GetStringWidth() or 60) + 14)
      ui.box(section.pill, C.bar, look == "good" and C.good or (look == "bad" and C.bad or C.border))
      for _, pair in ipairs({ { section.b1, first }, { section.b2, second } }) do
        local b, what = pair[1], pair[2]
        if what then setButton(b, what[1], what[2] and act(what[2], person.tag), what[3]) else b:Hide() end
      end
      if not section.nickname:HasFocus() then
        section.nickname.saved = ns.friendNickname and ns.friendNickname(person.tag) or ""
        section.nickname:SetText(section.nickname.saved)
      end
      place(section, p, 16, sectionY)
      local yy = sectionY + SECTION_TOP
      -- The one being played first when it isn't shared, then what they share, newest first.
      local chars = {}
      if c and c.notShared then chars[1] = c end
      for _, shared in ipairs(person.chars) do chars[#chars + 1] = shared end
      if #chars == 0 then
        place(nothingShared, p, 28, yy)
        yy = yy + 22
      end
      for _, ch in ipairs(chars) do
        local hidden = isHidden(ch)
        if hidden then hiddenCount = hiddenCount + 1 end
        if not hidden or showHidden then
          cards = cards + 1
          local row = pooled(charCards, cards, newCharCard)
          row.char, row.friend = ch, label
          if charKey(ch) == detailsKey then open, openFriend = ch, label end
          row:SetWidth(width - 24)
          setCharRow(row, ch)
          if ch.notShared then row.when:SetText("not shared") end
          row:SetAlpha(hidden and 0.5 or 1)
          row:SetFrameLevel((section:GetFrameLevel() or 1) + 2)
          place(row, p, 28, yy)
          yy = yy + CARD + 4
          yy = unfold(ch, label, yy)
        end
      end
      local h = yy - sectionY + 6
      section:SetHeight(h)
      return h
    end

    if openPerson then
      sectionRow = math.floor((openIndex - 1) / columns)
      local sectionY = gridTop + (sectionRow + 1) * (GRID_CARD + GRID_GAP)
      sectionHeight = drawSection(sectionY) + GRID_GAP
    else
      section:Hide()
    end

    for k in pairs(cardByTag) do cardByTag[k] = nil end
    for i, person in ipairs(list) do
      local card = pooled(friendCards, i, newFriendCard)
      local c, label = person.current, who(person.label, person.tag)
      card.tag, card.order = person.tag, order
      card:SetWidth(cardWidth)
      for _, fs in ipairs({ card.whoText, card.infoText }) do fs:SetWidth(cardWidth - 24) end
      card.nameText:SetWidth(cardWidth - 40)
      cardByTag[person.tag] = card
      card.nameText:SetText(c and fullName(c) or label)
      classColor(card.nameText, c and c.classFile)
      card.stripe:SetColorTexture(classRgb(c and c.classFile))
      card.whoText:SetText(c and label or "")
      card.infoText:SetText(friendInfo(person))
      if person.online then card.dot:SetColorTexture(0.3, 0.85, 0.4) else card.dot:SetColorTexture(0.45, 0.45, 0.45) end
      ui.box(card, C.card, person.tag == openFriendTag and C.accent or C.border)
      card:SetAlpha(person.online and 1 or 0.8)
      place(card, p, slot(i))
    end
    hideFrom(friendCards, #list + 1)
    hideFrom(charCards, cards + 1)
    y = gridTop + rows * (GRID_CARD + GRID_GAP) + sectionHeight
    if not unfolded and expansion then expansion:Hide() end
    if details and details:IsShown() then
      if open then drawDetails(open, openFriend) else details:Hide() end
    end
    if sharing and sharing:IsShown() then drawSharing() end

    local link = hiddenLink(p, hiddenCount)
    if link then
      place(link, p, 16, y)
      y = y + 30
    end

    place(yours, p, 0, y + 8)
    y = y + 8 + SHARE_DEFAULTS_HEIGHT
    p:SetHeight(y + 10)
  end
  ui.onRefresh(p, drawFriends)
end

-- Characters ------------------------------------------------------------------------------------
-- Your own characters on this WoW account (the saved file's snapshots, gear and level times), in the
-- same rows as friends' and opening the same details panel. The app shows every account's.
local function ownChars()
  local db = WoWCharacterTrackerDB or {}
  local out = {}
  for guid, snap in pairs(db.chars or {}) do
    if not ns.isIgnored(guid) then
      local c = ns.copy(snap)
      local items = db.items and db.items[guid]
      c.guid, c.at = guid, snap.lastSeen
      c.gear, c.enchants = items and items.gear, items and items.enchants
      c.levelList = ns.levelTimes(db.events, guid)
      out[#out + 1] = c
    end
  end
  table.sort(out, function(a, b) return (a.at or 0) > (b.at or 0) end)
  return out
end

local function buildCharacters(p)
  local width = PAGE_WIDTH - 30
  local title = ui.heading(p, "Your characters")
  local hint = ui.hint(p, "The characters you've played on this WoW account, newest first. Click one for its details, gear and level times. The app shows all your accounts.", title)
  hint:SetWidth(width)
  local none = ui.text(p, "GameFontDisableSmall", "None saved yet. Characters show up here once you've played them with the addon.")
  local rows = {}
  local expansion -- the open row's info, built on the first click
  local function draw()
    local list = ownChars()
    local y = 56
    none:SetShown(#list == 0)
    if #list == 0 then place(none, p, 16, y) end
    local open, unfolded
    local shownCount, hiddenCount = 0, 0
    for _, c in ipairs(list) do
      local hidden = isHidden(c)
      if hidden then hiddenCount = hiddenCount + 1 end
      if not hidden or showHidden then
        shownCount = shownCount + 1
        local row = pooled(rows, shownCount, function() return newCharRow(p, width) end)
        row.char, row.friend = c, nil
        if charKey(c) == detailsKey then open = c end
        setCharRow(row, c)
        row:SetAlpha(hidden and 0.5 or 1)
        place(row, p, 16, y)
        y = y + CARD + 4
        if expandedKey == charKey(c) then
          unfolded = true
          expansion = expansion or newExpansion(p, width - 12)
          place(expansion, p, 28, y - 2)
          y = y - 2 + drawExpansion(expansion, c) + 8
        end
      end
    end
    if expansion and not unfolded then expansion:Hide() end
    hideFrom(rows, shownCount + 1)
    local link = hiddenLink(p, hiddenCount)
    if link then
      place(link, p, 16, y + 4)
      y = y + 34
    end
    if open and details and details:IsShown() then drawDetails(open) end
    p:SetHeight(y + 10)
  end
  ui.onRefresh(p, draw)
end

-- Friends.lua calls this when a friend answers, asks, or sends something: redraws the tab if it's up.
function ns.friendsChanged()
  local t = tabs.friends
  if drawFriends and ns.windowShown() and t and t.holder and t.holder:IsShown() then drawFriends() end
end

-- What you share (under the Friends tab's lists) ---------------------------------------------
buildShareDefaults = function(p)
  local friendsTitle = ui.heading(p, "What you share with friends")
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
  local gridLast = ui.checkGrid(p, { { "gear", "Gear" }, { "talents", "Talents" }, { "location", "Location" }, { "professions", "Professions" },
    { "highlights", "Highlights" }, { "leveling", "Level times" }, { "gold", "Gold" }, { "map", "Map position" } }, whatTitle,
    function(key) return ns.shareSettings().defaults[key] == true end,
    function(key, on) ns.editShare(function(s) s.defaults[key] = on end) end)
  local otherFaction = ui.checkbox(p, "Show my map position to the other faction",
    "Off: a friend playing the other faction doesn't see you on the map, even with Map position on.",
    function() return ns.shareSettings().defaults.otherFaction == true end,
    function(on) ns.editShare(function(s) s.defaults.otherFaction = on end) end)
  otherFaction:SetPoint("TOPLEFT", gridLast, "BOTTOMLEFT", 0, -10)
  -- One rate for all friends: click to go from live to every minute to every 5 minutes.
  local RATE_TEXT = { [5] = "Map updates: live", [60] = "Map updates: every minute", [300] = "Map updates: every 5 minutes" }
  local NEXT_RATE = { [5] = 60, [60] = 300, [300] = 5 }
  local rate = ui.button(p, RATE_TEXT[5], 240, function(self)
    ns.editShare(function(s) s.mapRate = NEXT_RATE[s.mapRate] or 5 end)
    self:SetText(RATE_TEXT[ns.shareSettings().mapRate])
  end)
  rate:SetPoint("TOPLEFT", otherFaction, "BOTTOMLEFT", 4, -24)
  ui.onRefresh(p, function() rate:SetText(RATE_TEXT[ns.shareSettings().mapRate]) end)
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
    return "No app found. Discord posts, screenshots, the stream overlay and characters from other WoW accounts need the free Character Tracker app, from the link below (click it, then Ctrl+C). Guild and group lines, this window, friends and notes work without it."
  end
  local post = ns.setting("post") or {}
  local postKinds = {}
  for k, v in pairs(post) do postKinds[k] = (type(v) == "table" and v.on) or v == true end
  return table.concat({
    "Posted to Discord: " .. picked(postKinds, KINDS),
    "Screenshots: " .. picked(ns.setting("screenshot"), KINDS),
    " ",
    "Change these on the app's Sharing page. They reach the game on your next /reload.",
    "What you change in this window reaches the app on your next /reload or logout. The newest change wins.",
  }, "\n")
end

-- The Settings tab: how the addon runs, then the rarely touched options, this character and what
-- the app set.
local function buildSettings(p)
  local live = ui.checkbox(p, "Instant posting", "Tell the app about events right away (a one-frame pattern and screenshot). Off: they arrive on /reload or logout.",
    function() return WoWCharacterTrackerDB.live ~= false end,
    function(on) WoWCharacterTrackerDB.live = on end)
  live:SetPoint("TOPLEFT", 14, -12)

  local reloadTitle = ui.heading(p, "Reload reminders", live, -40)
  local reloadHint = ui.hint(p, "Gear, bags and bank reach the app on /reload or logout. Gentle: a chat line and a box on flight paths when they changed. Every chance: also a popup after each ding.", reloadTitle)
  local reload = ui.choice(p, RELOAD_CHOICES, reloadChoice, setReloadChoice)
  reload:SetPoint("TOPLEFT", reloadHint, "BOTTOMLEFT", 0, -10)
  local custom = ui.text(p, "GameFontDisableSmall", "Custom: your own mix (/wct popup turns the ding popup on or off).")
  custom:SetPoint("LEFT", reload, "RIGHT", 6, 0)
  custom:SetWidth(200)
  ui.onRefresh(p, function() custom:SetShown(reloadChoice() == "custom") end)

  -- Minimap.lua's, loaded after this file.
  local minimap = ui.checkbox(p, "Show the minimap button", "Hidden, /wct still opens this window.",
    function() return not ns.minimapButtonShown or ns.minimapButtonShown() end,
    function(on) if ns.setMinimapButtonShown then ns.setMinimapButtonShown(on) end end)
  minimap:SetPoint("TOPLEFT", reload, "BOTTOMLEFT", -2, -22)

  -- The window's colors, the app's Forever looks. Changes right away.
  local lookTitle = ui.heading(p, "Look", minimap, -40)
  local look = ui.choice(p, ui.THEMES, ui.theme, function(key)
    ui.applyTheme(key)
    ui.refresh(tabs[current].page) -- what each page colors as it draws (charts, tabs)
  end, 120)
  look:SetPoint("TOPLEFT", lookTitle, "BOTTOMLEFT", 0, -10)

  local advancedTitle = ui.heading(p, "Advanced", look, -30)
  local cornerButton
  local function showCorner() cornerButton:SetText("Pattern corner: " .. CORNER_NAMES[currentCorner()]) end
  cornerButton = ui.button(p, "", 200, function()
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
  cornerButton:SetPoint("TOPLEFT", advancedTitle, "BOTTOMLEFT", 2, -8)
  ui.onRefresh(p, showCorner)
  local gridLabel = ui.checkbox(p, "Show a small label next to the pattern", nil,
    function() return isOn("gridLabel") end,
    function(on) ns.editSettings(function(s) s.gridLabel = on end) end)
  gridLabel:SetPoint("TOPLEFT", cornerButton, "BOTTOMLEFT", -4, -6)
  local keepAll = ui.checkbox(p, "Keep all history", "On: every loot, duel and battleground stays in the saved file, which keeps growing. Applies on your next /reload.",
    function() return ns.setting("keepAllHistory") == true end,
    function(on) ns.editSettings(function(s) s.keepAllHistory = on end) end)
  keepAll:SetPoint("TOPLEFT", gridLabel, "BOTTOMLEFT", 0, -2)

  local thisTitle = ui.heading(p, "This character", keepAll, -40)
  local ignore = ui.checkbox(p, "Ignore this character",
    "Not tracked: nothing is recorded, posted, announced or shared with friends for it. In the app it moves to Ignored, under the character list.",
    function() return ns.isIgnored(UnitGUID("player")) end,
    function(on)
      local me = UnitGUID("player")
      if not me then return end
      ns.editSettings(function(s)
        s.ignored = s.ignored or {}
        s.ignored[me] = on or nil
      end)
    end)
  ignore:SetPoint("TOPLEFT", thisTitle, "BOTTOMLEFT", -2, -6)
  local appTitle = ui.heading(p, "Set in the Character Tracker app", ignore, -40)
  local appText = ui.text(p, "GameFontHighlightSmall")
  appText:SetPoint("TOPLEFT", appTitle, "BOTTOMLEFT", 0, -6)
  appText:SetWidth(PAGE_WIDTH - 40)
  appText:SetJustifyV("TOP")
  -- Without the app, where to get it.
  local appLink = ui.copyBox(p, ns.APP_URL, 380)
  appLink:SetPoint("TOPLEFT", appText, "BOTTOMLEFT", 0, -8)
  ui.onRefresh(p, function()
    appText:SetText(appSummary())
    appLink:SetShown(not ns.hasApp())
  end)
end

-- The window ------------------------------------------------------------------------------------
-- height: how tall the tab's content is; taller than the window gets a scroll frame.
local TABS = {
  { key = "home", label = "Home", height = 780, build = function(p) ns.buildHomeTab(p) end }, -- Home.lua
  { key = "characters", label = "Characters", height = 1000, build = buildCharacters }, -- grows with the list
  { key = "sharing", label = "Sharing", height = 700, build = buildSharingTab }, -- without the app: the whole chat table
  { key = "friends", label = "Friends", height = 1000, build = buildFriends }, -- grows with its lists
  { key = "note", label = "Note", height = 330, build = buildNote },
  { key = "settings", label = "Settings", height = 780, build = buildSettings },
}
local BY_KEY = {}
for _, t in ipairs(TABS) do BY_KEY[t.key] = t end
-- Tabs from before 0.39 (a saved last tab, a /wct command) open where their options went.
local OLD_TABS = { general = "settings", app = "settings", chat = "sharing", highlights = "sharing" }

local function buildTab(tab)
  local holder, page
  if tab.height > AREA_HEIGHT then
    holder = CreateFrame("ScrollFrame", nil, area, "UIPanelScrollFrameTemplate")
    holder:SetPoint("TOPLEFT", 0, 0)
    holder:SetPoint("BOTTOMRIGHT", -26, 0)
    page = CreateFrame("Frame", nil, holder)
    page:SetSize(pageWidth(), tab.height)
    holder:SetScrollChild(page)
  else
    page = CreateFrame("Frame", nil, area)
    page:SetAllPoints()
    holder = page
  end
  tab.build(page)
  tabs[tab.key].holder, tabs[tab.key].page = holder, page
end

selectTab = function(key)
  key = OLD_TABS[key] or key
  if not BY_KEY[key] then key = "home" end
  current = key
  saved().tab = key
  for _, t in ipairs(TABS) do
    local state = tabs[t.key]
    if t.key == key then
      if not state.page then buildTab(t) end
      state.holder:Show()
      ui.styleButton(state.button, "selected")
    else
      if state.holder then state.holder:Hide() end
      ui.styleButton(state.button)
    end
  end
  ui.refresh(tabs[key].page)
end

-- While the window is dragged, the side panels switch sides as they run out of room.
local function startDrag(self)
  self:StartMoving()
  self:SetScript("OnUpdate", placePanels)
end

local function savePosition(self)
  self:StopMovingOrSizing()
  self:SetScript("OnUpdate", nil)
  placePanels()
  local point, _, relative, x, y = self:GetPoint()
  local s = saved()
  s.point, s.relative, s.x, s.y = point, relative, x, y
end

-- Resizing: the grip at the bottom right, from the first size up to MAX_*. Scrolled pages take the
-- new width (the Friends grid fits more columns), and the size is kept with the position.
local MAX_WIDTH, MAX_HEIGHT = 1600, 1200

local function fitPages()
  for _, state in pairs(tabs) do
    if state.page and state.holder ~= state.page then state.page:SetWidth(pageWidth()) end
  end
end

local function resizeGrip()
  local grip = CreateFrame("Button", nil, window)
  grip:SetSize(16, 16)
  grip:SetPoint("BOTTOMRIGHT", -3, 3)
  grip:SetNormalTexture("Interface\\ChatFrame\\UI-ChatIM-SizeGrabber-Up")
  grip:SetHighlightTexture("Interface\\ChatFrame\\UI-ChatIM-SizeGrabber-Highlight")
  grip:SetPushedTexture("Interface\\ChatFrame\\UI-ChatIM-SizeGrabber-Down")
  grip:SetScript("OnMouseDown", function() window:StartSizing("BOTTOMRIGHT") end)
  grip:SetScript("OnMouseUp", function()
    window:StopMovingOrSizing()
    local s = saved()
    s.width, s.height = math.floor(window:GetWidth() + 0.5), math.floor(window:GetHeight() + 0.5)
    fitPages()
    placePanels()
    if current and tabs[current] and tabs[current].page then ui.refresh(tabs[current].page) end
  end)
  return grip
end

local function build()
  window = CreateFrame("Frame", NAME, UIParent)
  local size = saved()
  window:SetSize(math.max(WIDTH, math.min(MAX_WIDTH, tonumber(size.width) or WIDTH)),
    math.max(HEIGHT, math.min(MAX_HEIGHT, tonumber(size.height) or HEIGHT)))
  window:SetResizable(true)
  if window.SetResizeBounds then window:SetResizeBounds(WIDTH, HEIGHT, MAX_WIDTH, MAX_HEIGHT)
  elseif window.SetMinResize then -- older clients
    window:SetMinResize(WIDTH, HEIGHT)
    window:SetMaxResize(MAX_WIDTH, MAX_HEIGHT)
  end
  window:SetScript("OnSizeChanged", fitPages)
  window:SetFrameStrata("DIALOG")
  window:SetToplevel(true)
  window:SetClampedToScreen(true)
  window:EnableMouse(true)
  window:SetMovable(true)
  window:RegisterForDrag("LeftButton")
  window:SetScript("OnDragStart", startDrag)
  window:SetScript("OnDragStop", savePosition)
  local s = saved()
  if s.point then window:SetPoint(s.point, UIParent, s.relative or s.point, s.x or 0, s.y or 0)
  else window:SetPoint("CENTER", UIParent, "CENTER", 0, 40) end
  if UISpecialFrames then table.insert(UISpecialFrames, NAME) end -- Esc closes it

  ui.box(window, ui.COLOR.window, ui.COLOR.border)

  -- The tab strip, a shade darker, between two lines.
  local strip = CreateFrame("Frame", nil, window)
  strip:SetPoint("TOPLEFT", 1, -TITLE_BAR)
  strip:SetPoint("TOPRIGHT", -1, -TITLE_BAR)
  strip:SetHeight(TAB_BAR)
  ui.box(strip, ui.COLOR.bar, ui.COLOR.border)

  local logo = window:CreateTexture(nil, "ARTWORK")
  logo:SetSize(24, 24)
  logo:SetPoint("TOPLEFT", 14, -10)
  logo:SetTexture(ns.ICON) -- Minimap.lua's
  local title = ui.text(window, "GameFontHighlightLarge", "WoW Character Tracker")
  title:SetPoint("LEFT", logo, "RIGHT", 8, 0)
  version = ui.text(window, "GameFontDisableSmall")
  version:SetPoint("LEFT", title, "RIGHT", 8, -1)

  closeButton(window, function() window:Hide() end)

  local last
  for _, t in ipairs(TABS) do
    local key = t.key
    local b = ui.button(strip, t.label, 88, function() selectTab(key) end)
    b:SetHeight(24)
    if last then b:SetPoint("LEFT", last, "RIGHT", 6, 0)
    else b:SetPoint("LEFT", strip, "LEFT", 12, 0) end
    tabs[key] = { button = b }
    last = b
  end

  area = CreateFrame("Frame", nil, window)
  area:SetPoint("TOPLEFT", 8, -(TITLE_BAR + TAB_BAR + 4))
  area:SetPoint("BOTTOMRIGHT", -8, 8)
  resizeGrip():SetFrameLevel((area:GetFrameLevel() or 1) + 10)
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
