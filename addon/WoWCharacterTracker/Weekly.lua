-- Forever's weekly "In Demand - <what>" quests, from Pix Xizzix in Booty Bay, pay out an Undermine
-- Satchel. It holds one valuable (Gold Nugget 10g, Huge Quartz Gem 20g, Silver Necklace 40g, Gold Ring
-- 100g, by the fan sites) and maybe some materials. Opening it records a "weekly" moment: the most
-- valuable thing that came out, what it sells for, everything else that came out, what it all sells
-- for together, and which week's quest it was from.
-- The ids are Wowhead's (Forever), as fan sites quoted them in November 2026: not checked in game. A
-- quest whose title starts "In Demand" counts too, and its reward item becomes a satchel, so a new
-- week, or a wrong id here, still works.
local ADDON, ns = ...

local SATCHEL = 271994 -- Undermine Satchel
-- In Demand - Blackrock Legal Records, Ancient Elven Curiosities, Plague Records, Titan Relics.
local IN_DEMAND_QUESTS = { [96075] = true, [96076] = true, [96087] = true, [96088] = true }
local IN_DEMAND_PREFIX = "^In Demand"
-- What the valuables sell for, in copper, for when the game doesn't have the item yet (Gold Nugget,
-- Huge Quartz Gem, Gold Ring; the necklace id isn't known). A satchel's known valuable is always the
-- one named first, even when the game hasn't priced it yet and a material seems to sell for more.
local SELL_PRICES = { [272016] = 100000, [272015] = 200000, [272001] = 1000000 }
local KEEP_QUEST = 8 * 86400 -- a quest turned in longer ago than a week (and a bit) isn't this satchel's

local function plain(v) return ns.plain and ns.plain(v) or v end

local function idOf(link) return type(link) == "string" and tonumber(link:match("item:(%d+)")) or nil end

-- WoWCharacterTrackerDB.weekly[guid] = { quest, questId, at, bags = { itemId, ... } }: this
-- character's last In Demand turn-in, until its satchel is opened (it can be days later, after logouts).
local function saved(guid)
  local db = WoWCharacterTrackerDB
  if not (db and guid) then return nil end
  db.weekly = db.weekly or {}
  return db.weekly, db.weekly[guid]
end

local function isInDemand(questID, title)
  return IN_DEMAND_QUESTS[questID] == true or (type(title) == "string" and title:find(IN_DEMAND_PREFIX) ~= nil)
end

