-- Sharing characters with Battle.net friends (contacts) who have the addon too. On by default, but
-- nothing goes to anyone until you allow their request or share with them yourself (a contact).
-- What a contact gets is the general choice per category, unless set for them; only from
-- characters that are shared. What a contact sends is kept (WoWCharacterTrackerDB.friends, for the
-- app) only when receiving from them is on. Messages go through Comms.lua, one character's text
-- through FriendData.lua.
local ADDON, ns = ...

local GROUP_EVERY = 120  -- seconds between sends to a contact in your group
local ONLINE_EVERY = 600 -- to one online elsewhere
local HEARTBEAT = 1800   -- sent even when nothing changed
local MAX_CHARS = 50     -- characters kept per contact
local TICK = 30

-- What friends get, unless set for one of them. Gold and map position stay off until turned on.
local CATEGORIES = { "gear", "talents", "location", "professions", "highlights", "leveling", "gold", "map" }
-- Set like a category, but not data: otherFaction off means a friend playing the other faction
-- gets no map position.
local RULES = { "otherFaction" }
local DEFAULTS = { gear = true, talents = true, location = true, professions = true, highlights = true, leveling = true, gold = false, map = false,
  otherFaction = false }
local MAP_RATES = { [5] = true, [60] = true, [300] = true } -- seconds between two map position readings
ns.SHARE_CATEGORIES = CATEGORIES
ns.SHARE_RULES = RULES

-- This session only.
local helloSent = {} -- [account] = true: HI went out
local heard = {}     -- [account] = true: they sent something, so they have the addon
local lastSent = {}  -- [account] = { at, key }: the character being played, last sent
local saidNo = {}    -- [tag] = true: NO went out

-- Tells the window's Friends tab to redraw (Window.lua sets ns.friendsChanged).
local function changed()
  if ns.friendsChanged then ns.friendsChanged() end
end

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
  if s.enabled == nil then s.enabled = true end -- on, so friends can ask; only Allow shares
  if not MAP_RATES[s.mapRate] then s.mapRate = 5 end
  return s
end

function ns.shareSettings()
  local game, app = WoWCharacterTrackerDB and WoWCharacterTrackerDB.share, fromApp()
  if app and (not game or (app.at or 0) > (game.at or 0)) then return normalize(app) end
  return normalize(game or {})
end

-- Changes sharing settings: edit gets a copy of all of them, saved with a time newer than the app's.
function ns.editShare(edit)
  local s = ns.copy(ns.shareSettings())
  edit(s)
  local app = fromApp()
  s.at = math.max(time(), (app and app.at or 0) + 1)
  WoWCharacterTrackerDB.share = s
end

-- The categories and rules a contact gets: their own choice where there is one, else the general one.
function ns.allowedFor(s, contact)
  local out = {}
  for _, list in ipairs({ CATEGORIES, RULES }) do
    for _, k in ipairs(list) do
      local own = contact and contact[k]
      if own == nil then out[k] = s.defaults[k] == true else out[k] = own == true end
    end
  end
  return out
end

-- Removed on the app's Characters page (Settings.lua removed = { [guid] = when }) and not played since.
local function removedInApp(guid)
  local removed = WoWCharacterTrackerSettings and WoWCharacterTrackerSettings.removed
  local at = removed and removed[guid]
  if not at then return false end
  local char = WoWCharacterTrackerDB and WoWCharacterTrackerDB.chars and WoWCharacterTrackerDB.chars[guid]
  return not char or (char.lastSeen or 0) <= at
end

-- One of your characters goes to friends: its own choice, else the one for new characters. Never
-- one removed in the app, or one with Ignore tracking on.
function ns.isCharShared(s, guid)
  if not guid or removedInApp(guid) or ns.isIgnored(guid) then return false end
  local v = s.chars[guid]
  if v == nil then return s.newChars ~= false end
  return v == true
end

-- Battle.net friends ------------------------------------------------------------------------
-- The class file name ("MAGE") of a game account: the game's, else found from its class name.
local function classFileOf(g)
  if g.classFilename then return g.classFilename end
  for _, names in ipairs({ LOCALIZED_CLASS_NAMES_MALE or {}, LOCALIZED_CLASS_NAMES_FEMALE or {} }) do
    for file, name in pairs(names) do
      if name == g.className then return file end
    end
  end
  return nil
end

