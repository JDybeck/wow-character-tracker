-- The addon window (/wct, the minimap button): every in-game option in tabs. Settings the game
-- keeps (WoWCharacterTrackerDB) and the In game settings shared with the app (ns.editSettings, the
-- newest change wins) apply right away. What gets posted to Discord comes from the app
-- (Settings.lua) and is listed read-only on the Settings tab. Plain frames only, so it opens in combat.
-- Built on first open, each tab on its first showing.
local ADDON, ns = ...
local ui = ns.ui

local NAME = "WoWCharacterTrackerWindow"
local WIDTH, HEIGHT = 720, 590
local TITLE_BAR, TAB_BAR = 44, 40
local AREA_HEIGHT = HEIGHT - TITLE_BAR - TAB_BAR - 8 -- below the title and the tab row
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

-- The Sharing tab: how much gets shared (the app's preset, which the app applies on your next
-- /reload), the guild and group lines, and the ask box. The per-moment table is the app's; without
-- the app the chat table is here instead, since nothing else sets it.
local PRESETS_SHOWN = { { "quiet", "Quiet" }, { "normal", "Normal" }, { "everything", "Everything" } }
local ASK_KINDS = { "boss", "rare", "epic" }

local function buildSharingTab(p)
  local withApp = ns.hasApp()
  local top
  if withApp then
    local presetTitle = ui.heading(p, "How much gets shared")
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
  local ask = ui.checkbox(p, "Offer to post what Discord skips", "Boss kills and gear the app doesn't post by itself: a small Post or Skip box, out of combat.",
    function()
      local kinds = ns.setting("ask") or {}
      return kinds.boss == true or kinds.rare == true or kinds.epic == true
    end,
    function(on) ns.editSettings(function(s) for _, k in ipairs(ASK_KINDS) do s.ask[k] = on or nil end end) end)
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

local function classRgb(classFile)
  local c = RAID_CLASS_COLORS and classFile and RAID_CLASS_COLORS[classFile]
  if c then return c.r, c.g, c.b end
  return 1, 1, 1
end

local function classColor(fs, classFile) fs:SetTextColor(classRgb(classFile)) end

local ago = ns.ago

-- "12g 34s 56c" from copper.
local function gold(copper)
  copper = math.floor(copper)
  return string.format("%dg %ds %dc", math.floor(copper / 10000), math.floor(copper / 100) % 100, copper % 100)
end

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

