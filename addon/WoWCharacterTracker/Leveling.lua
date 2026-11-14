-- Numbers from a character's level times, for the window's Home tab and a friend's details: the
-- timed levels, average, fastest and slowest, and the played time left to max level at the recent
-- pace (the app's LevelStats, same method). No game calls, so the app's tests load it.
local ADDON, ns = ...

-- XP needed at each level in Classic (index = level, 1-59), for levels the client hasn't shown yet.
local CLASSIC_XP = {
  400, 900, 1400, 2100, 2800, 3600, 4500, 5400, 6500, 7600, 8800, 10100, 11400, 12900, 14400, 16000, 17700, 19400, 21300,
  23200, 25200, 27300, 29400, 31700, 34000, 36400, 38900, 41400, 44300, 47400, 50800, 54500, 58600, 62800, 67100, 71600, 76100,
  80800, 85700, 90700, 95800, 101000, 106300, 111800, 117500, 123200, 129100, 135100, 141200, 147500, 153900, 160400, 167100,
  173900, 180800, 187900, 195000, 202300, 209800,
}

-- A character's timed levels, oldest first: { level, seconds, at }, the newest record per level.
-- events: any list of events (other characters and kinds are skipped).
function ns.levelTimes(events, guid)
  local byLevel = {}
  for _, e in ipairs(events or {}) do
    if type(e) == "table" and (e.kind == nil or e.kind == "level") and e.guid == guid
      and type(e.level) == "number" and type(e.seconds) == "number" and e.seconds > 0 then
      byLevel[e.level] = { level = e.level, seconds = e.seconds, at = e.at or 0, zone = e.zone }
    end
  end
  local out = {}
  for _, l in pairs(byLevel) do out[#out + 1] = l end
  table.sort(out, function(a, b) return a.level < b.level end)
  return out
end

-- { count, average, fastest, slowest } (fastest and slowest are entries of levels), nil without any.
function ns.levelSummary(levels)
  if not levels or #levels == 0 then return nil end
  local total, fastest, slowest = 0, levels[1], levels[1]
  for _, l in ipairs(levels) do
    total = total + l.seconds
    if l.seconds < fastest.seconds then fastest = l end
    if l.seconds > slowest.seconds then slowest = l end
  end
  return { count = #levels, average = math.floor(total / #levels), fastest = fastest, slowest = slowest }
end

-- "44 to 45 took 2h 54m" for a timed level.
function ns.levelText(l)
  return string.format("%d to %d took %s", l.level - 1, l.level, ns.formatDuration(l.seconds))
end

-- How far into its level a character is, in whole percent (the game's XP bar), or nil: not known,
-- at max level, or the XP is from another level than `level` (default: its own).
function ns.levelPercent(c, level)
  level = level or (c and c.level)
  if not (c and type(c.xp) == "number" and type(c.xpMax) == "number" and c.xpMax > 0 and level) then return nil end
  if c.level and c.level ~= level then return nil end
  if c.maxLevel and level >= c.maxLevel then return nil end
  return math.floor(100 * c.xp / c.xpMax)
end

-- The leveling lines a character's details list, each { label, value }, only what's known.
-- c: { level, played, levelTime, xp, xpMax, maxLevel }; xpTable: this client's XP per level, or nil.
function ns.levelingFacts(levels, c, xpTable)
  local out = {}
  local function add(label, value) if value then out[#out + 1] = { label, value } end end
  local summary = ns.levelSummary(levels)
  if summary then
    add("Last level", ns.levelText(levels[#levels]))
    add("Average", string.format("%s over %d level%s", ns.formatDuration(summary.average), summary.count, summary.count == 1 and "" or "s"))
    if summary.count > 1 then
      add("Slowest", string.format("%s (%d to %d)", ns.formatDuration(summary.slowest.seconds), summary.slowest.level - 1, summary.slowest.level))
    end
  end
  local atMax = c.level and c.maxLevel and c.level >= c.maxLevel
  local percent = ns.levelPercent(c)
  if percent then add("Progress", string.format("%d%% into level %d", percent, c.level)) end
  if not atMax and type(c.levelTime) == "number" and c.levelTime > 0 then add("This level", ns.formatDuration(c.levelTime)) end
  if type(c.played) == "number" and c.played > 0 then add("Played", ns.formatPlayed(c.played)) end
  local left = not ns.IS_RETAIL and ns.timeToMax(levels, xpTable, c.level, c.xp, c.xpMax, c.maxLevel)
  if left then add("To max level", string.format("About %s played to level %d", ns.formatPlayed(left), c.maxLevel)) end
  return out
end

-- How the client's XP compares to Classic's, from the levels it has shown (1 when none).
local function scale(xpTable)
  local ratios = {}
  for level, xp in pairs(xpTable or {}) do
    if type(level) == "number" and CLASSIC_XP[level] and type(xp) == "number" then ratios[#ratios + 1] = xp / CLASSIC_XP[level] end
  end
  if #ratios == 0 then return 1 end
  table.sort(ratios)
  return ratios[math.floor(#ratios / 2) + 1]
end

-- Played seconds left to maxLevel at the recent pace, or nil with too little to go on (fewer than two
-- timed levels, or levels past Classic's table that the client hasn't shown). Monsters give more XP
-- as you level, so the pace is counted in kills: a level's XP over a kill's XP at that level.
function ns.timeToMax(levels, xpTable, level, xp, xpMax, maxLevel)
  if not (level and maxLevel) or maxLevel <= level or #(levels or {}) < 2 then return nil end
  local s = scale(xpTable)
  local function need(l)
    local known = xpTable and xpTable[l]
    if type(known) == "number" and known > 0 then return known end
    return CLASSIC_XP[l] and CLASSIC_XP[l] * s or nil
  end
  local function kill(l) return 45 + 5 * l end

  -- Level N's time is the climb from N-1 to N, which takes the XP level N-1 needs.
  local seconds, kills = 0, 0
  for i = #levels, math.max(1, #levels - 4), -1 do
    local l = levels[i]
    local n = need(l.level - 1)
    if not n then return nil end
    seconds = seconds + l.seconds
    kills = kills + n / kill(l.level - 1)
  end
  if kills == 0 then return nil end

  local left = ((xpMax or 0) > 0 and math.max(0, xpMax - (xp or 0)) or (need(level) or 0)) / kill(level)
  for l = level + 1, maxLevel - 1 do
    local n = need(l)
    if not n then return nil end
    left = left + n / kill(l)
  end
  return math.floor(left * seconds / kills)
end
