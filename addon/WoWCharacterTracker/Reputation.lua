-- Reputations: every faction the character has standing with, saved per character as the snapshot's
-- `reputations` text (saved file only), and a milestone (kind "reputation") when one reaches a standing
-- the app's Sharing page picks (Settings.lua post.reputationFrom, Exalted by default), or a Retail
-- renown faction its top renown. Like profession milestones, the first reading of a faction only sets
-- its baseline, so standing from before the addon posts nothing. Warband (account-wide) factions keep
-- one baseline for the account, so only the first character to see a new standing posts it.
-- Retail's C_Reputation and the older GetFactionInfo both work (each client has one or the other).
-- No frames, so the app's tests load it. The list itself is ReputationWindow.lua.
local ADDON, ns = ...

local EXALTED = 8
local LOWEST_STEP = 5 -- Friendly: the lowest standing the app offers as a milestone

local function num(v)
  v = ns.plain and ns.plain(v) or v
  return type(v) == "number" and v or nil
end

-- The faction list, the same way on both APIs: count(), at(i) as { id, name, standing, header,
-- collapsed, hasRep, low, high, value }, expand(i) and collapse(i). Nil on a client with neither.
local function factionList()
  local R = C_Reputation
  if R and R.GetNumFactions and R.GetFactionDataByIndex then
    return {
      count = R.GetNumFactions,
      at = function(i)
        local d = R.GetFactionDataByIndex(i)
        if type(d) ~= "table" then return nil end
        return { id = num(d.factionID), name = d.name, standing = num(d.reaction), header = d.isHeader, collapsed = d.isCollapsed,
          hasRep = not d.isHeader or d.isHeaderWithRep, low = num(d.currentReactionThreshold), high = num(d.nextReactionThreshold),
          value = num(d.currentStanding) }
      end,
      expand = R.ExpandFactionHeader, collapse = R.CollapseFactionHeader,
    }
  end
  if GetNumFactions and GetFactionInfo then
    return {
      count = GetNumFactions,
      at = function(i)
        local name, _, standing, low, high, value, _, _, header, collapsed, hasRep, _, _, id = GetFactionInfo(i)
        if not name then return nil end
        return { id = num(id), name = name, standing = num(standing), header = header, collapsed = collapsed,
          hasRep = not header or hasRep, low = num(low), high = num(high), value = num(value) }
      end,
      expand = ExpandFactionHeader, collapse = CollapseFactionHeader,
    }
  end
  return nil
end

local function call(fn, ...)
  if type(fn) ~= "function" then return nil end
  local ok, a, b = pcall(fn, ...)
  if ok then return a, b end
  return nil
end

