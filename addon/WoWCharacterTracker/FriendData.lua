-- One character as a friend sees it (Friends.lua): built from the saved snapshot, sent as one line
-- of text, and checked field by field when it arrives, since it comes from someone else's PC.
-- No game calls, so the app's tests load it.
local ADDON, ns = ...

-- Every field a friend may get: its type, longest length, and the category that allows it (none:
-- always sent). Only enchants keeps line breaks (one enchant per line).
local FIELDS = {
  guid = { kind = "string", max = 40 },
  char = { kind = "string", max = 24 },
  surname = { kind = "string", max = 24 }, -- Forever
  realm = { kind = "string", max = 40 },
  level = { kind = "number" },
  class = { kind = "string", max = 24 },
  classFile = { kind = "string", max = 16 },
  race = { kind = "string", max = 24 },
  raceFile = { kind = "string", max = 16 },
  sex = { kind = "string", max = 6, only = { male = true, female = true } },
  faction = { kind = "string", max = 10 },
  spec = { kind = "string", max = 32 },
  ilvl = { kind = "number" },
  at = { kind = "number" },
  talents = { kind = "string", max = 1024, category = "talents" },
  talentCalc = { kind = "string", max = 256, category = "talents" },
  gear = { kind = "string", max = 512, category = "gear" },
  enchants = { kind = "string", max = 2048, category = "gear", lines = true },
  zone = { kind = "string", max = 64, category = "location" },
  professions = { kind = "string", max = 256, category = "professions" },
  money = { kind = "number", category = "gold" },
  highlights = { kind = "string", max = 1024, category = "highlights", lines = true },
  -- Leveling: level times as "level~seconds~at" lines, played, time on this level, the XP bar.
  levels = { kind = "string", max = 1600, category = "leveling", lines = true },
  played = { kind = "number", category = "leveling" },
  levelTime = { kind = "number", category = "leveling" },
  xp = { kind = "number", category = "leveling" },
  xpMax = { kind = "number", category = "leveling" },
  maxLevel = { kind = "number", category = "leveling" },
}
local MAX_TEXT = 8192
-- Sent, but left out of the change key like `at`: they tick along while nothing else changes.
local VOLATILE = { at = true, played = true, levelTime = true, xp = true }
local MAX_LEVELS = 80

-- Highlights go as one line each, newest first: "kind~at~level" and then the kind's own fields in
-- this order. A field with a category (FIELD_CATEGORY) stays empty unless that one is allowed too.
-- Who was in the group stays home, only its size goes. The app reads the same order (FriendData.cs).
local MAX_HIGHLIGHTS = 10
local HIGHLIGHT_FIELDS = {
  boss = { "boss", "instance", "groupSize" },
  keyrun = { "instance", "keyLevel", "upgrades" },
  loot = { "item", "itemId", "quality" },
  mount = { "mount", "itemId", "bought" },
  duel = { "opponent", "lost", "fled" },
  makgora = { "opponent", "lost" },
  bg = { "instance", "lost" },
  profession = { "profession", "step" },
  mplus = { "step", "rating" },
  pvp = { "step", "bracket", "rating" },
  death = { "killer", "played", "gold", "zone" },
}
local FIELD_CATEGORY = { gold = "gold", zone = "location" }
ns.FRIEND_HIGHLIGHTS = MAX_HIGHLIGHTS

function ns.isFriendHighlight(e)
  return type(e) == "table" and HIGHLIGHT_FIELDS[e.kind] ~= nil and type(e.at) == "number"
end

