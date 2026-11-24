-- The addon window (/wct, the minimap button): every in-game option in tabs. Settings the game
-- keeps (WoWCharacterTrackerDB) and the In game settings shared with the app (ns.editSettings, the
-- newest change wins) apply right away, including the Sharing tab's table of what goes where. Plain
-- frames only, so it opens in combat.
-- Built on first open, each tab on its first showing.
local ADDON, ns = ...
local ui = ns.ui

local NAME = "WoWCharacterTrackerWindow"
-- The tabs are a bar of icons down the left (Shell.lua, ns.RAIL_WIDTH wide); a header names the page.
local WIDTH, HEIGHT = 760 + ns.RAIL_WIDTH, 590
local TITLE_BAR, TAB_BAR = 40, 0
local AREA_HEIGHT = HEIGHT - TITLE_BAR - TAB_BAR - 8 -- below the title and the tab row

local window, area, version

-- The width tab pages have now: the window can be made wider than WIDTH (the grip, bottom right).
local function pageWidth() return ((window and window:GetWidth()) or WIDTH) - 50 - ns.RAIL_WIDTH end
ns.pageWidth = pageWidth -- Home.lua's
-- The height a page is shown in now (the window can be made taller): Home fills it with moments.
function ns.pageHeight() return ((window and window:GetHeight()) or HEIGHT) - TITLE_BAR - TAB_BAR - 8 end
local tabs = {}  -- [key] = { button, holder, page }
local current

local function isOn(key) return ns.setting(key) ~= false end
local buildShareDefaults -- what all friends get, a section of the Sharing tab (set further down)

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

-- Auto: yards in the Americas region, meters elsewhere (ns.distanceUnit).
local DISTANCE_CHOICES = { { "auto", "Auto" }, { "meters", "Meters" }, { "yards", "Yards" } }

-- Chat ------------------------------------------------------------------------------------------
-- The same kinds and names as the app's In game page (FlavorViewModel.AnnounceNames).
local ANNOUNCE_KINDS = { { "level", "Level-ups" }, { "maxlevel", "Max level" }, { "loot", "Epic loot" }, { "rare", "Rare loot" }, { "followed", "Followed items" },
  { "mount", "Mounts" }, { "duel", "Duel wins" }, { "bg", "Battleground wins" }, { "profession", "Professions" }, { "reputation", "Reputations" } }