-- Retail renown (major factions): the level and the top level, nil for other factions.
local function renownOf(id)
  local R, M = C_Reputation, C_MajorFactions
  if not (R and M and call(R.IsMajorFaction, id)) then return nil end
  local data = call(M.GetMajorFactionData, id)
  local level = type(data) == "table" and num(data.renownLevel)
  if not level then return nil end
  local levels, top = call(M.GetRenownLevels, id), nil
  local last = type(levels) == "table" and levels[#levels]
  if type(last) == "table" then top = num(last.level) end
  return level, top, num(data.renownReputationEarned), num(data.renownLevelThreshold)
end

-- Retail friendships (a rank name like "Best Friend" in place of a standing): the rank's name, its
-- number and the top one, and the progress through it. Nil for other factions.
local function friendshipOf(id)
  local G = C_GossipInfo
  local f = G and call(G.GetFriendshipReputation, id)
  if type(f) ~= "table" or (num(f.friendshipFactionID) or 0) <= 0 then return nil end
  local ranks = call(G.GetFriendshipReputationRanks, id)
  local rank, top = nil, nil
  if type(ranks) == "table" then rank, top = num(ranks.currentLevel), num(ranks.maxLevel) end
  local base, standing, nextAt = num(f.reactionThreshold), num(f.standing), num(f.nextThreshold)
  local cur = base and standing and standing - base or nil
  local max = base and nextAt and nextAt - base or nil
  return tostring(f.reaction or ""), rank, top, cur, max
end

-- One faction as saved: { id, name, standing, cur, max, renown, renownMax, rank, rankMax, label, account }.
local function entry(f)
  local r = { id = f.id, name = f.name, standing = f.standing or 4 }
  if f.low and f.high and f.value then r.cur, r.max = f.value - f.low, f.high - f.low end
  local level, top, earned, need = renownOf(f.id)
  if level then
    r.renown, r.renownMax = level, top
    if earned and need then r.cur, r.max = earned, need end
  else
    local label, rank, rankTop, cur, max = friendshipOf(f.id)
    if label then
      r.label, r.rank, r.rankMax = label, rank, rankTop
      r.cur, r.max = cur, max
    end
  end
  r.account = C_Reputation and call(C_Reputation.IsAccountWideReputation, f.id) and true or nil
  return r
end

-- Every faction with standing, collapsed groups included: those are opened for the read and folded
-- again after, from the bottom up (folding one moves only the rows under it). Nil without the API.
local reading = false
function ns.readReputations()
  local list = factionList()
  if not list or reading then return nil end
  reading = true
  local out, seen, opened = {}, {}, {}
  pcall(function()
    local i = 1
    while i <= (list.count() or 0) and i <= 2000 do
      local f = list.at(i)
      if f and f.header and f.collapsed and list.expand and f.name then
        list.expand(i)
        opened[f.name] = true
        f = list.at(i)
      end
      if f and f.hasRep and f.id and f.id > 0 and not seen[f.id] and type(f.name) == "string" and f.name ~= "" then
        seen[f.id] = true
        out[#out + 1] = entry(f)
      end
      i = i + 1
    end
  end)
  if list.collapse and next(opened) then
    pcall(function()
      for i = list.count() or 0, 1, -1 do
        local f = list.at(i)
        if f and f.header and not f.collapsed and opened[f.name] then list.collapse(i) end
      end
    end)
  end
  reading = false
  return out
end

-- The saved text: a line per faction, "id~standing~cur~max~extra~name", the name last so it can hold
-- anything. extra: "R12/25" for renown (the top left out when unknown), "F3/6:Best Friend" for a
-- friendship (the numbers left out when unknown), else empty. The app reads it the same way
-- (Reputations.cs).
local function whole(n) return n and tostring(math.floor(n)) or "" end

function ns.encodeReputations(list)
  local lines = {}
  for _, r in ipairs(list or {}) do
    local extra = ""
    if r.renown then
      extra = "R" .. whole(r.renown) .. (r.renownMax and ("/" .. whole(r.renownMax)) or "")
    elseif r.label then
      extra = "F" .. (r.rank and (whole(r.rank) .. (r.rankMax and ("/" .. whole(r.rankMax)) or "")) or "") .. ":" .. r.label:gsub("[~\n]", " ")
    end
    lines[#lines + 1] = table.concat({ whole(r.id), whole(r.standing), whole(r.cur), whole(r.max), extra, (r.name:gsub("\n", " ")) }, "~")
  end
  return table.concat(lines, "\n")
end

-- The text back as entries (bad lines skipped), in the order they came.
function ns.parseReputations(text)
  local out = {}
  for line in string.gmatch(type(text) == "string" and text or "", "[^\n]+") do
    local id, standing, cur, max, extra, name = line:match("^(%d+)~(%d*)~(%-?%d*)~(%-?%d*)~([^~]*)~(.+)$")
    if id then
      local r = { id = tonumber(id), name = name, standing = tonumber(standing) or 4, cur = tonumber(cur), max = tonumber(max) }
      local level, top = extra:match("^R(%d+)/?(%d*)$")
      if level then
        r.renown, r.renownMax = tonumber(level), tonumber(top)
      else
        local rank, rankTop, label = extra:match("^F(%d*)/?(%d*):(.*)$")
        if label then r.rank, r.rankMax, r.label = tonumber(rank), tonumber(rankTop), label end
      end
      out[#out + 1] = r
    end
  end
  return out
end

-- The newest reading's text, for the snapshot (Character.lua), nil before the first.
function ns.reputationText() return ns.lastReputationText end

-- The standings from the app's lowest milestone (5 Friendly to 8 Exalted) up.
local function milestoneSteps()
  local from = tonumber(((ns.setting and ns.setting("post")) or {}).reputationFrom) or EXALTED
  from = math.max(LOWEST_STEP, math.min(EXALTED, math.floor(from)))
  local steps = {}
  for s = from, EXALTED do steps[#steps + 1] = s end
  return steps
end

-- Reads the factions, saves them with the character and records what passed a milestone.
function ns.checkReputations()
  local guid = UnitGUID("player")
  local db = WoWCharacterTrackerDB
  if not guid or not db then return end
  local list = ns.readReputations()
  if not list or #list == 0 then return end
  local text = ns.encodeReputations(list)
  ns.lastReputationText = text
  local saved = db.chars and db.chars[guid]
  if saved then saved.reputations = text end

  local steps = milestoneSteps()
  db.reputations = db.reputations or {}
  local mine = db.reputations[guid] or {}
  db.reputations[guid] = mine
  db.reputationsAccount = db.reputationsAccount or {}
  for _, r in ipairs(list) do
    local known = r.account and db.reputationsAccount or mine
    known.standing, known.renown = known.standing or {}, known.renown or {}
    local oldStanding, oldRenown = known.standing[r.id], known.renown[r.id]
    if r.renown then
      -- Renown: the top level, when the game says which that is.
      if oldRenown and r.renownMax and oldRenown < r.renownMax and r.renown >= r.renownMax then
        ns.recordOnce("reputation", { reputation = r.name, factionId = r.id, renown = r.renown },
          string.format("reputation:%d:renown", r.id))
      end
      known.renown[r.id] = r.renown
    elseif not r.label then -- friendships have ranks of their own, not standings
      local step = oldStanding and ns.stepPassed(oldStanding, r.standing, steps)
      if step then
        ns.recordOnce("reputation", { reputation = r.name, factionId = r.id, step = step },
          string.format("reputation:%d:%d", r.id, step))
      end
    end
    known.standing[r.id] = r.standing
  end
end
