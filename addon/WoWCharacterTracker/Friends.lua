-- Sharing characters with Battle.net friends (contacts) who have the addon too. Off until turned
-- on. What a contact gets is the general choice per category, unless set for them; only from
-- characters that are shared. What a contact sends is kept (WoWCharacterTrackerDB.friends, for the
-- app) only when receiving from them is on. Messages go through Comms.lua, one character's text
-- through FriendData.lua.
local ADDON, ns = ...

local GROUP_EVERY = 120  -- seconds between sends to a contact in your group
local ONLINE_EVERY = 600 -- to one online elsewhere
local HEARTBEAT = 1800   -- sent even when nothing changed
local MAX_CHARS = 50     -- characters kept per contact
local TICK = 30

-- What friends get, unless set for one of them. Gold stays off until turned on.
local CATEGORIES = { "gear", "talents", "location", "gold" }
local DEFAULTS = { gear = true, talents = true, location = false, gold = false }
ns.SHARE_CATEGORIES = CATEGORIES

-- This session only.
local helloSent = {} -- [account] = true: HI went out
local heard = {}     -- [account] = true: they answered, so they have the addon
local lastSent = {}  -- [account] = { at, key }: the character being played, last sent
local saidNo = {}    -- [tag] = true: NO went out

-- Sharing settings ----------------------------------------------------------------------------
-- Changed in game (WoWCharacterTrackerDB.share) or by the app (Settings.lua share): the newest
-- `at` wins, like notes. Its own time, not the In game settings' one, so an app that doesn't
-- write `share` can't reset it.
local function fromApp() return WoWCharacterTrackerSettings and WoWCharacterTrackerSettings.share end

local function normalize(s)
  s.contacts, s.chars, s.blocked = s.contacts or {}, s.chars or {}, s.blocked or {}
  s.defaults = s.defaults or {}
  for k, v in pairs(DEFAULTS) do
    if s.defaults[k] == nil then s.defaults[k] = v end
  end
  if s.newChars == nil then s.newChars = true end
  return s
end

function ns.shareSettings()
  local game, app = WoWCharacterTrackerDB and WoWCharacterTrackerDB.share, fromApp()
  if app and (not game or (app.at or 0) > (game.at or 0)) then return normalize(app) end
  return normalize(game or { enabled = false })
end

-- Changes sharing settings: edit gets a copy of all of them, saved with a time newer than the app's.
function ns.editShare(edit)
  local s = ns.copy(ns.shareSettings())
  edit(s)
  local app = fromApp()
  s.at = math.max(time(), (app and app.at or 0) + 1)
  WoWCharacterTrackerDB.share = s
end

-- The categories a contact gets: their own choice where there is one, else the general one.
function ns.allowedFor(s, contact)
  local out = {}
  for _, k in ipairs(CATEGORIES) do
    local own = contact and contact[k]
    if own == nil then out[k] = s.defaults[k] == true else out[k] = own == true end
  end
  return out
end

-- One of your characters goes to friends: its own choice, else the one for new characters.
function ns.isCharShared(s, guid)
  if not guid then return false end
  local v = s.chars[guid]
  if v == nil then return s.newChars ~= false end
  return v == true
end

