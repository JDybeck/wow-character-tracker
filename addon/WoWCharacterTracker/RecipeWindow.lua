-- The recipe window: one character's recipes in a profession, laid out like the game's profession
-- window (category headers and recipes on the left with a search, the picked recipe on the right with
-- its reagents and the item it makes), or everyone's recipes searched by name to find who can make
-- something. Opened from a character's unfolded row (Window.lua) and /wct recipes. Recipes come from
-- the character's saved or shared `recipes` text (Recipes.lua, FriendData.lua); names, icons and
-- reagents from the game itself.
local ADDON, ns = ...

local ui = ns.ui
local C = ui.COLOR

local NAME = "WoWCharacterTrackerRecipes"
local WIDTH, HEIGHT = 700, 480
local LIST_WIDTH = 270
local ROW = 18
local MAX_REAGENTS = 8
local MAX_KNOWERS = 12

-- What can be looked up by id: names and icons the game may not have cached yet (nil until it has).
local function spellName(id)
  if C_Spell and C_Spell.GetSpellName then return C_Spell.GetSpellName(id) end
  return GetSpellInfo and (GetSpellInfo(id)) or nil
end

local function spellIcon(id)
  if C_Spell and C_Spell.GetSpellTexture then return C_Spell.GetSpellTexture(id) end
  return GetSpellInfo and select(3, GetSpellInfo(id)) or nil
end

local function itemIcon(id)
  local f = (C_Item and C_Item.GetItemIconByID) or GetItemIcon
  if not f then return nil end
  local ok, icon = pcall(f, id)
  return ok and icon or nil
end

