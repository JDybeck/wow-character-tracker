-- The controls the addon window and the Options page are built from. Each control that shows a
-- setting registers a refresher with the page it's on; ns.ui.refresh(page) runs them.
local ADDON, ns = ...

local ui = {}
ns.ui = ui

-- How wide long lines get before they wrap (the window's content area, with room to spare).
ui.WRAP = 640

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

function ui.button(parent, label, width, onClick)
  local b = CreateFrame("Button", nil, parent, "UIPanelButtonTemplate")
  b:SetSize(width, 22)
  b:SetText(label)
  b:SetScript("OnClick", onClick)
  return b
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
