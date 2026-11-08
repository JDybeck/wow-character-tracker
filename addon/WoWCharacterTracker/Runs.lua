-- Dungeon runs: with the app's "Dungeon runs" on (Settings.lua post.run), a 5-player dungeon's boss
-- kills and loot go into one event, kind "run", instead of a post each. A run starts on entering
-- the dungeon and ends a few minutes after its end boss (the boss's loot and rolls come after the
-- kill), on leaving after it, or a while after leaving without it (a corpse run comes back). Kept
-- per character in WoWCharacterTrackerDB.runs, so a /reload or logout inside keeps it going.
-- Fields: instance, seconds, bosses ("Name^Name"), drops ("who~itemId~quality~name^...", who is ""
-- for this character), cleared, group, groupSize. Its boss and loot events carry run (its id).
-- Not on Retail, which has Mythic+ runs.
local ADDON, ns = ...

local LOOT_AFTER = 180 -- after the end boss: loot, rolls and the walk out
local AWAY = 900       -- out without the end boss: back by then (a corpse run, a summon) is the same run
local STALE = 7200     -- inside with nothing happening for this long: a new visit
local MAX_DROPS = 40   -- room in the post

local function wanted()
  local post = ns.setting("post")
  return not ns.IS_RETAIL and type(post) == "table" and post.run == true
end

local function runs()
  WoWCharacterTrackerDB.runs = WoWCharacterTrackerDB.runs or {}
  return WoWCharacterTrackerDB.runs
end

-- The 5-player dungeon we're in, or nil.
local function dungeonNow()
  if not (IsInInstance and GetInstanceInfo) then return nil end
  local inside, kind = IsInInstance()
  if not inside or kind ~= "party" then return nil end
  return ns.plain((GetInstanceInfo()))
end

-- This character's run, while inside its dungeon.
local function active()
  local guid = UnitGUID and UnitGUID("player")
  local run = guid and WoWCharacterTrackerDB and WoWCharacterTrackerDB.runs and WoWCharacterTrackerDB.runs[guid]
  if run and dungeonNow() == run.instance then return run end
  return nil
end

-- Something happened: the run is alive, and the party is kept while it's there (people leave at the end).
local function touch(run)
  run.lastAt = time()
  local group, size = ns.getGroup and ns.getGroup()
  if group and group ~= "" then run.group, run.groupSize = group, size end
end

local function finish(guid)
  local run = runs()[guid]
  runs()[guid] = nil
  if not run or (#run.bosses == 0 and #run.drops == 0) then return end
  local ended = run.clearedAt or run.lastAt or run.startedAt
  ns.record("run", {
    instance = run.instance, seconds = math.max(0, ended - run.startedAt),
    bosses = table.concat(run.bosses, "^"), drops = table.concat(run.drops, "^"),
    cleared = run.clearedAt and true or nil, group = run.group, groupSize = run.groupSize,
  }, "run:" .. run.id)
  ns.say(string.format("Dungeon run saved: %s, %d boss%s, %d item%s.", run.instance, #run.bosses,
    #run.bosses == 1 and "" or "es", #run.drops, #run.drops == 1 and "" or "s"))
end

-- Starts, keeps or ends this character's run by where they are. From loading screens, the logout
-- (loggingOut) and timers.
function ns.checkRun(loggingOut)
  local guid = UnitGUID and UnitGUID("player")
  if not guid or not WoWCharacterTrackerDB then return end
  local run, now, here = runs()[guid], time(), dungeonNow()
  if run then
    local inside = here == run.instance
    local dead = UnitIsDeadOrGhost and UnitIsDeadOrGhost("player")
    if run.clearedAt and (not inside or now - run.clearedAt >= LOOT_AFTER) then
      finish(guid)
    elseif (here and not inside) or (inside and now - (run.lastAt or run.startedAt) >= STALE) then
      finish(guid) -- another dungeon, or a new visit
    elseif inside then
      run.awayAt = nil
    else
      run.awayAt = run.awayAt or now
      if now - run.awayAt >= AWAY or (loggingOut and not dead) then
        finish(guid)
      elseif C_Timer then
        C_Timer.After(AWAY - (now - run.awayAt) + 1, function() ns.checkRun() end)
      end
    end
    run = runs()[guid]
  end
  if not run and here and wanted() and not loggingOut then
    runs()[guid] = { id = now, instance = here, startedAt = now, lastAt = now, bosses = {}, drops = {} }
  end
end

-- The run's id for events that belong to it (and wait for its post), else nil.
function ns.runId()
  local run = active()
  return run and run.id or nil
end

-- From recordBoss: any boss kill. isEnd: the dungeon's last boss. Returns the run's id, or nil.
function ns.runBossDown(name, isEnd)
  if not active() then ns.checkRun() end -- a /reload inside before the loading screen's check
  local run = active()
  if not run then return nil end
  table.insert(run.bosses, name)
  touch(run)
  if isEnd and not run.clearedAt then
    run.clearedAt = time()
    if C_Timer then C_Timer.After(LOOT_AFTER + 1, function() ns.checkRun() end) end
  end
  return run.id
end

-- "You receive loot: %s." and "%s receives loot: %s." (plus the stack variants) as patterns, with
-- each capture's argument number, so this works in the client's own language.
local function patterns(...)
  local list = {}
  for i = 1, select("#", ...) do
    local fmt = select(i, ...)
    if type(fmt) == "string" then
      local pattern, order = ns.formatPattern(fmt)
      table.insert(list, { pattern = pattern, order = order })
    end
  end
  return list
end
local SELF = patterns(LOOT_ITEM_SELF_MULTIPLE, LOOT_ITEM_SELF)
local OTHERS = patterns(LOOT_ITEM_MULTIPLE, LOOT_ITEM)

-- The format's arguments by number, from the first pattern that matches, or nil.
local function matchArgs(list, text)
  for _, p in ipairs(list) do
    local caps = { text:match(p.pattern) }
    if #caps > 0 then
      local args = {}
      for i, n in ipairs(p.order) do args[n] = caps[i] end
      return args
    end
  end
  return nil
end

-- From CHAT_MSG_LOOT: rare and better (or followed) loot anyone in the party got, while in a run.
function ns.runLoot(text)
  text = ns.plain(text)
  if type(text) ~= "string" then return end
  local run = active()
  if not run then return end
  local who, link
  local mine = matchArgs(SELF, text)
  if mine then
    who, link = "", mine[1]
  else
    local theirs = matchArgs(OTHERS, text)
    if not theirs then return end
    who, link = ns.withoutRealm(theirs[1]), theirs[2]
  end
  if type(link) ~= "string" then return end
  local itemId = tonumber(link:match("item:(%d+)"))
  local name = link:match("%[(.-)%]")
  if not itemId or not name then return end
  local quality = ns.itemQuality(link)
  if not ns.isFollowed(itemId) and (not quality or quality < 3) then return end
  if #run.drops >= MAX_DROPS then return end
  table.insert(run.drops, table.concat({ who or "", itemId, quality or 0, (name:gsub("[~^]", "")) }, "~"))
  touch(run)
end
