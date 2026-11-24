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

-- Copper as whole gold with thousands: 123456789 -> "12,345g" (the app's Format.Gold).
function ns.formatGold(copper)
  local g = tostring(math.floor(math.max(0, copper or 0) / 10000))
  local out = g:reverse():gsub("(%d%d%d)", "%1,"):reverse()
  if out:sub(1, 1) == "," then out = out:sub(2) end
  return out .. "g"
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

-- Reputation standings as the game numbers them (Reputation.lua): 1 Hated to 8 Exalted.
ns.STANDINGS = { "Hated", "Hostile", "Unfriendly", "Neutral", "Friendly", "Honored", "Revered", "Exalted" }

-- The game's name for a standing (in the client's language), else the English one.
function ns.standingName(n)
  local label = type(n) == "number" and _G and _G["FACTION_STANDING_LABEL" .. n]
  return type(label) == "string" and label or ns.STANDINGS[n] or "?"
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
local IN_GAME = { announce = true, ask = true, chatReminder = true,
  gridCorner = true, gridLabel = true, keepAllHistory = true, askSeconds = true, ignored = true, preset = true, filters = true,
  posts = true, screenshot = true, distanceUnit = true }

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

-- The app's Sharing page filters, an In game setting: level steps (post every `before` levels up to
-- `split`, then every `after`), rares Bind on Equip only, end bosses first kill only. An older app
-- only wrote them in its post rules.
function ns.filters()
  local f = ns.setting("filters")
  if type(f) == "table" then return f end
  local post = ns.setting("post") or {}
  local l = post.level or {}
  return { split = l.split, before = l.before, after = l.after, rareBoe = post.rareBoe, firstKill = true }
end

-- The app's post rules (Settings.lua `post`) with the Discord box of each moment from the Sharing
-- table, `posts` = { [moment key] = on }, an In game setting. Nil without the app; an older app has
-- no `posts`, then it's `post` as written. Read post flags through this, never ns.setting("post").
local POST_FIELDS = { level = { "level", "on" }, maxlevel = "maxLevel", keyrun = { "keyrun", "on" } }

function ns.postRules()
  local post, posts = ns.setting("post"), ns.setting("posts")
  if type(post) ~= "table" or type(posts) ~= "table" then return post end
  local out = ns.copy(post)
  for key, on in pairs(posts) do
    local field = POST_FIELDS[key] or key
    if type(field) == "table" then
      out[field[1]] = type(out[field[1]]) == "table" and out[field[1]] or {}
      out[field[1]][field[2]] = on == true
    else
      out[field] = on == true
    end
  end
  return out
end

-- Presets, applied and recognized in game: the app writes what each one sets (Settings.lua
-- `presets`, its SharingPresets) and the table's rows (`moments`: key, name, shot, announce, ask).
-- Nil from an older app, which applies a preset picked here itself.
local PRESET_KEYS = { "quiet", "normal", "everything" }

local function fromApp(key) return WoWCharacterTrackerSettings and WoWCharacterTrackerSettings[key] end

function ns.momentRows()
  local rows = fromApp("moments")
  return type(rows) == "table" and type(fromApp("presets")) == "table" and rows or nil
end

local function on(t, key) return type(t) == "table" and t[key] == true end

