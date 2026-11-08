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
local TRIM_KINDS = { loot = true, duel = true, bg = true, keyrun = true, ask = true, run = true }
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
  gridCorner = true, gridLabel = true, keepAllHistory = true, askSeconds = true, ignored = true, preset = true }

local function changedInGame()
  local changed = WoWCharacterTrackerDB and WoWCharacterTrackerDB.gameSettings
  local fromApp = WoWCharacterTrackerSettings and WoWCharacterTrackerSettings.inGameAt or 0
  return changed and (changed.at or 0) > fromApp and changed or nil
end

-- A setting: changed in game when that's newest, else from Settings.lua (written by the app), or nil
-- when there's none (an older app).
function ns.setting(key)
  local changed = IN_GAME[key] and changedInGame()
  -- A key an older version didn't keep in game still comes from the app.
  if changed and changed[key] ~= nil then return changed[key] end
  return WoWCharacterTrackerSettings and WoWCharacterTrackerSettings[key]
end

-- Ignore tracking (`ignored` = { [guid] = true }, an In game setting): nothing is recorded, sent
-- live, announced, offered or shared with friends for that character.
function ns.isIgnored(guid)
  local ignored = ns.setting("ignored")
  return guid ~= nil and type(ignored) == "table" and ignored[guid] == true
end

-- The chat line about a newer app version (Settings.lua appUpdate, written by the app when it
-- finds one), or nil when there's none. Said once per login, never on /reload.
function ns.appUpdateLine()
  local v = WoWCharacterTrackerSettings and WoWCharacterTrackerSettings.appUpdate
  if type(v) ~= "string" or not v:match("^%d+%.%d+%.%d+$") then return nil end
  return "Version " .. v .. " of the app is out. Open the Character Tracker app and click Update (it updates the addon too)."
end

-- From PLAYER_ENTERING_WORLD: only a real login (isInitialLogin), not a /reload or a loading screen.
function ns.sayAppUpdate(isInitialLogin)
  if isInitialLogin ~= true then return end
  C_Timer.After(8, function() -- once the chat has settled
    local line = ns.appUpdateLine()
    if line then ns.say(line) end
  end)
end

-- A deep copy of a table (anything else as it is).
function ns.copy(v)
  if type(v) ~= "table" then return v end
  local out = {}
  for k, inner in pairs(v) do out[k] = ns.copy(inner) end
  return out
end
local copy = ns.copy

-- Changes In game settings: edit gets a full table of them to change, and they apply right away.
function ns.editSettings(edit)
  local changed = changedInGame()
  if not changed then
    changed = {}
    for key in pairs(IN_GAME) do changed[key] = copy(ns.setting(key)) end
  end
  changed.announce = changed.announce or {}
  changed.announce.kinds = changed.announce.kinds or {}
  changed.announce.partyKinds = changed.announce.partyKinds or copy(changed.announce.kinds) -- from before the group had its own
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

-- The game's chat lockdown (Midnight-era clients, boss fights and the like): no chat, whispers or addon messages.
function ns.chatLocked()
  return (C_ChatInfo and C_ChatInfo.InChatMessagingLockdown and C_ChatInfo.InChatMessagingLockdown()) and true or false
end

-- On the app's Followed items list (Settings.lua `followed`, read at /reload or login).
function ns.isFollowed(itemId)
  local list = ns.setting("followed")
  return (list and itemId and list[itemId]) and true or false
end