-- A recipe's reagents ({ id, count }) and the item it makes (nil for one that makes none), from the
-- game's schematic for it. Empty when the client can't say.
local function schematic(recipeId)
  local ui_ = C_TradeSkillUI
  if not (ui_ and ui_.GetRecipeSchematic) then return {}, nil end
  local ok, s = pcall(ui_.GetRecipeSchematic, recipeId, false)
  if not (ok and type(s) == "table") then return {}, nil end
  local reagents = {}
  for _, slot in ipairs(s.reagentSlotSchematics or {}) do
    local first = slot.reagents and slot.reagents[1]
    local id = first and ns.plain(first.itemID)
    if type(id) == "number" and (slot.reagentType == nil or slot.reagentType == 1) then
      reagents[#reagents + 1] = { id = id, count = ns.plain(slot.quantityRequired) or 1 }
    end
  end
  local made = ns.plain(s.outputItemID)
  return reagents, (type(made) == "number" and made > 0) and made or nil
end

-- A character's skill in a profession from its professions text ("Mining 150/225, ..."): "150/225" or "150".
function ns.professionSkill(professions, name)
  for part in string.gmatch(professions or "", "[^,]+") do
    local n, skill = part:match("^%s*(.-)%s+(%d+[/%d]*)%s*$")
    if n == name then return skill end
  end
  return nil
end

-- Everyone whose recipes are known here: this account's characters and what friends share, as
-- { c = character, owner = friend's label or nil }.
local function everyone()
  local out = {}
  for guid, snap in pairs((WoWCharacterTrackerDB or {}).chars or {}) do
    if not (ns.isIgnored and ns.isIgnored(guid)) and snap.recipes then
      local c = ns.copy(snap)
      c.guid = guid
      out[#out + 1] = { c = c }
    end
  end
  for _, friend in ipairs(ns.sharedWithYou and ns.sharedWithYou() or {}) do
    for _, c in ipairs(friend.chars) do
      if c.recipes then out[#out + 1] = { c = c, owner = friend.label or friend.tag } end
    end
  end
  return out
end

-- Recipes whose name has text in it, across people ({ c, owner }): { { id, name, knowers = { { c,
-- owner, profession } } } } by name. nameOf(id) gives a recipe's name (nil when not known yet).
function ns.findRecipes(people, text, nameOf)
  text = (text or ""):lower()
  if text == "" then return {} end
  local byId, list = {}, {}
  for _, p in ipairs(people) do
    for _, prof in ipairs(ns.friendRecipes(p.c)) do
      for _, g in ipairs(prof.groups) do
        for _, id in ipairs(g.ids) do
          local name = nameOf(id)
          if name and name:lower():find(text, 1, true) then
            local hit = byId[id]
            if not hit then
              hit = { id = id, name = name, knowers = {} }
              byId[id] = hit
              list[#list + 1] = hit
            end
            hit.knowers[#hit.knowers + 1] = { c = p.c, owner = p.owner, profession = prof.profession }
          end
        end
      end
    end
  end
  table.sort(list, function(a, b) return a.name < b.name end)
  return list
end

-- The window and what it shows. book: { c, owner, profession, groups }; or search mode (book nil).
local win
local state = { book = nil, filter = "", search = "", picked = nil, folded = {}, hits = {} }
local rows = {}         -- the left list's pooled rows
local reagentRows = {}  -- the right side's pooled reagent rows
local knowerButtons = {}
local redrawSoon = false
local draw

-- Names and icons the game hasn't cached yet arrive a moment later: one redraw for a batch.
local function later()
  if redrawSoon then return end
  redrawSoon = true
  C_Timer.After(0.3, function()
    redrawSoon = false
    if win and win:IsShown() then draw() end
  end)
end

local function askSpell(id)
  if Spell and Spell.CreateFromSpellID then
    local ok, s = pcall(Spell.CreateFromSpellID, Spell, id)
    if ok and s and s.ContinueOnSpellLoad then s:ContinueOnSpellLoad(later) end
  end
end

local function askItem(id)
  if Item and Item.CreateFromItemID then
    local ok, item = pcall(Item.CreateFromItemID, Item, id)
    if ok and item and item.ContinueOnItemLoad then item:ContinueOnItemLoad(later) end
  end
end

local function nameOf(id)
  local name = spellName(id)
  if not name then askSpell(id) end
  return name
end

local function showTip(self)
  if not GameTooltip then return end
  GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
  if self.itemId then GameTooltip:SetHyperlink("item:" .. self.itemId)
  elseif self.spellId then GameTooltip:SetHyperlink("spell:" .. self.spellId) end
  GameTooltip:Show()
end

local function hideTip() if GameTooltip then GameTooltip:Hide() end end

local function newRow(parent)
  local r = CreateFrame("Button", nil, parent)
  r:SetSize(LIST_WIDTH - 30, ROW)
  r.bg = r:CreateTexture(nil, "BACKGROUND")
  r.bg:SetAllPoints()
  local hover = r:CreateTexture(nil, "HIGHLIGHT")
  ui.fill(hover, C.hover)
  hover:SetAllPoints()
  r.icon = r:CreateTexture(nil, "ARTWORK")
  r.icon:SetSize(14, 14)
  r.text = ui.text(r, "GameFontHighlightSmall")
  r.text:SetWordWrap(false)
  r.sign = ui.text(r, "GameFontNormalSmall")
  r.sign:SetPoint("RIGHT", r, "RIGHT", -6, 0)
  r:SetScript("OnClick", function(self) if self.act then self.act() end end)
  return r
end

-- A header ("Weapon Stones", a click folds it) or a recipe row in the left list.
local function setRow(r, header, text, act, picked, icon)
  r.act = act
  r.text:ClearAllPoints()
  r.icon:SetShown(not header and icon ~= nil)
  if header then
    ui.fill(r.bg, C.request)
    r.text:SetPoint("LEFT", r, "LEFT", 6, 0)
    r.text:SetFontObject("GameFontNormalSmall")
    ui.tint(r.text, C.accent)
  else
    if picked then ui.fill(r.bg, C.selected) else r.bg:SetColorTexture(0, 0, 0, 0) end
    r.icon:SetTexture(icon)
    r.icon:SetPoint("LEFT", r, "LEFT", 14, 0)
    r.text:SetPoint("LEFT", r, "LEFT", 32, 0)
    r.text:SetFontObject("GameFontHighlightSmall")
    r.text:SetTextColor(1, 1, 1)
  end
  r.text:SetWidth(LIST_WIDTH - (header and 52 or 66))
  r.text:SetText(text)
  r.sign:SetShown(header)
end

local function pick(id)
  state.picked = id
  draw()
end

-- The left list for a character's book: its categories in the game's order, filtered by the search.
local function bookLines()
  local lines, filter = {}, state.filter:lower()
  for _, g in ipairs(state.book.groups) do
    local shown = {}
    for _, id in ipairs(g.ids) do
      local name = nameOf(id) or ("Recipe " .. id)
      if filter == "" or name:lower():find(filter, 1, true) then shown[#shown + 1] = { id = id, name = name } end
    end
    if #shown > 0 then
      local key = g.name ~= "" and g.name or "Recipes"
      lines[#lines + 1] = { header = key, folded = state.folded[key] }
      if not state.folded[key] then
        for _, r in ipairs(shown) do lines[#lines + 1] = r end
      end
    end
  end
  return lines
end

local function searchLines()
  local lines = {}
  for _, hit in ipairs(state.hits) do lines[#lines + 1] = { id = hit.id, name = hit.name } end
  return lines
end

local function drawList()
  local lines = state.book and bookLines() or searchLines()
  local y = 0
  for i, line in ipairs(lines) do
    local r = rows[i] or newRow(win.listChild)
    rows[i] = r
    if line.header then
      local key = line.header
      setRow(r, true, key, function()
        state.folded[key] = not state.folded[key] or nil
        draw()
      end)
      r.sign:SetText(line.folded and "+" or "-")
    else
      local id = line.id
      local icon = spellIcon(id)
      setRow(r, false, line.name, function() pick(id) end, state.picked == id, icon)
    end
    r:ClearAllPoints()
    r:SetPoint("TOPLEFT", win.listChild, "TOPLEFT", 0, -y)
    r:Show()
    y = y + ROW + (line.header and 2 or 0)
  end
  for i = #lines + 1, #rows do rows[i]:Hide() end
  win.listChild:SetHeight(math.max(y, 1))
  win.empty:SetShown(#lines == 0)
  win.empty:SetText(state.book and "No recipe like that." or (state.search == "" and "Type a recipe's name to see who knows it." or "Nobody here knows a recipe like that."))
end

local function newReagentRow(parent)
  local r = CreateFrame("Frame", nil, parent)
  r:SetSize(190, 32)
  r:EnableMouse(true)
  r.icon = r:CreateTexture(nil, "ARTWORK")
  r.icon:SetSize(28, 28)
  r.icon:SetPoint("LEFT", r, "LEFT", 0, 0)
  r.count = ui.text(r, "NumberFontNormal")
  r.count:SetPoint("BOTTOMRIGHT", r.icon, "BOTTOMRIGHT", -1, 1)
  r.text = ui.text(r, "GameFontHighlightSmall")
  r.text:SetPoint("LEFT", r, "LEFT", 34, 0)
  r.text:SetWidth(150)
  r:SetScript("OnEnter", showTip)
  r:SetScript("OnLeave", hideTip)
  return r
end

-- The item's name in its quality color, "item 12345" until the game has it.
local function itemText(fs, id)
  local name, _, quality = ns.itemInfo(id)
  if not name then askItem(id) end
  fs:SetText(name or ("item " .. id))
  local color = quality and ITEM_QUALITY_COLORS and ITEM_QUALITY_COLORS[quality]
  if color then fs:SetTextColor(color.r, color.g, color.b) else fs:SetTextColor(1, 1, 1) end
end

-- The picked recipe: its icon and name, reagents, the item it makes, and in search mode who knows it.
local function drawPicked()
  local d = win.detail
  local id = state.picked
  d:SetShown(id ~= nil)
  if not id then return end
  d.recipe.spellId = id
  d.icon:SetTexture(spellIcon(id))
  d.name:SetText(nameOf(id) or ("Recipe " .. id))
  local reagents, made = schematic(id)
  local y = 64
  d.reagentTitle:SetShown(#reagents > 0)
  if #reagents > 0 then
    d.reagentTitle:SetPoint("TOPLEFT", d, "TOPLEFT", 0, -y)
    y = y + 18
  end
  for i = 1, math.min(#reagents, MAX_REAGENTS) do
    local rg = reagents[i]
    local r = reagentRows[i] or newReagentRow(d)
    reagentRows[i] = r
    r.itemId = rg.id
    r.icon:SetTexture(itemIcon(rg.id))
    r.count:SetText(rg.count > 1 and tostring(rg.count) or "")
    itemText(r.text, rg.id)
    r:ClearAllPoints()
    r:SetPoint("TOPLEFT", d, "TOPLEFT", ((i - 1) % 2) * 200, -(y + math.floor((i - 1) / 2) * 36))
    r:Show()
  end
  for i = math.min(#reagents, MAX_REAGENTS) + 1, #reagentRows do reagentRows[i]:Hide() end
  y = y + math.ceil(math.min(#reagents, MAX_REAGENTS) / 2) * 36 + 6
  d.madeTitle:SetShown(made ~= nil)
  d.made:SetShown(made ~= nil)
  if made then
    d.madeTitle:SetPoint("TOPLEFT", d, "TOPLEFT", 0, -y)
    y = y + 18
    d.made.itemId = made
    d.made.icon:SetTexture(itemIcon(made))
    itemText(d.made.text, made)
    local _, _, _, level, minLevel = ns.itemInfo(made)
    local bits = {}
    if level and level > 1 then bits[#bits + 1] = "Item level " .. level end
    if minLevel and minLevel > 1 then bits[#bits + 1] = "Requires level " .. minLevel end
    d.made.sub:SetText(table.concat(bits, ", "))
    d.made:SetPoint("TOPLEFT", d, "TOPLEFT", 0, -y)
    y = y + 40
  end
  d.noInfo:SetShown(#reagents == 0 and not made)
  if #reagents == 0 and not made then
    d.noInfo:SetPoint("TOPLEFT", d, "TOPLEFT", 0, -y)
    y = y + 30
  end
  -- Search mode: who knows it, a click opens their recipes on it.
  local knowers = {}
  if not state.book then
    for _, hit in ipairs(state.hits) do
      if hit.id == id then knowers = hit.knowers end
    end
  end
  d.knowTitle:SetShown(#knowers > 0)
  if #knowers > 0 then
    d.knowTitle:SetPoint("TOPLEFT", d, "TOPLEFT", 0, -y)
    y = y + 20
  end
  for i = 1, math.min(#knowers, MAX_KNOWERS) do
    local k = knowers[i]
    local b = knowerButtons[i] or ui.button(d, "", 190, function(self) if self.act then self.act() end end)
    knowerButtons[i] = b
    local skill = ns.professionSkill(k.c.professions, k.profession)
    b:SetText(ns.fullName(k.c) .. (k.owner and (" (" .. k.owner .. ")") or "") .. (skill and ("  " .. skill) or ""))
    b.act = function() ns.openRecipes(k.c, k.profession, k.owner, id) end
    b:ClearAllPoints()
    b:SetPoint("TOPLEFT", d, "TOPLEFT", ((i - 1) % 2) * 200, -(y + math.floor((i - 1) / 2) * 26))
    b:Show()
  end
  for i = math.min(#knowers, MAX_KNOWERS) + 1, #knowerButtons do knowerButtons[i]:Hide() end
end

local function drawHeader()
  local book = state.book
  if book then
    win.title:SetText(book.profession)
    win.who:SetText(ns.fullName(book.c) .. (book.owner and ("  (" .. book.owner .. ")") or ""))
    win.who:SetTextColor(ui.classRgb(book.c.classFile))
    win.skill:SetText(ns.professionSkill(book.c.professions, book.profession) or "")
    win.searchHint:SetText("Search")
    win.everyone:SetText("Who knows it?")
  else
    win.title:SetText("Find a recipe")
    win.who:SetText("Your characters and what friends share")
    win.who:SetTextColor(0.7, 0.7, 0.7)
    win.skill:SetText("")
    win.searchHint:SetText("Recipe name")
    win.everyone:SetText("Search")
  end
end

draw = function()
  drawHeader()
  if not state.book then state.hits = ns.findRecipes(everyone(), state.search, nameOf) end
  if state.picked == nil then
    local first = state.book and bookLines() or searchLines()
    for _, line in ipairs(first) do
      if line.id then state.picked = line.id break end
    end
  end
  drawList()
  drawPicked()
end

local function build()
  win = CreateFrame("Frame", NAME, UIParent)
  win:SetSize(WIDTH, HEIGHT)
  win:SetPoint("CENTER")
  win:SetFrameStrata("DIALOG")
  win:SetClampedToScreen(true)
  win:EnableMouse(true)
  win:SetMovable(true)
  win:RegisterForDrag("LeftButton")
  win:SetScript("OnDragStart", win.StartMoving)
  win:SetScript("OnDragStop", win.StopMovingOrSizing)
  if UISpecialFrames then table.insert(UISpecialFrames, NAME) end
  ui.box(win, C.window, C.border)

  local close = CreateFrame("Button", nil, win)
  close:SetSize(28, 28)
  close:SetPoint("TOPRIGHT", -4, -4)
  close:SetNormalTexture("Interface\\Buttons\\UI-Panel-MinimizeButton-Up")
  close:SetPushedTexture("Interface\\Buttons\\UI-Panel-MinimizeButton-Down")
  close:SetHighlightTexture("Interface\\Buttons\\UI-Panel-MinimizeButton-Highlight", "ADD")
  close:SetScript("OnClick", function() win:Hide() end)

  win.title = ui.text(win, "GameFontNormalLarge")
  win.title:SetPoint("TOPLEFT", 16, -14)
  ui.tint(win.title, C.accent)
  win.who = ui.text(win, "GameFontHighlight")
  win.who:SetPoint("LEFT", win.title, "RIGHT", 12, 0)
  win.skill = ui.text(win, "GameFontHighlight")
  win.skill:SetPoint("TOPRIGHT", -44, -16)

  -- The search: filters the book, or finds who knows a recipe.
  win.search = ui.inputBox(win, LIST_WIDTH - 20, 40, function() end)
  win.search:SetPoint("TOPLEFT", 16, -44)
  win.searchHint = ui.text(win.search, "GameFontDisableSmall", "Search")
  win.searchHint:SetPoint("LEFT", 8, 0)
  win.search:SetScript("OnTextChanged", function(self, user)
    local text = self:GetText() or ""
    win.searchHint:SetShown(text == "")
    if not user then return end -- set by show(), which draws itself
    if state.book then state.filter = text else state.search = text end
    state.picked = nil
    draw()
  end)
  -- From a book: search everyone for what's typed. In search mode: search now.
  win.everyone = ui.button(win, "Who knows it?", 110, function()
    local text = win.search:GetText() or ""
    ns.openRecipeSearch(text)
  end)
  win.everyone:SetPoint("LEFT", win.search, "RIGHT", 8, 0)

  local list = CreateFrame("ScrollFrame", nil, win, "UIPanelScrollFrameTemplate")
  list:SetPoint("TOPLEFT", 16, -74)
  list:SetSize(LIST_WIDTH - 26, HEIGHT - 90)
  win.listChild = CreateFrame("Frame", nil, list)
  win.listChild:SetSize(LIST_WIDTH - 30, 1)
  list:SetScrollChild(win.listChild)
  win.empty = ui.text(win, "GameFontDisableSmall")
  win.empty:SetPoint("TOPLEFT", 20, -80)
  win.empty:SetWidth(LIST_WIDTH - 40)

  local split = win:CreateTexture(nil, "ARTWORK")
  ui.fill(split, C.border)
  split:SetWidth(1)
  split:SetPoint("TOPLEFT", LIST_WIDTH + 14, -74)
  split:SetPoint("BOTTOMLEFT", LIST_WIDTH + 14, 16)

  local d = CreateFrame("Frame", nil, win)
  d:SetPoint("TOPLEFT", LIST_WIDTH + 34, -74)
  d:SetPoint("BOTTOMRIGHT", -20, 16)
  win.detail = d
  d.recipe = CreateFrame("Frame", nil, d)
  d.recipe:SetSize(400, 48)
  d.recipe:SetPoint("TOPLEFT", 0, 0)
  d.recipe:EnableMouse(true)
  d.recipe:SetScript("OnEnter", showTip)
  d.recipe:SetScript("OnLeave", hideTip)
  d.icon = d.recipe:CreateTexture(nil, "ARTWORK")
  d.icon:SetSize(44, 44)
  d.icon:SetPoint("LEFT", 0, 0)
  d.name = ui.text(d.recipe, "GameFontHighlightLarge")
  d.name:SetPoint("LEFT", d.icon, "RIGHT", 12, 0)
  d.name:SetWidth(320)
  d.reagentTitle = ui.text(d, "GameFontNormalSmall", "Reagents:")
  ui.tint(d.reagentTitle, C.accent)
  d.madeTitle = ui.text(d, "GameFontNormalSmall", "Makes:")
  ui.tint(d.madeTitle, C.accent)
  d.made = CreateFrame("Frame", nil, d)
  d.made:SetSize(400, 36)
  d.made:EnableMouse(true)
  d.made:SetScript("OnEnter", showTip)
  d.made:SetScript("OnLeave", hideTip)
  d.made.icon = d.made:CreateTexture(nil, "ARTWORK")
  d.made.icon:SetSize(32, 32)
  d.made.icon:SetPoint("LEFT", 0, 0)
  d.made.text = ui.text(d.made, "GameFontHighlight")
  d.made.text:SetPoint("TOPLEFT", 40, -2)
  d.made.sub = ui.text(d.made, "GameFontDisableSmall")
  d.made.sub:SetPoint("TOPLEFT", 40, -20)
  d.noInfo = ui.text(d, "GameFontDisableSmall", "Hover the name for the game's own tooltip.")
  d.knowTitle = ui.text(d, "GameFontNormalSmall", "Known by:")
  ui.tint(d.knowTitle, C.accent)
  win:Hide()
end

local function show(book, text, picked)
  if not win then build() end
  state.book = book
  state.filter, state.search = book and "" or (text or ""), book and "" or (text or "")
  state.picked = picked
  state.folded = {}
  win.search:SetText(book and "" or (text or ""))
  win:Show()
  draw()
end

-- One character's recipes in a profession (c: a snapshot or a friend's shared character with
-- `recipes`), owner: the friend's label, nil for your own. recipeId: the one to pick first.
function ns.openRecipes(c, profession, owner, recipeId)
  for _, prof in ipairs(ns.friendRecipes(c)) do
    if prof.profession == profession then
      return show({ c = c, owner = owner, profession = profession, groups = prof.groups }, nil, recipeId)
    end
  end
end

-- Everyone's recipes searched by name (empty: the window waits for a name).
function ns.openRecipeSearch(text)
  show(nil, text)
end

-- The professions with recipes a character has, for the buttons that open them: { { name, count } }.
function ns.recipeProfessions(c)
  local out = {}
  for _, prof in ipairs(ns.friendRecipes(c)) do
    local n = 0
    for _, g in ipairs(prof.groups) do n = n + #g.ids end
    out[#out + 1] = { name = prof.profession, count = n }
  end
  return out
end
