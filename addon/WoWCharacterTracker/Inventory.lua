-- What the character carries: equipped gear, bags, and the bank (as of the last visit). Saved in
-- WoWCharacterTrackerDB.items[guid] for the app; too big for the live grid, so it reaches the app
-- on /reload or logout.
--   gear     "slot~itemId,..."            (inventory slots 1-19)
--   bags     "bag~slot~itemId~count,..."  bagSizes "bag~size,..."
--   bank     the same for the bank        bankSizes, bankAt (when the bank was last open)
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

-- The given bags as two strings: what's in each slot, and each bag's size.
local function scanBags(bags)
  local items, sizes = {}, {}
  for _, bag in ipairs(bags) do
    local size = numSlots(bag)
    if size > 0 then
      sizes[#sizes + 1] = bag .. "~" .. size
      for slot = 1, size do
        local itemID, count = slotItem(bag, slot)
        if itemID then items[#items + 1] = string.format("%d~%d~%d~%d", bag, slot, itemID, count) end
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
    if id then items[#items + 1] = slot .. "~" .. id end
  end
  return table.concat(items, ",")
end

local function entry()
  local guid = UnitGUID("player")
  if not guid or not WoWCharacterTrackerDB then return nil end
  WoWCharacterTrackerDB.items = WoWCharacterTrackerDB.items or {}
  local e = WoWCharacterTrackerDB.items[guid] or {}
  WoWCharacterTrackerDB.items[guid] = e
  return e
end

-- Gear and bags. Called after bag and gear changes (a few seconds after the last one), at login
-- and logout.
function ns.scanInventory()
  local e = entry()
  if not e then return end
  ns.doing("inventory scan")
  e.gear = gear()
  e.bags, e.bagSizes = scanBags(carriedBags())
  e.scannedAt = time()
  if ns.bankOpen then ns.scanBank() end
end

function ns.scanBank()
  local e = entry()
  if not e then return end
  e.bank, e.bankSizes = scanBags(bankBags())
  e.bankAt = time()
end

-- Bags change a lot while looting, so the scan waits for a quiet moment.
local scanPending = false
function ns.scanInventorySoon()
  if scanPending then return end
  scanPending = true
  C_Timer.After(3, function() scanPending = false; ns.scanInventory() end)
end