-- Whether s (In game settings: posts, screenshot, filters, announce, ask) does what preset p does,
-- for the moments this game has (the app's SharingPresets.Same).
local function matches(s, p, rows)
  for _, row in ipairs(rows) do
    if on(s.posts, row.key) ~= on(p.posts, row.key) then return false end
    if row.shot and on(s.screenshot, row.shot) ~= on(p.screenshot, row.shot) then return false end
    local a = s.announce or {}
    if row.announce and (on(a.kinds, row.announce) ~= on(p.kinds, row.announce)
      or on(a.partyKinds or a.kinds, row.announce) ~= on(p.kinds, row.announce)) then return false end
    if row.ask and on(s.ask, row.ask) ~= on(p.ask, row.ask) then return false end
  end
  local f, pf = s.filters or {}, p.filters or {}
  return f.split == pf.split and f.before == pf.before and f.after == pf.after
    and (f.rareBoe == true) == (pf.rareBoe == true) and (f.firstKill ~= false) == (pf.firstKill ~= false)
end

-- "quiet", "normal", "everything" or "custom" for In game settings s; nil without the app's presets.
function ns.detectPreset(s)
  local rows, presets = ns.momentRows(), fromApp("presets")
  if not rows then return nil end
  for _, key in ipairs(PRESET_KEYS) do
    if type(presets[key]) == "table" and matches(s, presets[key], rows) then return key end
  end
  return "custom"
end

-- The preset the settings match now, or the one picked here when the app can't tell (older app).
function ns.currentPreset()
  local s = { posts = ns.setting("posts"), screenshot = ns.setting("screenshot"), filters = ns.filters(),
    announce = ns.setting("announce"), ask = ns.setting("ask") }
  return ns.detectPreset(s) or ns.setting("preset")
end

-- Picks a preset: its choices go in right away when the app said what they are, else the app
-- applies it on sync.
function ns.applyPreset(key)
  local p = (fromApp("presets") or {})[key]
  ns.editSettings(function(s)
    s.preset = key
    if type(p) ~= "table" then return end
    local copy = ns.copy
    s.posts, s.screenshot, s.filters, s.ask = copy(p.posts or {}), copy(p.screenshot or {}), copy(p.filters or {}), copy(p.ask or {})
    s.announce.kinds, s.announce.partyKinds = copy(p.kinds or {}), copy(p.kinds or {})
  end)
end

-- Whether the level rule posts this level (the app's FlavorSettings.LevelWanted).
function ns.levelPosted(level)
  local f = ns.filters()
  local every = (level <= (f.split or 0)) and (f.before or 1) or (f.after or 1)
  return level % math.max(1, every) == 0
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
  local v = ns.appUpdateVersion and ns.appUpdateVersion()
  if not v then return nil end
  return "Version " .. v .. " of the app is out. Open the Character Tracker app and click Update (it updates the addon too)."
end

-- This addon's version ("0.39.16", from the .toc), "" when the client can't say.
function ns.addonVersion()
  local metadata = (C_AddOns and C_AddOns.GetAddOnMetadata) or GetAddOnMetadata
  return metadata and tostring(metadata(ADDON, "Version") or "") or ""
end

-- a newer than b, both "x.y.z" (0.39.10 is newer than 0.39.9); false for anything else.
function ns.isNewerVersion(a, b)
  local a1, a2, a3 = tostring(a or ""):match("^(%d+)%.(%d+)%.(%d+)$")
  local b1, b2, b3 = tostring(b or ""):match("^(%d+)%.(%d+)%.(%d+)$")
  if not (a1 and b1) then return false end
  for _, pair in ipairs({ { a1, b1 }, { a2, b2 }, { a3, b3 } }) do
    local x, y = tonumber(pair[1]), tonumber(pair[2])
    if x ~= y then return x > y end
  end
  return false
end

-- Newer addons seen this session: friends' addons say their version in every hello and answer
-- (Friends.lua), so a newer one there means an update is out, also without the app. The first one
-- gets a chat line, and the window's version shows it (ns.versionChanged).
local newestAddon
function ns.noteAddonVersion(v)
  if type(v) ~= "string" or #v > 16 or not ns.isNewerVersion(v, ns.addonVersion()) then return end
  if newestAddon and not ns.isNewerVersion(v, newestAddon) then return end
  local first = newestAddon == nil
  newestAddon = v
  if first then
    ns.say(string.format("A friend has a newer WoW Character Tracker addon (%s, you have %s). %s", v, ns.addonVersion(),
      ns.hasApp() and "Update the Character Tracker app, it brings the new addon." or ("Get it where you got this one, or from " .. ns.APP_URL .. ".")))
  end
  if ns.versionChanged then ns.versionChanged() end
end

function ns.newerAddonSeen() return newestAddon end

-- The newer app version the app found (Settings.lua appUpdate), or nil.
function ns.appUpdateVersion()
  local v = WoWCharacterTrackerSettings and WoWCharacterTrackerSettings.appUpdate
  return (type(v) == "string" and v:match("^%d+%.%d+%.%d+$")) and v or nil
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
  changed.filters = changed.filters or copy(ns.filters()) -- from before filters were In game
  changed.screenshot = changed.screenshot or copy(ns.setting("screenshot")) or {} -- from before these were In game
  changed.posts = changed.posts or copy(ns.setting("posts")) -- nil from an older app
  edit(changed)
  -- The preset these now match, so the app never puts a preset back over a change made here.
  changed.preset = ns.detectPreset(changed) or changed.preset
  local fromApp = WoWCharacterTrackerSettings and WoWCharacterTrackerSettings.inGameAt or 0
  changed.at = math.max(time(), fromApp + 1)
  WoWCharacterTrackerDB.gameSettings = changed
end

-- Your main and pinned characters on this game: picked here (WoWCharacterTrackerDB.ownPicks =
-- { main = guid, pinned = { guid, ... }, at }) or in the app (Settings.lua ownMain, ownPinned with
-- ownPicksAt); the newer pick wins, like the friend order. The main leads your Characters tab and
-- goes to friends with every character you share (FriendData.lua `main`), pinned ones come after it.
function ns.ownPicks()
  local game = WoWCharacterTrackerDB and WoWCharacterTrackerDB.ownPicks
  local s = WoWCharacterTrackerSettings
  local appAt = s and tonumber(s.ownPicksAt) or 0
  if type(game) == "table" and (tonumber(game.at) or 0) > appAt then
    return { main = type(game.main) == "string" and game.main ~= "" and game.main or nil,
      pinned = type(game.pinned) == "table" and game.pinned or {} }
  end
  return { main = s and type(s.ownMain) == "string" and s.ownMain ~= "" and s.ownMain or nil,
    pinned = s and type(s.ownPinned) == "table" and s.ownPinned or {} }
end

function ns.ownMain() return ns.ownPicks().main end

function ns.isPinned(guid)
  for _, g in ipairs(ns.ownPicks().pinned) do
    if g == guid then return true end
  end
  return false
end

-- Picks main (guid, or false for none) and/or pins or unpins one character, as a newer pick.
local function setOwnPicks(main, pinned)
  local s = WoWCharacterTrackerSettings
  local appAt = s and tonumber(s.ownPicksAt) or 0
  local old = WoWCharacterTrackerDB.ownPicks
  WoWCharacterTrackerDB.ownPicks = { main = main or "", pinned = pinned,
    at = math.max(time(), appAt + 1, (type(old) == "table" and tonumber(old.at) or 0) + 1) }
end

function ns.setOwnMain(guid)
  local picks = ns.ownPicks()
  setOwnPicks(guid, copy(picks.pinned))
end

function ns.setPinned(guid, on)
  local picks, pinned = ns.ownPicks(), {}
  for _, g in ipairs(picks.pinned) do
    if g ~= guid then pinned[#pinned + 1] = g end
  end
  if on then pinned[#pinned + 1] = guid end
  setOwnPicks(picks.main, pinned)
end

-- s cut to at most n letters, never inside one: WoW's strings are UTF-8 bytes, so å or ö is two
-- of them, and :sub would split it (the game shows garbage, the app reads broken text).
function ns.cutLetters(s, n)
  local out, count = {}, 0
  for letter in s:gmatch("[^\128-\191][\128-\191]*") do
    count = count + 1
    if count > n then break end
    out[count] = letter
  end
  return table.concat(out)
end

-- Where the app (and everything about it) is, for the addon on its own.
ns.APP_URL = "https://github.com/JDybeck/wow-character-tracker"
-- Where tips go, same link as the app's corner and the README.
ns.TIP_URL = "https://ko-fi.com/jdybeck"

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

-- Rare (blue) crafting materials, Trade Goods (7) or Reagent (5): too common to be a moment, so
-- they're never recorded, unless followed.
function ns.isRareReagent(item, quality)
  if quality ~= 3 then return false end
  local classID = ns.itemClass(item)
  return classID == 7 or classID == 5
end

-- Rated PvP brackets as GetPersonalRatedInfo numbers them (the snapshot's ratings and the milestones).
ns.PVP_BRACKETS = { { 1, "2v2" }, { 2, "3v3" }, { 4, "Rated Battlegrounds" }, { 7, "Solo Shuffle" }, { 9, "Battleground Blitz" } }

-- "Grimtusk-Stormrage" -> "Grimtusk".
function ns.withoutRealm(name)
  return name and (name:gsub("%-.*$", "")) or nil
end

-- A realm name as Battle.net writes it: no spaces, hyphens or apostrophes ("TarrenMill"). The game
-- writes ours with them ("CAMWOW EU5 Realm 6 PvP"), so both go through this before comparing.
function ns.plainRealm(name)
  if type(name) ~= "string" or name == "" then return nil end
  return (name:gsub("[%s%-']", ""))
end

-- Forever's realms end in a home realm number and their type ("CAMWOW EU5 Realm 6 PvP" -> "PvP", 6),
-- written either way; nil for other names. The number is not the layer (Layer.lua has that): it
-- stays the same when you change layer, and players of Realm 4 and Realm 6 meet.
local REALM_TYPES = { { "RPPVP", "RP-PvP" }, { "PVP", "PvP" }, { "PVE", "PvE" }, { "NORMAL", "Normal" }, { "RP", "RP" } }
function ns.realmType(name)
  local plain = ns.plainRealm(name)
  if not plain then return nil end
  local upper = plain:upper()
  local number = upper:match("REALM(%d+)%a+$")
  if not number then return nil end
  for _, r in ipairs(REALM_TYPES) do
    if upper:sub(-#r[1]) == r[1] then return r[2], tonumber(number) end
  end
  return nil
end

-- The GUIDs of everyone in your party or raid, as a set.
function ns.groupGuids()
  local set = {}
  if not (IsInGroup and IsInGroup()) then return set end
  local raid = IsInRaid and IsInRaid()
  for i = 1, (GetNumGroupMembers and GetNumGroupMembers() or 0) do
    local guid = UnitGUID((raid and "raid" or "party") .. i)
    if guid then set[guid] = true end
  end
  return set
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
  local started = debugprofilestop and debugprofilestop()
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
  -- How long recording took (reading the character, saving, chat lines), for /wct debug: the
  -- screenshots are logged in shotLog, so a hitch at a moment can be told apart from them.
  if started and WoWCharacterTrackerDB then
    WoWCharacterTrackerDB.perfLog = ns.appendCapped(WoWCharacterTrackerDB.perfLog or {},
      string.format("%s %s %.1fms", date("%H:%M:%S"), kind, debugprofilestop() - started), 20)
  end
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

-- Incognito: for a while nothing goes out, and never later either. Each stretch is kept as
-- WoWCharacterTrackerDB.incognito = { { from, ["until"], friends }, ... } (the newest last, at most
-- INCOGNITO_KEEP): the app never posts what happened inside one, even when it arrives after a
-- /reload, and with friends on those highlights never go to friends.
local INCOGNITO_KEEP = 30

local function windows()
  local db = WoWCharacterTrackerDB
  if not db then return {} end
  db.incognito = db.incognito or {}
  return db.incognito
end

-- The stretch going on now, or nil.
function ns.incognitoActive()
  local list, now = windows(), time()
  local w = list[#list]
  if w and now >= (w.from or 0) and now < (w["until"] or 0) then return w end
  return nil
end

-- Whether at (a time()) fell inside a stretch; forFriends: only one that also paused friends.
function ns.inIncognito(at, forFriends)
  if type(at) ~= "number" then return false end
  for _, w in ipairs(windows()) do
    if at >= (w.from or 0) and at <= (w["until"] or 0) and (not forFriends or w.friends) then return true end
  end
  return false
end

-- Starts (or extends) a stretch of seconds from now; friends: also keep its highlights from friends.
function ns.startIncognito(seconds, friends)
  local list, now = windows(), time()
  local w = ns.incognitoActive()
  if w then
    w["until"], w.friends = now + seconds, friends == true
  else
    list[#list + 1] = { from = now, ["until"] = now + seconds, friends = friends == true }
    while #list > INCOGNITO_KEEP do table.remove(list, 1) end
  end
end

-- Ends the one going on now.
function ns.endIncognito()
  local w = ns.incognitoActive()
  if w then w["until"] = time() end
end

-- "Zeb Stonehand" with a Forever surname, else the name: a character, event or friend's character.
function ns.fullName(c)
  local name = c.char or "?"
  if c.surname and c.surname ~= "" then return name .. " " .. c.surname end
  return name
end

-- "just now", "3m ago", "2h ago" or "1d ago" since at (a time()).
function ns.ago(at)
  local s = math.max(0, time() - (at or time()))
  if s < 60 then return "just now" end
  if s < 3600 then return math.floor(s / 60) .. "m ago" end
  if s < 86400 then return math.floor(s / 3600) .. "h ago" end
  return math.floor(s / 86400) .. "d ago"
end

-- Names and icons by id, which the game may not have cached yet (nil until it has), on old and new APIs.
function ns.spellName(id)
  if C_Spell and C_Spell.GetSpellName then return C_Spell.GetSpellName(id) end
  return GetSpellInfo and (GetSpellInfo(id)) or nil
end

function ns.itemIcon(id)
  local f = (C_Item and C_Item.GetItemIconByID) or GetItemIcon
  if not f then return nil end
  local ok, icon = pcall(f, id)
  return ok and icon or nil
end

function ns.hideTip() if GameTooltip then GameTooltip:Hide() end end