for _, kind in ipairs(ns.IS_RETAIL
  and { { "keyrun", "Mythic+ runs" }, { "mplus", "Mythic+ rating" }, { "pvp", "PvP rating" } }
  or { { "boss", "Boss kills" }, { "makgora", "Mak'gora wins" }, { "death", "Deaths" }, { "achievement", "Legacy achievements" }, { "weekly", "Weekly satchel" } }) do
  ANNOUNCE_KINDS[#ANNOUNCE_KINDS + 1] = kind
end

-- Guild chat's kinds are `kinds`, the group's `partyKinds` (missing from before it had its own).
local function announceKinds(a, column)
  return column == "guild" and a.kinds or (a.partyKinds or a.kinds)
end

-- The Sharing tab, like the app's Sharing page: how much gets shared (a preset), a row per moment
-- with a box per place, the level steps and filters, the ask box and Incognito. The moments and
-- presets come from the app (ns.momentRows); an older app has the preset only, and without the
-- app the table has the group chat column alone.
local PRESETS_SHOWN = { { "quiet", "Quiet" }, { "normal", "Normal" }, { "everything", "Everything" } }
local ASK_KINDS = { "boss", "rare", "epic" }
local ASK_SECONDS = { { 5, "5 sec" }, { 10, "10 sec" }, { 15, "15 sec" }, { 30, "30 sec" }, { 60, "60 sec" } }

-- Incognito (Shared.lua, Commands.lua): a while without posts or chat lines, never caught up later.
-- First on the Sharing tab: it's the one thing there you do now rather than set up once.
local INCOGNITO_CHOICES = { { "30m", "30 minutes" }, { "1h", "1 hour" }, { "2h", "2 hours" }, { "4h", "4 hours" } }

local function buildIncognito(p, anchor)
  local title = ui.heading(p, "Incognito", anchor)
  local hint = ui.hint(p, ns.hasApp() and "Nothing is posted or said in chat for a while, and none of it is posted later."
    or "Nothing is said in chat for a while. Your own history keeps it.", title)
  local friendsToo = false
  local last
  for _, choice in ipairs(INCOGNITO_CHOICES) do
    local b = ui.button(p, choice[2], 90, function()
      ns.goIncognito(ns.INCOGNITO_TIMES[choice[1]], friendsToo)
      ui.refresh(p)
    end)
    if last then b:SetPoint("LEFT", last, "RIGHT", 6, 0) else b:SetPoint("TOPLEFT", hint, "BOTTOMLEFT", 0, -8) end
    last = b
  end
  local off = ui.button(p, "End now", 90, function()
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
  friends:SetPoint("TOPLEFT", hint, "BOTTOMLEFT", -2, -36)
  ui.onRefresh(p, function()
    local w = ns.incognitoActive()
    off:SetShown(w ~= nil)
    status:SetText(w and ("Incognito until " .. date("%H:%M", w["until"])) or "")
  end)
  return friends
end

-- "Posts at levels 5, 10, 15, 20, 25, 30, 31, 32 and so on." (the app's LevelPreview)
local function levelPreview()
  local post = ns.postRules() or {}
  if not (post.level and post.level.on) then return "Level-ups aren't posted." end
  local levels = {}
  for level = 2, 100 do
    if #levels >= 12 then break end
    if ns.levelPosted(level) then levels[#levels + 1] = level end
  end
  return #levels == 0 and "No levels match." or ("Posts at levels " .. table.concat(levels, ", ") .. " and so on.")
end

-- The app's level steps and filters (ns.filters, an In game setting): what gets posted, the Home
-- tab's moments and the chat lines follow them.
local LEVEL_BOXES = { { "split", "Up to level" }, { "before", "post every" }, { "after", "levels, then every" } }

local function buildFilters(p, anchor)
  local title = ui.heading(p, "Level steps and filters", anchor)
  local last
  for i, box in ipairs(LEVEL_BOXES) do
    local key = box[1]
    local label = ui.text(p, "GameFontHighlight", box[2])
    if last then label:SetPoint("LEFT", last, "RIGHT", 8, 0) else label:SetPoint("TOPLEFT", title, "BOTTOMLEFT", 0, -12) end
    local input = ui.inputBox(p, 40, 3, function(text)
      local n = tonumber(text)
      if n then ns.editSettings(function(s) s.filters[key] = math.max(1, math.min(100, math.floor(n))) end) end
      ui.refresh(p)
    end)
    input:SetPoint("LEFT", label, "RIGHT", 6, 0)
    ui.onRefresh(p, function() ui.showInput(input, key, tostring(ns.filters()[key] or 1)) end)
    last = input
    if i == #LEVEL_BOXES then
      local tail = ui.text(p, "GameFontHighlight", "level(s)")
      tail:SetPoint("LEFT", input, "RIGHT", 6, 0)
    end
  end
  local preview = ui.text(p, "GameFontDisableSmall")
  preview:SetPoint("TOPLEFT", title, "BOTTOMLEFT", 0, -40)
  ui.onRefresh(p, function() preview:SetText(levelPreview()) end)

  local rareBoe = ui.checkbox(p, "Rares: Bind on Equip only", nil,
    function() return ns.filters().rareBoe == true end,
    function(on) ns.editSettings(function(s) s.filters.rareBoe = on end) end)
  rareBoe:SetPoint("TOPLEFT", preview, "BOTTOMLEFT", -2, -8)
  if ns.IS_RETAIL then return rareBoe end -- Retail posts Mythic+ runs, not end bosses
  local firstKill = ui.checkbox(p, "End bosses: first kill only", nil,
    function() return ns.filters().firstKill ~= false end,
    function(on) ns.editSettings(function(s) s.filters.firstKill = on end) end)
  firstKill:SetPoint("LEFT", rareBoe, "RIGHT", 200, 0)
  return rareBoe
end

-- The chat places (guild while it works, ns.GUILD_CHAT_ON, and the group) under anchor; returns the last box.
local function buildChatPlaces(p, anchor)
  local places = { { "party", "Party / raid chat" } }
  if ns.GUILD_CHAT_ON then table.insert(places, 1, { "guild", "Guild chat" }) end
  return ui.checkGrid(p, places, anchor,
    function(key) return (ns.setting("announce") or {})[key] == true end,
    function(key, on) ns.editSettings(function(s) s.announce[key] = on end) end)
end

-- A chat column's box: the kind in guild's or the group's list (Group, and Guild once it works again).
local function chatOn(column, kind)
  local kinds = announceKinds(ns.setting("announce") or {}, column)
  return kinds ~= nil and kinds[kind] == true
end
local function setChat(column, kind, on)
  ns.editSettings(function(s) announceKinds(s.announce, column)[kind] = on or nil end)
end

-- The app's Sharing table: a row per moment (ns.momentRows), Discord, Screenshot, Group and Ask
-- first. No Guild column while guild chat is off (ns.GUILD_CHAT_ON); add { "guild", "Guild" } here
-- when it's back. Stream alerts and the Test buttons stay in the app.
local MOMENT_COLUMNS = { { "post", "Discord" }, { "shot", "Screenshot" }, { "party", "Group" }, { "ask", "Ask first" } }

local function buildMomentTable(p, anchor, rows)
  local byKey, items = {}, {}
  for _, row in ipairs(rows) do
    byKey[row.key] = row
    items[#items + 1] = { row.key, row.name }
  end
  -- Which kind each column keys on: the moment for Discord, the shared screenshot, chat and ask kinds.
  local function kind(column, key)
    local row = byKey[key]
    if column == "post" then return key end
    if column == "shot" then return row.shot end
    if column == "ask" then return row.ask end
    return row.announce
  end
  local function get(column, key)
    local k = kind(column, key)
    if column == "post" then return ((ns.setting("posts") or {})[k]) == true end
    if column == "shot" then return ((ns.setting("screenshot") or {})[k]) == true end
    if column == "ask" then return ((ns.setting("ask") or {})[k]) == true end
    return chatOn(column, k)
  end
  local function set(column, key, on)
    local k = kind(column, key)
    if column == "party" or column == "guild" then return setChat(column, k, on) end
    ns.editSettings(function(s)
      s.posts = s.posts or {}
      local list = column == "post" and s.posts or (column == "shot" and s.screenshot or s.ask)
      if column == "post" or column == "shot" then list[k] = on else list[k] = on or nil end
    end)
    ui.refresh(p) -- a screenshot box can stand for two rows (rare and epic loot), and the preset follows
  end
  return ui.checkTable(p, items, MOMENT_COLUMNS, anchor, get, set,
    { title = "Moment", first = 210, width = 105, has = function(column, key) return kind(column, key) ~= nil end })
end

-- How long the ask box stays (askSeconds, the app's choices).
local function buildAskSeconds(p, anchor, x)
  local label = ui.text(p, "GameFontHighlight", "The ask box stays")
  label:SetPoint("TOPLEFT", anchor, "BOTTOMLEFT", x or 0, -18)
  local choice = ui.choice(p, ASK_SECONDS, function() return tonumber(ns.setting("askSeconds")) or 10 end,
    function(n) ns.editSettings(function(s) s.askSeconds = n end) end, 64)
  choice:SetPoint("LEFT", label, "RIGHT", 10, 0)
  return label
end

-- How much gets shared: the preset. With the app's presets it goes in right away and follows the
-- table; from an older app the app applies it on sync.
local function buildPresets(p, anchor)
  local title = ui.heading(p, "How much gets shared", anchor)
  local hint = ui.hint(p, "Pick one, or choose per moment below.", title)
  local presets = ui.choice(p, PRESETS_SHOWN, ns.currentPreset, function(key)
    ns.applyPreset(key)
    ui.refresh(p) -- the table's boxes and the filters
  end)
  presets:SetPoint("TOPLEFT", hint, "BOTTOMLEFT", 0, -10)
  local custom = ui.text(p, "GameFontDisableSmall", "Custom")
  custom:SetPoint("LEFT", presets, "RIGHT", 6, 0)
  ui.onRefresh(p, function() custom:SetShown(ns.currentPreset() == "custom") end)
  return presets
end

-- Said once, at the bottom of the Sharing tab, instead of under every heading.
local function syncNote(p, anchor, x)
  local note = ui.text(p, "GameFontDisableSmall", "Changes here reach the app on your next /reload or logout, and the app's reach the game the same way. The newest change wins.")
  note:SetPoint("TOPLEFT", anchor, "BOTTOMLEFT", x or 0, -ui.SECTION_GAP)
  ui.wrap(note)
end

local function buildSharingTab(p)
  local withApp = ns.hasApp()
  local rows = withApp and ns.momentRows()
  local top = buildIncognito(p)
  if rows then
    local presets = buildPresets(p, top)
    local chat = buildChatPlaces(p, presets)
    local last = buildMomentTable(p, chat, rows)
    local always = ui.hint(p, "Legendary gear is always posted. Stream alerts and test posts are in the app.", last)
    always:SetPoint("TOPLEFT", last, "BOTTOMLEFT", 0, -14)
    syncNote(p, buildShareDefaults(p, buildAskSeconds(p, buildFilters(p, always))), -4)
    return
  end

  if withApp then -- an older app: the preset, the filters, the chat places and the ask box
    top = buildFilters(p, buildPresets(p, top))
  end

  local chatTitle = ui.heading(p, "Tell in chat", top)
  local chatHint = ui.hint(p, "A line in chat as you when something happens, once you're out of combat.", chatTitle)
  local columns = { { "party", "Group chat" } }
  if ns.GUILD_CHAT_ON then table.insert(columns, 1, { "guild", "Guild chat" }) end
  local whereLast = buildChatPlaces(p, chatHint)
  local last = whereLast
  if not withApp then
    buildShareDefaults(p, ui.checkTable(p, ANNOUNCE_KINDS, columns, whereLast, chatOn, setChat))
    return
  end

  local askTitle = ui.heading(p, "Ask before skipping", last)
  -- Boss kills and gear only: the other moments picked in the app's Sharing table stay as they are.
  local ask = ui.checkbox(p, "Offer to post what Discord skips", "A small Post or Skip box for boss kills and gear the app doesn't post, out of combat.",
    function()
      local kinds = ns.setting("ask") or {}
      for _, k in ipairs(ASK_KINDS) do if kinds[k] then return true end end
      return false
    end,
    function(on)
      ns.editSettings(function(s)
        for _, k in ipairs(ASK_KINDS) do s.ask[k] = on or nil end
      end)
    end)
  ask:SetPoint("TOPLEFT", askTitle, "BOTTOMLEFT", -2, -6)
  syncNote(p, buildShareDefaults(p, buildAskSeconds(p, ask, 34), -32), -4)
end

-- Note box ---------------------------------------------------------------------------------------
-- A section of the Settings tab (it had a tab of its own before): the on-screen note box.
local function buildNote(p, anchor, gap)
  local title = ui.heading(p, "Note box", anchor, gap)
  local hint = ui.hint(p, ns.hasApp() and "The character's note on screen. Edit it in the box, or on the character's Notes tab in the app."
    or "The character's note on screen. Edit it right in the box.", title)

  local showNote = ui.checkbox(p, "Show the note box", nil,
    function() return ns.noteBoxShown() end,
    function(on) ns.toggleNoteBox(on) end)
  showNote:SetPoint("TOPLEFT", hint, "BOTTOMLEFT", -2, -6)

  local lockNote = ui.checkbox(p, "Lock it in place", "Clicks go through it to the game.",
    function() return ns.noteBoxLocked() end,
    function(on) ns.lockNoteBox(on, true) end)
  lockNote:SetPoint("TOPLEFT", showNote, "BOTTOMLEFT", 0, -2)

  local hoverNote = ui.checkbox(p, "Buttons only on mouseover", nil,
    function() return ns.noteButtonsOnHover() end,
    function(on) ns.setNoteButtonsOnHover(on) end)
  hoverNote:SetPoint("TOPLEFT", lockNote, "BOTTOMLEFT", 0, -18)

  local opacity = ui.slider(p, "Background opacity",
    function() return math.floor(ns.noteAlpha() * 100 + 0.5) end,
    function(v) ns.setNoteAlpha(v / 100) end)
  opacity:SetPoint("TOPLEFT", hoverNote, "BOTTOMLEFT", 6, -30)

  local reset = ui.button(p, "Reset position", 120, function() ns.resetNoteBox() end)
  reset:SetPoint("LEFT", opacity, "RIGHT", 60, 0)
  return opacity
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
  if p.blocked then return "Blocked", "bad", { "Unblock", ns.unblockFriend } end
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
  local realm = ns.realmOf(c.guid, c.realm)
  return realm and string.format("%s (%s)", fullName(c), realm) or fullName(c)
end

-- The item's name ("item 12345" until the game has it cached) and its quality color, white without one.
local function itemName(id)
  local name, _, quality = ns.itemInfo(id)
  local color = quality and ITEM_QUALITY_COLORS and ITEM_QUALITY_COLORS[quality]
  if color then return name or ("item " .. id), color.r, color.g, color.b end
  return name or ("item " .. id), 1, 1, 1
end

local hideTip = ns.hideTip

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

local itemIcon = ns.itemIcon

local function showItemTip(self) ui.itemTip(self, self.itemId, self.enchantId) end

-- A label and its value, the value wrapping in what's left of width.
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
local highlights -- the highlights panel, built on its first open

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
  placeBeside(highlights)
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
-- The facts the row above doesn't already say (name, faction, race, zone, level, item level and when it
-- was seen are on the row; the friend is the section's title). Professions have their own rows.
local function facts(c)
  local out = {}
  local function add(label, value, tex) if value and value ~= "" then out[#out + 1] = { label, value, tex } end end
  -- Forever has no realm to speak of, only a ruleset: the realm name's type ("CAMWOW EU5 Realm 6 PvP"
  -- -> "PvP"), plus the game's rules (Hardcore, Self-found) when they aren't the usual ones.
  local realm = ns.realmOf(c.guid, c.realm)
  local realmType = not ns.IS_RETAIL and ns.realmType(realm)
  local rules = c.ruleset ~= "Normal" and c.ruleset or nil
  if realmType then
    add("Ruleset", rules and string.format("%s, %s", realmType, rules) or realmType)
  else
    add("Realm", realm)
    add("Ruleset", rules)
  end
  add("Gold", (c.money or 0) >= 10000 and ns.formatGold(c.money) or (c.money and c.money > 0 and gold(c.money)))
  local talents = ui.talentText(c.talents)
  if talents then out[#out + 1] = { "Talents", talents, nil, true } end -- a whole row: it's long
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
  if highlights then highlights:Hide() end
  details:Show()
end

-- The highlights panel: one character's highlights beside the window, newest first, scrolling.
-- Yours come from the saved events, a friend's from what they share (their newest few). Its parts
-- live in HL (the file is near Lua 5.1's 200 locals).
local HL = { WIDTH = 380, MAX = 480, ROW = 22, OWN = 200, rows = {},
  LOSSES = { duel = "Lost a duel to %s.", makgora = "Lost a Mak'gora to %s." } }

-- The chat line for a highlight (Announce.lua's), and the losses it leaves out; nil: nothing to show.
local function highlightLine(e)
  local line = ns.momentLine and ns.momentLine(e)
  if line or not e.lost then return line end
  if e.kind == "bg" then return string.format("Lost %s.", e.instance or "a battleground") end
  return HL.LOSSES[e.kind] and string.format(HL.LOSSES[e.kind], e.opponent or "someone")
end

-- c's highlights that have a line, newest first: { e, line }. friend nil: one of yours.
local function highlightsOf(c, friend)
  local out = {}
  local function add(e)
    local line = highlightLine(e)
    if line then out[#out + 1] = { e = e, line = line } end
  end
  if friend == nil and c.guid then
    local events = (WoWCharacterTrackerDB or {}).events or {}
    for i = #events, 1, -1 do
      local e = events[i]
      if #out >= HL.OWN then break end
      if type(e) == "table" and e.guid == c.guid and ns.isFriendHighlight(e) then add(e) end
    end
  else
    for _, e in ipairs(ns.friendHighlights(c)) do add(e) end
  end
  return out
end

-- A row: the item's icon, the line and when. Hover: the item's game tooltip for loot and mounts,
-- else the whole line (a long one is cut short).
local function newHighlightRow()
  local r = CreateFrame("Frame", nil, HL.list)
  r:SetSize(HL.WIDTH - 52, HL.ROW)
  r:EnableMouse(true)
  r.icon = r:CreateTexture(nil, "ARTWORK")
  r.icon:SetSize(18, 18)
  r.icon:SetPoint("LEFT", r, "LEFT", 0, 0)
  r.lineText = ui.text(r, "GameFontHighlightSmall")
  r.lineText:SetPoint("LEFT", r, "LEFT", 24, 0)
  r.lineText:SetWidth(HL.WIDTH - 52 - 24 - 62)
  r.lineText:SetWordWrap(false)
  r.when = ui.text(r, "GameFontDisableSmall")
  r.when:SetPoint("RIGHT", r, "RIGHT", 0, 0)
  r:SetScript("OnEnter", function(self)
    if not GameTooltip then return end
    if self.itemId then return showItemTip(self) end
    GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
    GameTooltip:SetText(self.full or "", 1, 1, 1, 1, true)
    GameTooltip:Show()
  end)
  r:SetScript("OnLeave", hideTip)
  return r
end

local function drawHighlights(c, friend)
  HL.key = charKey(c)
  HL.title:SetText(fullName(c))
  classColor(HL.title, c.classFile)
  local list = highlightsOf(c, friend)
  HL.sub:SetText(#list == 1 and "1 highlight, newest first" or string.format("%d highlights, newest first", #list))
  HL.none:SetText(friend and "No highlights shared yet." or "None yet. Boss kills, loot, mounts, duels and the like show up here.")
  HL.none:SetShown(#list == 0)
  for i, h in ipairs(list) do
    local r = pooled(HL.rows, i, newHighlightRow)
    local e = h.e
    r.itemId = (e.kind == "loot" or e.kind == "mount") and type(e.itemId) == "number" and e.itemId or nil
    r.icon:SetTexture(r.itemId and itemIcon(r.itemId) or nil)
    r.lineText:SetText(h.line)
    r.when:SetText(e.at and ago(e.at) or "")
    r.full = h.line
    place(r, HL.list, 0, (i - 1) * HL.ROW)
  end
  hideFrom(HL.rows, #list + 1)
  HL.list:SetHeight(math.max(1, #list * HL.ROW))
  ui.scrollTo(HL.scroll, 0)
  highlights:SetHeight(math.min(HL.MAX, 60 + math.max(1, #list) * HL.ROW + 16))
  placeBeside(highlights)
  if details then details:Hide() end -- one side panel at a time
  if sharing then sharing:Hide() end
  highlights:Show()
end

-- Opens c's highlights beside the window (built on the first open), or closes them when they're
-- the ones shown.
local function toggleHighlights(c, friend)
  if not highlights then
    highlights = sidePanel("WoWCharacterTrackerHighlightsPanel")
    highlights:SetWidth(HL.WIDTH)
    HL.title = ui.text(highlights, "GameFontHighlightLarge")
    HL.title:SetPoint("TOPLEFT", 14, -14)
    HL.title:SetWidth(HL.WIDTH - 50)
    HL.sub = ui.text(highlights, "GameFontDisableSmall")
    HL.sub:SetPoint("TOPLEFT", HL.title, "BOTTOMLEFT", 0, -4)
    HL.sub:SetWidth(HL.WIDTH - 28)
    HL.scroll = ui.scrollFrame(highlights)
    HL.scroll:SetPoint("TOPLEFT", 14, -60)
    HL.scroll:SetPoint("BOTTOMRIGHT", -30, 12)
    HL.list = CreateFrame("Frame", nil, HL.scroll)
    HL.list:SetSize(HL.WIDTH - 52, 1)
    HL.scroll:SetScrollChild(HL.list)
    HL.none = ui.text(highlights, "GameFontDisableSmall")
    HL.none:SetPoint("TOPLEFT", 14, -62)
    HL.none:SetWidth(HL.WIDTH - 28)
  end
  if highlights:IsShown() and HL.key == charKey(c) then
    highlights:Hide()
  else
    drawHighlights(c, friend)
  end
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
    if highlights then highlights:Hide() end
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

-- A click that opens an online friend: the character they're playing unfolds, its gear beside the
-- window. Only one they share: what Battle.net alone tells would unfold nearly empty.
function ns.unfoldFriendPlaying(c, friend)
  if not c or c.notShared then return end
  if not details then buildDetails() end
  expandedKey = charKey(c)
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
  ex.left, ex.right, ex.profs = {}, {}, {}
  ex.chart = ui.levelChart(ex, 30, EXPAND_CHART, math.floor(ex.half / 30))
  return ex
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
  -- Both columns start at the same line and step in the same rhythm (Ui.lua FACT_TILE, FACT_GAP).
  local top = 12
  local left = ui.factTiles(ex, ex.left, facts(c), 12, top, ex.half, 2)
  local profs = ui.parseProfessions(c.professions)
  if #profs > 0 then left = ui.professionRows(ex, ex.profs, profs, 12, left > top and (left + ui.FACT_GAP) or top, ex.half) - ui.FACT_GAP
  else ui.professionRows(ex, ex.profs, profs, 12, left, ex.half) end
  local x = 24 + ex.half
  local levels = levelsOf(c)
  local right = ui.factTiles(ex, ex.right, ns.levelingFacts(levels, c), x, top, ex.half, 2)
  ex.chart:SetShown(#levels > 0)
  if #levels > 0 then
    place(ex.chart, ex, x, right + ui.FACT_GAP)
    local playing = (c.levelTime or 0) > 0 and c.level and not (c.maxLevel and c.level >= c.maxLevel)
    ex.chart.draw(levels, playing and { level = c.level + 1, seconds = c.levelTime } or nil)
    right = right + ui.FACT_GAP + EXPAND_CHART
  end
  -- Hide (or show again): out of the list, and the info folds away.
  ex.hide = ex.hide or cardButton(ex, 90)
  ex.hide:SetHeight(20)
  local hidden = isHidden(c)
  setButton(ex.hide, hidden and "Show again" or "Hide", function()
    setHidden(c, not hidden)
    expandedKey = nil
    if details then details:Hide() end
    if highlights then highlights:Hide() end
    redrawTab()
  end)
  local h = math.max(left, right) + 14
  place(ex.hide, ex, ex.width - 102, h)
  -- Highlights (the panel beside the window), your own character's reputations (ReputationWindow.lua),
  -- then a button per profession with known recipes, which opens them in the recipe window (RecipeWindow.lua).
  local actions = {}
  local count = #highlightsOf(c, friend)
  if count > 0 then
    actions[1] = { string.format("Highlights (%d)", count), function() toggleHighlights(c, friend) end, "Highlights" }
  end
  -- Your own: your main (friends you share with see it first) and pinned to the top (ns.ownPicks).
  if friend == nil and c.guid and ns.setOwnMain then
    local isMain, pinned = ns.ownMain() == c.guid, ns.isPinned(c.guid)
    actions[#actions + 1] = { isMain and "Unset as main" or "Set as my main", function()
      ns.setOwnMain(not isMain and c.guid or false)
      redrawTab()
    end }
    actions[#actions + 1] = { pinned and "Unpin" or "Pin to top", function()
      ns.setPinned(c.guid, not pinned)
      redrawTab()
    end }
  end
  local reputations = friend == nil and ns.reputationCount and ns.reputationCount(c) or 0 -- your own only
  if reputations > 0 then
    actions[#actions + 1] = { string.format("Reputations (%d)", reputations), function() ns.openReputations(c) end, "Reputations" }
  end
  for _, prof in ipairs(ns.recipeProfessions and ns.recipeProfessions(c) or {}) do
    actions[#actions + 1] = { string.format("%s (%d)", prof.name, prof.count), function() ns.openRecipes(c, prof.name, friend) end, prof.name }
  end
  -- An even grid of small buttons (the icon says which recipes), clear of Hide on the right: as many
  -- columns as fit at about 140 wide, every button the same width, so the rows line up.
  ex.recipes = ex.recipes or {}
  local room = ex.width - 12 - 112
  local cols = math.max(1, math.min(#actions, math.floor((room + 6) / 146)))
  local bw = math.floor((room - (cols - 1) * 6) / cols)
  if #actions < cols then bw = 140 end
  for i, action in ipairs(actions) do
    local col, row = (i - 1) % cols, math.floor((i - 1) / cols)
    local b = pooled(ex.recipes, i, function() return cardButton(ex, 140) end)
    b:SetSize(bw, 20)
    setButton(b, action[1], action[2])
    ui.buttonIcon(b, ui.PROFESSION_ICONS[action[3] or ""])
    place(b, ex, 12 + col * (bw + 6), h + row * 26)
  end
  hideFrom(ex.recipes, #actions + 1)
  h = h + math.max(1, math.ceil(#actions / cols)) * 26 + 4
  ex:SetHeight(h)
  ex:Show()
  return h
end

-- The sharing panel: what one contact gets from you, beside the window, with the same choices as
-- the app's Friends page. Changes apply right away (Friends.lua's ns.setContact*).
local SHARING_PANEL = "WoWCharacterTrackerSharingPanel"
local SHARING_HEIGHT = 712
local CATEGORY_NAMES = { gear = "Gear", talents = "Talents", location = "Location", professions = "Professions", reputations = "Reputations", highlights = "Highlights",
  leveling = "Level times", gold = "Gold", map = "Map position", otherFaction = "Map for the other faction" }
local sharingTag, sharingTitle, sharingSummary, theirsText, askSaid, nickname
local categoryButtons, askButtons = {}, {}
local askWants = {} -- [category] = true: picked to ask them for

-- "Gear: like all friends (on)", "Gear: on" or "Gear: off".
local function categoryText(cat)
  local name = CATEGORY_NAMES[cat.key] or cat.key
  if cat.own == nil then return string.format("%s: like all friends (%s)", name, cat.general and "on" or "off") end
  return string.format("%s: %s", name, cat.own and "on" or "off")
end

local function sharingNow() return sharingTag and ns.contactSharing and ns.contactSharing(sharingTag) end

-- "They share with you: gear, talents, level times." from what their characters came with.
local function theirsLine(o)
  if not o.receive then return "You don't keep what they share, so it doesn't show here." end
  if o.theirCount == 0 then return "Nothing from them yet." end
  return "They share with you: " .. ns.shareSummary(o.theirs) .. "."
end

local function drawSharing()
  local o = sharingNow()
  if not o then
    sharing:Hide()
    return
  end
  sharingTitle:SetText(o.label)
  ui.showInput(nickname, sharingTag, o.nickname)
  for i, cat in ipairs(o.categories) do
    local b = categoryButtons[i]
    if b then
      b.key = cat.key
      b:SetText(categoryText(cat))
      ui.styleButton(b, cat.own ~= nil and "selected" or nil) -- their own choice stands out
    end
  end
  sharingSummary:SetText(o.share and ("They get " .. o.summary .. ".") or "They get nothing while you don't share with them.")
  theirsText:SetText(theirsLine(o))
  -- What they already share needs no asking: greyed out.
  for _, b in ipairs(askButtons) do
    local have = o.theirs[b.key] == true
    if have then askWants[b.key] = nil end
    b:SetEnabled(not have)
    ui.styleButton(b, askWants[b.key] and "selected" or nil)
  end
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
  nickname = ui.inputBox(sharing, DETAILS_WIDTH - 28 - 70, 32, function(text, tag)
    if tag and ns.setFriendNickname then ns.setFriendNickname(tag, text) end
    if sharing:IsShown() then drawSharing() end
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
  theirsText = ui.text(sharing, "GameFontHighlightSmall")
  theirsText:SetPoint("TOPLEFT", sharingSummary, "BOTTOMLEFT", 0, -16)
  theirsText:SetWidth(DETAILS_WIDTH - 28)
  local askTitle = ui.text(sharing, "GameFontNormalSmall", "Ask them to share:")
  askTitle:SetPoint("TOPLEFT", theirsText, "BOTTOMLEFT", 0, -12)
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
  if highlights then highlights:Hide() end
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
local REQUEST_SHORT, REQUEST_TALL = 150, 314
local PRESETS = {
  { "Like all friends" },
  { "Everything", { gear = true, talents = true, location = true, professions = true, reputations = true, highlights = true, leveling = true, gold = true, map = true,
    otherFaction = true } },
  { "Basics only", { gear = false, talents = false, location = false, professions = false, reputations = false, highlights = false, leveling = false, gold = false, map = false,
    otherFaction = false } },
}
local requestBox

-- "Allow gives them 3 of your characters: name, level, class and item level, plus gear and talents."
-- and, when they sent theirs along, "They sent 2 of theirs, kept when you allow."
local function requestText(box)
  local gifts = (box.gifts or 0) > 0
    and string.format(" They sent %d of theirs, kept when you allow.", box.gifts) or ""
  if box.chars == 0 then return "You don't share any characters right now, so Allow gives them nothing until you do (Sharing tab, What you share with friends)." .. gifts end
  local what = ns.shareSummary and ns.shareSummary(box.choices) or ""
  return string.format("Allow gives them %s: name, level, class and item level%s.",
    box.chars == 1 and "1 of your characters" or (box.chars .. " of your characters"),
    what == "only the basics" and "" or (", plus " .. what)) .. gifts
end

local function drawRequest(box)
  box.info:SetText(requestText(box))
  ui.setToggle(box.toggle, "Choose what they get", box.open)
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
  choices:SetSize(REQUEST_WIDTH - 32, 162)
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
  for i, key in ipairs(ns.SHARE_CHOICES or {}) do
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
  for _, key in ipairs(ns.SHARE_CHOICES or {}) do items[#items + 1] = { key, CATEGORY_NAMES[key] or key } end
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
    for _, key in ipairs(ns.SHARE_CHOICES or {}) do
      if box.ask[key] then wants[#wants + 1] = key end
    end
    local said = ns.askFriend(box.tag, wants, box.sendMine and box.give or nil)
    box:Hide()
    if box.onSaid then box.onSaid(said) end
  end, "primary")
  go:SetPoint("RIGHT", cancel, "LEFT", -6, 0)
  box:SetHeight(492)
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
    p.chars, p.main = friend.chars or {}, friend.main
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

-- "Level 54 Arms Warrior · Felwood · online, updated 2m ago" under a friend's character (updated:
-- when what they share of it last arrived), "... · last seen 2h ago" offline. Who it is sits above.
local function friendInfo(p)
  local parts, c = {}, p.current
  if c then
    local line = headline(c)
    if line ~= "" then parts[#parts + 1] = line end
    if c.zone and c.zone ~= "" then parts[#parts + 1] = c.zone end
  end
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

-- A shared character's row, like the app's: its portrait, name and race/zone, then class and spec,
-- level and item level in pills, and when it was last seen. Clicking it opens the details.
local function newCharRow(p, width)
  local row = newCard(p, width)
  row.portrait = ui.portrait(row, 28)
  row.portrait:SetPoint("LEFT", row, "LEFT", 16, 0)
  row.nameText:SetPoint("TOPLEFT", row, "TOPLEFT", 18 + ui.ROW_PORTRAIT, -6)
  row.infoText:SetPoint("TOPLEFT", row, "TOPLEFT", 18 + ui.ROW_PORTRAIT, -23)
  row.infoText:SetWidth(width - 380 - ui.ROW_PORTRAIT)
  row.classText = ui.text(row, "GameFontHighlightSmall")
  row.classText:SetPoint("LEFT", row, "RIGHT", -360, 0)
  row.classText:SetWidth(150)
  row.classText:SetWordWrap(false)
  row.level = newPill(row, 36)
  row.level:SetPoint("LEFT", row, "RIGHT", -200, 0)
  row.ilvl = newPill(row, 36)
  row.ilvl:SetPoint("LEFT", row, "RIGHT", -158, 0)
  -- A pin right of the item level on your pinned characters (Pin.tga, white, tinted).
  row.pin = row:CreateTexture(nil, "OVERLAY")
  row.pin:SetSize(14, 14)
  row.pin:SetPoint("LEFT", row, "RIGHT", -114, 0)
  row.pin:SetTexture("Interface\\AddOns\\WoWCharacterTracker\\Pin")
  row.pin:SetVertexColor(0.8, 0.84, 0.9)
  row.pin:Hide()
  row.when = ui.text(row, "GameFontDisableSmall")
  row.when:SetPoint("RIGHT", row, "RIGHT", -12, 0)
  row.when:SetJustifyH("RIGHT")
  return row
end

-- main: c is the main (yours, or the friend's own pick); pinned: one of yours pinned to the top.
local function setCharRow(row, c, main, pinned)
  setCard(row, fullName(c) .. (main and "  |cffFACC15MAIN|r" or ""), c.classFile, charInfo(c))
  row.pin:SetShown(pinned == true)
  ui.setPortrait(row.portrait, c)
  ui.setFactionBadge(row, c.faction)
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
local SECTION_TOP = 90 -- the friend section's header, above their characters

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
  -- What all friends get is on the Sharing tab; this opens it there.
  local yours = ui.button(p, "What you share", 120, function() ns.showShareDefaults() end)
  -- Everyone's professions in one list (RecipeWindow.lua), not one friend at a time.
  local professions = ui.button(p, "Professions", 110, function() ns.openProfessions() end)
  ui.buttonIcon(professions, "Trade_BlackSmithing")
  local hint = ui.text(p, "GameFontDisableSmall", "Click a friend for their characters and nickname, drag one to reorder.")
  hint:SetWidth(width - 420)
  local status = ui.text(p, "GameFontHighlightSmall")
  status:SetWidth(ui.WRAP)

  local function note(s)
    local fs = ui.text(p, "GameFontDisableSmall", s)
    fs:SetWidth(ui.WRAP)
    return fs
  end
  local requestsTitle = ui.heading(p, "Requests")
  local none = note("No friends here yet. Battle.net friends online in this game show up here, and so does everyone you share with.")
  local nothingShared = note("Nothing shared with you yet.")
  -- Online or Offline friends in the grid. Opening the tab clears a pick (selectTab); until one is
  -- clicked again it's Online whenever anyone is online, else Offline (autoView, set by drawView).
  local autoView = "online"
  local function friendsView()
    local picked = (WoWCharacterTrackerDB.window or {}).friendsView
    if picked == "online" or picked == "offline" then return picked end
    return autoView
  end
  local function setFriendsView(key)
    WoWCharacterTrackerDB.window = WoWCharacterTrackerDB.window or {}
    WoWCharacterTrackerDB.window.friendsView = key
    openFriendTag = nil
    drawFriends()
  end
  local view = ui.choice(p, { { "online", "Online" }, { "offline", "Offline" } }, friendsView, setFriendsView, 110)
  -- All friends' gold and played added up, beside Online / Offline (ns.friendTotals).
  local totalsText = ui.text(p, "GameFontHighlightSmall")
  totalsText:SetWordWrap(false)
  totalsText:SetJustifyH("LEFT")
  local emptyView = note("")
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

  -- Ends a drag: the card back in the grid, and the order it got kept when keep is true.
  local function endDrag(card, keep)
    card:StopMovingOrSizing()
    if card.SetUserPlaced then card:SetUserPlaced(false) end
    card:SetScript("OnUpdate", nil)
    card:SetFrameLevel((p:GetFrameLevel() or 1) + 1)
    if keep and dragging and ns.setFriendOrder then ns.setFriendOrder(dragging.order) end
    card.dragged = keep or nil -- a drag cut short isn't followed by the click that ends one
    dragging = nil
    drawFriends()
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
    -- The portrait of the character they play, then who (nickname and BattleTag, beside the online
    -- dot) and that character.
    card.portrait = ui.portrait(card, 40)
    card.portrait:SetPoint("LEFT", card, "LEFT", 16, 0)
    card.whoText = ui.text(card, "GameFontNormalSmall")
    card.whoText:SetPoint("TOPLEFT", card, "TOPLEFT", 16 + ui.CARD_PORTRAIT, -9)
    card.whoText:SetWidth(cardWidth - 40 - ui.CARD_PORTRAIT)
    card.whoText:SetWordWrap(false)
    card.nameText = ui.text(card, "GameFontHighlight")
    card.nameText:SetPoint("TOPLEFT", card, "TOPLEFT", 16 + ui.CARD_PORTRAIT, -24)
    card.nameText:SetWidth(cardWidth - 24 - ui.CARD_PORTRAIT)
    card.nameText:SetWordWrap(false)
    card.infoText = ui.text(card, "GameFontDisableSmall")
    card.infoText:SetPoint("TOPLEFT", card, "TOPLEFT", 16 + ui.CARD_PORTRAIT, -43)
    card.infoText:SetWidth(cardWidth - 24 - ui.CARD_PORTRAIT)
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
      if openFriendTag and self.playing then ns.unfoldFriendPlaying(self.playing, self.label) end
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
    card:SetScript("OnDragStop", function(self) endDrag(self, true) end)
    -- Hidden mid-drag (Esc, another tab): no OnDragStop comes, so the drag ends here, unsaved.
    card:SetScript("OnHide", function(self)
      if dragging and dragging.card == self then endDrag(self, false) end
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
  -- Their gold and played added up (ns.friendTotalsLine).
  section.totals = ui.text(section, "GameFontHighlightSmall")
  section.totals:SetPoint("LEFT", section.pill, "RIGHT", 10, 0)
  section.totals:SetWordWrap(false)
  section.totals:SetJustifyH("LEFT")
  section.b1, section.b2 = cardButton(section, 90), cardButton(section, 90)
  section.b1:SetPoint("TOPRIGHT", section, "TOPRIGHT", -106, -8)
  section.b2:SetPoint("TOPRIGHT", section, "TOPRIGHT", -10, -8)
  local nickLabel = ui.text(section, "GameFontNormalSmall", "Nickname")
  nickLabel:SetPoint("TOPLEFT", section, "TOPLEFT", 12, -42)
  section.nickname = ui.inputBox(section, 220, 32, function(text, tag)
    if tag and ns.setFriendNickname then ns.setFriendNickname(tag, text) end -- redraws the tab
  end)
  section.nickname:SetPoint("LEFT", nickLabel, "LEFT", 66, 0)
  section.theirs = ui.text(section, "GameFontHighlightSmall")
  section.theirs:SetPoint("TOPLEFT", section, "TOPLEFT", 12, -66)

  local function newCharCard() return newCharRow(p, width - 24) end
  local expansion -- the open row's info (newExpansion), built on the first click

  local CHOICES = { { "Allow", "allow", "primary" }, { "Not now", "notnow" }, { "Never", "never" } }

  -- The draw is split in parts that each use few outside locals: WoW's Lua allows a function at
  -- most 60 (upvalues), and drawFriends in one piece needed more. One draw's state goes in `st`.
  local st = {} -- open, openFriend (the details panel's character and friend), unfolded, cards, hiddenCount

  -- Requests at the top; returns the y under them.
  local function drawRequests(y)
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
    return y
  end

  -- The title, Find online friends, the hint and what the last button said; returns the y under them.
  local function drawHeader(y, empty)
    place(friendsTitle, p, 16, y)
    place(find, p, 16 + width - 150, y - 4)
    place(yours, p, 16 + width - 150 - 6 - 120, y - 4)
    place(professions, p, 16 + width - 150 - 6 - 120 - 6 - 110, y - 4)
    hint:SetWidth(width - 420)
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
    if empty then
      place(none, p, 16, y)
      y = y + 20
    end
    return y
  end

  -- Online / Offline with their counts at y, over the grid; returns the friends in the picked one
  -- and the y under it. Revealing a friend's character switches to the view that friend is in.
  local function drawView(y, all, revealTag)
    local online, offline = {}, {}
    local w = saved()
    for _, person in ipairs(all) do
      if person.online then online[#online + 1] = person else offline[#offline + 1] = person end
      if person.tag == revealTag then w.friendsView = person.online and "online" or "offline" end
    end
    autoView = (#online > 0 or #offline == 0) and "online" or "offline"
    emptyView:Hide()
    -- Gold and played over everyone in the grid, online or not, hidden characters left out.
    st.totals = ns.friendTotals and ns.friendTotals(all, isHidden) or nil
    local totals = ns.allFriendsTotalsLine and ns.allFriendsTotalsLine(st.totals) or ""
    if #all == 0 or totals == "" then totalsText:Hide() end
    if #all == 0 then
      view:Hide()
      return all, y
    end
    if totals ~= "" then
      totalsText:SetText(totals)
      totalsText:SetWidth(math.max(100, width - 244))
      place(totalsText, p, 16 + 244, y + 6)
    end
    view.buttons[1]:SetText(string.format("Online (%d)", #online))
    view.buttons[2]:SetText(string.format("Offline (%d)", #offline))
    view.draw() -- a reveal may have switched it
    place(view, p, 16, y)
    y = y + 34
    local list = friendsView() == "offline" and offline or online
    if #list == 0 then
      emptyView:SetText(friendsView() == "offline" and "Everyone's online." or "None of them are online in this game right now.")
      place(emptyView, p, 16, y)
      y = y + 20
    end
    return list, y
  end

  -- The open character's info goes under its row, once.
  local function unfold(c, friend, y)
    if st.unfolded or expandedKey ~= charKey(c) then return y end
    st.unfolded = true
    expansion = expansion or newExpansion(p, width - 24)
    local h = drawExpansion(expansion, c, friend)
    place(expansion, p, 28, y - 2)
    return y - 2 + h + 8
  end

  -- A friend's characters from y: the one being played first when it isn't shared, then what
  -- they share, newest first. Returns the y under them.
  local function drawCharRows(person, label, y)
    local c, chars = person.current, {}
    if c and c.notShared then chars[1] = c end
    for _, shared in ipairs(person.chars) do chars[#chars + 1] = shared end
    if #chars == 0 then
      place(nothingShared, p, 28, y)
      y = y + 22
    end
    for _, ch in ipairs(chars) do
      local hidden = isHidden(ch)
      if hidden then st.hiddenCount = st.hiddenCount + 1 end
      if not hidden or showHidden then
        st.cards = st.cards + 1
        local row = pooled(charCards, st.cards, newCharCard)
        row.char, row.friend = ch, label
        if charKey(ch) == detailsKey then st.open, st.openFriend = ch, label end
        row:SetWidth(width - 24)
        setCharRow(row, ch, person.main ~= nil and ch.guid == person.main)
        if ch.notShared then row.when:SetText("not shared") end
        row:SetAlpha(hidden and 0.5 or 1)
        row:SetFrameLevel((section:GetFrameLevel() or 1) + 2)
        place(row, p, 28, y)
        y = y + CARD + 4
        y = unfold(ch, label, y)
      end
    end
    return y
  end

  -- The open friend's section at y: who, state and buttons, nickname, characters. Returns its height.
  local function drawSection(person, y)
    local label = who(person.label, person.tag)
    section.tag = person.tag
    section.title:SetText(label)
    local state, look, first, second = friendState(person)
    section.stateText:SetText(state)
    section.pill:SetWidth((section.stateText:GetStringWidth() or 60) + 14)
    section.totals:SetText(ns.friendTotalsLine and ns.friendTotalsLine(st.totals, person.tag) or "")
    -- Up to the buttons on the right; the nickname and pill come first.
    section.totals:SetWidth(math.max(60, width - 230 - (section.title:GetStringWidth() or 100) - (section.pill:GetWidth() or 60)))
    ui.box(section.pill, C.bar, look == "good" and C.good or (look == "bad" and C.bad or C.border))
    for _, pair in ipairs({ { section.b1, first }, { section.b2, second } }) do
      local b, what = pair[1], pair[2]
      if what then setButton(b, what[1], what[2] and act(what[2], person.tag), what[3]) else b:Hide() end
    end
    ui.showInput(section.nickname, person.tag, ns.friendNickname and ns.friendNickname(person.tag) or "")
    local theirs = ns.theirShareSummary and ns.theirShareSummary(person.chars)
    section.theirs:SetText(theirs and ("They share with you: " .. theirs .. ".") or "They don't share with you yet.")
    place(section, p, 16, y)
    local h = drawCharRows(person, label, y + SECTION_TOP) - y + 6
    section:SetHeight(h)
    return h
  end

  -- The grid's cards, in list order (order: their tags, for dragging).
  local function drawCards(list, order)
    for k in pairs(cardByTag) do cardByTag[k] = nil end
    for i, person in ipairs(list) do
      local card = pooled(friendCards, i, newFriendCard)
      local c, label = person.current, who(person.label, person.tag)
      card.tag, card.order = person.tag, order
      card.playing, card.label = person.online and c or nil, label -- unfolds when the card opens
      card:SetWidth(cardWidth)
      for _, fs in ipairs({ card.nameText, card.infoText }) do fs:SetWidth(cardWidth - 24 - ui.CARD_PORTRAIT) end
      card.whoText:SetWidth(cardWidth - 40 - ui.CARD_PORTRAIT)
      ui.setPortrait(card.portrait, c)
      cardByTag[person.tag] = card
      card.whoText:SetText(label)
      card.nameText:SetText(c and fullName(c) or "")
      classColor(card.nameText, c and c.classFile)
      card.stripe:SetColorTexture(classRgb(c and c.classFile))
      card.infoText:SetText(friendInfo(person))
      if person.online then card.dot:SetColorTexture(0.3, 0.85, 0.4) else card.dot:SetColorTexture(0.45, 0.45, 0.45) end
      ui.box(card, C.card, person.tag == openFriendTag and C.accent or C.border)
      card:SetAlpha(person.online and 1 or 0.8)
      place(card, p, slot(i))
    end
    hideFrom(friendCards, #list + 1)
  end

  -- Online friends who aren't your friends here yet, found with Find online friends: in their own
  -- lists under the grid, by whether they have the addon. A row: the character, who, their state
  -- and buttons (friendState: Ask and Share, Invite).
  local FOUND = 30
  local FOUND_GROUPS = {
    { key = true, title = "Have the addon" },
    { key = nil, title = "Still checking" },
    { key = false, title = "No addon" },
  }
  local foundRows, foundTitles = {}, {}
  for i, group in ipairs(FOUND_GROUPS) do foundTitles[i] = ui.heading(p, group.title) end

  local function newFoundRow()
    local r = CreateFrame("Frame", nil, p)
    r:SetHeight(FOUND)
    ui.box(r, C.card, C.border)
    r.stripe = r:CreateTexture(nil, "ARTWORK")
    r.stripe:SetSize(3, FOUND - 12)
    r.stripe:SetPoint("LEFT", r, "LEFT", 7, 0)
    r.nameText = ui.text(r, "GameFontHighlight")
    r.nameText:SetPoint("LEFT", r, "LEFT", 16, 0)
    r.nameText:SetWidth(170)
    r.nameText:SetWordWrap(false)
    r.infoText = ui.text(r, "GameFontDisableSmall")
    r.infoText:SetPoint("LEFT", r, "LEFT", 192, 0)
    r.infoText:SetWordWrap(false)
    r.b1, r.b2 = cardButton(r, 70), cardButton(r, 64)
    return r
  end

  -- Returns the y under the lists.
  local function drawFound(y, found)
    local n = 0
    for g, group in ipairs(FOUND_GROUPS) do
      local members = {}
      for _, person in ipairs(found) do
        if person.hasAddon == group.key then members[#members + 1] = person end
      end
      foundTitles[g]:SetText(string.format("%s (%d)", group.title, #members))
      foundTitles[g]:SetShown(#members > 0)
      if #members > 0 then
        place(foundTitles[g], p, 16, y + 6)
        y = y + 28
      end
      for _, person in ipairs(members) do
        n = n + 1
        local r = pooled(foundRows, n, newFoundRow)
        local f = person.online
        r:SetWidth(width)
        r.infoText:SetWidth(width - 192 - 160)
        r.nameText:SetText(f.character or "?")
        classColor(r.nameText, f.classFile)
        r.stripe:SetColorTexture(classRgb(f.classFile))
        local state, look, first, second = friendState(person)
        local bits = { who(person.label, person.tag) }
        if f.level then bits[#bits + 1] = "Level " .. f.level end
        if f.zone and f.zone ~= "" then bits[#bits + 1] = f.zone end
        if not first or look == "bad" then bits[#bits + 1] = state end -- Blocked, Checking...
        r.infoText:SetText(table.concat(bits, "  ·  "))
        for _, pair in ipairs({ { r.b1, first }, { r.b2, second } }) do
          local b, what = pair[1], pair[2]
          if what then setButton(b, what[1], what[2] and act(what[2], person.tag), what[3]) else b:Hide() end
        end
        alignButtons(r, { r.b1, r.b2 })
        place(r, p, 16, y)
        y = y + FOUND + 4
      end
    end
    hideFrom(foundRows, n + 1)
    return y
  end

  -- What's under the grid from y: the hidden link. Also tidies what this draw didn't use.
  local function drawBelow(y)
    hideFrom(charCards, st.cards + 1)
    if not st.unfolded and expansion then expansion:Hide() end
    if details and details:IsShown() then
      if st.open then drawDetails(st.open, st.openFriend) else details:Hide() end
    end
    if sharing and sharing:IsShown() then drawSharing() end
    local link = hiddenLink(p, st.hiddenCount)
    if link then
      place(link, p, 16, y)
      y = y + 30
    end
    p:SetHeight(y + 10)
  end

  -- A draw can set off another (a nickname box saving as it moves to another friend): that one runs
  -- after this one, not inside it.
  local drawing, again = false, false
  local drawNow
  drawFriends = function()
    if dragging then return end -- the drag places the cards; letting go redraws
    if drawing then
      again = true
      return
    end
    drawing = true
    local ok, err
    repeat
      again = false
      ok, err = pcall(drawNow)
    until not (ok and again)
    drawing = false -- also after an error, or the tab would never draw again
    if not ok then error(err, 0) end
  end

  drawNow = function()
    measure()
    section:SetWidth(width)
    -- The grid: your friends here (contacts, whoever shares with you). Everyone else online goes in
    -- the found lists, and while any of them is still being checked the button says so.
    local list, found, checking = {}, {}, 0
    for _, person in ipairs(friendRows(rowsOf(ns.onlineFriends), rowsOf(ns.sharedWithYou))) do
      if person.contact or person.sharesWithYou then
        list[#list + 1] = person
      else
        found[#found + 1] = person
        if person.hasAddon == nil then checking = checking + 1 end
      end
    end
    find:SetText(checking > 0 and string.format("Checking... (%d left)", checking) or "Find online friends")
    local revealTag
    if revealKey then
      for _, person in ipairs(list) do
        for _, c in ipairs(person.chars) do
          if charKey(c) == revealKey then openFriendTag, revealTag = person.tag, person.tag end
        end
      end
      revealKey = nil
    end
    st.open, st.openFriend, st.unfolded, st.cards, st.hiddenCount = nil, nil, false, 0, 0
    local y = drawHeader(drawRequests(14), #list == 0)
    list, y = drawView(y, list, revealTag)

    -- The friend section goes under the open card's row: drawn first, so the rows below move down.
    local order, openIndex, openPerson = {}, nil, nil
    for i, person in ipairs(list) do
      order[i] = person.tag
      if person.tag == openFriendTag then openIndex, openPerson = i, person end
    end
    if openFriendTag and not openPerson then openFriendTag = nil end
    gridTop, sectionRow, sectionHeight = y, nil, 0
    if openPerson then
      sectionRow = math.floor((openIndex - 1) / columns)
      sectionHeight = drawSection(openPerson, gridTop + (sectionRow + 1) * (GRID_CARD + GRID_GAP)) + GRID_GAP
    else
      section:Hide()
    end
    drawCards(list, order)
    drawBelow(drawFound(gridTop + math.ceil(#list / columns) * (GRID_CARD + GRID_GAP) + sectionHeight, found))
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
  -- Your main first, then the pinned ones (ns.ownPicks, set here or in the app), then newest first.
  local main = ns.ownMain()
  for _, c in ipairs(out) do c.isMain, c.isPinned = c.guid == main, ns.isPinned(c.guid) end
  table.sort(out, function(a, b)
    if a.isMain ~= b.isMain then return a.isMain end
    if a.isPinned ~= b.isPinned then return a.isPinned end
    return (a.at or 0) > (b.at or 0)
  end)
  return out
end

local function buildCharacters(p)
  local width = pageWidth() - 30
  local title = ui.heading(p, "Your characters")
  local hint = ui.hint(p, "The characters you've played on this WoW account, your main and pinned ones first, then newest first. Click one for its details, gear and level times, and to make it your main. The app shows all your accounts.", title)
  hint:SetWidth(width)
  local none = ui.text(p, "GameFontDisableSmall", "None saved yet. Characters show up here once you've played them with the addon.")
  local rows = {}
  local expansion -- the open row's info, built on the first click
  local function draw()
    local list = ownChars()
    width = pageWidth() - 30 -- the window can be resized: the rows follow its width
    hint:SetWidth(width)
    local y = 56
    none:SetShown(#list == 0)
    if #list == 0 then place(none, p, 16, y) end
    local open, unfolded
    local shownCount, hiddenCount = 0, 0
    -- Roomy rows (bigger portraits and pills, a line more) while they all fit on the page.
    local fits = 0
    for _, c in ipairs(list) do if showHidden or not isHidden(c) then fits = fits + 1 end end
    local roomy = fits > 0 and y + fits * (ui.CHAR_ROW_ROOMY + 4) + 40 <= ns.pageHeight()
    for _, c in ipairs(list) do
      local hidden = isHidden(c)
      if hidden then hiddenCount = hiddenCount + 1 end
      if not hidden or showHidden then
        shownCount = shownCount + 1
        local row = pooled(rows, shownCount, function() return newCharRow(p, width) end)
        row:SetWidth(width)
        row.char, row.friend = c, nil
        if charKey(c) == detailsKey then open = c end
        setCharRow(row, c, c.isMain, c.isPinned)
        row:SetAlpha(hidden and 0.5 or 1)
        place(row, p, 16, y)
        y = y + ui.sizeCharRow(row, roomy, c) + 4
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
buildShareDefaults = function(p, anchor, x)
  local friendsTitle = ui.heading(p, "What you share with friends", anchor)
  if x then friendsTitle:SetPoint("TOPLEFT", anchor, "BOTTOMLEFT", x, -ui.SECTION_GAP) end
  p.shareTitle = friendsTitle -- the Friends tab's What you share scrolls to it
  local friendsHint = ui.hint(p, "Friends with the addon can ask to see your characters. Nothing is shared until you allow it.", friendsTitle)
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
  local whatTitle = ui.text(p, "GameFontNormalSmall", "What friends get, besides name, level, class and item level")
  whatTitle:SetPoint("TOPLEFT", shareNew, "BOTTOMLEFT", 4, -24)
  local gridLast = ui.checkGrid(p, { { "gear", "Gear" }, { "talents", "Talents" }, { "location", "Location" }, { "professions", "Professions" },
    { "reputations", "Reputations" }, { "highlights", "Highlights" }, { "leveling", "Level times" }, { "gold", "Gold" },
    { "map", "Map position" } }, whatTitle,
    function(key) return ns.shareSettings().defaults[key] == true end,
    function(key, on) ns.editShare(function(s) s.defaults[key] = on end) end)
  local otherFaction = ui.checkbox(p, "Show my map position to the other faction",
    "Off: a friend playing the other faction doesn't see you on the map, even with Map position on.",
    function() return ns.shareSettings().defaults.otherFaction == true end,
    function(on) ns.editShare(function(s) s.defaults.otherFaction = on end) end)
  otherFaction:SetPoint("TOPLEFT", gridLast, "BOTTOMLEFT", 0, -10)
  -- One rate for all friends.
  local rateLabel = ui.text(p, "GameFontHighlight", "Map updates")
  rateLabel:SetPoint("TOPLEFT", otherFaction, "BOTTOMLEFT", 4, -26)
  local rate = ui.choice(p, { { 5, "Live" }, { 60, "Every minute" }, { 300, "Every 5 min" } }, function() return ns.shareSettings().mapRate or 5 end,
    function(n) ns.editShare(function(s) s.mapRate = n end) end, 100)
  rate:SetPoint("LEFT", rateLabel, "LEFT", 110, 0)
  return rateLabel
end

-- App -------------------------------------------------------------------------------------------
-- One line on the app: what it adds without it, where its settings live with it. What it posts is
-- the Sharing tab's table, so it isn't listed again here.
local function appSummary()
  if not ns.hasApp() then
    return "Discord posts, screenshots, the stream overlay and characters from your other WoW accounts need the free Character Tracker app. Get it from the link below (click it, then Ctrl+C)."
  end
  return "Connected. What gets posted to Discord is on the Sharing tab. Changes reach the app on your next /reload or logout, and the newest change wins. The guide and updates are at the link below (click it, then Ctrl+C)."
end

-- The Settings tab, in sections: General, Look, Note box, This character, Advanced and the app.
-- Instant posting and the pattern only do something with the app.
local function buildSettings(p)
  local withApp = ns.hasApp()
  local generalTitle = ui.heading(p, "General")
  -- Minimap.lua's, loaded after this file.
  local minimap = ui.checkbox(p, "Show the minimap button", "Hidden, /wct still opens this window.",
    function() return not ns.minimapButtonShown or ns.minimapButtonShown() end,
    function(on) if ns.setMinimapButtonShown then ns.setMinimapButtonShown(on) end end)
  minimap:SetPoint("TOPLEFT", generalTitle, "BOTTOMLEFT", -2, -6)
  local last, lastGap = minimap, ui.gapAfter(minimap)
  if withApp then
    local live = ui.checkbox(p, "Instant posting", "Tell the app about events right away. Off: they arrive on /reload or logout.",
      function() return WoWCharacterTrackerDB.live ~= false end,
      function(on) WoWCharacterTrackerDB.live = on end)
    live:SetPoint("TOPLEFT", minimap, "BOTTOMLEFT", 0, -18)
    last, lastGap = live, ui.gapAfter(live)
  end

  -- The window's colors (the app's Forever looks, right away) and how far away friends on the map
  -- are (MapPins.lua, an In game setting).
  local lookTitle = ui.heading(p, "Look", last, lastGap)
  local look = ui.choice(p, ui.THEMES, ui.theme, function(key)
    ui.applyTheme(key)
    ui.refresh(tabs[current].page) -- what each page colors as it draws (charts, tabs)
  end, 120)
  look:SetPoint("TOPLEFT", lookTitle, "BOTTOMLEFT", 0, -10)
  local distanceLabel = ui.labeledChoice(p, look, 0, -16, "Map distances", DISTANCE_CHOICES,
    function()
      local unit = ns.setting("distanceUnit")
      return (unit == "meters" or unit == "yards") and unit or "auto"
    end,
    function(key) ns.editSettings(function(s) s.distanceUnit = key end) end)

  local note = buildNote(p, distanceLabel, -(ui.SECTION_GAP + 6))

  local thisTitle = ui.heading(p, "This character", note, -(ui.SECTION_GAP + 4))
  local ignore = ui.checkbox(p, "Ignore this character",
    withApp and "Nothing is recorded, posted, said in chat or shared with friends for it. The app lists it under Ignored."
      or "Nothing is recorded, said in chat or shared with friends for it.",
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

  local advancedTitle = ui.heading(p, "Advanced", ignore, ui.gapAfter(ignore))
  local keepAll = ui.checkbox(p, "Keep all history", "Every loot, duel and battleground stays in the saved file, which keeps growing. Applies on your next /reload.",
    function() return ns.setting("keepAllHistory") == true end,
    function(on) ns.editSettings(function(s) s.keepAllHistory = on end) end)
  keepAll:SetPoint("TOPLEFT", advancedTitle, "BOTTOMLEFT", -2, -6)
  last = keepAll
  if withApp then
    local corners = {}
    for i, c in ipairs(CORNERS) do corners[i] = { c, CORNER_NAMES[c] } end
    local cornerLabel = ui.labeledChoice(p, keepAll, 4, -30, "Pattern corner", corners, currentCorner,
      function(key) ns.editSettings(function(s) s.gridCorner = key end) end, 96)
    local gridLabel = ui.checkbox(p, "Show a small label next to the pattern", nil,
      function() return isOn("gridLabel") end,
      function(on) ns.editSettings(function(s) s.gridLabel = on end) end)
    gridLabel:SetPoint("TOPLEFT", cornerLabel, "BOTTOMLEFT", -6, -10)
    last = gridLabel
  end

  local appTitle = ui.heading(p, "Character Tracker app", last, ui.gapAfter(last))
  local appText = ui.text(p, "GameFontHighlightSmall")
  appText:SetPoint("TOPLEFT", appTitle, "BOTTOMLEFT", 0, -6)
  ui.wrap(appText)
  appText:SetJustifyV("TOP")
  -- The app's page: where to get it, or its guide and updates.
  local appLink = ui.copyBox(p, ns.APP_URL, 380)
  appLink:SetPoint("TOPLEFT", appText, "BOTTOMLEFT", 0, -8)
  ui.onRefresh(p, function() appText:SetText(appSummary()) end)
  -- Tips, never needed: the same Ko-fi link as the app's corner.
  local tipText = ui.text(p, "GameFontDisableSmall", "Like it? Tips are welcome on Ko-fi. Never needed, always appreciated.")
  tipText:SetPoint("TOPLEFT", appLink, "BOTTOMLEFT", 0, -16)
  ui.wrap(tipText)
  local tipLink = ui.copyBox(p, ns.TIP_URL, 380)
  tipLink:SetPoint("TOPLEFT", tipText, "BOTTOMLEFT", 0, -6)
end

-- The window ------------------------------------------------------------------------------------
-- height: how tall the tab's content is; taller than the window gets a scroll frame. fit: the page
-- then takes the height of what's on it once it's laid out (no scrolling into empty space).
local TABS = {
  { key = "home", label = "Home", height = 780, build = function(p) ns.buildHomeTab(p) end }, -- Home.lua
  { key = "characters", label = "Characters", height = 1000, build = buildCharacters }, -- grows with the list
  { key = "friends", label = "Friends", height = 1000, build = buildFriends }, -- grows with its lists
  { key = "sharing", label = "Sharing", height = 1600, build = buildSharingTab, fit = true }, -- a row per moment, then friends
  { key = "settings", label = "Settings", height = 1200, build = buildSettings, fit = true },
}
local BY_KEY = {}
for _, t in ipairs(TABS) do BY_KEY[t.key] = t end
-- Tabs from before 0.39 (a saved last tab, a /wct command) open where their options went.
-- The Note tab's options are a section of Settings since 0.42.4.
local OLD_TABS = { general = "settings", app = "settings", chat = "sharing", highlights = "sharing", note = "settings" }

local function buildTab(tab)
  local holder, page
  if tab.height > AREA_HEIGHT then
    holder = ui.scrollFrame(area)
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
  if key == "friends" then saved().friendsView = nil end -- Online or Offline is picked again (drawView)
  for _, t in ipairs(TABS) do
    local state = tabs[t.key]
    if t.key == key then
      if not state.page then buildTab(t) end
      state.holder:Show()
    elseif state.holder then
      state.holder:Hide()
    end
  end
  ns.railSelect(key) -- Shell.lua
  ui.refresh(tabs[key].page)
  local page = tabs[key].page
  if BY_KEY[key].fit and page ~= tabs[key].holder and C_Timer then
    C_Timer.After(0, function() ui.fitHeight(page) end) -- the next frame, once the anchors are resolved
  end
end

-- The Friends tab's What you share: the Sharing tab, scrolled to what all friends get.
function ns.showShareDefaults()
  selectTab("sharing")
  local state = tabs.sharing
  local title = state.page and state.page.shareTitle
  if not (title and C_Timer and state.holder ~= state.page) then return end
  C_Timer.After(0, function()
    local top, at = state.page:GetTop(), title:GetTop()
    if not (top and at) then return end
    local y = math.max(0, top - at - 12)
    local range = state.holder.GetVerticalScrollRange and state.holder:GetVerticalScrollRange()
    if range then y = math.min(y, range) end
    state.holder:SetVerticalScroll(y)
  end)
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
  ui.rewrap()
end

local function setSizeBounds(maxWidth, maxHeight)
  if window.SetResizeBounds then window:SetResizeBounds(WIDTH, HEIGHT, maxWidth, maxHeight)
  elseif window.SetMinResize then -- older clients
    window:SetMinResize(WIDTH, HEIGHT)
    window:SetMaxResize(maxWidth, maxHeight)
  end
end

-- Sizing by hand rather than StartSizing: the game's sizing could jump the window to its largest
-- size on a plain click of the grip. Now the window follows the cursor only once it has moved a few
-- pixels, so a click changes nothing. Clamped to the screen, a window sized past the screen's edge
-- is pushed back instead, title bar and all off the top, so it grows from its top left and only as
-- far as the screen has room.
local function resizeGrip()
  local grip = CreateFrame("Button", nil, window)
  grip:SetSize(16, 16)
  grip:SetPoint("BOTTOMRIGHT", -3, 3)
  grip:SetNormalTexture("Interface\\ChatFrame\\UI-ChatIM-SizeGrabber-Up")
  grip:SetHighlightTexture("Interface\\ChatFrame\\UI-ChatIM-SizeGrabber-Highlight")
  grip:SetPushedTexture("Interface\\ChatFrame\\UI-ChatIM-SizeGrabber-Down")
  local function finish()
    window:SetScript("OnUpdate", nil)
    savePosition(window) -- the sizing anchored it at its top left
    local s = saved()
    s.width, s.height = math.floor(window:GetWidth() + 0.5), math.floor(window:GetHeight() + 0.5)
    fitPages()
    if current and tabs[current] and tabs[current].page then ui.refresh(tabs[current].page) end
  end
  grip:SetScript("OnMouseDown", function()
    local left, top, screen = window:GetLeft(), window:GetTop(), UIParent:GetRight()
    if not (left and top and screen) then return end
    window:ClearAllPoints()
    window:SetPoint("TOPLEFT", UIParent, "BOTTOMLEFT", left, top)
    local maxW = math.max(WIDTH, math.min(MAX_WIDTH, screen - left))
    local maxH = math.max(HEIGHT, math.min(MAX_HEIGHT, top))
    setSizeBounds(maxW, maxH)
    local scale = window:GetEffectiveScale() or 1
    local x0, y0 = 0, 0
    if GetCursorPosition then x0, y0 = GetCursorPosition() end
    local w0, h0 = window:GetWidth(), window:GetHeight()
    local sizing = { moved = false }
    window:SetScript("OnUpdate", function(self)
      if IsMouseButtonDown and not IsMouseButtonDown("LeftButton") then return finish() end -- let go off the grip
      if not GetCursorPosition then return end
      local x, y = GetCursorPosition()
      local dx, dy = (x - x0) / scale, (y0 - y) / scale
      if not sizing.moved and math.abs(dx) < 4 and math.abs(dy) < 4 then return end
      sizing.moved = true
      self:SetSize(math.max(WIDTH, math.min(maxW, w0 + dx)), math.max(HEIGHT, math.min(maxH, h0 + dy)))
    end)
  end)
  grip:SetScript("OnMouseUp", finish)
  return grip
end

-- The version beside the title: ours, and what's newer when something is (the app's update from
-- Settings.lua, a newer addon a friend's addon has). A click asks online friends' addons for theirs
-- (invisible, Friends.lua's PING), its tooltip says what's known.
local function versionLine()
  local text = ui.version()
  local app, addon = ns.appUpdateVersion and ns.appUpdateVersion(), ns.newerAddonSeen and ns.newerAddonSeen()
  if app then text = text .. "  ·  |cffffd100app v" .. app .. " is out|r" end
  if addon then text = text .. "  ·  |cffffd100addon v" .. addon .. " seen|r" end
  return text
end

local function versionTip(self)
  if not GameTooltip then return end
  GameTooltip:SetOwner(self, "ANCHOR_BOTTOM")
  GameTooltip:SetText("Check for new version", 1, 1, 1)
  GameTooltip:AddLine("Asks your friends online whose addon is newer (they see nothing).", 0.8, 0.8, 0.8, true)
  local app, addon = ns.appUpdateVersion and ns.appUpdateVersion(), ns.newerAddonSeen and ns.newerAddonSeen()
  if app then GameTooltip:AddLine("App " .. app .. " is out: open the app and click Update, it brings the addon too.", 1, 0.82, 0, true) end
  if addon then GameTooltip:AddLine("A friend has addon " .. addon .. ".", 1, 0.82, 0, true) end
  if not (app or addon) then
    GameTooltip:AddLine(ns.hasApp() and "The app checks for updates itself and tells the addon." or "Without the app, newer versions are seen through your friends' addons.", 0.6, 0.6, 0.6, true)
  end
  GameTooltip:Show()
end

local function buildVersion()
  local b = CreateFrame("Button", nil, window)
  b:SetHeight(18)
  local fs = ui.text(b, "GameFontDisableSmall")
  fs:SetPoint("LEFT", b, "LEFT", 2, 0)
  b.line = fs
  b:SetScript("OnEnter", versionTip)
  b:SetScript("OnLeave", function() if GameTooltip then GameTooltip:Hide() end end)
  b:SetScript("OnClick", function(self)
    if ns.findOnlineFriends then ns.findOnlineFriends() end
    self.line:SetText(versionLine() .. "  ·  checking...")
    C_Timer.After(6, function() if ns.versionChanged then ns.versionChanged() end end)
  end)
  function b:Refresh()
    fs:SetText(versionLine())
    self:SetWidth((fs:GetStringWidth() or 60) + 6)
  end
  return b
end

-- Friends.lua or Shared.lua found something newer: the version line says so.
function ns.versionChanged()
  if version and version.Refresh then version:Refresh() end
end

local function build()
  window = CreateFrame("Frame", NAME, UIParent)
  local size = saved()
  window:SetSize(math.max(WIDTH, math.min(MAX_WIDTH, tonumber(size.width) or WIDTH)),
    math.max(HEIGHT, math.min(MAX_HEIGHT, tonumber(size.height) or HEIGHT)))
  window:SetResizable(true)
  setSizeBounds(MAX_WIDTH, MAX_HEIGHT)
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

  -- The tabs down the left and the page's name over it (Shell.lua), the version beside the name.
  for _, t in ipairs(TABS) do tabs[t.key] = {} end
  local title = ns.buildRail(window, TABS, function(key) selectTab(key) end, TITLE_BAR)
  version = buildVersion()
  version:SetPoint("LEFT", title, "RIGHT", 8, -1)

  local close = closeButton(window, function() window:Hide() end)
  local author = ui.text(window, "GameFontDisableSmall", ui.author())
  author:SetPoint("RIGHT", close, "LEFT", -4, 0)

  area = CreateFrame("Frame", nil, window)
  area:SetPoint("TOPLEFT", ns.RAIL_WIDTH + 8, -(TITLE_BAR + TAB_BAR + 4))
  area:SetPoint("BOTTOMRIGHT", -8, 8)
  resizeGrip():SetFrameLevel((area:GetFrameLevel() or 1) + 10)
end

-- Opens the window on a tab (a key of TABS), or the last one open.
function ns.openWindow(tab)
  if not window then build() end
  version:Refresh()
  selectTab(tab or current or saved().tab)
  window:Show()
end

function ns.windowShown() return window ~= nil and window:IsShown() end

function ns.toggleWindow()
  if ns.windowShown() then window:Hide() else ns.openWindow() end
end
