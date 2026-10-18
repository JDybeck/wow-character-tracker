-- Small helpers every file uses. No frames or game calls at load, so the app's tests can load it too.
local ADDON, ns = ...

-- 2492 -> "41m 32s", 7380 -> "2h 3m", 41 -> "41s" (level times; the app's Format.Duration).
function ns.formatDuration(s)
  s = math.floor(s or 0)
  local h, m, sec = math.floor(s / 3600), math.floor(s % 3600 / 60), s % 60
  if h > 0 then return string.format("%dh %dm", h, m) end
  if m > 0 then return string.format("%dm %ds", m, sec) end
  return string.format("%ds", sec)
end

-- Total played, days too: "6d 4h 12m" (the app's Format.Played).
function ns.formatPlayed(s)
  s = math.floor(s or 0)
  if s < 86400 then return ns.formatDuration(s) end
  return string.format("%dd %dh %dm", math.floor(s / 86400), math.floor(s % 86400 / 3600), math.floor(s % 3600 / 60))
end

-- A setting from Settings.lua (written by the app), or nil when there's none (an older app).
function ns.setting(key)
  return WoWCharacterTrackerSettings and WoWCharacterTrackerSettings[key]
end

-- "Grimtusk-Stormrage" -> "Grimtusk".
function ns.withoutRealm(name)
  return name and (name:gsub("%-.*$", "")) or nil
end

-- Records a moment for the current character: what characterInfo knows, plus fields, as kind.
-- The id is "<guid>:<idSuffix>", or "<guid>:<kind>:<time>" without one. Returns the event.
function ns.record(kind, fields, idSuffix)
  local e = ns.characterInfo()
  -- characterInfo's maxLevel is the game's level cap (for the saved snapshot). On an event,
  -- maxLevel means "this ding reached it", so only a max level ding sets it.
  e.maxLevel = nil
  for k, v in pairs(fields or {}) do e[k] = v end
  e.kind = kind
  e.at = e.at or time()
  e.id = idSuffix and (e.guid .. ":" .. idSuffix) or string.format("%s:%s:%d", e.guid, kind, e.at)
  ns.addEvent(e)
  return e
end