-- `\`, `;` (between fields) and line breaks are escaped, so the text is one line.
local function escape(s)
  s = s:gsub("\\", "\\\\")
  s = s:gsub("\n", "\\n")
  s = s:gsub(";", "\\s")
  return s
end

local function unescape(s)
  return (s:gsub("\\(.)", function(c)
    if c == "n" then return "\n" end
    if c == "s" then return ";" end
    return c
  end))
end

-- No | (WoW's escape character for links and textures) and no control characters.
local function clean(s, field)
  s = s:gsub("|", "")
  s = s:gsub("%c", function(c) return (field.lines and c == "\n") and c or "" end)
  return s:sub(1, field.max)
end

-- Not NaN or infinite (both make n - n NaN).
local function finite(n)
  return n ~= nil and n - n == 0
end

-- One value in a highlight line: no ~ (between fields), | or line breaks; true is 1.
local function highlightValue(v)
  if type(v) == "boolean" then return v and "1" or "" end
  if type(v) == "number" then return finite(v) and tostring(math.floor(v)) or "" end
  if type(v) == "string" then return (v:gsub("[~|%c]", "")) end
  return ""
end

-- The newest highlights among events (any order, other kinds skipped) as lines, nil without any.
local function encodeHighlights(events, allowed)
  local list = {}
  for _, e in ipairs(events or {}) do
    if ns.isFriendHighlight(e) then list[#list + 1] = e end
  end
  table.sort(list, function(x, y) return x.at > y.at end)
  local lines, size = {}, 0
  for i = 1, math.min(#list, MAX_HIGHLIGHTS) do
    local e = list[i]
    local parts = { e.kind, highlightValue(e.at), highlightValue(e.level) }
    for _, key in ipairs(HIGHLIGHT_FIELDS[e.kind]) do
      local category = FIELD_CATEGORY[key]
      parts[#parts + 1] = (not category or allowed[category]) and highlightValue(e[key]) or ""
    end
    local line = table.concat(parts, "~")
    if size + #line + 1 > FIELDS.highlights.max then break end
    lines[#lines + 1] = line
    size = size + #line + 1
  end
  return #lines > 0 and table.concat(lines, "\n") or nil
end

-- Level times ({ level, seconds, at }, any order) as lines, the newest MAX_LEVELS levels, nil without any.
local function encodeLevels(levels)
  local list = {}
  for _, l in ipairs(levels or {}) do
    if type(l.level) == "number" and type(l.seconds) == "number" and finite(l.seconds) and l.seconds > 0 then list[#list + 1] = l end
  end
  table.sort(list, function(x, y) return x.level > y.level end)
  local lines = {}
  for i = math.min(#list, MAX_LEVELS), 1, -1 do -- oldest of the kept first
    local l = list[i]
    lines[#lines + 1] = string.format("%d~%d~%d", l.level, math.floor(l.seconds), math.floor(finite(l.at) and l.at or 0))
  end
  return #lines > 0 and table.concat(lines, "\n") or nil
end

-- A friend character's level times, oldest first: { level, seconds, at }; bad lines are skipped.
function ns.friendLevels(c)
  local out = {}
  for level, seconds, at in string.gmatch(c and c.levels or "", "(%d+)~(%d+)~(%d+)") do
    level, seconds = tonumber(level), tonumber(seconds)
    if level >= 2 and level <= 200 and seconds > 0 then out[#out + 1] = { level = level, seconds = seconds, at = tonumber(at) } end
  end
  table.sort(out, function(a, b) return a.level < b.level end)
  return out
end

-- The text for one of your characters, and the same without the volatile fields (`at` changes with
-- every snapshot, so change detection compares that one). allowed: the categories the friend may
-- see; events: the character's highlights (ns.isFriendHighlight), or nil; levels: its level times.
function ns.encodeFriendChar(guid, char, items, allowed, events, levels)
  local values = {}
  for key in pairs(FIELDS) do values[key] = char[key] end
  values.guid, values.at = guid, char.lastSeen
  values.gear, values.enchants = items and items.gear, items and items.enchants
  values.highlights = allowed.highlights and encodeHighlights(events, allowed) or nil
  values.levels = allowed.leveling and encodeLevels(levels) or nil
  local parts, volatile = {}, {}
  for key, field in pairs(FIELDS) do
    local v = values[key]
    if key ~= "at" and type(v) == field.kind and (not field.category or allowed[field.category]) then
      local part = key .. "=" .. escape(tostring(v))
      if VOLATILE[key] then volatile[#volatile + 1] = part else parts[#parts + 1] = part end
    end
  end
  table.sort(parts) -- the same character is the same text
  table.sort(volatile)
  local key = table.concat(parts, ";")
  volatile[#volatile + 1] = "at=" .. tostring(values.at or 0)
  return key .. ";" .. table.concat(volatile, ";"), key
end

-- A friend character's gear in slot order: { slot, id, enchant, enchantId } (enchant and
-- enchantId nil when not known; enchant lines only count for the same item in the same slot).
function ns.friendGear(c)
  local enchants = {}
  for line in string.gmatch(c.enchants or "", "[^\n]+") do
    local slot, id, text, enchantId = line:match("^(%d+)~(%d+)~(.-)~(%d+)$")
    if slot then enchants[slot .. "~" .. id] = { text = text ~= "" and text or nil, id = tonumber(enchantId) } end
  end
  local out = {}
  for slot, id in string.gmatch(c.gear or "", "(%d+)~(%d+)") do
    local e = enchants[slot .. "~" .. id] or {}
    out[#out + 1] = { slot = tonumber(slot), id = tonumber(id), enchant = e.text, enchantId = (e.id or 0) > 0 and e.id or nil }
  end
  table.sort(out, function(a, b) return a.slot < b.slot end)
  return out
end

-- The character in a friend's text, or nil when it isn't one. Only known fields are kept.
function ns.decodeFriendChar(text)
  if type(text) ~= "string" or #text > MAX_TEXT then return nil end
  local out = {}
  for part in text:gmatch("[^;]+") do
    local key, raw = part:match("^(%a+)=(.*)$")
    local field = key and FIELDS[key]
    if field then
      local v = unescape(raw)
      if field.kind == "number" then
        v = tonumber(v)
        if not finite(v) then v = nil end
      else
        v = clean(v, field)
        if v == "" or (field.only and not field.only[v]) then v = nil end
      end
      out[key] = v
    end
  end
  if not (out.guid and out.guid:match("^Player%-%d+%-%x+$") and out.char) then return nil end
  return out
end
