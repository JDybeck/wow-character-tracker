-- The addon's page in the game's options (Esc, Options, AddOns, WoW Character Tracker; or /wct options).
-- Players look here first, so it points to the addon window, where every option is.
local ADDON, ns = ...
local ui = ns.ui

local NAME = "WoW Character Tracker"
local panel = CreateFrame("Frame")
panel.name = NAME
local category

local title = ui.text(panel, "GameFontNormalLarge", NAME)
title:SetPoint("TOPLEFT", 16, -16)
local version = ui.text(panel, "GameFontDisableSmall")
version:SetPoint("LEFT", title, "RIGHT", 8, -1)
local line = ui.text(panel, "GameFontHighlight", "Every option is in the addon's own window. /wct opens it too, and so does the minimap button.")
line:SetPoint("TOPLEFT", title, "BOTTOMLEFT", 0, -12)
line:SetWidth(560)
local open = ui.button(panel, "Open the Character Tracker window", 260, function() ns.openWindow() end, "primary")
open:SetPoint("TOPLEFT", line, "BOTTOMLEFT", 0, -14)

panel:SetScript("OnShow", function() version:SetText(ui.version()) end)

if Settings and Settings.RegisterCanvasLayoutCategory then
  category = Settings.RegisterCanvasLayoutCategory(panel, NAME)
  Settings.RegisterAddOnCategory(category)
elseif InterfaceOptions_AddCategory then
  InterfaceOptions_AddCategory(panel)
end

