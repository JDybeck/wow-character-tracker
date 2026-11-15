-- Removing saved data. The game owns the saved file and writes it back on logout and /reload, so the
-- app can't edit it: its Data page writes cleanup orders into Settings.lua instead, and the addon
-- applies each one once per account at ADDON_LOADED (applyCleanups), before anything new is recorded.
-- Forever's stress test (before launch on 2026-11-05) left characters behind; they go the same way,
-- once per account at login (cleanStressTest).
-- No game calls, so the app's tests load it.
local ADDON, ns = ...

ns.STRESS_TEST_BEFORE = 1793746800 -- 2026-11-04 00:00 Swedish time

-- Tables keyed by character GUID.
local BY_GUID = { "chars", "items", "notes", "start", "startLevel", "logoutPlayed", "professions", "recipes", "today", "days", "runs" }

-- Keeps only the events keep(e) says yes to, in order.
local function keepEvents(db, keep)
  if type(db.events) ~= "table" then return end
  local kept = {}
  for _, e in ipairs(db.events) do
    if keep(e) then kept[#kept + 1] = e end
  end
  db.events = kept
end

-- Everything saved about these characters (gone = { [guid] = true }).
local function removeCharacters(db, gone)
  for _, name in ipairs(BY_GUID) do
    local t = db[name]
    if type(t) == "table" then
      for guid in pairs(gone) do t[guid] = nil end
    end
  end
  keepEvents(db, function(e) return not gone[e.guid] end)
end

-- Friends' characters received before `before`.
local function removeFriendsBefore(db, before)
  for _, friend in pairs(db.friends or {}) do
    for guid, c in pairs(friend.chars or {}) do
      if (c.at or 0) < before then friend.chars[guid] = nil end
    end
  end
end

-- Removes what's from before `before` (keep: the GUID being played). Returns how many characters went.
function ns.cleanStressTest(db, before, keep)
  local newest = {} -- [guid] = its newest event
  for _, e in ipairs(db.events or {}) do
    if e.guid then newest[e.guid] = math.max(newest[e.guid] or 0, e.at or 0) end
  end
  local gone, count = {}, 0
  local function old(guid, seen)
    if guid == keep or gone[guid] or seen >= before or (newest[guid] or 0) >= before then return end
    gone[guid] = true
    count = count + 1
  end
  for guid, c in pairs(db.chars or {}) do old(guid, c.lastSeen or 0) end
  for guid, at in pairs(newest) do
    if not (db.chars and db.chars[guid]) then old(guid, at) end
  end
  removeCharacters(db, gone)
  removeFriendsBefore(db, before)
  return count
end

-- One order (the app's CleanupOrder): chars = { [guid] = true } go completely; events of `kinds`
-- older than `before` (an ask event counts as the kind it asked about); day stats older than
-- `beforeDay` ("YYYY-MM-DD", the app's local date); friends' characters received before `friendsBefore`.
local function applyOrder(db, o)
  if type(o.chars) == "table" then removeCharacters(db, o.chars) end
  if (o.before or 0) > 0 and type(o.kinds) == "table" then
    keepEvents(db, function(e)
      local kind = e.kind == "ask" and e.askedKind or e.kind
      return not (kind and o.kinds[kind] and (e.at or 0) < o.before)
    end)
  end
  if type(o.beforeDay) == "string" and o.beforeDay ~= "" then
    for _, byDay in pairs(db.days or {}) do
      for day in pairs(byDay) do
        if day < o.beforeDay then byDay[day] = nil end
      end
    end
    for guid, t in pairs(db.today or {}) do
      if type(t) == "table" and (t.day or "") < o.beforeDay then db.today[guid] = nil end
    end
  end
  if (o.friendsBefore or 0) > 0 then removeFriendsBefore(db, o.friendsBefore) end
end

-- Applies the orders this account hasn't had yet (newer than db.cleanedAt). Returns how many.
function ns.applyCleanups(db, orders)
  if type(orders) ~= "table" then return 0 end
  local done, newest, count = db.cleanedAt or 0, db.cleanedAt or 0, 0
  for _, o in ipairs(orders) do
    if type(o) == "table" and (o.at or 0) > done then
      applyOrder(db, o)
      newest = math.max(newest, o.at)
      count = count + 1
    end
  end
  db.cleanedAt = newest
  return count
end
