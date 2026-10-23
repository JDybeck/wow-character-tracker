-- What the character carries: equipped gear, bags, and the bank (as of the last visit). Saved in
-- WoWCharacterTrackerDB.items[guid] for the app; too big for the live grid, so it reaches the app
-- on /reload or logout.
--   gear     "slot~itemId,..."            (inventory slots 1-19)
--   bags     "bag~slot~itemId~count,..."  bagSizes "bag~size,..."
--   bank     the same for the bank        bankSizes, bankAt (when the bank was last open)
--   sets, effects, enchants: the gear's set bonuses, Equip/Use lines and enchants (scanGearEffects), setsAt when read
--   equipSets: the Equipment Manager's sets and where their items are (scanEquipSets)
--   savedAt, savedZone: when and where the saved file was last written (/reload or logout)
local ADDON, ns = ...

-- Looked up when used, since some clients only have the older global functions.
local function numSlots(bag)
  local f = (C_Container and C_Container.GetContainerNumSlots) or GetContainerNumSlots
  if not f then return 0 end
  local ok, size = pcall(f, bag)
  return ok and type(size) == "number" and size or 0
end

-- itemId and stack size in a bag slot, on clients with C_Container (a table) and without it.
local function slotItem(bag, slot)
  if C_Container and C_Container.GetContainerItemInfo then
    local info = C_Container.GetContainerItemInfo(bag, slot)
    if info then return info.itemID, info.stackCount or 1 end
    return nil
  end
  if GetContainerItemInfo then
    local _, count, _, _, _, _, link, _, _, itemID = GetContainerItemInfo(bag, slot)
    itemID = itemID or (link and tonumber(link:match("item:(%d+)")))
    if itemID then return itemID, count or 1 end
  end
  return nil
end

-- itemClasses[itemId] = the item's class (0 consumable, 2 weapon, 4 armor, 5 reagent, 7 trade
-- goods, 12 quest...), shared by all characters, so the app can sort items by type.
-- GetItemInfoInstant needs no item cache, so it answers right away.
local function noteClass(id)
  local db = WoWCharacterTrackerDB
  if not db or not id then return end
  db.itemClasses = db.itemClasses or {}
  if db.itemClasses[id] then return end
  local f = (C_Item and C_Item.GetItemInfoInstant) or GetItemInfoInstant
  if not f then return end
  local ok, _, _, _, _, _, classID = pcall(f, id)
  if ok and type(classID) == "number" then db.itemClasses[id] = classID end
end

