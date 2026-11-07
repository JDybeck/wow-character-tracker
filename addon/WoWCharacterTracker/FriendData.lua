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
  faction = { kind = "string", max = 10 },
  spec = { kind = "string", max = 32 },
  ilvl = { kind = "number" },
  at = { kind = "number" },
  talents = { kind = "string", max = 1024, category = "talents" },
  talentCalc = { kind = "string", max = 256, category = "talents" },
  gear = { kind = "string", max = 512, category = "gear" },
  enchants = { kind = "string", max = 2048, category = "gear", lines = true },
  zone = { kind = "string", max = 64, category = "location" },
  money = { kind = "number", category = "gold" },
}
local MAX_TEXT = 8192

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

-- The text for one of your characters, and the same without `at` (it changes with every snapshot,
-- so change detection compares that one). allowed: the categories the friend may see.
function ns.encodeFriendChar(guid, char, items, allowed)
  local values = {}
  for key in pairs(FIELDS) do values[key] = char[key] end
  values.guid, values.at = guid, char.lastSeen
  values.gear, values.enchants = items and items.gear, items and items.enchants
  local parts = {}
  for key, field in pairs(FIELDS) do
    local v = values[key]
    if key ~= "at" and type(v) == field.kind and (not field.category or allowed[field.category]) then
      parts[#parts + 1] = key .. "=" .. escape(tostring(v))
    end
  end
  table.sort(parts) -- the same character is the same text
  local key = table.concat(parts, ";")
  return key .. ";at=" .. tostring(values.at or 0), key
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
        if v == "" then v = nil end
      end
      out[key] = v
    end
  end
  if not (out.guid and out.guid:match("^Player%-%d+%-%x+$") and out.char) then return nil end
  return out
end
