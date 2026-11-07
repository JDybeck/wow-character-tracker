-- Forever's stress test (before launch on 2026-11-05) left characters behind in the saved data.
-- They go once per account, completely: snapshot, items, events, notes, level timing, and friends'
-- characters from then. The character being played always stays, and so does one with events after.
-- No game calls, so the app's tests load it; WoWCharacterTracker.lua runs it at login on Forever.
local ADDON, ns = ...

ns.STRESS_TEST_BEFORE = 1793746800 -- 2026-11-04 00:00 Swedish time

-- Tables keyed by character GUID.
local BY_GUID = { "chars", "items", "notes", "start", "startLevel", "logoutPlayed", "professions" }

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

  for _, name in ipairs(BY_GUID) do
    local t = db[name]
    if type(t) == "table" then
      for guid in pairs(gone) do t[guid] = nil end
    end
  end
  if db.events then
    local kept = {}
    for _, e in ipairs(db.events) do
      if not gone[e.guid] then kept[#kept + 1] = e end
    end
    db.events = kept
  end
  for _, friend in pairs(db.friends or {}) do
    for guid, c in pairs(friend.chars or {}) do
      if (c.at or 0) < before then friend.chars[guid] = nil end
    end
  end
  return count
end
