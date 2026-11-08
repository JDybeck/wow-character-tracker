-- Friends on the world map, the logic (MapPins.lua draws). While your world map is open, contacts
-- you keep things from get WATCH every WATCH_EVERY seconds. A contact who allows you Map position
-- answers with POS while that watch lasts: their position, their rate and how old the reading is.
-- Positions live in memory only, never in the saved file, so the app never sees them.
local ADDON, ns = ...

local WATCH_EVERY = 20 -- seconds between two WATCHes to one friend while the map is open
local WATCH_LASTS = 30 -- a watch that isn't renewed ends
local TICK = 5         -- the sender looks at its position this often while anyone watches
local RESEND = 20      -- a position that didn't move still goes this often
local RATES = { [5] = true, [60] = true, [300] = true }

-- Sending
local watchers = {} -- [account] = { untilAt, sent (the reading they got last), sentAt, hadPosition, cleared }
local reading       -- { mapID, x, y, at }: our last position reading
local ticking = false

-- Receiving
local mapOpen = false
local watchSentAt = {} -- [account] = when WATCH last went to them
local positions = {}   -- [account] = { mapID, x, y, rate, readAt }
local watchLoop = false

local function round(v) return math.floor(v * 1000 + 0.5) / 1000 end

local function pinsChanged()
  if ns.friendPinsChanged then ns.friendPinsChanged() end
end

local function online()
  local ok, list = pcall(ns.onlineFriends)
  return ok and list or {}
end

local function friendByAccount(account)
  for _, f in ipairs(online()) do
    if f.account == account then return f end
  end
  return nil
end

-- Sending -------------------------------------------------------------------------------------
-- Our position: mapID, x, y, or nil in an instance or where the game gives none.
local function readPosition()
  if IsInInstance and IsInInstance() then return nil end
  if not (C_Map and C_Map.GetBestMapForUnit and C_Map.GetPlayerMapPosition) then return nil end
  local mapID = C_Map.GetBestMapForUnit("player")
  local pos = mapID and C_Map.GetPlayerMapPosition(mapID, "player")
  if not pos or not pos.x or not pos.y or (pos.x == 0 and pos.y == 0) then return nil end
  return mapID, pos.x, pos.y
end

-- The reading to send: a new one once the rate allows, else the last one; nil without a position.
local function currentReading(now, rate)
  local mapID, x, y = readPosition()
  if not mapID then
    reading = nil
    return nil
  end
  if reading and now - reading.at < rate then return reading end
  reading = { mapID = mapID, x = round(x), y = round(y), at = now }
  return reading
end

-- Battle.net's factionName against either of UnitFactionGroup's names. Unknown is never the same.
local function sameFaction(faction)
  if type(faction) ~= "string" or faction == "" or not UnitFactionGroup then return false end
  local english, localized = UnitFactionGroup("player")
  return faction == english or faction == localized
end

-- f (a row of ns.onlineFriends) may get our position right now.
local function mayGet(f)
  local s = ns.shareSettings()
  if not s.enabled or s.blocked[f.tag] then return false end
  local contact = s.contacts[f.tag]
  if not (contact and contact.share) then return false end
  if not ns.isCharShared(s, UnitGUID("player")) then return false end
  local allowed = ns.allowedFor(s, contact)
  return allowed.map and (allowed.otherFaction or sameFaction(f.faction))
end

local function moved(r, last)
  return r.mapID ~= last.mapID or r.x ~= last.x or r.y ~= last.y
end

-- r nil: one empty POS, only if they had a position from us. Else the reading, when it's new to
-- them and moved, or RESEND after the last one they got.
local function sendTo(account, w, r, rate, now)
  if not r then
    if w.hadPosition and not w.cleared and ns.comms.send(account, "POS", "") then w.cleared, w.sent = true, nil end
    return
  end
  if w.sent == r then return end
  if w.sent and not moved(r, w.sent) and now - w.sentAt < RESEND then return end
  if ns.comms.send(account, "POS", string.format("%d,%.3f,%.3f,%d,%d", r.mapID, r.x, r.y, rate, now - r.at)) then
    w.sent, w.sentAt, w.hadPosition, w.cleared = r, now, true, false
  end
end

-- Each watcher gets what they may. True while anyone still watches. Nothing is queued while Comms
-- holds everything back (combat, a boss fight): it would go out late with the wrong age.
function ns.friendMapTick()
  local now, rate = time(), ns.shareSettings().mapRate
  local paused = ns.comms.canSend and not ns.comms.canSend()
  local r, looked, any = nil, false, false
  for account, w in pairs(watchers) do
    if now > w.untilAt then
      watchers[account] = nil
    else
      any = true
      if not paused then
        if not looked then r, looked = currentReading(now, rate), true end
        local f = friendByAccount(account)
        sendTo(account, w, (f and mayGet(f)) and r or nil, rate, now)
      end
    end
  end
  return any
end

local function keepTicking()
  if ticking then return end
  ticking = true
  C_Timer.After(TICK, function()
    ticking = false
    if ns.friendMapTick() then keepTicking() end
  end)
end

-- A contact has their map open: answer now, then every TICK while the watch lasts.
ns.onFriendKind("WATCH", function(f)
  local w = watchers[f.account] or {}
  w.untilAt = time() + WATCH_LASTS
  watchers[f.account] = w
  ns.friendMapTick()
  keepTicking()
end)