-- The state text, its look (good, bad or nil) and up to two buttons ({ label, fn(tag), style }, fn
-- nil: shown disabled).
local function friendState(f)
  if f.blocked then return "Blocked", "bad" end
  if f.contact then
    local bits = {}
    if f.contact.share then bits[#bits + 1] = "You share" end
    if f.sharesWithYou then bits[#bits + 1] = #bits > 0 and "shares with you" or "Shares with you" end
    -- Asking again only makes sense while they don't share with you.
    return #bits > 0 and table.concat(bits, ", ") or "Contact", #bits > 0 and "good" or nil,
      { "Sharing", openSharing }, not f.sharesWithYou and { "Ask again", ns.askFriend } or nil
  end
  if f.hasAddon == nil then return "Checking..." end
  if f.hasAddon then return "Has the addon", nil, { "Ask", ns.askFriend, "primary" }, { "Share", ns.shareWithFriend } end
  if f.invitedAt and time() - f.invitedAt < DAY then return "No addon", nil, { "Invited" } end
  return "No addon", nil, { "Invite", ns.inviteFriend, "primary" }
end

-- "Zeb Stonehand" with a Forever surname, else the name.
local function fullName(c)
  local name = c.char or "?"
  if c.surname and c.surname ~= "" then return name .. " " .. c.surname end
  return name
end

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
local factRows, gearRows = {}, {}
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

local function newFactRow()
  local r = CreateFrame("Frame", nil, details)
  r:SetSize(DETAILS_WIDTH - 28, 16)
  r.labelText = ui.text(r, "GameFontDisableSmall")
  r.labelText:SetPoint("TOPLEFT", r, "TOPLEFT", 0, 0)
  r.labelText:SetWidth(86)
  r.valueText = ui.text(r, "GameFontHighlightSmall")
  r.valueText:SetPoint("TOPLEFT", r, "TOPLEFT", 92, 0)
  r.valueText:SetWidth(DETAILS_WIDTH - 28 - 92)
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
  r.nameText:SetPoint("TOPLEFT", r, "TOPLEFT", 96, -3)
  r.nameText:SetWidth(DETAILS_WIDTH - 24 - 96)
  r.enchantText = ui.text(r, "GameFontHighlightSmall")
  r.enchantText:SetPoint("TOPLEFT", r.nameText, "BOTTOMLEFT", 0, -2)
  r.enchantText:SetWidth(DETAILS_WIDTH - 24 - 96)
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

-- The label and value lines the panel lists, each only when known.
local function facts(c, friend)
  local out = {}
  local function add(label, value) if value and value ~= "" then out[#out + 1] = { label, value } end end
  add("Friend", friend)
  add("Realm", c.realm)
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

-- c: a shared character, or what Battle.net says about an online one (c.notShared).
local function drawDetails(c, friend)
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
  y = y + 10

  local list = facts(c, friend)
  for i, fact in ipairs(list) do
    local r = pooled(factRows, i, newFactRow)
    r.labelText:SetText(fact[1])
    r.valueText:SetText(fact[2])
    local h = math.max(14, textHeight(r.valueText, 14))
    r:SetHeight(h)
    place(r, details, 14, y)
    y = y + h + 7
  end
  hideFrom(factRows, #list + 1)

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
    r.nameText:SetText(name)
    r.nameText:SetTextColor(red, green, blue)
    r.enchantText:SetText(item.enchant or "")
    r.enchantText:SetShown(item.enchant ~= nil)
    place(r, details, 14, y)
    y = y + GEAR_ROW
  end
  hideFrom(gearRows, #items + 1)
  details:SetHeight(y + 14)
  placeBeside(details)
  if sharing then sharing:Hide() end -- one side panel at a time
  details:Show()
end

-- A click on a card: opens its character, or closes the panel when it already shows it.
local function toggleDetails(self)
  local c = self.char
  if not c then return end
  if not details then buildDetails() end
  if details:IsShown() and detailsKey == charKey(c) then details:Hide() else drawDetails(c, self.friend) end
end

-- The sharing panel: what one contact gets from you, beside the window, with the same choices as
-- the app's Friends page. Changes apply right away (Friends.lua's ns.setContact*).
local SHARING_PANEL = "WoWCharacterTrackerSharingPanel"
local SHARING_HEIGHT = 438
local CATEGORY_NAMES = { gear = "Gear", talents = "Talents", location = "Location", professions = "Professions", highlights = "Highlights",
  gold = "Gold", map = "Map position", otherFaction = "Map for the other faction" }
local sharingTag, sharingTitle, sharingSummary
local categoryButtons = {}

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
  for i, cat in ipairs(o.categories) do
    local b = categoryButtons[i]
    if b then
      b.key = cat.key
      b:SetText(categoryText(cat))
      ui.styleButton(b, cat.own ~= nil and "selected" or nil) -- their own choice stands out
    end
  end
  sharingSummary:SetText(o.share and ("They get " .. o.summary .. ".") or "They get nothing while you don't share with them.")
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
  local shareBox = ui.checkbox(sharing, "Share your characters", nil,
    function() local o = sharingNow(); return o ~= nil and o.share end,
    function(on)
      if sharingTag then ns.setContactShare(sharingTag, on) end
      drawSharing()
    end)
  shareBox:SetPoint("TOPLEFT", sub, "BOTTOMLEFT", -4, -10)
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
  { "Everything", { gear = true, talents = true, location = true, professions = true, highlights = true, gold = true, map = true } },
  { "Basics only", { gear = false, talents = false, location = false, professions = false, highlights = false, gold = false, map = false } },
}
local requestBox

-- "Allow gives them 3 of your characters: name, level, class and item level, plus gear and talents."
local function requestText(box)
  if box.chars == 0 then return "You don't share any characters right now, so Allow gives them nothing until you do (Friends tab, What you share)." end
  local what = ns.shareSummary and ns.shareSummary(box.choices) or ""
  return string.format("Allow gives them %s: name, level, class and item level%s.",
    box.chars == 1 and "1 of your characters" or (box.chars .. " of your characters"),
    what == "only the basics" and "" or (", plus " .. what))
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
  box.chars, box.general, box.choices = info.chars, info.general, ns.copy(info.choices)
  box.line:SetText(info.label .. " wants to see your characters.")
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

local function charInfo(c)
  local parts = {}
  if c.level then parts[#parts + 1] = "level " .. c.level end
  if c.spec then parts[#parts + 1] = c.spec end
  if c.ilvl then parts[#parts + 1] = "ilvl " .. math.floor(c.ilvl) end
  if c.zone then parts[#parts + 1] = c.zone end
  parts[#parts + 1] = ago(c.received or c.at)
  return table.concat(parts, ", ")
end

-- "60, Orgrimmar, Bobby (Bob#2)" under an online friend's name.
local function onlineInfo(f)
  local parts = {}
  if f.level then parts[#parts + 1] = tostring(f.level) end
  if f.zone and f.zone ~= "" then parts[#parts + 1] = f.zone end
  parts[#parts + 1] = who(f.label, f.tag)
  return table.concat(parts, ", ")
end

-- What the card of an online friend opens: their shared character when they share this one,
-- else what Battle.net tells.
local function onlineChar(f, sharedByGuid)
  local c = f.guid and sharedByGuid[f.tag .. "~" .. f.guid]
  if c then return c end
  return { guid = f.guid, char = f.character, classFile = f.classFile, level = f.level, zone = f.zone, notShared = true }
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

local function buildFriends(p)
  local width = PAGE_WIDTH - 30
  local status, said = nil, "" -- said: what the last button said, shown under the hint
  local onlineTitle = ui.heading(p, "Online friends")
  local find = ui.button(p, "Find online friends", 150, function()
    searched = true
    if ns.findOnlineFriends then ns.findOnlineFriends() end
    said = ""
    drawFriends()
  end, "primary")
  local hint = ui.text(p, "GameFontDisableSmall", "Battle.net friends online in this game. Checking who has the addon sends them nothing they can see. Click a friend for details.")
  hint:SetWidth(width - 170)
  status = ui.text(p, "GameFontHighlightSmall")
  status:SetWidth(ui.WRAP)

  local function title(s) return ui.heading(p, s) end
  local function note(s)
    local fs = ui.text(p, "GameFontDisableSmall", s)
    fs:SetWidth(ui.WRAP)
    return fs
  end
  local requestsTitle, sharedTitle = title("Requests"), title("Shared with you")
  -- What all friends get, after the lists (it moves down as they grow).
  local yours = CreateFrame("Frame", nil, p)
  yours:SetSize(width + 16, SHARE_DEFAULTS_HEIGHT)
  buildShareDefaults(yours)
  ui.onRefresh(p, function() ui.refresh(yours) end)
  local prompt = note("Click Find online friends to see who's on.")
  local noneOnline = note("None of your Battle.net friends are online in this game.")
  local noneShared = note("Nothing shared with you yet. Ask a friend above.")
  local requestRows, friendCards, sharedHeads, sharedButtons, charCards = {}, {}, {}, {}, {}
  local sharedStates = {}

  -- Under a friend's name in Shared with you: what they share with you, and whether (and what) you
  -- share back, in green, or in grey when you don't.
  local GREEN, GREY = "|cff6fd67f", "|cff9a9a9a"
  local function shareStatus(friend)
    local n = #(friend.chars or {})
    local they = string.format("%sShares %d character%s with you|r", GREEN, n, n == 1 and "" or "s")
    local o = ns.contactSharing and ns.contactSharing(friend.tag)
    local you = GREY .. "You don't share yours|r"
    if o and o.share then
      you = GREEN .. "You share yours" .. ((o.summary or "") ~= "" and (": " .. o.summary) or "") .. "|r"
    end
    return they .. "    " .. you
  end

  -- Runs fn(tag) and shows what it said (ask/share return a line, invite ok and a line).
  local function act(fn, tag)
    return function()
      local a, b = fn(tag)
      said = type(a) == "string" and a or (type(b) == "string" and b or "")
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

  local function newFriendCard()
    local card = newCard(p, width)
    card.pill = CreateFrame("Frame", nil, card)
    card.pill:SetHeight(16)
    card.pill:SetPoint("LEFT", card.nameText, "RIGHT", 8, 0)
    card.stateText = ui.text(card.pill, "GameFontHighlightSmall")
    card.stateText:SetPoint("CENTER", card.pill, "CENTER", 0, 0)
    card.b1, card.b2 = cardButton(card, 76), cardButton(card, 64)
    return card
  end

  local function newCharCard() return newCard(p, width - 8) end

  local CHOICES = { { "Allow", "allow", "primary" }, { "Not now", "notnow" }, { "Never", "never" } }

  drawFriends = function()
    local shared = rowsOf(ns.sharedWithYou)
    local sharedByGuid = {}
    for _, friend in ipairs(shared) do
      for _, c in ipairs(friend.chars or {}) do
        if c.guid then sharedByGuid[friend.tag .. "~" .. c.guid] = c end
      end
    end
    local open -- the open panel's character in the new data, and its friend
    local openFriend

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
      r.line:SetText(who(req.label, req.tag) .. " wants to see your characters")
      for j, choice in ipairs(CHOICES) do
        setButton(r.buttons[j], choice[1], ns.answerFriendRequest and function()
          ns.answerFriendRequest(req, choice[2])
          drawFriends()
        end, choice[3])
      end
      alignButtons(r, r.buttons)
      place(r, p, 16, y)
      y = y + REQUEST + 6
    end
    hideFrom(requestRows, #requests + 1)
    if #requests > 0 then y = y + 10 end

    place(onlineTitle, p, 16, y)
    place(find, p, 16 + width - 150, y - 4)
    y = y + 20
    place(hint, p, 16, y)
    y = y + textHeight(hint, 24) + 6
    if said ~= "" then
      status:SetText(said)
      place(status, p, 16, y)
      y = y + 18
    else
      status:Hide()
    end
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
      local card = pooled(friendCards, i, newFriendCard)
      card.char, card.friend = onlineChar(f, sharedByGuid), who(f.label, f.tag)
      if charKey(card.char) == detailsKey then open, openFriend = card.char, card.friend end
      setCard(card, f.character or "?", f.classFile, onlineInfo(f))
      local state, look, first, second = friendState(f)
      card.stateText:SetText(state)
      card.pill:SetWidth((card.stateText:GetStringWidth() or 60) + 14)
      ui.box(card.pill, C.bar, look == "good" and C.good or (look == "bad" and C.bad or C.border))
      for _, pair in ipairs({ { card.b1, first }, { card.b2, second } }) do
        local b, what = pair[1], pair[2]
        if what then setButton(b, what[1], what[2] and act(what[2], f.tag), what[3]) else b:Hide() end
      end
      alignButtons(card, { card.b1, card.b2 })
      place(card, p, 16, y)
      y = y + CARD + 6
    end
    hideFrom(friendCards, #online + 1)
    y = y + 12

    place(sharedTitle, p, 16, y)
    y = y + 22
    noneShared:Hide()
    if #shared == 0 then
      place(noneShared, p, 16, y)
      y = y + 20
    end
    local heads, cards = 0, 0
    for _, friend in ipairs(shared) do
      heads = heads + 1
      local h = pooled(sharedHeads, heads, function() return ui.text(p, "GameFontNormalSmall") end)
      local label = who(friend.label, friend.tag) or "?"
      h:SetText(label)
      place(h, p, 18, y + 4)
      local st = pooled(sharedStates, heads, function()
        local fs = ui.text(p, "GameFontHighlightSmall")
        fs:SetWidth(width - 110)
        return fs
      end)
      st:SetText(shareStatus(friend))
      place(st, p, 18, y + 20)
      -- What you share with them and what you keep of theirs, also when they're offline.
      local b = pooled(sharedButtons, heads, function() return cardButton(p, 90) end)
      setButton(b, "Options", function() openSharing(friend.tag) end)
      place(b, p, 24 + width - 8 - 90, y + 6)
      y = y + 42
      for _, c in ipairs(friend.chars or {}) do
        cards = cards + 1
        local card = pooled(charCards, cards, newCharCard)
        card.char, card.friend = c, label
        if charKey(c) == detailsKey then open, openFriend = c, label end
        setCard(card, fullName(c), c.classFile, charInfo(c))
        place(card, p, 24, y)
        y = y + CARD + 6
      end
      y = y + 6
    end
    hideFrom(sharedHeads, heads + 1)
    hideFrom(sharedStates, heads + 1)
    hideFrom(sharedButtons, heads + 1)
    hideFrom(charCards, cards + 1)
    if details and details:IsShown() then
      if open then drawDetails(open, openFriend) else details:Hide() end
    end
    if sharing and sharing:IsShown() then drawSharing() end

    place(yours, p, 0, y + 8)
    y = y + 8 + SHARE_DEFAULTS_HEIGHT
    p:SetHeight(y + 10)
  end
  ui.onRefresh(p, drawFriends)
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
    { "highlights", "Highlights" }, { "gold", "Gold" }, { "map", "Map position" } }, whatTitle,
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
    return "No app found. Discord posts, screenshots and the character overview need the free Character Tracker app. Guild and group lines work without it."
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

  local advancedTitle = ui.heading(p, "Advanced", minimap, -40)
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
  ui.onRefresh(p, function() appText:SetText(appSummary()) end)
end

-- The window ------------------------------------------------------------------------------------
-- height: how tall the tab's content is; taller than the window gets a scroll frame.
local TABS = {
  { key = "sharing", label = "Sharing", height = 560, build = buildSharingTab }, -- without the app: the whole chat table
  { key = "friends", label = "Friends", height = 1000, build = buildFriends }, -- grows with its lists
  { key = "note", label = "Note", height = 330, build = buildNote },
  { key = "settings", label = "Settings", height = 700, build = buildSettings },
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
  key = OLD_TABS[key] or key
  if not BY_KEY[key] then key = "sharing" end
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

local function build()
  window = CreateFrame("Frame", NAME, UIParent)
  window:SetSize(WIDTH, HEIGHT)
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
