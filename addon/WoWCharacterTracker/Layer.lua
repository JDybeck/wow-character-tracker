-- Which layer we're on (not on Retail). The game has no layer API, but a creature's GUID is
-- Creature-0-<server>-<map>-<zoneUID>-<npc>-<spawn>, and on Forever everyone on one layer sees the
-- same server part (zoneUID can still differ between them). The realm name's "Realm 6" is the home
-- realm and stays the same when you change layer.
-- Read from creatures passing by (nameplates, target, mouseover), never pets or totems: they keep
-- the server they were summoned on. Each creature counts once, and a new layer only counts once
-- AGREE different creatures show it, so the old layer's nameplates still in view after a swap
-- don't flip it back. A loading screen forgets it.
local ADDON, ns = ...

local AGREE = 2
local SEEN_MAX = 500 -- creatures remembered; past this the list starts over

local current   -- { server, map, at, votes }
local candidate -- { server, map, votes }
local seen, seenCount = {}, 0
local lastGuid

-- The server and map parts of a creature's GUID, or nil for anything else.
function ns.layerFromGuid(guid)
  if type(guid) ~= "string" then return nil end
  local kind, server, map = guid:match("^(%a+)%-%d+%-(%d+)%-(%d+)%-%d+%-%d+%-")
  if kind ~= "Creature" and kind ~= "Vehicle" then return nil end
  return tonumber(server), tonumber(map)
end

local function remember(guid)
  if seenCount >= SEEN_MAX then seen, seenCount = {}, 0 end
  seen[guid], seenCount = true, seenCount + 1
end

local function playerControlled(unit)
  if not UnitPlayerControlled then return false end
  local ok, pc = pcall(UnitPlayerControlled, unit)
  return ok and ns.plain(pc) == true
end

-- A unit the game just showed us (NAME_PLATE_UNIT_ADDED, PLAYER_TARGET_CHANGED, UPDATE_MOUSEOVER_UNIT).
function ns.observeLayer(unit)
  if ns.IS_RETAIL or (IsInInstance and IsInInstance()) then return end
  local ok, guid = pcall(UnitGUID, unit)
  guid = ok and ns.plain(guid) or nil
  if type(guid) ~= "string" or seen[guid] then return end
  local server, map = ns.layerFromGuid(guid)
  if not server or playerControlled(unit) then return end
  remember(guid)
  lastGuid = guid
  if current and current.server == server and current.map == map then
    current.votes, candidate = current.votes + 1, nil
    return
  end
  if candidate and candidate.server == server and candidate.map == map then
    candidate.votes = candidate.votes + 1
  else
    candidate = { server = server, map = map, votes = 1 }
  end
  if candidate.votes >= AGREE then
    current = { server = server, map = map, at = time(), votes = candidate.votes }
    candidate = nil
  end
end

-- PLAYER_ENTERING_WORLD: a loading screen can put us on another continent or layer.
function ns.forgetLayer()
  current, candidate, lastGuid = nil, nil, nil
  seen, seenCount = {}, 0
end

-- Our layer as server and map, or nil while not known.
function ns.currentLayer()
  if not current then return nil end
  return current.server, current.map
end

-- One line for /wct layer and /wct debug.
function ns.layerStatus()
  if ns.IS_RETAIL then return "Layers are only read outside Retail." end
  local last = lastGuid and (" Last creature: " .. lastGuid) or ""
  if not current then
    return "Layer not known yet. Target or mouse over a couple of NPCs (not pets)." .. last
  end
  return string.format("Layer %d on map %d, from %d creatures since %s.%s", current.server, current.map,
    current.votes, date("%H:%M", current.at), last)
end
