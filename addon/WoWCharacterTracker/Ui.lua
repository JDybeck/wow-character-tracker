-- The controls the addon window and the Options page are built from. Each control that shows a
-- setting registers a refresher with the page it's on; ns.ui.refresh(page) runs them.
local ADDON, ns = ...

local ui = {}
ns.ui = ui

-- How wide long lines get before they wrap (the window's content area, with room to spare).
ui.WRAP = 640

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

function ui.fill(texture, c) texture:SetColorTexture(c[1], c[2], c[3], c[4] or 1) end

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

-- "v0.33.2" from the .toc, or "" when the client can't say.
function ui.version()
  local metadata = (C_AddOns and C_AddOns.GetAddOnMetadata) or GetAddOnMetadata
  return metadata and ("v" .. tostring(metadata(ADDON, "Version") or "")) or ""
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
    t:SetWidth(ui.WRAP - 40)
  end
  cb:SetScript("OnClick", function(self) set(self:GetChecked() and true or false) end)
  ui.onRefresh(parent, function() cb:SetChecked(get()) end)
  return cb
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
-- get() says the picked key (a key not among them picks none), set(key) picks one. Returns the row.
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

-- Under anchor, or at the top of the page without one.
function ui.heading(parent, s, anchor, gap)
  local h = ui.text(parent, "GameFontNormal", s)
  h:SetTextColor(COLOR.accent[1], COLOR.accent[2], COLOR.accent[3])
  if anchor then h:SetPoint("TOPLEFT", anchor, "BOTTOMLEFT", 2, gap or -26)
  else h:SetPoint("TOPLEFT", parent, "TOPLEFT", 16, -14) end
  return h
end

function ui.hint(parent, s, anchor)
  local h = ui.text(parent, "GameFontDisableSmall", s)
  h:SetPoint("TOPLEFT", anchor, "BOTTOMLEFT", 0, -4)
  h:SetWidth(ui.WRAP)
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
function ui.checkTable(parent, items, columns, anchor, get, set)
  local FIRST, WIDTH = 230, 110
  local header = ui.text(parent, "GameFontNormalSmall", "What")
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
      local cb = ui.checkbox(parent, "", nil, function() return get(column[1], item[1]) end, function(on) set(column[1], item[1], on) end)
      cb:SetPoint("LEFT", name, "LEFT", FIRST + (c - 1) * WIDTH - 4, 0)
    end
    last = name
  end
  return last
end
