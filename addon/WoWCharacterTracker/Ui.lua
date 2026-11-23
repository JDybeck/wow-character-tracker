-- The controls the addon window and the Options page are built from. Each control that shows a
-- setting registers a refresher with the page it's on; ns.ui.refresh(page) runs them.
local ADDON, ns = ...

local ui = {}
ns.ui = ui

-- How wide long lines get before they wrap (the window's content area, with room to spare). Hints
-- follow the window when it's resized (ui.rewrap).
ui.WRAP = 640
-- The space above a section's heading: one gap everywhere, so the pages read alike.
ui.SECTION_GAP = 30
local wrapped = setmetatable({}, { __mode = "k" }) -- [font string] = how much narrower than the page

-- Text that wraps at the page's width less inset (and keeps doing so as the window is resized).
function ui.wrap(fs, inset)
  wrapped[fs] = inset or 0
  fs:SetWidth(math.max(200, (ns.pageWidth and ns.pageWidth() or ui.WRAP + 70) - 70 - (inset or 0)))
end

-- The window was resized: wrapping text takes the new width.
function ui.rewrap()
  for fs, inset in pairs(wrapped) do ui.wrap(fs, inset) end
end

-- The app's look: warm dark panels, thin borders, an amber accent. { r, g, b, a }
local COLOR = {
  window = { 0.078, 0.063, 0.047, 0.97 },
  bar = { 0.063, 0.05, 0.039, 1 },
  card = { 0.102, 0.078, 0.063, 1 },
  border = { 0.227, 0.184, 0.141, 1 },
  accent = { 0.94, 0.63, 0.13, 1 },
  primary = { 0.71, 0.33, 0.04, 1 },
  selected = { 0.165, 0.114, 0.055, 1 },
  request = { 0.141, 0.098, 0.047, 1 },
  requestBorder = { 0.42, 0.29, 0.08, 1 },
  good = { 0.18, 0.42, 0.18, 1 },
  bad = { 0.45, 0.2, 0.16, 1 },
  hover = { 1, 1, 1, 0.05 },
}
ui.COLOR = COLOR

-- Themes: the app's Forever looks (Ui/Theme.cs, the same hex values). Picking one copies its values
-- into COLOR (the tables stay the same, so everything that holds one keeps working) and repaints
-- what's on screen. Kept in WoWCharacterTrackerDB.window.theme. Good, bad and hover stay in every theme.
local function hex(s, alpha)
  return { tonumber(s:sub(2, 3), 16) / 255, tonumber(s:sub(4, 5), 16) / 255, tonumber(s:sub(6, 7), 16) / 255, alpha or 1 }
end

-- From the app's palette: background, card, card header, line, accent, accent text, second deep.
local function look(bg, card, header, line, accent, accentText, deep)
  return { window = hex(card, 0.97), bar = hex(bg), card = hex(header), border = hex(line), accent = hex(accentText),
    primary = hex(accent), selected = hex(line), request = hex(header), requestBorder = hex(deep) }
end

local THEMES = {
  classic = look("#080604", "#120E08", "#19130B", "#2B2216", "#B45309", "#FCD34D", "#713F12"),
  wisp = look("#03080D", "#07121B", "#0B1A26", "#15303F", "#0E7490", "#67E8F9", "#1E3A8A"),
  emerald = look("#030A06", "#08140C", "#0C1D12", "#17331F", "#15803D", "#86EFAC", "#134E4A"),
  ember = look("#0A0503", "#140A06", "#1C0F08", "#35190E", "#C2410C", "#FDBA74", "#7F1D1D"),
}
ui.THEMES = { { "classic", "Classic gold" }, { "wisp", "Forever wisp" }, { "emerald", "Emerald Dream" }, { "ember", "Blackrock ember" } }
-- What was painted with which colors, so a theme change can paint it again (weak: frames that go away go).
local painted = setmetatable({}, { __mode = "k" }) -- [texture] = color
local tinted = setmetatable({}, { __mode = "k" })  -- [font string] = color

function ui.theme()
  local key = WoWCharacterTrackerDB and WoWCharacterTrackerDB.window and WoWCharacterTrackerDB.window.theme
  return THEMES[key] and key or "classic"
end

-- Applies the saved theme (save: pick key first). Everything painted so far changes with it.
function ui.applyTheme(key)
  if key and THEMES[key] and WoWCharacterTrackerDB then
    WoWCharacterTrackerDB.window = WoWCharacterTrackerDB.window or {}
    WoWCharacterTrackerDB.window.theme = key
  end
  for name, values in pairs(THEMES[ui.theme()]) do
    for i, v in ipairs(values) do COLOR[name][i] = v end
  end
  for texture, c in pairs(painted) do texture:SetColorTexture(c[1], c[2], c[3], c[4] or 1) end
  for fs, c in pairs(tinted) do fs:SetTextColor(c[1], c[2], c[3]) end
end

-- A font string in one of the theme's colors (headings in the accent).
function ui.tint(fs, c)
  tinted[fs] = c
  fs:SetTextColor(c[1], c[2], c[3])
end

-- A class's color as r, g, b (white when unknown).
function ui.classRgb(classFile)
  local c = RAID_CLASS_COLORS and classFile and RAID_CLASS_COLORS[classFile]
  if c then return c.r, c.g, c.b end
  return 1, 1, 1
end

-- "12g 34s 56c" from copper.
function ui.gold(copper)
  copper = math.floor(copper)
  return string.format("%dg %ds %dc", math.floor(copper / 10000), math.floor(copper / 100) % 100, copper % 100)
end

function ui.fill(texture, c)
  painted[texture] = c
  texture:SetColorTexture(c[1], c[2], c[3], c[4] or 1)
end

local SIDES = { "TOP", "BOTTOM", "LEFT", "RIGHT" }

-- A flat background and, with border, a 1 px edge, from plain textures (the same on every client).
-- Calling it again on the same frame recolors it.
function ui.box(frame, bg, border)
  frame.boxBg = frame.boxBg or frame:CreateTexture(nil, "BACKGROUND")
  ui.fill(frame.boxBg, bg)
  frame.boxBg:SetAllPoints()
  if not border then return end
  frame.boxEdges = frame.boxEdges or {}
  for i, side in ipairs(SIDES) do
    local t = frame.boxEdges[i]
    if not t then
      t = frame:CreateTexture(nil, "BORDER")
      frame.boxEdges[i] = t
      if i <= 2 then
        t:SetPoint(side .. "LEFT")
        t:SetPoint(side .. "RIGHT")
        t:SetHeight(1)
      else
        t:SetPoint("TOP" .. side)
        t:SetPoint("BOTTOM" .. side)
        t:SetWidth(1)
      end
    end
    ui.fill(t, border)
  end
end

local refreshers = setmetatable({}, { __mode = "k" }) -- [page] = { fn, ... }

function ui.onRefresh(parent, fn)
  local list = refreshers[parent]
  if not list then
    list = {}
    refreshers[parent] = list
  end
  list[#list + 1] = fn
end

function ui.refresh(parent)
  for _, fn in ipairs(refreshers[parent] or {}) do fn() end
end

-- Smooth scrolling: every scroll frame in the addon moves the same distance per wheel notch and
-- glides there instead of jumping (Blizzard's template jumps half the frame's height per notch).
-- Notches given while it glides add up, so a fast spin goes further. Anything else that moves the
-- frame (the scroll bar, its arrows, code) stops the glide where it is.
ui.SCROLL_STEP = 48 -- pixels per wheel notch
local GLIDE = 16 -- how fast it catches up, per second (about a sixth of a second to settle)

local function scrollRange(frame)
  return math.max(0, frame:GetVerticalScrollRange() or 0)
end

-- Frames gliding now, moved each frame by one driver (the template's own scripts stay untouched).
local gliding = {}
local driver

-- One step of frame's glide; true once it has arrived (or something else moved it).
local function glide(frame, elapsed)
  local target = math.max(0, math.min(frame.wctTarget or 0, scrollRange(frame)))
  local now = frame:GetVerticalScroll() or 0
  if math.abs(now - (frame.wctSet or now)) > 1 then return true end -- moved by something else
  -- At least a pixel a frame: the scroll bar can round the offset, which would stall a smaller step.
  local diff = target - now
  local step = math.max(1, math.abs(diff) * math.min(1, elapsed * GLIDE))
  local y = math.abs(diff) <= step and target or now + (diff > 0 and step or -step)
  frame.wctSet = y
  frame:SetVerticalScroll(y)
  return y == target
end

local function stopGlide(frame)
  gliding[frame], frame.wctTarget, frame.wctSet = nil, nil, nil
end

local function onDriverUpdate(self, elapsed)
  for frame in pairs(gliding) do
    if not frame:IsVisible() or glide(frame, elapsed or 0) then stopGlide(frame) end
  end
  if not next(gliding) then self:Hide() end
end

-- Scrolls frame by delta wheel notches (1 up, -1 down), gliding. False when it can't move that
-- way (at its end, or nothing to scroll), so a nested list can hand the wheel on.
function ui.scrollBy(frame, delta)
  local now = frame:GetVerticalScroll() or 0
  local from = gliding[frame] and frame.wctTarget or now
  local target = math.max(0, math.min(from - delta * ui.SCROLL_STEP, scrollRange(frame)))
  if math.abs(target - from) < 0.5 then return gliding[frame] ~= nil end
  if not gliding[frame] then frame.wctSet = now end
  frame.wctTarget = target
  gliding[frame] = true
  if not driver then
    driver = CreateFrame("Frame")
    driver:SetScript("OnUpdate", onDriverUpdate)
  end
  driver:Show()
  return true
end

-- Jumps frame to y (0: the top) at once, stopping any glide.
function ui.scrollTo(frame, y)
  stopGlide(frame)
  if frame.SetVerticalScroll then frame:SetVerticalScroll(math.max(0, math.min(y or 0, scrollRange(frame)))) end
end

-- A UIPanelScrollFrameTemplate scroll frame that scrolls smoothly (see above).
function ui.scrollFrame(parent)
  local frame = CreateFrame("ScrollFrame", nil, parent, "UIPanelScrollFrameTemplate")
  frame:EnableMouseWheel(true)
  frame:SetScript("OnMouseWheel", function(self, delta) ui.scrollBy(self, delta) end)
  return frame
end

-- A field from the .toc (## Version, ## Author), or "" when the client can't say.
local function tocField(field)
  local metadata = (C_AddOns and C_AddOns.GetAddOnMetadata) or GetAddOnMetadata
  return metadata and tostring(metadata(ADDON, field) or "") or ""
end

-- "v0.33.2", or "".
function ui.version()
  local v = tocField("Version")
  return v ~= "" and ("v" .. v) or ""
end

function ui.author()
  local name = tocField("Author")
  return name ~= "" and ("by " .. name) or ""
end

-- The game's own tooltip for an item, next to owner: item is an id (with enchantId, if known) or a
-- whole "item:..." link from chat. anchor: the game's ANCHOR_ name, ANCHOR_RIGHT by default.
function ui.itemTip(owner, item, enchantId, anchor)
  if not (GameTooltip and item) then return end
  GameTooltip:SetOwner(owner, anchor or "ANCHOR_RIGHT")
  GameTooltip:SetHyperlink(type(item) == "string" and item or ("item:" .. item .. ":" .. (enchantId or "")))
  GameTooltip:Show()
end

-- Item links in chat show that tooltip on hover too (normally it takes a click). Hooked, never
-- replaced, so the chat frames' own handlers run as before.
function ui.hookChatItemTips()
  if ui.chatTipsHooked then return end
  ui.chatTipsHooked = true
  for i = 1, (NUM_CHAT_WINDOWS or 0) do
    local frame = _G["ChatFrame" .. i]
    if frame and frame.HookScript then
      frame:HookScript("OnHyperlinkEnter", function(self, link)
        if type(link) == "string" and link:match("^item:") then ui.itemTip(self, link, nil, "ANCHOR_CURSOR") end
      end)
      frame:HookScript("OnHyperlinkLeave", function() if GameTooltip then GameTooltip:Hide() end end)
    end
  end
end

function ui.text(parent, template, s)
  local fs = parent:CreateFontString(nil, "ARTWORK", template)
  fs:SetJustifyH("LEFT")
  fs:SetText(s or "")
  return fs
end

function ui.checkbox(parent, label, tip, get, set)
  local cb = CreateFrame("CheckButton", nil, parent, "UICheckButtonTemplate")
  cb:SetSize(26, 26)
  local l = ui.text(cb, "GameFontHighlight", label)
  l:SetPoint("LEFT", cb, "RIGHT", 4, 1)
  if tip then
    local t = ui.text(cb, "GameFontDisableSmall", tip)
    t:SetPoint("TOPLEFT", l, "BOTTOMLEFT", 0, -2)
    ui.wrap(t, 40)
    cb.tip = t
  end
  cb:SetScript("OnClick", function(self) set(self:GetChecked() and true or false) end)
  ui.onRefresh(parent, function() cb:SetChecked(get()) end)
  return cb
end

-- A one-line box holding text to copy (a link: WoW can't open one). A click selects it all for
-- Ctrl+C; typing can't change it; Esc or Enter lets go.
function ui.copyBox(parent, text, width)
  local box = CreateFrame("EditBox", nil, parent)
  box:SetSize(width, 22)
  box:SetAutoFocus(false)
  box:SetFontObject("GameFontHighlightSmall")
  box:SetTextInsets(6, 6, 0, 0)
  ui.box(box, COLOR.bar, COLOR.border)
  box:SetText(text)
  box:SetCursorPosition(0)
  box:SetScript("OnEditFocusGained", function(self) self:HighlightText() end)
  box:SetScript("OnMouseUp", function(self) self:HighlightText() end)
  box:SetScript("OnTextChanged", function(self, user)
    if user then
      self:SetText(text)
      self:HighlightText()
    end
  end)
  box:SetScript("OnEscapePressed", function(self) self:ClearFocus() end)
  box:SetScript("OnEnterPressed", function(self) self:ClearFocus() end)
  return box
end

-- A one-line box to type in. Enter or leaving it saves: save(text, key) gets what was typed and the
-- box.key it had when typing started (what it's for, so a box reused for another friend saves to the
-- one it was typed for); Esc puts back what it had. ui.showInput sets what it shows from outside.
function ui.inputBox(parent, width, maxLetters, save)
  local box = CreateFrame("EditBox", nil, parent)
  box:SetSize(width, 22)
  box:SetAutoFocus(false)
  box:SetMaxLetters(maxLetters)
  box:SetFontObject("GameFontHighlightSmall")
  box:SetTextInsets(6, 6, 0, 0)
  ui.box(box, COLOR.bar, COLOR.border)
  box.saved = ""
  local function commit(self)
    local text = self:GetText() or ""
    if text ~= self.saved then
      self.saved = text
      save(text, self.editing)
    end
  end
  box:SetScript("OnEnterPressed", function(self) self:ClearFocus() end)
  box:SetScript("OnEditFocusGained", function(self) self.editing = self.key end)
  box:SetScript("OnEditFocusLost", commit)
  box:SetScript("OnEscapePressed", function(self)
    self:SetText(self.saved)
    self:ClearFocus()
  end)
  return box
end

-- Shows text in an inputBox for key. Typing in it for another key is saved to that one first
-- (leaving it saves); typing for this key is left alone.
function ui.showInput(box, key, text)
  if box:HasFocus() then
    if box.editing == key then return end
    box:ClearFocus()
  end
  box.key, box.saved = key, text or ""
  box:SetText(box.saved)
end

-- "primary": filled amber, for the one thing to do; "selected": a picked tab; else quiet and outlined.
function ui.styleButton(b, style)
  if style == "primary" then ui.box(b, COLOR.primary, COLOR.accent)
  elseif style == "selected" then ui.box(b, COLOR.selected, COLOR.accent)
  else ui.box(b, COLOR.card, COLOR.border) end
  b:SetNormalFontObject(style == "selected" and "GameFontNormalSmall" or "GameFontHighlightSmall")
end

function ui.button(parent, label, width, onClick, style)
  local b = CreateFrame("Button", nil, parent)
  b:SetSize(width, 22)
  b:SetFontString(b:CreateFontString(nil, "ARTWORK", "GameFontHighlightSmall"))
  b:SetDisabledFontObject("GameFontDisableSmall")
  local hover = b:CreateTexture(nil, "HIGHLIGHT")
  ui.fill(hover, COLOR.hover)
  hover:SetAllPoints()
  ui.styleButton(b, style)
  b:SetText(label)
  b:SetScript("OnClick", onClick)
  return b
end

-- A row of buttons where one is picked (presets, reload reminders). items: { { key, label }, ... };
-- get() says the picked key (a key not among them picks none), set(key) picks one. Returns the row;
-- row.buttons[i] is item i's button, for labels that change (counts); row.draw() shows a pick made elsewhere.
function ui.choice(parent, items, get, set, width)
  local row = CreateFrame("Frame", nil, parent)
  local w = width or 104
  row:SetSize(#items * (w + 6), 24)
  local buttons = {}
  local function draw()
    local now = get()
    for _, b in ipairs(buttons) do ui.styleButton(b, b.key == now and "selected" or nil) end
  end
  for i, item in ipairs(items) do
    local b = ui.button(row, item[2], w, function()
      set(item[1])
      draw()
    end)
    b.key = item[1]
    b:SetHeight(24)
    b:SetPoint("LEFT", row, "LEFT", (i - 1) * (w + 6), 0)
    buttons[i] = b
  end
  row.buttons, row.draw = buttons, draw
  ui.onRefresh(parent, draw)
  return row
end

-- A plain slider from 0 to 100 in steps of 5 (the game's slider templates differ between clients).
function ui.slider(parent, label, get, set)
  local s = CreateFrame("Slider", nil, parent)
  s:SetOrientation("HORIZONTAL")
  s:SetSize(220, 17)
  s:SetMinMaxValues(0, 100)
  s:SetValueStep(5)
  if s.SetObeyStepOnDrag then s:SetObeyStepOnDrag(true) end
  s:EnableMouse(true)
  s:SetThumbTexture("Interface\\Buttons\\UI-SliderBar-Button-Horizontal")
  local track = s:CreateTexture(nil, "BACKGROUND")
  track:SetColorTexture(1, 1, 1, 0.15)
  track:SetHeight(6)
  track:SetPoint("LEFT")
  track:SetPoint("RIGHT")
  local l = ui.text(s, "GameFontHighlight", label)
  l:SetPoint("BOTTOMLEFT", s, "TOPLEFT", 0, 4)
  local value = ui.text(s, "GameFontHighlightSmall")
  value:SetPoint("LEFT", s, "RIGHT", 10, 0)
  s:SetScript("OnValueChanged", function(_, v)
    v = math.floor(v / 5 + 0.5) * 5
    value:SetText(v .. "%")
    set(v)
  end)
  ui.onRefresh(parent, function() s:SetValue(get()) end)
  return s
end

-- A small level times chart: a bar per level (the last `count`), the slowest in the
-- accent color, the time on hover. chart.draw(levels) takes { level, seconds } oldest first.
local MUTED = { 0.55, 0.5, 0.45, 0.6 }

local function showBarTip(self)
  if not (GameTooltip and self.text) then return end
  GameTooltip:SetOwner(self, "ANCHOR_TOP")
  GameTooltip:SetText(self.text, 1, 1, 1)
  GameTooltip:Show()
end
local function hideBarTip() if GameTooltip then GameTooltip:Hide() end end

function ui.levelChart(parent, count, height, step)
  local chart = CreateFrame("Frame", nil, parent)
  chart:SetSize(count * step, height)
  local base = chart:CreateTexture(nil, "BACKGROUND")
  ui.fill(base, COLOR.border)
  base:SetHeight(1)
  base:SetPoint("BOTTOMLEFT")
  base:SetPoint("BOTTOMRIGHT")
  local bars = {}
  for i = 1, count do
    local b = CreateFrame("Frame", nil, chart)
    b:SetWidth(step - 3)
    b:SetPoint("BOTTOMLEFT", chart, "BOTTOMLEFT", (i - 1) * step, 1)
    b:EnableMouse(true)
    b.fill = b:CreateTexture(nil, "ARTWORK")
    b.fill:SetAllPoints()
    b:SetScript("OnEnter", showBarTip)
    b:SetScript("OnLeave", hideBarTip)
    bars[i] = b
  end
  -- now: { level, seconds } for the level being played (time so far), drawn as a paler last bar.
  function chart.draw(levels, now)
    local shown = {}
    local room = now and count - 1 or count
    for i = math.max(1, #levels - room + 1), #levels do shown[#shown + 1] = levels[i] end
    local summary = ns.levelSummary(shown)
    local slowest = math.max(summary and summary.slowest.seconds or 1, now and now.seconds or 1)
    if now then shown[#shown + 1] = now end
    for i, b in ipairs(bars) do
      local l = shown[i]
      if l then
        b:SetHeight(math.max(2, (height - 2) * l.seconds / slowest))
        local marked = l == now or (summary ~= nil and l == summary.slowest)
        local c = marked and COLOR.accent or MUTED
        b.fill:SetColorTexture(c[1], c[2], c[3], l == now and 0.35 or (c[4] or 1))
        b.text = l == now and string.format("%d to %d so far: %s", l.level - 1, l.level, ns.formatDuration(l.seconds))
          or string.format("%d to %d: %s", l.level - 1, l.level, ns.formatDuration(l.seconds))
        b:Show()
      else
        b:Hide()
      end
    end
  end
  return chart
end

-- Under anchor, or at the top of the page without one.
function ui.heading(parent, s, anchor, gap)
  local h = ui.text(parent, "GameFontNormal", s)
  ui.tint(h, COLOR.accent)
  if anchor then h:SetPoint("TOPLEFT", anchor, "BOTTOMLEFT", 2, gap or -ui.SECTION_GAP)
  else h:SetPoint("TOPLEFT", parent, "TOPLEFT", 16, -14) end
  return h
end

function ui.hint(parent, s, anchor)
  local h = ui.text(parent, "GameFontDisableSmall", s)
  h:SetPoint("TOPLEFT", anchor, "BOTTOMLEFT", 0, -4)
  ui.wrap(h)
  return h
end

-- Checkboxes in two columns, under anchor. get(key) and set(key, on). Returns the last row's box.
function ui.checkGrid(parent, items, anchor, get, set)
  local lastLeft, last = anchor, anchor
  for i, item in ipairs(items) do
    local key = item[1]
    local cb = ui.checkbox(parent, item[2], nil, function() return get(key) end, function(on) set(key, on) end)
    if i % 2 == 1 then
      cb:SetPoint("TOPLEFT", lastLeft, "BOTTOMLEFT", i == 1 and -2 or 0, i == 1 and -6 or -2)
      lastLeft = cb
    else
      cb:SetPoint("LEFT", lastLeft, "LEFT", 270, 0)
    end
    last = lastLeft
  end
  return last
end

-- Checkboxes in a table under anchor: a row per item (its name on the left), a column per entry of
-- columns ({ key, title }). get(column, key) and set(column, key, on). Returns the last row's name.
-- opts (optional): first (the names' width), width (each column's), has(column, key) false = a dash
-- instead of a box, for a place that moment can't go.
function ui.checkTable(parent, items, columns, anchor, get, set, opts)
  opts = opts or {}
  local FIRST, WIDTH = opts.first or 230, opts.width or 110
  local header = ui.text(parent, "GameFontNormalSmall", opts.title or "What")
  header:SetPoint("TOPLEFT", anchor, "BOTTOMLEFT", 0, -10)
  for c, column in ipairs(columns) do
    local title = ui.text(parent, "GameFontNormalSmall", column[2])
    title:SetPoint("LEFT", header, "LEFT", FIRST + (c - 1) * WIDTH, 0)
  end
  local last = header
  for _, item in ipairs(items) do
    local name = ui.text(parent, "GameFontHighlight", item[2])
    name:SetPoint("TOPLEFT", last, "BOTTOMLEFT", 0, -14)
    for c, column in ipairs(columns) do
      local x = FIRST + (c - 1) * WIDTH
      if opts.has and not opts.has(column[1], item[1]) then
        local dash = ui.text(parent, "GameFontDisable", "-")
        dash:SetPoint("LEFT", name, "LEFT", x + 6, 0)
      else
        local cb = ui.checkbox(parent, "", nil, function() return get(column[1], item[1]) end, function(on) set(column[1], item[1], on) end)
        cb:SetPoint("LEFT", name, "LEFT", x - 4, 0)
      end
    end
    last = name
  end
  return last
end

-- A label with a row of choices beside it, under anchor (x: how far in from anchor's left).
function ui.labeledChoice(p, anchor, x, gap, label, items, get, set, width)
  local text = ui.text(p, "GameFontHighlight", label)
  text:SetPoint("TOPLEFT", anchor, "BOTTOMLEFT", x, gap)
  local row = ui.choice(p, items, get, set, width)
  row:SetPoint("LEFT", text, "LEFT", 130, 0)
  return text, row
end

-- The gap above a heading under a checkbox: its tip line takes room below the box.
function ui.gapAfter(cb) return cb.tip and -(ui.SECTION_GAP + 14) or nil end

-- How tall a fit page's content is: from its top to the lowest thing shown on it, plus a margin.
function ui.fitHeight(page)
  local top = page:GetTop()
  if not top then return end -- not laid out yet
  local lowest = top
  local function lower(r)
    if r:IsShown() then
      local bottom = r:GetBottom()
      if bottom and bottom < lowest then lowest = bottom end
    end
  end
  for _, r in ipairs({ page:GetRegions() }) do lower(r) end
  for _, r in ipairs({ page:GetChildren() }) do lower(r) end
  if lowest < top then page:SetHeight(math.floor(top - lowest + 20)) end
end

-- A character's picture, round: the app's portrait of it (Portraits.lua, a slot file in the
-- Portraits folder), over its class icon. The class icon shows while there's no portrait, and also
-- when a slot file came after the game started (the game only reads it after a restart, and until
-- then it draws nothing). Your current character without one gets its live portrait.
local PORTRAITS = "Interface\\AddOns\\WoWCharacterTracker\\Portraits\\"
local CLASS_CIRCLES = "Interface\\TargetingFrame\\UI-Classes-Circles"

function ns.portraitSlot(guid)
  local index = WoWCharacterTrackerPortraits
  local slot = type(index) == "table" and guid and index[guid]
  return type(slot) == "number" and slot or nil
end

-- The room a portrait and its gap take before the text: on a character row, on a friend's card.
ui.ROW_PORTRAIT, ui.CARD_PORTRAIT = 36, 48

function ui.portrait(parent, size)
  local f = CreateFrame("Frame", nil, parent)
  f:SetSize(size, size)
  f.class = f:CreateTexture(nil, "ARTWORK", nil, 1)
  f.class:SetAllPoints()
  f.face = f:CreateTexture(nil, "ARTWORK", nil, 2)
  f.face:SetAllPoints()
  return f
end

function ui.setPortrait(f, c)
  local coords = c and c.classFile and CLASS_ICON_TCOORDS and CLASS_ICON_TCOORDS[c.classFile]
  if coords then
    f.class:SetTexture(CLASS_CIRCLES)
    f.class:SetTexCoord(coords[1], coords[2], coords[3], coords[4])
    f.class:Show()
  else
    f.class:Hide()
  end
  local slot = c and ns.portraitSlot(c.guid)
  f.face:SetTexCoord(0, 1, 0, 1)
  if slot then
    f.face:SetTexture(PORTRAITS .. slot)
    f.face:Show()
  elseif c and c.guid and UnitGUID and c.guid == UnitGUID("player") and SetPortraitTexture and pcall(SetPortraitTexture, f.face, "player") then
    f.face:Show()
  else
    f.face:Hide()
  end
end

-- A class's round icon on texture (the same circles the portraits sit on), or false when unknown.
function ui.classIcon(texture, classFile)
  local coords = classFile and CLASS_ICON_TCOORDS and CLASS_ICON_TCOORDS[classFile]
  if not coords then return false end
  texture:SetTexture(CLASS_CIRCLES)
  texture:SetTexCoord(coords[1], coords[2], coords[3], coords[4])
  return true
end

-- A left-to-right fade of one color on texture, from alpha `from` to alpha `to`. Retail and the
-- newer classic clients take color objects, older ones SetGradientAlpha; whichever works.
function ui.gradient(texture, r, g, b, from, to)
  texture:SetTexture("Interface\\Buttons\\WHITE8X8")
  texture:SetVertexColor(1, 1, 1, 1)
  if CreateColor and texture.SetGradient
    and pcall(texture.SetGradient, texture, "HORIZONTAL", CreateColor(r, g, b, from), CreateColor(r, g, b, to)) then
    return
  end
  if texture.SetGradientAlpha then
    texture:SetGradientAlpha("HORIZONTAL", r, g, b, from, r, g, b, to)
  else
    texture:SetColorTexture(r, g, b, (from + to) / 2)
  end
end