-- Battle.net friends ------------------------------------------------------------------------
-- Each game account of a Battle.net friend that is online in this same game: { tag, account, guid }.
function ns.friendsOnline()
  local out = {}
  if not (BNGetNumFriends and C_BattleNet and C_BattleNet.GetFriendAccountInfo) then return out end
  local wow = BNET_CLIENT_WOW or "WoW"
  for i = 1, (BNGetNumFriends() or 0) do
    local info = C_BattleNet.GetFriendAccountInfo(i)
    if info and info.battleTag then
      for j = 1, (C_BattleNet.GetFriendNumGameAccounts(i) or 0) do
        local g = C_BattleNet.GetFriendGameAccountInfo(i, j)
        if g and g.isOnline and g.clientProgram == wow and g.wowProjectID == WOW_PROJECT_ID and g.gameAccountID then
          out[#out + 1] = { tag = info.battleTag, account = g.gameAccountID, guid = g.playerGuid }
        end
      end
    end
  end
  return out
end

local function online()
  local ok, list = pcall(ns.friendsOnline)
  return ok and list or {}
end

-- The BattleTag behind a game account that sent us something, or nil (not a friend online here).
local function tagOf(account)
  for _, f in ipairs(online()) do
    if f.account == account then return f.tag end
  end
  return nil
end

local function groupGuids()
  local set = {}
  if not (IsInGroup and IsInGroup()) then return set end
  local raid = IsInRaid and IsInRaid()
  for i = 1, (GetNumGroupMembers and GetNumGroupMembers() or 0) do
    local guid = UnitGUID((raid and "raid" or "party") .. i)
    if guid then set[guid] = true end
  end
  return set
end

local function version()
  local metadata = (C_AddOns and C_AddOns.GetAddOnMetadata) or GetAddOnMetadata
  return metadata and tostring(metadata(ADDON, "Version") or "") or ""
end

-- Sending -------------------------------------------------------------------------------------
-- One of your characters as a contact may see it, and its change key; nil without a snapshot.
local function charText(guid, contact)
  local db = WoWCharacterTrackerDB
  local char = db.chars and db.chars[guid]
  if not char then return nil end
  return ns.encodeFriendChar(guid, char, db.items and db.items[guid], ns.allowedFor(ns.shareSettings(), contact))
end

-- The character being played, to one contact: when due (GROUP_EVERY or ONLINE_EVERY since the
-- last one, and changed or HEARTBEAT old), or right away when forced.
local function sendCurrent(f, contact, inGroup, force)
  local me = UnitGUID("player")
  if not ns.isCharShared(ns.shareSettings(), me) then return end
  local text, key = charText(me, contact)
  if not text then return end
  local last, now = lastSent[f.account], time()
  if not force and last then
    local since = now - last.at
    if since < (inGroup and GROUP_EVERY or ONLINE_EVERY) then return end
    if key == last.key and since < HEARTBEAT then return end
  end
  lastSent[f.account] = { at = now, key = key }
  ns.comms.send(f.account, "DATA", text)
end

-- Every TICK seconds, and soon after a friend comes online: hello to contacts newly online, and
-- the character being played to those that are due.
function ns.friendsTick()
  local s = ns.shareSettings()
  if not s.enabled then return end
  local group = groupGuids()
  for _, f in ipairs(online()) do
    local contact = s.contacts[f.tag]
    if contact and not s.blocked[f.tag] then
      if not helloSent[f.account] then
        helloSent[f.account] = true
        ns.comms.send(f.account, "HI", version())
      elseif heard[f.account] and contact.share then
        sendCurrent(f, contact, f.guid ~= nil and group[f.guid] == true, false)
      end
    end
  end
end

local ticker
function ns.startFriends()
  if ticker or not (C_Timer and C_Timer.NewTicker) then return end
  ticker = C_Timer.NewTicker(TICK, ns.friendsTick)
  ns.friendsTick()
end

-- Receiving -----------------------------------------------------------------------------------
local function friendEntry(tag)
  local db = WoWCharacterTrackerDB
  db.friends = db.friends or {}
  local e = db.friends[tag] or {}
  e.chars = e.chars or {}
  db.friends[tag] = e
  return e
end

-- Keeps a friend's character, at most MAX_CHARS per contact (the ones seen longest ago go).
local function keep(tag, char)
  local e = friendEntry(tag)
  char.received = time()
  e.chars[char.guid] = char
  e.seen, e.refused = time(), nil
  local guids = {}
  for guid in pairs(e.chars) do guids[#guids + 1] = guid end
  if #guids <= MAX_CHARS then return end
  table.sort(guids, function(x, y) return (e.chars[x].at or 0) > (e.chars[y].at or 0) end)
  for i = MAX_CHARS + 1, #guids do e.chars[guids[i]] = nil end
end

local function noteVersion(f, contact, body)
  if not contact.receive then return end
  local e = friendEntry(f.tag)
  e.version, e.protocol, e.seen = body, nil, time()
end

local on = {}

-- A contact came online (or reloaded): answer, and send them the character being played.
function on.HI(f, contact, body)
  heard[f.account] = true
  noteVersion(f, contact, body)
  ns.comms.send(f.account, "HEY", version())
  if contact.share then sendCurrent(f, contact, false, true) end
end

-- The answer to our hello (never answered back, so no ping-pong).
function on.HEY(f, contact, body)
  heard[f.account] = true
  noteVersion(f, contact, body)
  if contact.share and not lastSent[f.account] then sendCurrent(f, contact, false, true) end
end

function on.DATA(f, contact, body)
  heard[f.account] = true
  if not contact.receive then return end
  local char = ns.decodeFriendChar(body)
  if char then keep(f.tag, char) end
end

-- Their addon speaks another protocol: the app shows that one of you needs to update.
function on.VERSION(f, contact, protocol)
  if contact.receive then friendEntry(f.tag).protocol = tonumber(protocol) end
end

-- They said Never to our request.
function on.NO(f)
  friendEntry(f.tag).refused = time()
end

-- Everything from Comms.lua. Off means off. A blocked friend is told no once a session when they
-- ask; everything else needs them to be a contact, except a request (on.REQ, below).
ns.comms.onReceive = function(account, kind, body)
  local s = ns.shareSettings()
  if not s.enabled then return end
  local tag = tagOf(account)
  if not tag then return end
  local f = { tag = tag, account = account }
  if s.blocked[tag] then
    if kind == "REQ" and not saidNo[tag] then
      saidNo[tag] = true
      ns.comms.send(account, "NO", "")
    end
    return
  end
  local contact = s.contacts[tag]
  if kind == "REQ" then
    if on.REQ then on.REQ(f, contact) end
  elseif contact and on[kind] then
    on[kind](f, contact, body)
  end
end
