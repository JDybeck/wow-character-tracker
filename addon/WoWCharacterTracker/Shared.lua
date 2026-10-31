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

-- A function that runs fn once, delay seconds after it's first called; calls in the meantime belong
-- to that same run (bag updates while looting, money ticks, a whole equipment set swapping in).
function ns.debounce(delay, fn)
  local pending = false
  return function()
    if pending then return end
    pending = true
    C_Timer.After(delay, function() pending = false; fn() end)
  end
end

-- Loot, duels, battlegrounds and Mythic+ runs pile up over months of play, and the game and the app
-- handle the whole list on every /reload, logout and sync. Only the newest TRIM_KEEP of each are kept
-- per character; level-ups, boss kills (level times and first kills depend on them) and everything
-- else stay. Events are appended as they happen, so the end of the list is the newest.
-- (The duel and battleground records the app shows count what's kept.) Settings.lua keepAllHistory
-- (the app's In game page) turns it off; ADDON_LOADED checks that.
local TRIM_KINDS = { loot = true, duel = true, bg = true, keyrun = true, ask = true }
local TRIM_KEEP = 300

-- Trims the saved list in place. Returns how many events went.
function ns.trimEvents(events)
  local counts, keep, removed = {}, {}, 0
  for i = #events, 1, -1 do
    local e = events[i]
    if type(e) == "table" and TRIM_KINDS[e.kind] then
      local key = tostring(e.guid) .. "~" .. e.kind
      counts[key] = (counts[key] or 0) + 1
      if counts[key] <= TRIM_KEEP then keep[#keep + 1] = e else removed = removed + 1 end
    else
      keep[#keep + 1] = e
    end
  end
  if removed == 0 then return 0 end
  for i = 1, #events do events[i] = nil end
  for i = #keep, 1, -1 do events[#events + 1] = keep[i] end -- back in time order
  return removed
end

-- The highest step passed going from old to new (only upward), or nil. Rating and profession milestones.
function ns.stepPassed(old, new, steps)
  local best
  for _, s in ipairs(steps) do
    if old < s and new >= s and (not best or s > best) then best = s end
  end
  return best
end

-- The settings of the app's In game page, which can be changed in game too. Like notes, the newest
-- edit wins: a change in game goes to WoWCharacterTrackerDB.gameSettings with its time and applies
-- right away, the app takes it from the saved file, and a later change in the app (Settings.lua
-- inGameAt) wins over it.
local IN_GAME = { announce = true, ask = true, flightReload = true, chatReminder = true,
  gridCorner = true, gridLabel = true, keepAllHistory = true }

local function changedInGame()
  local changed = WoWCharacterTrackerDB and WoWCharacterTrackerDB.gameSettings
  local fromApp = WoWCharacterTrackerSettings and WoWCharacterTrackerSettings.inGameAt or 0
  return changed and (changed.at or 0) > fromApp and changed or nil
end

-- A setting: changed in game when that's newest, else from Settings.lua (written by the app), or nil
-- when there's none (an older app).
function ns.setting(key)
  local changed = IN_GAME[key] and changedInGame()
  if changed then return changed[key] end
  return WoWCharacterTrackerSettings and WoWCharacterTrackerSettings[key]
end

local function copy(v)
  if type(v) ~= "table" then return v end
  local out = {}
  for k, inner in pairs(v) do out[k] = copy(inner) end
  return out
end

-- Changes In game settings: edit gets a full table of them to change, and they apply right away.
function ns.editSettings(edit)
  local changed = changedInGame()
  if not changed then
    changed = {}
    for key in pairs(IN_GAME) do changed[key] = copy(ns.setting(key)) end
  end
  changed.announce = changed.announce or {}
  changed.announce.kinds = changed.announce.kinds or {}
  changed.ask = changed.ask or {}
  edit(changed)
  local fromApp = WoWCharacterTrackerSettings and WoWCharacterTrackerSettings.inGameAt or 0
  changed.at = math.max(time(), fromApp + 1)
  WoWCharacterTrackerDB.gameSettings = changed
end

-- Is the app there? It writes `post` into Settings.lua; the addon on its own (from an addon site)
-- has only the bundled defaults, and nobody to send live events to.
function ns.hasApp()
  return ns.setting("post") ~= nil
end

-- The value, or nil when the game hides it from addons (a secret value on Midnight-era clients).
function ns.plain(v)
  if v == nil or (issecretvalue and issecretvalue(v)) then return nil end
  return v
end

-- "Grimtusk-Stormrage" -> "Grimtusk".
-- On the app's Followed items list (Settings.lua `followed`, read at /reload or login).
function ns.isFollowed(itemId)
  local list = ns.setting("followed")
  return (list and itemId and list[itemId]) and true or false
end

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
