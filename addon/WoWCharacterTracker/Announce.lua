-- Tells your guild and/or group about a moment, in the game's own chat: level-ups, max level, end
-- bosses, Mythic+ runs, epic loot (as an item link), new mounts, PvP wins, profession and rating milestones, hardcore deaths.
-- What and where come from Settings.lua `announce` (the app's In game page). Messages wait until
-- you're out of combat, and boss kills don't list the group (they're right there).
local ADDON, ns = ...

-- The item as a clickable link when the game has it, else its name in brackets.
local function itemLink(id, name)
  local link = id and GetItemInfo and select(2, GetItemInfo(id))
  return link or string.format("[%s]", name or "?")
end
ns.itemLink = itemLink

-- The line for an event, or nil when it's nothing to tell (losses, rare loot...).
local function message(e)
  local k = e.kind
  if k == "level" and e.maxLevel then
    return string.format("Reached max level %d!", e.level) .. (e.played and string.format(" Total played %s.", ns.formatPlayed(e.played)) or "")
  elseif k == "level" then
    return string.format("Ding! Level %d.", e.level) .. (e.seconds and string.format(" %d to %d took %s.", e.level - 1, e.level, ns.formatDuration(e.seconds)) or "")
  elseif k == "boss" then
    return string.format("Killed %s%s!", e.boss, (e.instance and e.instance ~= "") and (" in " .. e.instance) or "")
  elseif k == "loot" and (e.quality or 0) >= 4 and not ((e.quality or 0) >= 5 and e.mat) then -- not legendary materials
    return "Looted " .. itemLink(e.itemId, e.item) .. "!"
  elseif k == "mount" then
    return (e.bought and "Bought a new mount: " or "Got a new mount: ") .. (e.itemId and itemLink(e.itemId, e.mount) or e.mount) .. "!"
  elseif k == "duel" and not e.lost then
    return string.format("Won a duel against %s.", e.opponent or "someone")
  elseif k == "makgora" and not e.lost then
    return string.format("Won a Mak'gora against %s!", e.opponent or "someone")
  elseif k == "bg" and not e.lost then
    return string.format("Won %s!", e.instance or "a battleground")
  elseif k == "keyrun" then
    local rule = (ns.setting("post") or {}).keyrun -- the same lowest key level as the Discord posts
    if rule and (e.keyLevel or 0) < (rule.min or 0) then return nil end
    local dungeon = (e.instance and e.instance ~= "") and (" " .. e.instance) or ""
    if (e.upgrades or 0) > 0 then return string.format("Timed +%d%s (+%d)!", e.keyLevel or 0, dungeon, e.upgrades) end
    return string.format("Finished +%d%s.", e.keyLevel or 0, dungeon)
  elseif k == "profession" then
    return string.format("Reached %d %s!", e.step or 0, e.profession or "in a profession")
  elseif k == "mplus" then
    return string.format("Reached %d Mythic+ rating!", e.step or 0)
  elseif k == "pvp" then
    return string.format("Reached %d rating in %s!", e.step or 0, e.bracket or "PvP")
  elseif k == "death" then
    local killer = (e.killer and e.killer ~= "") and (" to " .. e.killer) or ""
    return string.format("Fell at level %d%s. Rest in peace.", e.level or 0, killer)
  end
  return nil
end

-- Guild chat is shared, and the game can throttle a burst: one line every 2 seconds, at most 8 a
-- minute. What's over that waits its turn, nothing is dropped.
local SPACING, PER_MINUTE = 2, 8
local pending = {}
local sentAt = {}
local flushWaiting = false

local function send(msg)
  local a = ns.setting("announce")
  if not a or type(SendChatMessage) ~= "function" then return end
  if a.guild and IsInGuild and IsInGuild() then pcall(SendChatMessage, msg, "GUILD") end
  if a.party and IsInGroup and IsInGroup() then
    local channel = "PARTY"
    if IsInRaid and IsInRaid() then channel = "RAID"
    elseif LE_PARTY_CATEGORY_HOME and not IsInGroup(LE_PARTY_CATEGORY_HOME) then channel = "INSTANCE_CHAT" end -- only a dungeon finder group
    pcall(SendChatMessage, msg, channel)
  end
end

-- How long until another line may go: 0 for now.
local function waitBeforeNext()
  local now = GetTime()
  while sentAt[1] and now - sentAt[1] >= 60 do table.remove(sentAt, 1) end
  local wait = #sentAt >= PER_MINUTE and 60 - (now - sentAt[1]) or 0
  local last = sentAt[#sentAt]
  return math.max(wait, last and SPACING - (now - last) or 0, 0)
end

function ns.flushAnnouncements()
  if flushWaiting or #pending == 0 or (InCombatLockdown and InCombatLockdown()) then return end
  local wait = waitBeforeNext()
  if wait <= 0 then
    send(table.remove(pending, 1))
    table.insert(sentAt, GetTime())
    if #pending == 0 then return end
    wait = waitBeforeNext()
  end
  flushWaiting = true
  C_Timer.After(wait, function()
    flushWaiting = false
    ns.flushAnnouncements()
  end)
end

-- Called for every recorded event (ns.addEvent).
function ns.announce(e)
  local a = ns.setting("announce")
  if not a or not (a.guild or a.party) or not a.kinds then return end
  -- A followed item says so, instead of (not as well as) its usual line.
  local followed = a.kinds.followed and (e.kind == "loot" or e.kind == "mount") and ns.isFollowed(e.itemId)
  if not followed and not a.kinds[e.maxLevel and "maxlevel" or e.kind] then return end
  local msg = followed and string.format("Got %s from my followed items!", itemLink(e.itemId, e.item or e.mount)) or message(e)
  if not msg then return end
  table.insert(pending, msg)
  -- A moment later, so it follows the game's own lines (the level-up, the loot), or after combat.
  C_Timer.After(1, ns.flushAnnouncements)
end
