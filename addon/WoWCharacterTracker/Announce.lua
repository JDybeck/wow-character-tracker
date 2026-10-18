-- Tells your guild and/or group about a moment, in the game's own chat: level-ups, max level, end
-- bosses, epic loot (as an item link), new mounts, PvP wins, rating milestones, hardcore deaths.
-- What and where come from Settings.lua `announce` (the app's In game page). Messages wait until
-- you're out of combat, and boss kills don't list the group (they're right there).
local ADDON, ns = ...

-- The item as a clickable link when the game has it, else its name in brackets.
local function itemLink(id, name)
  local link = id and GetItemInfo and select(2, GetItemInfo(id))
  return link or string.format("[%s]", name or "?")
end

-- The line for an event, or nil when it's nothing to tell (losses, rare loot...).
local function message(e)
  local k = e.kind
  if k == "level" and e.maxLevel then
    return string.format("Reached max level %d!", e.level) .. (e.played and string.format(" Total played %s.", ns.formatPlayed(e.played)) or "")
  elseif k == "level" then
    return string.format("Ding! Level %d.", e.level) .. (e.seconds and string.format(" %d to %d took %s.", e.level - 1, e.level, ns.formatDuration(e.seconds)) or "")
  elseif k == "boss" then
    return string.format("Killed %s%s!", e.boss, (e.instance and e.instance ~= "") and (" in " .. e.instance) or "")
  elseif k == "loot" and (e.quality or 0) >= 4 then
    return "Looted " .. itemLink(e.itemId, e.item) .. "!"
  elseif k == "mount" then
    return (e.bought and "Bought a new mount: " or "Got a new mount: ") .. (e.itemId and itemLink(e.itemId, e.mount) or e.mount) .. "!"
  elseif k == "duel" and not e.lost then
    return string.format("Won a duel against %s.", e.opponent or "someone")
  elseif k == "makgora" and not e.lost then
    return string.format("Won a Mak'gora against %s!", e.opponent or "someone")
  elseif k == "bg" and not e.lost then
    return string.format("Won %s!", e.instance or "a battleground")
  elseif k == "mplus" then
    return string.format("Reached %d Mythic+ rating!", e.step or 0)
  elseif k == "pvp" then
    return string.format("Reached %d rating in %s!", e.step or 0, e.bracket or "PvP")
  elseif k == "death" then
    return string.format("Fell at level %d. Rest in peace.", e.level or 0)
  end
  return nil
end

local pending = {}

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

function ns.flushAnnouncements()
  if InCombatLockdown and InCombatLockdown() then return end
  while #pending > 0 do send(table.remove(pending, 1)) end
end

-- Called for every recorded event (ns.addEvent).
function ns.announce(e)
  local a = ns.setting("announce")
  if not a or not (a.guild or a.party) or not a.kinds then return end
  if not a.kinds[e.maxLevel and "maxlevel" or e.kind] then return end
  local msg = message(e)
  if not msg then return end
  table.insert(pending, msg)
  -- A moment later, so it follows the game's own lines (the level-up, the loot), or after combat.
  if C_Timer then C_Timer.After(1, ns.flushAnnouncements) else ns.flushAnnouncements() end
end