-- The reward panel is open (QUEST_COMPLETE): its title and reward items, kept until the turn-in.
local offered
function ns.onQuestComplete()
  offered = nil
  local id = GetQuestID and plain(GetQuestID()) or nil
  local title = GetTitleText and plain(GetTitleText()) or nil
  if not isInDemand(id, title) then return end
  local bags = {}
  for _, kind in ipairs({ "reward", "choice" }) do
    local count = kind == "reward" and (GetNumQuestRewards and GetNumQuestRewards() or 0) or (GetNumQuestChoices and GetNumQuestChoices() or 0)
    for i = 1, count do
      local itemId = GetQuestItemLink and idOf(plain(GetQuestItemLink(kind, i)))
      if itemId then bags[#bags + 1] = itemId end
    end
  end
  offered = { questId = id, quest = title, bags = bags }
end

local function questTitle(questID)
  if C_QuestLog and C_QuestLog.GetTitleForQuestID then
    local ok, title = pcall(C_QuestLog.GetTitleForQuestID, questID)
    if ok and type(plain(title)) == "string" then return plain(title) end
  end
  if QuestUtils_GetQuestName then
    local ok, title = pcall(QuestUtils_GetQuestName, questID)
    if ok and type(plain(title)) == "string" and plain(title) ~= "" then return plain(title) end
  end
  return nil
end

-- QUEST_TURNED_IN: an In Demand quest is remembered for its satchel.
function ns.onWeeklyTurnedIn(questID)
  questID = plain(questID)
  local o = offered
  offered = nil
  if o and o.questId and o.questId ~= questID then o = nil end
  local title = (o and o.quest) or questTitle(questID)
  if not isInDemand(questID, title) then return end
  local list = saved(UnitGUID("player"))
  if not list then return end
  list[UnitGUID("player")] = { quest = title, questId = questID, at = time(), bags = o and o.bags or {} }
end

local function itemCount(id)
  local f = (C_Item and C_Item.GetItemCount) or GetItemCount
  if not f then return 0 end
  local ok, n = pcall(f, id)
  return ok and tonumber(plain(n)) or 0
end

-- The satchel ids for this character: the known one, and the last In Demand quest's rewards.
local function satchelIds()
  local ids = { [SATCHEL] = true }
  local _, w = saved(UnitGUID("player"))
  for _, id in ipairs(w and w.bags or {}) do ids[id] = true end
  return ids
end

-- { [itemId] = count } of the satchels in the bags, and their total.
local function held(ids)
  local counts, n = {}, 0
  for id in pairs(ids) do
    counts[id] = itemCount(id)
    n = n + counts[id]
  end
  return counts, n
end

-- A loot window is open. From an item (a container), while a satchel is in the bags: what's in it,
-- kept until the bags show one satchel fewer (then it was the satchel, not another container).
local pending
function ns.onWeeklyLootOpened()
  pending = nil
  local ids = satchelIds()
  local counts, total = held(ids)
  if total == 0 or not GetNumLootItems then return end
  local source = GetLootSourceInfo and plain((GetLootSourceInfo(1))) or nil
  if type(source) == "string" and not source:find("^Item") then return end -- a corpse, chest or node
  local items = {}
  for slot = 1, GetNumLootItems() do
    local link = GetLootSlotLink and plain(GetLootSlotLink(slot))
    if idOf(link) and not ids[idOf(link)] then
      local _, _, quantity = GetLootSlotInfo(slot)
      items[#items + 1] = { link = link, count = tonumber(plain(quantity)) or 1 }
      -- Ask for the item now, so its price is known when the satchel is found gone.
      if C_Item and C_Item.RequestLoadItemDataByID then pcall(C_Item.RequestLoadItemDataByID, idOf(link)) end
    end
  end
  if #items > 0 then pending = { items = items, counts = counts, ids = ids, at = time() } end
end

local function sellPrice(link)
  local price = select(11, ns.itemInfo(link))
  if type(price) == "number" and price > 0 then return price end
  return SELL_PRICES[idOf(link)] or 0
end

local function quality(link)
  return (ns.itemQuality and ns.itemQuality(link)) or 1
end

-- The valuable is a known one (Gold Ring, Huge Quartz Gem...), else what sells for the most (by
-- quality when prices are unknown); everything else that came out is listed too.
function ns.weeklyReward(items)
  local best, bestKnown, bestValue, bestQuality
  for _, it in ipairs(items) do
    it.value = sellPrice(it.link) * it.count
    local known = SELL_PRICES[idOf(it.link)] ~= nil
    local q = quality(it.link)
    local better = not best
      or (known and not bestKnown)
      or (known == bestKnown and (it.value > bestValue or (it.value == bestValue and q > bestQuality)))
    if better then
      best, bestKnown, bestValue, bestQuality = it, known, it.value, q
    end
  end
  local extras, links, total = {}, {}, 0
  for _, it in ipairs(items) do
    total = total + it.value
    if it ~= best then
      local name = it.link:match("%[(.-)%]") or "?"
      extras[#extras + 1] = it.count > 1 and string.format("%s x%d", name, it.count) or name
      links[#links + 1] = it.count > 1 and string.format("%sx%d", it.link, it.count) or it.link
    end
  end
  return best, bestQuality, extras, links, total
end

-- After the loot window closes or the bags change: one satchel fewer means it was opened.
function ns.checkWeeklySatchel()
  local p = pending
  if not p then return end
  if time() - p.at > 60 then pending = nil return end
  local counts = held(p.ids)
  local opened
  for id, before in pairs(p.counts) do
    if (counts[id] or 0) < before then opened = id end
  end
  if not opened then return end
  pending = nil
  local best, q, extras, links, total = ns.weeklyReward(p.items)
  local name = best.link:match("%[(.-)%]")
  if not name then return end
  local list, w = saved(UnitGUID("player"))
  if w and time() - (w.at or 0) > KEEP_QUEST then w = nil end
  if list then list[UnitGUID("player")] = nil end
  local now = time()
  local e = ns.record("weekly", {
    item = name, itemId = idOf(best.link), quality = q, sell = best.value > 0 and best.value or nil,
    quest = w and w.quest or nil, bag = (ns.itemInfo(opened)) or (opened == SATCHEL and "Undermine Satchel") or nil,
    extras = #extras > 0 and table.concat(extras, "^") or nil, total = total > 0 and total or nil, at = now,
  }, string.format("weekly:%d:%d", idOf(best.link), now))
  if e and ns.say then
    -- Everything that came out, the valuable first, and what it all sells for.
    table.insert(links, 1, best.count > 1 and string.format("%sx%d", best.link, best.count) or best.link)
    ns.say(string.format("Weekly satchel: %s.%s", table.concat(links, ", "),
      total > 0 and (" Sells for " .. ns.goldText(total) .. " in all.") or ""))
  end
end

-- "100g", "12g 50s", "75s" from copper: what a vendor pays, in the chat line and the post.
function ns.goldText(copper)
  copper = math.floor(tonumber(copper) or 0)
  local g, s = math.floor(copper / 10000), math.floor(copper / 100) % 100
  if g > 0 and s > 0 then return string.format("%dg %ds", g, s) end
  if g > 0 then return string.format("%dg", g) end
  if s > 0 then return string.format("%ds", s) end
  return string.format("%dc", copper % 100)
end
