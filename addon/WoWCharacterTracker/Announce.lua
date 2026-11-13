-- Tells your guild and/or group about a moment, in the game's own chat: level-ups, max level, end
-- bosses, Mythic+ runs, epic and rare loot (as an item link), new mounts, PvP wins, profession and rating milestones, hardcore deaths.
-- What and where come from Settings.lua `announce` (the app's In game page): `kinds` for guild chat,
-- `partyKinds` for group chat. Messages wait until
-- you're out of combat, and boss kills don't list the group (they're right there). On Forever the
-- game blocks guild chat sent from a timer, so guild lines go to the ask box and send on a click.
local ADDON, ns = ...

-- The item as a clickable link when the game has it, else its name in brackets.
local function itemLink(id, name)
  local link = id and select(2, ns.itemInfo(id))
  return link or string.format("[%s]", name or "?")
end
ns.itemLink = itemLink

-- The line for an event, or nil when it's nothing to tell (losses, common loot...). The ask box says
-- the same (Ask.lua).
local function message(e)
  local k = e.kind
  if k == "level" and e.maxLevel then
    return string.format("Reached max level %d!", e.level) .. (e.played and string.format(" Total played %s.", ns.formatPlayed(e.played)) or "")
  elseif k == "level" then
    return string.format("Ding! Level %d.", e.level) .. (e.seconds and string.format(" %d to %d took %s.", e.level - 1, e.level, ns.formatDuration(e.seconds)) or "")
  elseif k == "boss" then
    return string.format("Killed %s%s!", e.boss, (e.instance and e.instance ~= "") and (" in " .. e.instance) or "")
  elseif k == "loot" and (e.quality or 0) >= 3 and not ((e.quality or 0) >= 5 and e.mat) then -- not legendary materials
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
ns.momentLine = message

-- The chat switch an event falls under: rare loot has its own (and follows Bind on Equip only, like
-- its Discord posts), epic and better is "loot", max level "maxlevel".
local function announceKey(e)
  if e.maxLevel then return "maxlevel" end
  if e.kind == "loot" and (e.quality or 0) == 3 then
    local post = ns.setting("post") or {}
    if post.rareBoe and not e.boe then return nil end
    return "rare"
  end
  return e.kind
end

-- Guild chat is shared, and the game can throttle a burst: one line every 2 seconds, at most 8 a
-- minute. What's over that waits its turn, nothing is dropped.
local SPACING, PER_MINUTE = 2, 8
local LOCKDOWN_RETRY = 10 -- seconds between looks while the game's chat lockdown is on
local pending = {}
local sentAt = {}
local flushWaiting = false

-- Newer clients only have C_ChatInfo's: the global is a deprecation fallback, off by default.
local function chatSender()
  return (C_ChatInfo and C_ChatInfo.SendChatMessage) or SendChatMessage
end

-- What happened to the last line, for /wct debug: the game refusing a send is otherwise silent.
local function sendTo(say, msg, channel)
  local ok, err = pcall(say, msg, channel)
  return string.format("%s: %s", channel, ok and "sent" or tostring(err))
end

-- line = { msg, guild = true/nil, party = true/nil }: which chats get it was settled in ns.announce.
local function send(line)
  local say = chatSender()
  if type(say) ~= "function" then
    ns.lastChatSend = "no chat function"
    return
  end
  local msg = line.msg
  local results = {} -- every chat's outcome, so the party line doesn't hide the guild one
  if line.guild then
    results[#results + 1] = (IsInGuild and IsInGuild()) and sendTo(say, msg, "GUILD") or "not in a guild"
  end
  if line.party and IsInGroup and IsInGroup() then
    local channel = "PARTY"
    if IsInRaid and IsInRaid() then channel = "RAID"
    elseif LE_PARTY_CATEGORY_HOME and not IsInGroup(LE_PARTY_CATEGORY_HOME) then channel = "INSTANCE_CHAT" end -- only a dungeon finder group
    results[#results + 1] = sendTo(say, msg, channel)
  end
  if #results > 0 then ns.lastChatSend = table.concat(results, ", ") end
end

-- A guild line sent right now, from the player's click in the ask box (Ask.lua).
function ns.sayInGuild(msg)
  send({ msg = msg, guild = true })
end

-- For /wct debug: lines still waiting (combat, chat lockdown or spacing).
function ns.pendingAnnouncements() return #pending end

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
  local wait = ns.chatLocked() and LOCKDOWN_RETRY or waitBeforeNext()
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

-- The line one chat gets for an event, from that chat's kinds, or nil.
local function lineFor(e, kinds)
  if not kinds then return nil end
  -- A followed item says so, instead of (not as well as) its usual line.
  if kinds.followed and (e.kind == "loot" or e.kind == "mount") and ns.isFollowed(e.itemId) then
    return string.format("Got %s from my followed items!", itemLink(e.itemId, e.item or e.mount))
  end
  local key = announceKey(e)
  if not key or not kinds[key] then return nil end
  return message(e)
end

local function queue(line)
  table.insert(pending, line)
  -- A moment later, so it follows the game's own lines (the level-up, the loot), or after combat.
  C_Timer.After(1, ns.flushAnnouncements)
end

-- Called for every recorded event (ns.addEvent). Guild and group chat each have their own kinds
-- (partyKinds is missing in a Settings.lua from before they did: both used kinds).
function ns.announce(e)
  local a = ns.setting("announce")
  if not a then return end
  local guild = a.guild and lineFor(e, a.kinds)
  local party = a.party and lineFor(e, a.partyKinds or a.kinds)
  -- Where guild chat only goes out on a click, the ask box offers it instead of the queue.
  if guild and ns.GUILD_CHAT_NEEDS_CLICK then
    if IsInGuild and IsInGuild() then ns.offerGuildLine(e, guild) end
    guild = nil
  end
  if guild and guild == party then return queue({ msg = guild, guild = true, party = true }) end
  if guild then queue({ msg = guild, guild = true }) end
  if party then queue({ msg = party, party = true }) end
end
