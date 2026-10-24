-- Duels, Mak'gora and rating milestones.
local ADDON, ns = ...

-- Duels ----------------------------------------------------------------------

-- Turns a game string like "%1$s has defeated %2$s in a duel" into a Lua pattern, plus the order
-- its arguments appear in (so translated strings with swapped arguments still work).
local function toPattern(fmt)
  local order, out, i = {}, {}, 1
  while i <= #fmt do
    local n = fmt:match("^%%(%d)%$s", i)
    if n then
      order[#order + 1] = tonumber(n); out[#out + 1] = "(.-)"; i = i + 4
    elseif fmt:sub(i, i + 1) == "%s" then
      order[#order + 1] = #order + 1; out[#out + 1] = "(.-)"; i = i + 2
    else
      local c = fmt:sub(i, i)
      out[#out + 1] = c:match("%W") and ("%" .. c) or c
      i = i + 1
    end
  end
  return "^" .. table.concat(out) .. "$", order
end

-- Returns the string's arguments in argument order, or nil when it doesn't match.
local function matchFormat(text, compiled)
  local caps = { text:match(compiled[1]) }
  if #caps == 0 then return nil end
  local args = {}
  for k, idx in ipairs(compiled[2]) do args[idx] = caps[k] end
  return args
end

local duelPatterns, makgoraPatterns
local function buildDuelPatterns()
  duelPatterns, makgoraPatterns = {}, {}
  -- Argument 1 is the winner in both: "%1$s has defeated %2$s in a duel", "%2$s has fled from %1$s in a duel".
  for _, name in ipairs({ "DUEL_WINNER_KNOCKOUT", "DUEL_WINNER_RETREAT" }) do
    local s = _G[name]
    if type(s) == "string" then
      local p, order = toPattern(s)
      duelPatterns[#duelPatterns + 1] = { p, order, fled = name == "DUEL_WINNER_RETREAT" }
    end
  end
  -- Hardcore Mak'gora (duel to the death): the game's own strings, whatever they're called.
  for k, v in pairs(_G) do
    if type(k) == "string" and type(v) == "string" and #v < 200 and select(2, v:gsub("%%", "")) >= 2
        and (v:find("Mak'gora") or v:find("to the death")) and (v:find("defeated") or v:find("slain") or v:find("won")) then
      local p, order = toPattern(v)
      makgoraPatterns[#makgoraPatterns + 1] = { p, order }
    end
  end
end

local function isMe(name)
  if not name then return false end
  local me, surname = UnitName("player")
  name = ns.withoutRealm(name)
  return name == me or (surname and name == me .. " " .. surname)
end

-- The opponent's "level~CLASSFILE~Class~Race", from a unit the game has for them right now
-- (target, focus, mouseover, party, nameplates), else from when they challenged us. Other players'
-- spec would need an inspect, so it isn't here.
local seenOpponents = {}
local plain = ns.plain
local function opponentInfo(name)
  if not name then return nil end
  name = ns.withoutRealm(name)
  local units = { "target", "focus", "mouseover", "targettarget" }
  for i = 1, 4 do units[#units + 1] = "party" .. i end
  for i = 1, 40 do units[#units + 1] = "nameplate" .. i end
  for _, u in ipairs(units) do
    local ok, info = pcall(function()
      if not (UnitExists(u) and UnitIsPlayer(u)) then return nil end
      local n, second = UnitName(u) -- second: the realm, or the surname on Forever
      n, second = plain(n), plain(second)
      if not n or (n ~= name and not (second and second ~= "" and n .. " " .. second == name)) then return nil end
      local level = plain(UnitLevel(u))
      local class, classFile = UnitClass(u)
      local race = UnitRace(u)
      return string.format("%s~%s~%s~%s", (level and level > 0) and level or "??",
        plain(classFile) or "", plain(class) or "", plain(race) or "")
    end)
    if ok and info then return info end
  end
  return seenOpponents[name]
end

-- DUEL_REQUESTED: someone challenged us; they're usually close and visible now, maybe not at the end.
function ns.onDuelRequested(name)
  if name then seenOpponents[ns.withoutRealm(name)] = opponentInfo(name) end
end

-- lost: this character lost (to opponent). fled: the loser ran away (the opponent, or this
-- character when lost is set).
local function recordDuel(kind, opponent, fled, lost)
  ns.record(kind, { opponent = opponent, opponentInfo = opponentInfo(opponent), fled = fled or nil, lost = lost or nil }) -- "duel" or "makgora"
end

-- Argument 1 is the winner, argument 2 the loser, in every duel and Mak'gora message.
function ns.onSystemMessage(text)
  if not text then return end
  if not duelPatterns then buildDuelPatterns() end
  for _, c in ipairs(makgoraPatterns) do
    local a = matchFormat(text, c)
    if a and isMe(a[1]) then recordDuel("makgora", a[2]); return end
    if a and isMe(a[2]) then recordDuel("makgora", a[1], nil, true); return end
  end
  for _, c in ipairs(duelPatterns) do
    local a = matchFormat(text, c)
    if a and isMe(a[1]) then recordDuel("duel", a[2], c.fled); return end
    if a and isMe(a[2]) then recordDuel("duel", a[1], c.fled, true); return end
  end
end

-- Battlegrounds --------------------------------------------------------------

-- When a battleground ends the game knows the winning team (GetBattlefieldWinner: 0 Horde, 1
-- Alliance, nil while it's on). Recorded once per visit as kind "bg" with the battleground's name
-- and lost when the other team won. Arenas don't count, and a draw records nothing.
local bgRecorded = false
function ns.checkBattleground()
  local inInstance, instanceType = false, nil
  if IsInInstance then inInstance, instanceType = IsInInstance() end
  if not inInstance or instanceType ~= "pvp" then bgRecorded = false; return end
  if bgRecorded or type(GetBattlefieldWinner) ~= "function" then return end
  local winner = GetBattlefieldWinner()
  if winner ~= 0 and winner ~= 1 then return end
  -- Our team: GetBattlefieldArenaFaction follows retail's mercenary mode; else the faction.
  local mine = type(GetBattlefieldArenaFaction) == "function" and GetBattlefieldArenaFaction() or nil
  if mine ~= 0 and mine ~= 1 then mine = UnitFactionGroup("player") == "Horde" and 0 or 1 end
  bgRecorded = true
  ns.record("bg", { instance = GetInstanceInfo and GetInstanceInfo() or GetZoneText(), lost = winner ~= mine or nil })
end

-- Rating milestones (retail) -------------------------------------------------

local PVP_BRACKETS = { [1] = "2v2", [2] = "3v3", [4] = "Rated Battlegrounds", [7] = "Solo Shuffle", [9] = "Battleground Blitz" }
local DEFAULT_MPLUS_STEPS = { 2000, 2500, 3000, 3500, 4000 }
local DEFAULT_PVP_STEPS = { 1800, 2100, 2400, 2700 }

-- The highest step passed going from old to new (only upward), or nil.
local function stepPassed(old, new, steps)
  local best
  for _, s in ipairs(steps) do
    if old < s and new >= s and (not best or s > best) then best = s end
  end
  return best
end

local function recordRating(kind, rating, step, bracket)
  -- Once per step and season: a new season can post the same step again.
  local season = (kind == "mplus" and C_MythicPlus and C_MythicPlus.GetCurrentSeason and C_MythicPlus.GetCurrentSeason())
    or (kind == "pvp" and GetCurrentArenaSeason and GetCurrentArenaSeason()) or 0
  ns.record(kind, { rating = rating, step = step, bracket = bracket }, -- "mplus" or "pvp"
    string.format("%s:%s:%d:s%s", kind, bracket or "", step, tostring(season)))
end

-- The first reading per character only sets the baseline, so nothing is posted for ratings that
-- were already there before the addon.
function ns.checkRatings()
  local guid = UnitGUID("player")
  if not guid or not WoWCharacterTrackerDB then return end
  local post = ns.setting("post") or {}
  WoWCharacterTrackerDB.ratings = WoWCharacterTrackerDB.ratings or {}
  local known = WoWCharacterTrackerDB.ratings[guid] or {}
  WoWCharacterTrackerDB.ratings[guid] = known

  if C_ChallengeMode and C_ChallengeMode.GetOverallDungeonScore then
    local score = C_ChallengeMode.GetOverallDungeonScore() or 0
    if known.mplus then
      local step = stepPassed(known.mplus, score, post.mplusSteps or DEFAULT_MPLUS_STEPS)
      if step then recordRating("mplus", score, step) end
    end
    known.mplus = score
  end
  if type(GetPersonalRatedInfo) == "function" then
    for index, name in pairs(PVP_BRACKETS) do
      local ok, rating = pcall(GetPersonalRatedInfo, index)
      if ok and type(rating) == "number" then
        local key = "pvp" .. index
        if known[key] then
          local step = stepPassed(known[key], rating, post.pvpSteps or DEFAULT_PVP_STEPS)
          if step then recordRating("pvp", rating, step, name) end
        end
        known[key] = rating
      end
    end
  end
end