-- Receiving -----------------------------------------------------------------------------------
-- Kept only from a contact we keep things from, and only while our WATCH to them is recent.
ns.onFriendKind("POS", function(f, contact, body)
  if not contact.receive then return end
  local asked = watchSentAt[f.account]
  if not asked or time() - asked > WATCH_LASTS then return end
  if body == nil or body == "" then
    if positions[f.account] then
      positions[f.account] = nil
      pinsChanged()
    end
    return
  end
  if #body > 64 then return end
  local mapID, x, y, rate, age = string.match(body, "^(%d+),([%d%.]+),([%d%.]+),(%d+),(%d+)$")
  mapID, x, y, rate, age = tonumber(mapID), tonumber(x), tonumber(y), tonumber(rate), tonumber(age)
  if not (mapID and x and y and rate and age) or mapID <= 0 or x < 0 or x > 1 or y < 0 or y > 1 or not RATES[rate] then return end
  -- The character it belongs to: another one logged in later doesn't get this spot.
  local row = friendByAccount(f.account)
  positions[f.account] = { mapID = mapID, x = x, y = y, rate = rate, readAt = time() - age, guid = row and row.guid }
  pinsChanged()
end)

-- Faded after the first, gone after the second (seconds), by the sender's rate.
local function limits(rate) return math.max(30, 2 * rate), math.max(120, 4 * rate) end

-- A realm name as Battle.net writes it: no spaces, hyphens or apostrophes ("TarrenMill"). The game
-- writes ours with them ("CAMWOW EU5 Realm 6 PvP"), so both go through this before comparing.
local function plainRealm(name)
  if type(name) ~= "string" or name == "" then return nil end
  return (name:gsub("[%s%-']", ""))
end

-- Forever's realms end in their ruleset after a number ("CAMWOWEU5Realm6PvP"); nil for other names.
local RULESETS = { { "RPPVP", "RP-PvP" }, { "PVP", "PvP" }, { "PVE", "PvE" }, { "NORMAL", "Normal" }, { "RP", "RP" } }
local function ruleset(plain)
  local upper = plain:upper()
  if not upper:match("REALM%d+%a+$") then return nil end
  for _, r in ipairs(RULESETS) do
    if upper:sub(-#r[1]) == r[1] then return r[2] end
  end
  return nil
end

-- How a friend's realm differs from ours, for the tooltip; nil when it's the same. On Forever the
-- number is a layer, so the same ruleset means another layer of it.
local function realmNote(theirs, mine)
  if not theirs or not mine or theirs == mine then return nil end
  if not ns.IS_RETAIL then
    local their, our = ruleset(theirs), ruleset(mine)
    if their and their == our then return "On another layer" end
    if their then return "On another realm: " .. their end
  end
  return "On another realm: " .. theirs
end

-- Still ours to show: sharing on, a contact we keep things from, not blocked, the same character.
local function stillShown(f, p)
  return f ~= nil and f.contact ~= nil and f.contact.receive and not f.blocked and f.guid == p.guid
end

-- What MapPins.lua draws: friends online here with a position that isn't too old, by label.
function ns.friendPins()
  local now, out, rows = time(), {}, {}
  if ns.shareSettings().enabled then
    for _, f in ipairs(online()) do rows[f.account] = f end
  end
  local myRealm = plainRealm(GetRealmName and GetRealmName())
  for account, p in pairs(positions) do
    local fade, gone = limits(p.rate)
    local age = math.max(0, now - p.readAt)
    local f = rows[account]
    if not stillShown(f, p) or age > gone then
      positions[account] = nil
    else
      local note = realmNote(plainRealm(f.realm), myRealm)
      out[#out + 1] = { tag = f.tag, label = f.label, character = f.character, classFile = f.classFile, level = f.level,
        realm = f.realm, otherRealm = note ~= nil, realmNote = note,
        mapID = p.mapID, x = p.x, y = p.y, age = age, faded = age > fade }
    end
  end
  table.sort(out, function(x, y) return x.label < y.label end)
  return out
end

-- WATCH to every contact online here we keep things from, at most every WATCH_EVERY seconds each
-- (a second less, so the renewal timer never just misses it). Not while Comms holds everything back.
-- A new watch after a gap drops what we kept from before: the friend sends it again if they still may.
local function sendWatches()
  if not ns.shareSettings().enabled or (ns.comms.canSend and not ns.comms.canSend()) then return end
  local now, dropped = time(), false
  for _, f in ipairs(online()) do
    local c, last = f.contact, watchSentAt[f.account]
    if c and c.receive and not f.blocked and f.hasAddon ~= false and (not last or now - last >= WATCH_EVERY - 1) then
      if ns.comms.send(f.account, "WATCH", "") then
        watchSentAt[f.account] = now
        if last and now - last > WATCH_LASTS and positions[f.account] then
          positions[f.account], dropped = nil, true
        end
      end
    end
  end
  if dropped then pinsChanged() end
end

local function keepWatching()
  if watchLoop then return end
  watchLoop = true
  C_Timer.After(WATCH_EVERY, function()
    watchLoop = false
    if not mapOpen then return end
    sendWatches()
    keepWatching()
  end)
end

-- MapPins.lua: the world map opened or closed. Closing sends nothing, the watches just run out.
function ns.setMapOpen(open)
  mapOpen = open == true
  if mapOpen then
    sendWatches()
    keepWatching()
  end
end