-- The given bags as two strings: what's in each slot, and each bag's size.
local function scanBags(bags)
  local items, sizes = {}, {}
  for _, bag in ipairs(bags) do
    local size = numSlots(bag)
    if size > 0 then
      sizes[#sizes + 1] = bag .. "~" .. size
      for slot = 1, size do
        local itemID, count = slotItem(bag, slot)
        if itemID then
          items[#items + 1] = string.format("%d~%d~%d~%d", bag, slot, itemID, count)
          noteClass(itemID)
        end
      end
    end
  end
  return table.concat(items, ","), table.concat(sizes, ",")
end

-- Backpack (0) and the equipped bags; retail also has a reagent bag after them.
local function carriedBags()
  local last = NUM_TOTAL_EQUIPPED_BAG_SLOTS or NUM_BAG_SLOTS or 4
  local list = {}
  for bag = 0, last do list[#list + 1] = bag end
  return list
end

-- The bank's own slots (-1) and its bag slots after the carried bags; retail's bank tabs instead
-- where the client has them.
local function bankBags()
  local list = { BANK_CONTAINER or -1 }
  local first = (NUM_BAG_SLOTS or 4) + 1
  for bag = first, first + (NUM_BANKBAGSLOTS or 7) - 1 do list[#list + 1] = bag end
  if Enum and Enum.BagIndex and Enum.BagIndex.CharacterBankTab_1 then
    list = {}
    for i = 1, 6 do
      local bag = Enum.BagIndex["CharacterBankTab_" .. i]
      if bag then list[#list + 1] = bag end
    end
  end
  return list
end

local function gear()
  local items = {}
  for slot = 1, 19 do
    local id = GetInventoryItemID and GetInventoryItemID("player", slot)
    if id then items[#items + 1] = slot .. "~" .. id; noteClass(id) end
  end
  return table.concat(items, ",")
end

-- The text lines of an equipped item's tooltip, as the game shows it (C_TooltipInfo where the client
-- has it, else our own hidden tooltip). Secret values are left out.
local scanTip
local function tooltipLines(slot)
  local out = {}
  local function add(text)
    if type(text) == "string" and not (issecretvalue and issecretvalue(text)) then out[#out + 1] = text end
  end
  if C_TooltipInfo and C_TooltipInfo.GetInventoryItem then
    local ok, data = pcall(C_TooltipInfo.GetInventoryItem, "player", slot)
    if ok and type(data) == "table" and type(data.lines) == "table" then
      for _, line in ipairs(data.lines) do add(line.leftText) end
      return out
    end
  end
  if not CreateFrame then return out end
  scanTip = scanTip or CreateFrame("GameTooltip", "WoWCharacterTrackerScanTip", nil, "GameTooltipTemplate")
  scanTip:SetOwner(WorldFrame, "ANCHOR_NONE")
  scanTip:ClearLines()
  if not pcall(scanTip.SetInventoryItem, scanTip, "player", slot) then return out end
  for i = 1, scanTip:NumLines() do
    local fs = _G["WoWCharacterTrackerScanTipTextLeft" .. i]
    if fs then add(fs:GetText()) end
  end
  scanTip:Hide()
  return out
end

local function plain(s) return (s:gsub("(%W)", "%%%1")) end
-- "Set", "Equip:", "Use:" in the client's language.
local SET = plain(((ITEM_SET_BONUS or "Set: %s"):match("^(.-):")) or "Set")
local EQUIP = plain(ITEM_SPELL_TRIGGER_ONEQUIP or "Equip:")
local USE = plain(ITEM_SPELL_TRIGGER_ONUSE or "Use:")
local ENCHANTED = "^" .. plain(((ENCHANTED_TOOLTIP_LINE or "Enchanted: %s"):match("^(.-)%%s")) or "Enchanted: ") .. "(.+)$"

-- Color codes and icons (the enchant's quality star) out, and the characters the saved lines use
-- as separators.
local function clean(text)
  text = text:gsub("|c%x%x%x%x%x%x%x%x", ""):gsub("|r", ""):gsub("|A.-|a", ""):gsub("|T.-|t", ""):gsub("[~\r\n]", " ")
  return (text:gsub("^%s+", ""):gsub("%s+$", ""))
end

-- A set bonus line: "(2) Set: ..." while it isn't active yet, plain "Set: ..." once it is (the game
-- drops the count then). Tier bonuses may name a spec ("(2) Set Blood: ..."); other specs' lines are
-- left out. Returns the pieces needed (0 = active, count not shown) and the text, or nil.
local function setBonus(text, spec)
  local need, label, bonus = text:match("^%((%d+)%) " .. SET .. "([^:]*):%s*(.+)$")
  if not need then label, bonus = text:match("^" .. SET .. "([^:]*):%s*(.+)$") end
  if not bonus or (label ~= "" and not label:match("^ ")) then return nil end -- "Sets the..." isn't one
  label = label:gsub("^%s+", ""):gsub("%s+$", "")
  if label ~= "" and spec and label ~= spec then return nil end
  return tonumber(need) or 0, bonus
end

-- Set bonuses and item effects of the equipped gear, read from the game's own tooltips, so pieces
-- that count for a set without being one (catalyst, redirected tier) are counted as the game counts
-- them. Saved-file only, one line each:
--   sets     "set name~pieces worn~pieces in set~pieces needed (0 = active)~bonus text"
--   effects  "slot~itemId~Equip: ... / Use: ..."
--   enchants "slot~itemId~enchant~enchantId" (the id from the item link: Classic clients only show the
--            enchant's stats, the app finds the enchant by its id)
local function enchantId(slot)
  local link = GetInventoryItemLink and GetInventoryItemLink("player", slot)
  return tonumber(type(link) == "string" and link:match("item:%-?%d+:(%d+)") or nil) or 0
end

function ns.scanGearEffects()
  local sets, effects, enchants, seen = {}, {}, {}, {}
  local spec = ns.specName and ns.specName()
  for slot = 1, 19 do
    local id = GetInventoryItemID and GetInventoryItemID("player", slot)
    if id then
      local setName, have, max
      for _, raw in ipairs(tooltipLines(slot)) do
        local text = clean(raw)
        local name, h, m = text:match("^(.+) %((%d+)/(%d+)%)$")
        if name and not setName then
          setName, have, max = name, h, m
        elseif setName then
          local need, bonus = setBonus(text, spec)
          local key = bonus and (setName .. "~" .. bonus)
          if key and not seen[key] then
            seen[key] = true
            sets[#sets + 1] = table.concat({ setName, have, max, need, bonus }, "~")
          end
        end
        if text:match("^" .. EQUIP) or text:match("^" .. USE) then
          effects[#effects + 1] = slot .. "~" .. id .. "~" .. text
        end
        local enchant = text:match(ENCHANTED)
        if enchant then enchants[#enchants + 1] = slot .. "~" .. id .. "~" .. enchant .. "~" .. enchantId(slot) end
      end
    end
  end
  return table.concat(sets, "\n"), table.concat(effects, "\n"), table.concat(enchants, "\n")
end

-- Where an item of an equipment set is, from its location in C_EquipmentSet.GetItemLocations: the
-- game's sentinels (-1 missing, 0 slot left empty, 1 slot ignored), else bit flags (bank, bags,
-- player = equipped; bank and bag items carry the player flag too, so those come first).
local function setItemPlace(loc)
  if type(loc) ~= "number" then return nil end
  if loc == (EQUIPMENT_SET_ITEM_MISSING or -1) then return "missing" end
  if loc == (EQUIPMENT_SET_EMPTY_SLOT or 0) then return "empty" end
  if loc == (EQUIPMENT_SET_IGNORED_SLOT or 1) then return "ignored" end
  local function has(flag) return math.floor(loc / flag) % 2 == 1 end
  if has(ITEM_INVENTORY_LOCATION_BANK or 0x00400000) then return "bank" end
  if has(ITEM_INVENTORY_LOCATION_BAGS or 0x00200000) then return "bags" end
  if has(ITEM_INVENTORY_LOCATION_PLAYER or 0x00100000) then return "equipped" end
  return "missing"
end

-- The Equipment Manager's sets, one per line (saved file only):
--   equipSets "icon~equipped (1/0)~slot:itemId:place,...~set name"
-- place: equipped, bags, bank, missing, empty (the set leaves the slot empty) or ignored (the set
-- doesn't touch it). The name goes last, so it may hold anything but a line break.
-- nil on clients without C_EquipmentSet, "" for a character without sets.
function ns.scanEquipSets()
  local api = C_EquipmentSet
  if not (api and api.GetEquipmentSetIDs and api.GetEquipmentSetInfo) then return nil end
  local lines = {}
  for _, id in ipairs(api.GetEquipmentSetIDs() or {}) do
    local name, icon, _, isEquipped = api.GetEquipmentSetInfo(id)
    if type(name) == "string" then
      local ids = api.GetItemIDs and api.GetItemIDs(id) or {}
      local locs = api.GetItemLocations and api.GetItemLocations(id) or {}
      local items = {}
      for slot = 1, 19 do
        local place = setItemPlace(locs[slot])
        local item = tonumber(ids[slot]) or 0
        if place == "ignored" or place == "empty" or item <= 1 then item = 0 end -- the old API used 0/1 for empty/ignored
        if place == "equipped" and item == 0 then item = GetInventoryItemID and GetInventoryItemID("player", slot) or 0 end
        if place or item > 0 then items[#items + 1] = slot .. ":" .. item .. ":" .. (place or "missing") end
      end
      lines[#lines + 1] = table.concat({ tonumber(icon) or 0, isEquipped and 1 or 0, table.concat(items, ","),
        (name:gsub("[\r\n]", " ")) }, "~")
    end
  end
  return table.concat(lines, "\n")
end

-- The equipped items' links (enchants and gems are in them) and the spec, as of the last tooltip
-- read: bag changes while looting don't change the gear, so its 19 tooltips aren't read again for
-- them. Read again anyway after a while, in case an item's tooltip wasn't loaded yet the first time.
local GEAR_REREAD = 300
local lastGearKey, lastGearAt

local function gearKey()
  local links = { ns.specName and ns.specName() or "" }
  for slot = 1, 19 do links[#links + 1] = GetInventoryItemLink and GetInventoryItemLink("player", slot) or "" end
  return table.concat(links, ",")
end

-- What the saved file had when this session started, to tell whether anything changed since.
local atLoad

local function entry()
  local guid = UnitGUID("player")
  if not guid or not WoWCharacterTrackerDB then return nil end
  WoWCharacterTrackerDB.items = WoWCharacterTrackerDB.items or {}
  local e = WoWCharacterTrackerDB.items[guid] or {}
  WoWCharacterTrackerDB.items[guid] = e
  atLoad = atLoad or { gear = e.gear, bags = e.bags, bank = e.bank }
  return e
end

-- Gear, bags or bank differ from what the saved file has, so a /reload would update the app.
function ns.itemsChanged()
  local e = entry()
  return e ~= nil and atLoad ~= nil and (e.gear ~= atLoad.gear or e.bags ~= atLoad.bags or e.bank ~= atLoad.bank)
end

-- Gear and bags. Called after bag and gear changes (a few seconds after the last one) and after
-- login. Not at logout: by then the game has already emptied its bag and gear info. A scan that
-- comes back empty (the game not ready, or logging out) never replaces a real one: the backpack
-- always has slots, and a character always wears something.
function ns.scanInventory()
  local e = entry()
  if not e then return end
  ns.doing("inventory scan")
  local worn = gear()
  if worn ~= "" then e.gear = worn end
  -- Tooltips can come back secret in combat, so the set bonuses wait for the next scan out of it.
  if worn ~= "" and not (InCombatLockdown and InCombatLockdown()) then
    local key = gearKey()
    if key ~= lastGearKey or not e.setsAt or time() - (lastGearAt or 0) >= GEAR_REREAD then
      local ok, sets, effects, enchants = pcall(ns.scanGearEffects)
      if ok then
        e.sets, e.effects, e.enchants, e.setsAt = sets, effects, enchants, time()
        lastGearKey, lastGearAt = key, time()
      end
    end
  end
  -- Equipment Manager sets, also out of combat only (a client without them keeps what it had: none).
  if not (InCombatLockdown and InCombatLockdown()) then
    local ok, equipSets = pcall(ns.scanEquipSets)
    if ok and equipSets then e.equipSets = equipSets end
  end
  local bags, sizes = scanBags(carriedBags())
  if sizes ~= "" then e.bags, e.bagSizes = bags, sizes end
  if worn ~= "" or sizes ~= "" then e.scannedAt = time() end
  if ns.bankOpen then ns.scanBank() end
end

function ns.scanBank()
  local e = entry()
  if not e then return end
  local bank, sizes = scanBags(bankBags())
  if sizes == "" then return end -- the bank isn't readable right now
  e.bank, e.bankSizes, e.bankAt = bank, sizes, time()
end

-- When and where the game last wrote the saved file (on /reload and logout), so the app can say
-- how fresh the bags it shows are. Bags can't be read by then, but the zone can.
function ns.noteSaved()
  local e = entry()
  if not e then return end
  e.savedAt = time()
  e.savedZone = (GetRealZoneText and GetRealZoneText()) or nil
end

-- Bags change a lot while looting, so the scan waits for a quiet moment.
local scanPending = false
function ns.scanInventorySoon()
  if scanPending then return end
  scanPending = true
  C_Timer.After(3, function() scanPending = false; ns.scanInventory() end)
end