-- Each game account of a Battle.net friend that is online in this same game: { tag, account, guid,
-- bnet, character, classFile, level, zone, faction, realm }.
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
          out[#out + 1] = { tag = info.battleTag, account = g.gameAccountID, guid = g.playerGuid, bnet = info.bnetAccountID,
            character = g.characterName, classFile = classFileOf(g), level = g.characterLevel, zone = g.areaName,
            faction = g.factionName, realm = g.realmName }
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
-- The newest highlights per character (ns.FRIEND_HIGHLIGHTS of them), kept up to date like
-- ns.hasEvent's ids: events are appended, so only new ones are read; another or shorter list
-- (trimmed, cleaned up) is read again.
-- Level times per character come along the same way ([guid][level] = { level, seconds, at }).
local recentHighlights, recentLevels, highlightsList, highlightsCounted = {}, {}, nil, 0
local function readEvents()
  local events = WoWCharacterTrackerDB.events or {}
  if events ~= highlightsList or #events < highlightsCounted then
    recentHighlights, recentLevels, highlightsList, highlightsCounted = {}, {}, events, 0
  end
  for i = highlightsCounted + 1, #events do
    local e = events[i]
    if ns.isFriendHighlight(e) and e.guid and not ns.inIncognito(e.at, true) then -- incognito with friends paused: never shared
      local list = recentHighlights[e.guid] or {}
      list[#list + 1] = e
      table.sort(list, function(x, y) return x.at > y.at end)
      list[ns.FRIEND_HIGHLIGHTS + 1] = nil
      recentHighlights[e.guid] = list
    elseif type(e) == "table" and (e.kind == nil or e.kind == "level") and e.guid and type(e.level) == "number" and type(e.seconds) == "number" and e.seconds > 0 then
      recentLevels[e.guid] = recentLevels[e.guid] or {}
      recentLevels[e.guid][e.level] = { level = e.level, seconds = e.seconds, at = e.at }
    end
  end
  highlightsCounted = #events
end

local function levelsOf(guid)
  local out = {}
  for _, l in pairs(recentLevels[guid] or {}) do out[#out + 1] = l end
  return out
end

-- One of your characters as a contact may see it, and its change key; nil without a snapshot.
local function charText(guid, contact)
  local db = WoWCharacterTrackerDB
  local char = db.chars and db.chars[guid]
  if not char then return nil end
  readEvents()
  return ns.encodeFriendChar(guid, char, db.items and db.items[guid], ns.allowedFor(ns.shareSettings(), contact),
    recentHighlights[guid], levelsOf(guid))
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

-- One chat line a minute after a real login (never on /reload) while sharing is on but nothing
-- reaches any friend: you share with nobody, or not this character. Turning sharing off stops it.
function ns.remindSharing(isInitialLogin)
  if isInitialLogin ~= true then return end
  C_Timer.After(60, ns.maybeShareTip)
end

function ns.maybeShareTip()
  local s = ns.shareSettings()
  if not s.enabled then return end
  local anyone = false
  for tag, c in pairs(s.contacts) do
    if c.share and not s.blocked[tag] then anyone = true end
  end
  if not anyone then
    ns.say("You're not sharing with any friends yet. Battle.net friends with the addon can see each other's characters, "
      .. "level times, gear and talents: /wct, Friends tab, or /wct friends share <BattleTag>.")
  elseif not ns.isCharShared(s, UnitGUID("player")) and not ns.isIgnored(UnitGUID("player")) then
    ns.say("This character isn't shared with your friends. /wct, Friends tab, Share this character.")
  end
end

-- Who has the addon ---------------------------------------------------------------------------
-- PING asks a friend's addon to say PONG, invisibly. Each game account is pinged once a session.
local FIND_EVERY = 60 -- seconds between two PONGs to one account
local PING_WAIT = 10  -- seconds without an answer before it's "no addon"

local pinged = {}       -- [account] = true: PING went out
local silent = {}       -- [account] = true: no answer to it in PING_WAIT
local answeredPing = {} -- [account] = when we last said PONG

-- No answer counts only PING_WAIT after the PING really went: while anything still waits to go out
-- (combat, lockdown, a backlog), the wait starts over.
local function waitForAnswer(account, sentAlready)
  C_Timer.After(PING_WAIT, function()
    if heard[account] then return end
    local queued = ns.comms.pending and ns.comms.pending() > 0
    if queued or not sentAlready then return waitForAnswer(account, not queued) end
    silent[account] = true
    changed()
  end)
end

-- Pings every friend online here that hasn't been pinged yet this session. A friend who came online
-- since the last look is pinged right away; nobody is pinged twice.
function ns.findOnlineFriends()
  for _, f in ipairs(online()) do
    local account = f.account
    if not pinged[account] and not heard[account] and ns.comms.send(account, "PING", "") then
      pinged[account] = true
      waitForAnswer(account, not (ns.comms.pending and ns.comms.pending() > 0))
    end
  end
end

-- true once they sent anything this session, false when a PING went unanswered, nil before we know.
local function hasAddon(account)
  if heard[account] then return true end
  if silent[account] then return false end
  return nil
end

local function answerPing(account)
  local now = time()
  if answeredPing[account] and now - answeredPing[account] < FIND_EVERY then return end
  answeredPing[account] = now
  ns.comms.send(account, "PONG", version())
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

-- Their addon's version, kept only when it looks like one ("0.33.0").
local function noteVersion(f, contact, body)
  if not contact.receive then return end
  local e = friendEntry(f.tag)
  e.protocol, e.seen = nil, time()
  if type(body) == "string" and #body <= 16 and body:match("^[%w%.%-]+$") then e.version = body end
end

local on = {}
local answeredHi = {}

-- Another file's handler for a kind (FriendMap.lua: WATCH and POS), called like these: (f, contact,
-- body), only for contacts, only with sharing on.
function ns.onFriendKind(kind, handler) on[kind] = handler end -- [account] = when we last answered their hello

-- A contact came online (or reloaded): answer, and send them the character being played. At most
-- once per GROUP_EVERY, so a contact saying hello over and over can't flood us (or Battle.net).
local sendCharList -- below, with the requests

function on.HI(f, contact, body)
  noteVersion(f, contact, body)
  local now = time()
  if answeredHi[f.account] and now - answeredHi[f.account] < GROUP_EVERY then return end
  answeredHi[f.account] = now
  ns.comms.send(f.account, "HEY", version())
  local last = lastSent[f.account]
  if contact.share and not (last and now - last.at < GROUP_EVERY) then
    sendCurrent(f, contact, false, true)
    sendCharList(f)
  end
end

-- The answer to our hello (never answered back, so no ping-pong).
function on.HEY(f, contact, body)
  noteVersion(f, contact, body)
  if contact.share and not lastSent[f.account] then
    sendCurrent(f, contact, false, true)
    sendCharList(f)
  end
end

-- Which characters they still share: anything else we kept from them goes (hidden, removed, deleted).
function on.CHARS(f, contact, body)
  if not contact.receive then return end
  local still = {}
  for guid in string.gmatch(body or "", "[^,]+") do still[guid] = true end
  local e = friendEntry(f.tag)
  for guid in pairs(e.chars) do
    if not still[guid] then e.chars[guid] = nil end
  end
  changed()
end

function on.DATA(f, contact, body)
  if not contact.receive then return end
  local char = ns.decodeFriendChar(body)
  if not char then return end
  keep(f.tag, char)
  changed()
end

-- Their addon speaks another protocol: the app shows that one of you needs to update.
function on.VERSION(f, contact, protocol)
  if contact.receive then friendEntry(f.tag).protocol = tonumber(protocol) end
end

-- They said Never to our request.
function on.NO(f)
  friendEntry(f.tag).refused = time()
end

-- Everything from Comms.lua, from friends online here. Any message says they have the addon, and a
-- PING gets a PONG (only that) even with sharing off. Otherwise off means off. A blocked friend is
-- told no once a session when they ask; everything else needs them to be a contact, except a
-- request (on.REQ, below).
ns.comms.onReceive = function(account, kind, body)
  local tag = tagOf(account)
  if not tag then return end
  if not heard[account] then
    heard[account] = true
    changed()
  end
  if kind == "PING" then return answerPing(account) end
  local s = ns.shareSettings()
  if not s.enabled or kind == "PONG" then return end
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
    if on.REQ then on.REQ(f, contact, body) end
  elseif contact and on[kind] then
    on[kind](f, contact, body)
  end
end

-- Requests ------------------------------------------------------------------------------------
local REQUEST_COOLDOWN = 30 -- between two requests to, or from, the same contact

local asked = {}     -- [tag] = when we last asked them
local requested = {} -- [tag] = when they last asked us
local notNow = {}    -- [tag] = true: "Not now" this session
local altsSent = {}  -- [tag] = true: the alts went out this session

-- "Zebbe (Zeb#2222)" with a nickname from the app (Settings.lua friendNames), else the BattleTag.
local function label(tag)
  local names = WoWCharacterTrackerSettings and WoWCharacterTrackerSettings.friendNames
  local name = names and names[tag]
  if name and name ~= "" then return string.format("%s (%s)", name, tag) end
  return tag
end

local function accountsOf(tag)
  local out = {}
  for _, f in ipairs(online()) do
    if f.tag == tag then out[#out + 1] = f.account end
  end
  return out
end

-- Your shared characters other than the one being played.
local function sharedAlts()
  local s, me, out = ns.shareSettings(), UnitGUID("player"), {}
  for guid in pairs(WoWCharacterTrackerDB.chars or {}) do
    if guid ~= me and ns.isCharShared(s, guid) then out[#out + 1] = guid end
  end
  table.sort(out)
  return out
end

-- The GUIDs of every character we share, so the friend drops the ones we don't anymore.
sendCharList = function(f)
  local list, me = sharedAlts(), UnitGUID("player")
  if me and ns.isCharShared(ns.shareSettings(), me) then list[#list + 1] = me end
  ns.comms.send(f.account, "CHARS", table.concat(list, ","))
end

-- Everything a contact may see, for one who asked: the character being played, the list of what we
-- share, and the alts once a session.
local function answer(f, contact)
  sendCurrent(f, contact, false, true)
  sendCharList(f)
  if altsSent[f.tag] then return end
  altsSent[f.tag] = true
  for _, guid in ipairs(sharedAlts()) do
    local text = charText(guid, contact)
    if text then ns.comms.send(f.account, "DATA", text) end
  end
end

-- Makes tag a contact whose characters we keep, and shares ours with them too when share is true
-- (what they get follows the general choice until set for them). Turns sharing with friends on.
local function addContact(tag, share)
  if not ns.shareSettings().enabled then ns.say("Sharing with friends is now on (/wct friends off turns it off).") end
  ns.editShare(function(s)
    s.enabled = true
    local c = s.contacts[tag] or {}
    c.receive = true
    if share then c.share = true end
    s.contacts[tag] = c
  end)
  return ns.shareSettings().contacts[tag]
end

-- The categories a request asks for ("map,gold"), known ones only, in CATEGORIES order. Older
-- addons send an empty body and ignore one.
local function wantsOf(body)
  local asked, out = {}, {}
  for k in tostring(body or ""):gmatch("[^,]+") do asked[k] = true end
  for _, k in ipairs(CATEGORIES) do
    if asked[k] then out[#out + 1] = k end
  end
  return out
end

local function asSet(list)
  local out = {}
  for _, k in ipairs(list or {}) do out[k] = true end
  return out
end

-- A friend asks for our characters: answered when we share with them, else asked about in a popup.
-- Asking for categories they don't get yet (map position, gold) asks about those, even when we share.
function on.REQ(f, contact, body)
  local now = time()
  if requested[f.tag] and now - requested[f.tag] < REQUEST_COOLDOWN then return end
  requested[f.tag] = now
  local wants = wantsOf(body)
  if contact and contact.share then
    answer(f, contact)
    local allowed, missing = ns.allowedFor(ns.shareSettings(), contact), {}
    for _, k in ipairs(wants) do
      if not allowed[k] then missing[#missing + 1] = k end
    end
    if #missing == 0 then return end
    f.wants, f.more = missing, true
  else
    f.wants = wants
  end
  if not notNow[f.tag] then ns.offerFriendRequest(f) end
end

-- "wants to see your characters", "... with your map position" or "asks to also see your gold".
local function asks(f)
  local what = #(f.wants or {}) > 0 and ns.shareSummary(asSet(f.wants)) or nil
  if f.more then return "asks to also see your " .. (what or "characters") end
  if what then return "wants to see your characters, with your " .. what end
  return "wants to see your characters"
end

-- The popup: one at a time, never in combat (PLAYER_REGEN_ENABLED shows the next one). The Friends
-- tab lists the same requests (ns.pendingRequests).
local waitingRequests = {}
local showing

function ns.offerFriendRequest(f)
  if showing and showing.tag == f.tag then return end
  for _, w in ipairs(waitingRequests) do
    if w.tag == f.tag then return end
  end
  f.at = f.at or time()
  table.insert(waitingRequests, f)
  ns.showNextFriendRequest()
  changed()
end

-- Requests waiting for an answer, oldest first: { tag, account, label, at, wants, more, asks }.
function ns.pendingRequests()
  local out = {}
  local function add(f)
    out[#out + 1] = { tag = f.tag, account = f.account, label = label(f.tag), at = f.at, wants = f.wants, more = f.more, asks = asks(f) }
  end
  if showing then add(showing) end
  for _, w in ipairs(waitingRequests) do add(w) end
  return out
end

-- Your characters friends would get: the shared ones, the one being played included.
local function sharedCount()
  local n, me = #sharedAlts(), UnitGUID("player")
  if me and ns.isCharShared(ns.shareSettings(), me) then n = n + 1 end
  return n
end

-- What the request box shows for f: { label, line, more, chars (how many of yours they'd get),
-- choices (the categories Allow gives them: your general choice, or what you set for them before,
-- plus what they asked for), general }.
function ns.requestInfo(f)
  local s = ns.shareSettings()
  local choices = ns.allowedFor(s, s.contacts[f.tag])
  for _, k in ipairs(f.wants or {}) do choices[k] = true end
  return { label = label(f.tag), line = label(f.tag) .. " " .. asks(f) .. ".", more = f.more == true, chars = sharedCount(),
    choices = choices, general = ns.allowedFor(s, nil) }
end

-- Shows a request: the window's own box (Window.lua, with the choices), else a Blizzard popup.
local function showRequest(f)
  if ns.showRequestBox then return ns.showRequestBox(f) end
  return ns.popup("CHARACTERTRACKER_FRIEND_REQUEST", {
    text = "%s (Character Tracker)",
    button1 = "Allow", button2 = "Not now", button3 = "Never",
    OnAccept = function(_, data) ns.answerFriendRequest(data, "allow") end,
    OnCancel = function(_, data) ns.answerFriendRequest(data, "notnow") end,
    OnAlt = function(_, data) ns.answerFriendRequest(data, "never") end,
    -- Closed without an answer (another popup took its place): the next one may show.
    OnHide = function(_, data)
      if showing == data then
        showing = nil
        ns.showNextFriendRequest()
      end
    end,
  }, ns.requestInfo(f).line, f)
end

local function hideRequest()
  if ns.hideRequestBox then ns.hideRequestBox() end
  if StaticPopup_Hide then StaticPopup_Hide("CHARACTERTRACKER_FRIEND_REQUEST") end
end

function ns.showNextFriendRequest()
  if showing or #waitingRequests == 0 then return end
  if InCombatLockdown and InCombatLockdown() then return end
  showing = table.remove(waitingRequests, 1)
  local dialog = showRequest(showing)
  if not dialog then -- the game had no room for it: try again in a moment
    table.insert(waitingRequests, 1, showing)
    showing = nil
    C_Timer.After(5, ns.showNextFriendRequest)
  end
end

-- Allow with choices (category -> on, from the request box): only what differs from your general
-- choice is kept for them, so the rest still follows it.
local function keepChoices(tag, choices)
  if not choices then return end
  ns.editShare(function(s)
    local c = s.contacts[tag]
    for _, k in ipairs(CATEGORIES) do
      if choices[k] ~= nil then
        local on = choices[k] == true
        if on == (s.defaults[k] == true) then c[k] = nil else c[k] = on end
      end
    end
  end)
end

-- Allow shares with them, Not now waits for the next session, Never blocks them and tells their
-- addon to stop asking. From the request box (f is the one showing) or the Friends tab's list (a
-- copy), which closes the box when it's theirs. choices: see keepChoices.
function ns.answerFriendRequest(f, choice, choices)
  if not f or (showing and showing.tag == f.tag) then
    local fromList = f and showing ~= f
    showing = nil
    if fromList then hideRequest() end
  end
  if f then
    for i = #waitingRequests, 1, -1 do
      if waitingRequests[i].tag == f.tag then table.remove(waitingRequests, i) end
    end
    if choice == "allow" then
      addContact(f.tag, true)
      -- From the list there are no choices: what they asked for comes along.
      if not choices and #(f.wants or {}) > 0 then choices = ns.requestInfo(f).choices end
      keepChoices(f.tag, choices)
      local s = ns.shareSettings()
      local contact = s.contacts[f.tag]
      answer(f, contact)
      ns.say(string.format("%s can now see your shared characters (%s). Change it on the Friends tab (/wct).",
        label(f.tag), ns.shareSummary(ns.allowedFor(s, contact))))
    elseif choice == "never" then
      ns.editShare(function(s) s.blocked[f.tag] = true end)
      saidNo[f.tag] = true
      ns.comms.send(f.account, "NO", "")
    else
      notNow[f.tag] = true
    end
  end
  ns.showNextFriendRequest()
  changed()
end

-- /wct friends ---------------------------------------------------------------------------------
-- The Battle.net friend's BattleTag as the game writes it, from what was typed (any case), or nil.
function ns.findFriendTag(typed)
  typed = typed and typed:lower()
  if not typed or typed == "" or not (BNGetNumFriends and C_BattleNet and C_BattleNet.GetFriendAccountInfo) then return nil end
  for i = 1, (BNGetNumFriends() or 0) do
    local info = C_BattleNet.GetFriendAccountInfo(i)
    if info and info.battleTag and info.battleTag:lower() == typed then return info.battleTag end
  end
  return nil
end

local HELP = {
  "/wct friends lists your contacts and what they get.",
  "/wct friends on or off turns sharing with Battle.net friends on or off.",
  "/wct friends ask <BattleTag> [map gold ...] asks a friend for their characters, or for more of them, like their map position.",
  "/wct friends share <BattleTag> shares your characters with them.",
  "/wct friends set all gear/talents/location/professions/highlights/leveling/gold/map on/off changes what friends get. Gold and map (your spot on their world map) are off until you turn them on.",
  "/wct friends set <BattleTag> gear/talents/location/professions/highlights/leveling/gold/map on/off/default changes it for one friend.",
  "/wct friends set all/<BattleTag> otherfaction on/off/default: on lets friends playing the other faction see you on the map too.",
  "/wct friends maprate live/1m/5m sets how often friends' maps get your position.",
  "/wct friends stop <BattleTag> stops sharing with them, remove <BattleTag> forgets them and what they sent.",
  "/wct friends hide or unhide keeps this character from friends, or shares it again.",
  "/wct friends unblock <BattleTag> lets someone you said Never to ask again.",
}

-- What set takes: a category or a rule, its key exact (IS_CHOICE) or typed in any case (CHOICE_BY_WORD).
local IS_CHOICE, CHOICE_BY_WORD = {}, {}
for _, list in ipairs({ CATEGORIES, RULES }) do
  for _, k in ipairs(list) do IS_CHOICE[k], CHOICE_BY_WORD[k:lower()] = true, k end
end

local SUMMARY_NAMES = { map = "map position", leveling = "level times" }

local function picked(allowed)
  local out = {}
  for _, k in ipairs(CATEGORIES) do
    if allowed[k] then out[#out + 1] = SUMMARY_NAMES[k] or k end
  end
  return #out > 0 and table.concat(out, ", ") or "only the basics"
end
-- "gear, talents, professions" from { category = on }, for the request box, its chat line and /wct friends.
ns.shareSummary = picked

local function listContacts()
  local s, me = ns.shareSettings(), UnitGUID("player")
  ns.say(string.format("Sharing with friends is %s. This character is %s. Friends get: %s.", s.enabled and "on" or "off",
    ns.isCharShared(s, me) and "shared" or "hidden", picked(s.defaults)))
  local onlineTags = {}
  for _, f in ipairs(online()) do onlineTags[f.tag] = true end
  local any = false
  for tag, c in pairs(s.contacts) do
    any = true
    local got = WoWCharacterTrackerDB.friends and WoWCharacterTrackerDB.friends[tag]
    local count = 0
    for _ in pairs(got and got.chars or {}) do count = count + 1 end
    ns.say(string.format("%s%s: %s, %s, %d of their characters here.", label(tag), onlineTags[tag] and " (online)" or "",
      c.share and ("gets " .. picked(ns.allowedFor(s, c))) or "gets nothing",
      c.receive and "theirs kept" or "theirs not kept", count))
  end
  if not any then ns.say("No contacts yet. /wct friends ask <BattleTag> or /wct friends share <BattleTag>.") end
end

-- Says message and returns it, for the Friends tab.
local function tell(message)
  ns.say(message)
  return message
end

-- Asks tag for their characters, and for the categories in wants (a list, optional).
local function ask(tag, wants)
  local now = time()
  if asked[tag] and now - asked[tag] < REQUEST_COOLDOWN then return tell("Asked " .. label(tag) .. " a moment ago.") end
  local got = WoWCharacterTrackerDB.friends and WoWCharacterTrackerDB.friends[tag]
  if got and got.refused then return tell(label(tag) .. " said no to sharing.") end
  addContact(tag, false)
  local accounts = accountsOf(tag)
  if #accounts == 0 then return tell(label(tag) .. " isn't online in this game right now.") end
  asked[tag] = now
  local list = wantsOf(table.concat(wants or {}, ","))
  for _, account in ipairs(accounts) do ns.comms.send(account, "REQ", table.concat(list, ",")) end
  local what = #list > 0 and (" for " .. picked(asSet(list))) or ""
  return tell("Asked " .. label(tag) .. what .. ". What they share reaches the app on your next /reload or logout.")
end

local function share(tag)
  addContact(tag, true)
  ns.friendsTick()
  return tell(label(tag) .. " gets your shared characters. They need the addon, with you as a contact.")
end

-- One contact's own choice for a category: true, false, or nil for what all friends get.
function ns.setContactCategory(tag, key, value)
  if not IS_CHOICE[key] or not ns.shareSettings().contacts[tag] then return false end
  ns.editShare(function(s) s.contacts[tag][key] = value end)
  changed()
  return true
end

-- Like all friends, then on, then off, then like all friends again (the app's chips do the same).
function ns.cycleContactCategory(tag, key)
  local c = ns.shareSettings().contacts[tag]
  if not c then return false end
  local nextValue
  if c[key] == nil then nextValue = true elseif c[key] == true then nextValue = false end
  return ns.setContactCategory(tag, key, nextValue)
end

-- Share with a contact or stop. Returns what it said.
function ns.setContactShare(tag, on)
  if on then return share(tag) end
  if not ns.shareSettings().contacts[tag] then return nil end
  ns.editShare(function(s) s.contacts[tag].share = false end)
  changed()
  return tell(label(tag) .. " doesn't get your characters anymore.")
end

-- Keep what a contact sends, or not.
function ns.setContactReceive(tag, on)
  if not ns.shareSettings().contacts[tag] then return end
  ns.editShare(function(s) s.contacts[tag].receive = on end)
  changed()
end

-- What a contact gets, for the Friends tab's sharing panel: nil when tag isn't a contact.
-- categories: { key, own (true, false or nil = like all friends), general, on, rule } for CATEGORIES,
-- then RULES (rule = true).
function ns.contactSharing(tag)
  local s = ns.shareSettings()
  local c = s.contacts[tag]
  if not c then return nil end
  local allowed, categories = ns.allowedFor(s, c), {}
  for _, k in ipairs(CATEGORIES) do
    categories[#categories + 1] = { key = k, own = c[k], general = s.defaults[k] == true, on = allowed[k] }
  end
  for _, k in ipairs(RULES) do
    categories[#categories + 1] = { key = k, own = c[k], general = s.defaults[k] == true, on = allowed[k], rule = true }
  end
  return { tag = tag, label = label(tag), share = c.share == true, receive = c.receive == true,
    categories = categories, summary = picked(allowed) }
end

-- /wct friends set all|<BattleTag> <category or rule> <on|off|default>
local function set(typed, what, value)
  local word = (what or ""):lower()
  local key = CHOICE_BY_WORD[word]
  value = (value or ""):lower()
  local general = (typed or ""):lower() == "all"
  if not key or not (value == "on" or value == "off" or (value == "default" and not general)) then
    ns.say(HELP[5])
    ns.say(HELP[6])
    return ns.say(HELP[7])
  end
  if general then
    ns.editShare(function(s) s.defaults[key] = value == "on" end)
    return ns.say(string.format("Friends now get %s: %s.", word, value))
  end
  local tag = ns.findFriendTag(typed)
  if not tag then return ns.say((typed or "That") .. " isn't on your Battle.net friends list.") end
  if not ns.shareSettings().contacts[tag] then return ns.say(label(tag) .. " isn't a contact. /wct friends share " .. tag .. " adds them.") end
  local own
  if value ~= "default" then own = value == "on" end
  ns.setContactCategory(tag, key, own)
  ns.say(string.format("%s: %s is now %s.", label(tag), word, value == "default" and "the same as for all friends" or value))
end

local RATE_WORDS = { live = 5, ["1m"] = 60, ["5m"] = 300 }
local RATE_NAMES = { [5] = "live", [60] = "once a minute", [300] = "every 5 minutes" }

local TAG_COMMANDS = { ask = true, share = true, stop = true, remove = true, unblock = true }

function ns.friendsCommand(args)
  local cmd, rest = strtrim(args or ""):match("^(%S*)%s*(.-)$")
  cmd = (cmd or ""):lower()
  if cmd == "" then return listContacts() end
  if cmd == "on" or cmd == "off" then
    ns.editShare(function(s) s.enabled = cmd == "on" end)
    return ns.say("Sharing with friends is now " .. cmd .. ".")
  end
  if cmd == "hide" or cmd == "unhide" then
    local me = UnitGUID("player")
    if me then ns.editShare(function(s) s.chars[me] = cmd == "unhide" end) end
    return ns.say(cmd == "hide" and "Friends won't get this character anymore." or "Friends get this character again.")
  end
  if cmd == "maprate" then
    local rate = RATE_WORDS[rest:lower()]
    if not rate then return ns.say(HELP[8]) end
    ns.editShare(function(s) s.mapRate = rate end)
    return ns.say("Friends' maps now get your position " .. RATE_NAMES[rate] .. ".")
  end
  local typed, what, value = rest:match("^(%S+)%s*(%S*)%s*(%S*)$")
  if cmd == "set" then return set(typed, what, value) end
  if not TAG_COMMANDS[cmd] then
    for _, line in ipairs(HELP) do ns.say(line) end
    return
  end
  local tag = ns.findFriendTag(typed)
  if not tag then return ns.say((typed or "That") .. " isn't on your Battle.net friends list.") end
  if cmd == "ask" then
    local wants, first = {}, true
    for word in rest:gmatch("%S+") do
      if first then
        first = false -- the BattleTag
      else
        local key = CHOICE_BY_WORD[word:lower()]
        if not key or key == "otherFaction" then return ns.say(HELP[3]) end
        wants[#wants + 1] = key
      end
    end
    return ask(tag, wants)
  end
  if cmd == "share" then return share(tag) end
  if cmd == "unblock" then
    ns.editShare(function(s) s.blocked[tag] = nil end)
    return ns.say(label(tag) .. " can ask again.")
  end
  if not ns.shareSettings().contacts[tag] then return ns.say(label(tag) .. " isn't a contact. /wct friends share " .. tag .. " adds them.") end
  if cmd == "stop" then return ns.setContactShare(tag, false) end
  ns.editShare(function(s) s.contacts[tag] = nil end)
  if WoWCharacterTrackerDB.friends then WoWCharacterTrackerDB.friends[tag] = nil end
  ns.say(label(tag) .. " is forgotten, with what they sent.")
end

-- The Friends tab (Window.lua) ------------------------------------------------------------------
local INVITE = "I use WoW Character Tracker to share characters with friends: https://github.com/JDybeck/wow-character-tracker"
local INVITE_EVERY = 86400 -- one invite a day per friend

local function byLabel(x, y)
  if x.label ~= y.label then return x.label:lower() < y.label:lower() end
  return (x.character or "") < (y.character or "")
end

local function invitedAt(tag)
  local db = WoWCharacterTrackerDB
  return db.invitedAt and db.invitedAt[tag]
end

local function sharesWithYou(tag)
  local got = WoWCharacterTrackerDB.friends and WoWCharacterTrackerDB.friends[tag]
  return got ~= nil and got.chars ~= nil and next(got.chars) ~= nil
end

-- One row per game account of a friend online here, by label.
function ns.onlineFriends()
  local s, out = ns.shareSettings(), {}
  for _, f in ipairs(online()) do
    out[#out + 1] = { tag = f.tag, account = f.account, guid = f.guid, label = label(f.tag), character = f.character,
      classFile = f.classFile, level = f.level, zone = f.zone, faction = f.faction, realm = f.realm, hasAddon = hasAddon(f.account),
      contact = s.contacts[f.tag], sharesWithYou = sharesWithYou(f.tag), blocked = s.blocked[f.tag] == true,
      invitedAt = invitedAt(f.tag) }
  end
  table.sort(out, byLabel)
  return out
end

ns.askFriend = ask
ns.shareWithFriend = share

-- The only real chat message the addon sends: one Battle.net whisper with a link, on the player's
-- click, at most once a day per friend and never to one who has the addon. Returns ok, message.
function ns.inviteFriend(tag)
  -- A contact, or someone who shared with you, runs the addon (an older one may not answer PING).
  local known = ns.shareSettings().contacts[tag] or (WoWCharacterTrackerDB.friends and WoWCharacterTrackerDB.friends[tag])
  if known then return false, label(tag) .. " has the addon already." end
  local friend
  for _, f in ipairs(online()) do
    if f.tag == tag then
      if heard[f.account] then return false, label(tag) .. " has the addon already." end
      if not silent[f.account] then return false, "Still checking whether " .. label(tag) .. " has the addon." end
      friend = friend or f
    end
  end
  if not friend then return false, label(tag) .. " isn't online in this game right now." end
  local at = invitedAt(tag)
  if at and time() - at < INVITE_EVERY then return false, "You invited " .. label(tag) .. " today already." end
  local whisper = (C_BattleNet and C_BattleNet.SendWhisper) or BNSendWhisper
  if not (whisper and friend.bnet) then return false, "Can't whisper " .. label(tag) .. " from here." end
  if ns.chatLocked() then
    return false, "Can't whisper right now. Try again after the fight."
  end
  if not pcall(whisper, friend.bnet, INVITE) then return false, "The whisper to " .. label(tag) .. " didn't go." end
  local db = WoWCharacterTrackerDB
  db.invitedAt = db.invitedAt or {}
  db.invitedAt[tag] = time()
  return true, "Invited " .. label(tag) .. "."
end

-- What contacts you keep (receive, not blocked) shared with you: friends by label, characters newest first.
function ns.sharedWithYou()
  local s, out = ns.shareSettings(), {}
  for tag, e in pairs(WoWCharacterTrackerDB.friends or {}) do
    local contact = s.contacts[tag]
    if contact and contact.receive and not s.blocked[tag] then
      local chars = {}
      for _, char in pairs(e.chars or {}) do chars[#chars + 1] = char end
      if #chars > 0 then
        table.sort(chars, function(x, y)
          if (x.at or 0) ~= (y.at or 0) then return (x.at or 0) > (y.at or 0) end
          return (x.char or "") < (y.char or "")
        end)
        out[#out + 1] = { tag = tag, label = label(tag), chars = chars }
      end
    end
  end
  table.sort(out, byLabel)
  return out
end
