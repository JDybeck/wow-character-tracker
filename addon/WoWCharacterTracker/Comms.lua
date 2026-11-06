-- Addon messages between friends' game clients over Battle.net (C_BattleNet.SendGameData, received
-- on BN_CHAT_MSG_ADDON). The only file that sends or receives them. Long texts go in parts and are
-- put back together, sends are paced and wait out combat, boss fights and the game's chat lockdown,
-- and nothing is ever printed. What to send and what to keep is Friends.lua's (comms.onReceive).
local ADDON, ns = ...

local comms = {}
ns.comms = comms

comms.PREFIX = "WCTF"
comms.PROTOCOL = 1
comms.CHUNK = 240       -- body bytes per message: raise once the real limit is measured in game
local MAX_PARTS = 40    -- 40 x 240 is more than FriendData's 8 KB
local MAX_PARTIAL = 8   -- half messages waiting at once
local MAX_PER_SENDER = 2 -- of those, from one sender
local MAX_QUEUE = 200   -- messages waiting to go out
local PART_TIMEOUT = 30 -- seconds half a message waits for the rest
local SPACING = 0.25    -- seconds between two sends
local RETRY = 5         -- while sending isn't allowed
local BACKOFF = 10      -- after the game turned a send down (throttled)
local MAX_TRIES = 3

if C_ChatInfo and C_ChatInfo.RegisterAddonMessagePrefix then
  pcall(C_ChatInfo.RegisterAddonMessagePrefix, comms.PREFIX)
end

local function sendGameData()
  return (C_BattleNet and C_BattleNet.SendGameData) or BNSendGameData
end

-- Not in combat, a boss fight or the game's chat lockdown (Midnight-era clients).
function comms.canSend()
  if InCombatLockdown and InCombatLockdown() then return false end
  if IsEncounterInProgress and IsEncounterInProgress() then return false end
  if C_ChatInfo and C_ChatInfo.InChatMessagingLockdown and C_ChatInfo.InChatMessagingLockdown() then return false end
  return sendGameData() ~= nil
end

local queue = {} -- { to, text, tries }
local nextId = 0
local waiting = false

-- Older clients return nothing, newer ones a result where 0 is success.
local function accepted(ok, result)
  return ok and (result == nil or result == true or result == 0)
end

function comms.flush()
  if waiting or #queue == 0 then return end
  local wait = RETRY
  if comms.canSend() then
    local item = queue[1]
    wait = SPACING
    if accepted(pcall(sendGameData(), item.to, comms.PREFIX, item.text)) then
      table.remove(queue, 1)
    else
      item.tries = item.tries + 1
      wait = BACKOFF
      if item.tries >= MAX_TRIES then table.remove(queue, 1) end
    end
    if #queue == 0 then return end
  end
  waiting = true
  C_Timer.After(wait, function()
    waiting = false
    comms.flush()
  end)
end

-- body in parts of at most CHUNK bytes, never cut inside a UTF-8 letter.
local function split(body)
  local parts, i = {}, 1
  repeat
    local j = math.min(i + comms.CHUNK - 1, #body)
    while j < #body and j > i do
      local nextByte = body:byte(j + 1)
      if nextByte < 0x80 or nextByte > 0xBF then break end -- not a continuation byte
      j = j - 1
    end
    parts[#parts + 1] = body:sub(i, j)
    i = j + 1
  until i > #body
  return parts
end

-- Queues kind (HI, HEY, REQ, DATA, NO) with body for one game account. False when it's too long
-- or too much is already waiting.
function comms.send(account, kind, body)
  body = (body or ""):gsub("|", "")
  local parts = split(body)
  if #parts > MAX_PARTS or #queue + #parts > MAX_QUEUE then return false end
  nextId = nextId % 9999 + 1
  for part, chunk in ipairs(parts) do
    queue[#queue + 1] = { to = account, tries = 0,
      text = string.format("%d:%s:%d:%d:%d:%s", comms.PROTOCOL, kind, nextId, part, #parts, chunk) }
  end
  comms.flush()
  return true
end

-- How many messages wait to go out.
function comms.pending() return #queue end

local partial = {} -- ["sender:id"] = { parts, got, total, kind, at }

-- Drops half messages that waited too long; returns how many still wait, and how many of those from sender.
local function expire(now, sender)
  local count, fromSender = 0, 0
  for key, p in pairs(partial) do
    if now - p.at > PART_TIMEOUT then
      partial[key] = nil
    else
      count = count + 1
      if p.sender == sender then fromSender = fromSender + 1 end
    end
  end
  return count, fromSender
end

-- A friend's message can't make our code throw: an error is kept for /wct debug and the message dropped.
local function deliver(account, kind, body)
  if not comms.onReceive then return end
  local ok, err = pcall(comms.onReceive, account, kind, body)
  if not ok then ns.lastCommsError = tostring(err) end
end

-- BN_CHAT_MSG_ADDON.
function comms.receive(prefix, text, _, senderID)
  if prefix ~= comms.PREFIX or type(text) ~= "string" or not senderID then return end
  local protocol, kind, id, part, total, chunk = text:match("^(%d%d?%d?):(%u+):(%d%d?%d?%d?):(%d%d?):(%d%d?):(.*)$")
  if not protocol then return end
  if tonumber(protocol) ~= comms.PROTOCOL then return deliver(senderID, "VERSION", protocol) end
  part, total = tonumber(part), tonumber(total)
  if total < 1 or total > MAX_PARTS or part < 1 or part > total then return end
  chunk = chunk:gsub("|", "")
  if total == 1 then return deliver(senderID, kind, chunk) end
  local waitingNow, fromSender = expire(GetTime(), senderID)
  local key = senderID .. ":" .. id
  local p = partial[key]
  if not p or p.total ~= total or p.kind ~= kind then
    if waitingNow >= MAX_PARTIAL or fromSender >= MAX_PER_SENDER then return end
    p = { parts = {}, got = 0, total = total, kind = kind, at = GetTime(), sender = senderID }
    partial[key] = p
  end
  if not p.parts[part] then
    p.parts[part] = chunk
    p.got = p.got + 1
  end
  if p.got < total then return end
  partial[key] = nil
  deliver(senderID, kind, table.concat(p.parts))
end