-- Adds line to the end of list and drops the oldest past max (the debug logs). Returns the list.
function ns.appendCapped(list, line, max)
  list[#list + 1] = line
  while #list > max do table.remove(list, 1) end
  return list
end

-- GetItemInfo's values, from C_Item where the client has it (newer clients drop the globals).
function ns.itemInfo(item)
  local f = (C_Item and C_Item.GetItemInfo) or GetItemInfo
  if f and item then return f(item) end
  return nil
end

-- The item's class and subclass (0 consumable, 2 weapon, 4 armor, 15/5 mount...). Needs no item
-- cache, so it answers right away. Nil when the client can't tell.
function ns.itemClass(item)
  local f = (C_Item and C_Item.GetItemInfoInstant) or GetItemInfoInstant
  if not f or not item then return nil end
  local ok, _, _, _, _, _, classID, subclassID = pcall(f, item)
  if ok then return classID, subclassID end
  return nil
end

-- Rated PvP brackets as GetPersonalRatedInfo numbers them (the snapshot's ratings and the milestones).
ns.PVP_BRACKETS = { { 1, "2v2" }, { 2, "3v3" }, { 4, "Rated Battlegrounds" }, { 7, "Solo Shuffle" }, { 9, "Battleground Blitz" } }

-- "Grimtusk-Stormrage" -> "Grimtusk".
function ns.withoutRealm(name)
  return name and (name:gsub("%-.*$", "")) or nil
end

-- Text matched literally inside a Lua pattern ("Set:" -> "Set%:").
function ns.escapePattern(s)
  return (s:gsub("%W", "%%%0"))
end

-- A game format string ("You receive loot: %sx%d.", "%1$s has defeated %2$s in a duel") as an
-- anchored Lua pattern, plus the argument number of each capture (translations may swap them).
function ns.formatPattern(fmt)
  local out, order, i = {}, {}, 1
  while i <= #fmt do
    local n, kind, len = fmt:match("^%%(%d)%$([sd])", i)
    if n then len = 4 else kind = fmt:match("^%%([sd])", i); len = 2 end
    if kind then
      order[#order + 1] = n and tonumber(n) or #order + 1
      out[#out + 1] = kind == "d" and "(%d+)" or "(.-)"
      i = i + len
    elseif fmt:sub(i, i + 1) == "%%" then
      out[#out + 1] = "%%"
      i = i + 2
    else
      out[#out + 1] = ns.escapePattern(fmt:sub(i, i))
      i = i + 1
    end
  end
  return "^" .. table.concat(out) .. "$", order
end

-- Saved event ids, so finding one doesn't walk the whole list each time. Events are appended
-- (ns.addEvent) and only trimmed at load, so new ones are picked up from the end, and a shorter
-- list (trimmed) is read again.
local knownIds, idsCounted = {}, 0
function ns.hasEvent(id)
  local events = WoWCharacterTrackerDB and WoWCharacterTrackerDB.events or {}
  if #events < idsCounted then knownIds, idsCounted = {}, 0 end
  for i = idsCounted + 1, #events do
    local e = events[i]
    if type(e) == "table" and e.id then knownIds[e.id] = true end
  end
  idsCounted = #events
  return knownIds[id] == true
end

-- "<guid>:<idSuffix>", or "<guid>:<kind>:<time>" without one.
function ns.eventId(guid, kind, at, idSuffix)
  return idSuffix and (guid .. ":" .. idSuffix) or string.format("%s:%s:%d", guid, kind, at)
end
local eventId = ns.eventId

-- Fields characterInfo has for the saved snapshot that an event doesn't need (the app reads them
-- from the character or the check-in, never from events). characterInfo(true) skips most of them.
local SNAPSHOT_ONLY = { "talentPicks", "talentExport", "stats", "xp", "xpMax" }

-- Records a moment for the current character: what characterInfo knows, plus fields, as kind.
-- The id is "<guid>:<idSuffix>", or "<guid>:<kind>:<time>" without one. Returns the event.
function ns.record(kind, fields, idSuffix)
  if ns.isIgnored(UnitGUID and UnitGUID("player")) then return nil end
  local e = ns.characterInfo(true)
  -- characterInfo's maxLevel is the game's level cap (for the saved snapshot). On an event,
  -- maxLevel means "this ding reached it", so only a max level ding sets it.
  e.maxLevel = nil
  for _, k in ipairs(SNAPSHOT_ONLY) do e[k] = nil end
  for k, v in pairs(fields or {}) do e[k] = v end
  e.kind = kind
  e.at = e.at or time()
  e.id = eventId(e.guid, kind, e.at, idSuffix)
  ns.addEvent(e)
  return e
end

-- A Blizzard popup. It's only added to StaticPopupDialogs when first used: anything an addon puts
-- in Blizzard's tables can spread taint, so it stays out unless it's needed. data reaches the
-- dialog's OnAccept, OnCancel and OnAlt as their second argument.
function ns.popup(name, dialog, textArg, data)
  if not StaticPopupDialogs[name] then
    dialog.timeout = dialog.timeout or 0
    dialog.whileDead, dialog.hideOnEscape, dialog.preferredIndex = true, true, 3
    StaticPopupDialogs[name] = dialog
  end
  return StaticPopup_Show(name, textArg, nil, data)
end

-- Like ns.record, but only when this character has no event with that id yet (a milestone
-- reached again in the same season or after a dip). Returns the event, or nil.
function ns.recordOnce(kind, fields, idSuffix)
  local guid = UnitGUID("player")
  if guid and ns.hasEvent(eventId(guid, kind, 0, idSuffix)) then return nil end
  return ns.record(kind, fields, idSuffix)
end

-- "just now", "3m ago", "2h ago" or "1d ago" since at (a time()).
function ns.ago(at)
  local s = math.max(0, time() - (at or time()))
  if s < 60 then return "just now" end
  if s < 3600 then return math.floor(s / 60) .. "m ago" end
  if s < 86400 then return math.floor(s / 3600) .. "h ago" end
  return math.floor(s / 86400) .. "d ago"
end
