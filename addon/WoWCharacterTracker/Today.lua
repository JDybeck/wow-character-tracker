-- Today's numbers per character that aren't events: quests turned in, gold and item level gained,
-- herbs, veins, skins and fish gathered. WoWCharacterTrackerDB.today[guid] holds one day
-- ({ day = "2026-11-09", ilvlStart, ilvl, moneyStart, money, quests, herbs, ore, skins, fish }) and
-- starts over on the next. ns.todayStats ranks them with the event counts for the Home tab (the
-- app's TodayStats, same order). No game calls at load, so the app's tests load it.
local ADDON, ns = ...

local function dayKey() return date("%Y-%m-%d") end

-- Finished days stay in WoWCharacterTrackerDB.days[guid][day] (the newest KEEP_DAYS), for the app's
-- Stats page and friends (ns.recentDays).
local KEEP_DAYS = 8
local function archive(db, guid, t)
  if type(t) ~= "table" or type(t.day) ~= "string" then return end
  db.days = db.days or {}
  local list = db.days[guid] or {}
  list[t.day] = t
  local keys = {}
  for day in pairs(list) do keys[#keys + 1] = day end
  table.sort(keys)
  for i = 1, #keys - KEEP_DAYS do list[keys[i]] = nil end
  db.days[guid] = list
end

-- This character's day, a fresh one after midnight (nil before the saved data has loaded).
local function todayFor(guid)
  local db = WoWCharacterTrackerDB
  if not (db and guid) then return nil end
  db.today = db.today or {}
  local t = db.today[guid]
  local day = dayKey()
  if not t or t.day ~= day then
    archive(db, guid, t)
    t = { day = day }
    db.today[guid] = t
  end
  return t
end

-- A character's days, today's and the finished ones, any order.
function ns.recentDays(guid)
  local db = WoWCharacterTrackerDB or {}
  local out = {}
  for day, t in pairs(db.days and db.days[guid] or {}) do
    if type(t) == "table" then out[#out + 1] = setmetatable({ day = day }, { __index = t }) end
  end
  local t = db.today and db.today[guid]
  if type(t) == "table" and t.day then out[#out + 1] = t end
  return out
end

function ns.todayCount(key)
  local t = todayFor(UnitGUID("player"))
  if t then t[key] = (t[key] or 0) + 1 end
end

-- A quest turned in, unless the game calls it repeatable, daily or weekly (those would only count
-- the same quest again). Which quests were done isn't kept.
local function repeatable(questID)
  local function says(fn, ...)
    if type(fn) ~= "function" then return false end
    local ok, yes = pcall(fn, ...)
    return ok and ns.plain(yes) == true
  end
  return says(C_QuestLog and C_QuestLog.IsRepeatableQuest, questID) or says(QuestIsDaily) or says(QuestIsWeekly)
end

function ns.onQuestTurnedIn(questID)
  questID = ns.plain(questID)
  if type(questID) == "number" and repeatable(questID) then return end
  ns.todayCount("quests")
end

-- Item level and gold now (from the saved snapshot, so values the game can't give right now are
-- skipped); the first of the day is the start.
function ns.todaySnapshot(info)
  local t = info and todayFor(info.guid or UnitGUID("player"))
  if not t then return end
  if type(info.ilvl) == "number" and info.ilvl > 0 then
    t.ilvl = info.ilvl
    t.ilvlStart = t.ilvlStart or info.ilvl
  end
  if type(info.money) == "number" then
    t.money = info.money
    t.moneyStart = t.moneyStart or info.money
  end
end

-- Gathering: the cast says what (by the base spells' names, so any rank and language), the loot
-- window that follows says which node, so a vein mined three times counts once. Either can come first.
local GATHER_SPELLS = { herbs = 2366, ore = 2575, skins = 8613 }
local gatherNames -- [spell name] = key, filled on first use (the names need the game)
local castIds = {} -- [spellID] = key or false
local seenNodes, seenDay = {}, nil
local pendingCast, pendingLoot -- { key, at } / { source, at }

local function spellName(id)
  if C_Spell and C_Spell.GetSpellName then return C_Spell.GetSpellName(id) end
  return GetSpellInfo and (GetSpellInfo(id)) or nil
end

local function gatherKind(spellID)
  local known = castIds[spellID]
  if known ~= nil then return known or nil end
  if not gatherNames then
    gatherNames = {}
    for key, id in pairs(GATHER_SPELLS) do
      local name = spellName(id)
      if name then gatherNames[name] = key end
    end
  end
  local name = spellName(spellID)
  castIds[spellID] = name and gatherNames[name] or false
  return castIds[spellID] or nil
end

local function countNode(key, source)
  local day = dayKey()
  if seenDay ~= day then seenNodes, seenDay = {}, day end
  if source then
    local id = key .. source
    if seenNodes[id] then return end
    seenNodes[id] = true
  end
  ns.todayCount(key)
end

function ns.onSpellcast(unit, _, spellID)
  if unit ~= "player" or not ns.plain(spellID) then return end
  local key = gatherKind(spellID)
  if not key then return end
  local now = GetTime()
  if pendingLoot and now - pendingLoot.at < 1 then
    countNode(key, pendingLoot.source)
    pendingLoot = nil
  else
    pendingCast = { key = key, at = now }
  end
end

function ns.onLootOpened()
  local now = GetTime()
  if IsFishingLoot and IsFishingLoot() then return ns.todayCount("fish") end
  local source = GetLootSourceInfo and ns.plain((GetLootSourceInfo(1))) or nil
  if pendingCast and now - pendingCast.at < 5 then
    countNode(pendingCast.key, source)
    pendingCast = nil
  else
    pendingLoot = { source = source, at = now }
  end
end

-- What Home's TODAY row shows, most important first: { key, name, value } where value is a count,
-- or copper (gold) or item levels (ilvl). events: the account's events; today: db.today; since:
-- midnight. Only stats above 0, padded to four with the main ones at 0.
local MAIN = { "levels", "bosses", "epics", "mounts" }
local ORDER = { "levels", "bosses", "epics", "mounts", "ilvl", "quests", "gold", "herbs", "ore", "skins", "fish" }
local NAMES = { levels = "levels", bosses = "bosses", epics = "epics", mounts = "mounts", ilvl = "item level",
  quests = "quests", gold = "gold", herbs = "herbs", ore = "veins", skins = "skins", fish = "fish" }
ns.TODAY_SHOWN = 4

function ns.todayStats(events, today, day, since, isRetail)
  local v = {}
  for _, key in ipairs(ORDER) do v[key] = 0 end
  for _, e in ipairs(events or {}) do
    if type(e) == "table" and (e.at or 0) >= since then
      if e.kind == "level" or e.kind == nil then v.levels = v.levels + 1
      elseif e.kind == "boss" or e.kind == "keyrun" then v.bosses = v.bosses + 1
      elseif e.kind == "loot" and (e.quality or 0) >= 4 then v.epics = v.epics + 1
      elseif e.kind == "mount" then v.mounts = v.mounts + 1 end
    end
  end
  for _, t in pairs(today or {}) do
    if type(t) == "table" and t.day == day then
      for _, key in ipairs({ "quests", "herbs", "ore", "skins", "fish" }) do v[key] = v[key] + (tonumber(t[key]) or 0) end
      local gained = (tonumber(t.ilvl) or 0) - (tonumber(t.ilvlStart) or 0)
      if gained > 0 then v.ilvl = v.ilvl + gained end
      local earned = (tonumber(t.money) or 0) - (tonumber(t.moneyStart) or 0)
      if earned > 0 then v.gold = v.gold + earned end
    end
  end
  if v.ilvl < 0.05 then v.ilvl = 0 end -- rounding left over from averages
  if v.gold < 100 then v.gold = 0 end -- less than a silver isn't worth a spot

  local out = {}
  local function add(key) out[#out + 1] = { key = key, name = key == "bosses" and isRetail and "keys" or NAMES[key], value = v[key] } end
  for _, key in ipairs(ORDER) do if v[key] > 0 then add(key) end end
  for _, key in ipairs(MAIN) do
    if #out >= ns.TODAY_SHOWN then break end
    if v[key] == 0 then add(key) end
  end
  return out
end

-- The value as Home shows it: "+2.5" item levels, "12g" or "40s", else the count.
function ns.todayValue(s)
  if s.key == "ilvl" then
    local text = string.format("%.1f", s.value):gsub("%.0$", "")
    return "+" .. text
  end
  if s.key == "gold" then
    if s.value >= 10000 then return string.format("%dg", math.floor(s.value / 10000)) end
    return string.format("%ds", math.floor(s.value / 100))
  end
  return tostring(s.value)
end
