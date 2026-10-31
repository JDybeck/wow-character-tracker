-- Recording what happens: level-ups (with /played), end boss kills (Mythic+ runs on Retail), loot,
-- mounts and hardcore deaths.
local ADDON, ns = ...

-- Last boss of each dungeon and raid (English names, as ENCOUNTER_END reports them). Killing one
-- of these is posted as an end boss kill. The name on the right is only a fallback for the post:
-- the game's own instance name (GetInstanceInfo) is used when there is one.
--
-- KEEP THIS UP TO DATE (check on every release PR, see .github/pull_request_template.md):
-- Forever adds dungeons and raids as it goes. The game keeps a "<Boss> Kills (<Dungeon>)"
-- statistic for each final boss, which Wowhead lists, so new ones show up there:
-- https://www.wowhead.com/forever/guide/dungeons-overview-locations-details
-- https://www.wowhead.com/forever/guide/raids-overview-hub-dates-locations
-- Still unknown (October 2026): The Drowned City, Krol'dok Stronghold, Blackmaw Hold, and the
-- Forever raids (The Wild King in Hyjal Summit and Sonya Darkhallow in Barrow Deeps have kill
-- statistics already; add them once the raids are out).
-- Later, if players ask: other languages. Names only match an English client; the encounter id
-- from ENCOUNTER_END would work in every language.
local END_BOSSES = {
  -- Forever's new dungeons (final bosses as the game's kill statistics name them).
  ["Durgen Dirgehammer"] = "The Hall of Thanes",
  ["Rath'mael"] = "Ruins of Lordaeron",
  ["Relic Guardian"] = "Excavation Site: Wetlands", -- from the dungeon guide, no kill statistic yet
  ["Shade of the Archmage"] = "City of Dalaran",
  ["Blazeroar"] = "Alcaz Prison",
  ["Nanaya"] = "Shaper's Terrace",
  -- Classic dungeons and raids (also in Forever).
  ["Bazzalan"] = "Ragefire Chasm",
  ["Mutanus the Devourer"] = "Wailing Caverns",
  ["Edwin VanCleef"] = "The Deadmines",
  ["Archmage Arugal"] = "Shadowfang Keep",
  ["Aku'mai"] = "Blackfathom Deeps",
  ["Bazil Thredd"] = "The Stockade",
  ["Mekgineer Thermaplugg"] = "Gnomeregan",
  ["Charlga Razorflank"] = "Razorfen Kraul",
  ["Bloodmage Thalnos"] = "Scarlet Monastery Graveyard",
  ["Arcanist Doan"] = "Scarlet Monastery Library",
  ["Herod"] = "Scarlet Monastery Armory",
  ["High Inquisitor Whitemane"] = "Scarlet Monastery Cathedral",
  ["Amnennar the Coldbringer"] = "Razorfen Downs",
  ["Archaedas"] = "Uldaman",
  ["Chief Ukorz Sandscalp"] = "Zul'Farrak",
  ["Princess Theradras"] = "Maraudon",
  ["Shade of Eranikus"] = "Sunken Temple",
  ["Emperor Dagran Thaurissan"] = "Blackrock Depths",
  ["Overlord Wyrmthalak"] = "Lower Blackrock Spire",
  ["General Drakkisath"] = "Upper Blackrock Spire",
  ["Alzzin the Wildshaper"] = "Dire Maul East",
  ["Prince Tortheldrin"] = "Dire Maul West",
  ["King Gordok"] = "Dire Maul North",
  ["Darkmaster Gandling"] = "Scholomance",
  ["Balnazzar"] = "Stratholme (Living side)",
  ["Baron Rivendare"] = "Stratholme (Undead side)",
  ["Onyxia"] = "Onyxia's Lair",
  ["Ragnaros"] = "Molten Core",
  ["Nefarian"] = "Blackwing Lair",
  ["Hakkar"] = "Zul'Gurub",
  ["Ossirian the Unscarred"] = "Ruins of Ahn'Qiraj",
  ["C'Thun"] = "Temple of Ahn'Qiraj",
  ["Kel'Thuzad"] = "Naxxramas",
}

-- Events ---------------------------------------------------------------------

-- Our own /played requests would print "Total time played" in chat. While one is out, the chat
-- windows stop listening for the answer; they listen again once it has arrived.
local mutedFrames = {}

function ns.unmutePlayed()
  for _, f in ipairs(mutedFrames) do f:RegisterEvent("TIME_PLAYED_MSG") end
  mutedFrames = {}
end

local function mutePlayed()
  for i = 1, (NUM_CHAT_WINDOWS or 10) do
    local f = _G["ChatFrame" .. i]
    if f and f.IsEventRegistered and f:IsEventRegistered("TIME_PLAYED_MSG") then
      f:UnregisterEvent("TIME_PLAYED_MSG")
      table.insert(mutedFrames, f)
    end
  end
end

-- The last 20 /played requests and answers with times, for /wct debug: shows whether the game
-- answers the addon's own requests on this client.
function ns.notePlayed(text)
  local log = WoWCharacterTrackerDB.playedLog or {}
  WoWCharacterTrackerDB.playedLog = log
  table.insert(log, date("%H:%M:%S ") .. text)
  while #log > 20 do table.remove(log, 1) end
end

function ns.requestPlayed(reason)
  ns.notePlayed("request: " .. (reason or "?"))
  ns.doing("RequestTimePlayed")
  mutePlayed()
  RequestTimePlayed()
  -- Safety net so the player's own /played is never muted for long.
  C_Timer.After(5, ns.unmutePlayed)
end

-- (Older versions also replaced ChatFrame_DisplayTimePlayed to hide it. Replacing Blizzard
-- functions taints the default UI, which on Classic clients blocked Esc and showed "blocked from an
-- action" errors, so that's gone. Muting the chat windows above does the same job safely.)

-- Reload prompts ----------------------------------------------------------------
-- Small boxes that offer a /reload (the game only writes the saved file then, and only a click may
-- reload). A dialog is only added to Blizzard's StaticPopupDialogs when it's first used: anything an
-- addon puts in Blizzard's tables can spread taint, so it stays out unless it's needed.
local function askReload(name, text, accept, decline, arg)
  if not StaticPopupDialogs[name] then
    StaticPopupDialogs[name] = {
      text = text, button1 = accept, button2 = decline,
      OnAccept = function() ReloadUI() end,
      timeout = 0, whileDead = true, hideOnEscape = true, preferredIndex = 3,
    }
  end
  StaticPopup_Show(name, arg)
end

-- Optional (off by default, /wct popup): after a ding, for when the live channel doesn't work on a client.
function ns.offerPost(level)
  if not WoWCharacterTrackerDB.popup then return end
  if InCombatLockdown() then
    ns.popupLevel = level -- show it when combat ends
    return
  end
  askReload("CHARACTERTRACKER_POST", "Ding! Level %d.\n\nReload your UI now to post it to Discord?", "Post now", "Later", level)
end

-- On a flight path, with gear or bags changed since the last save: so the app gets the new items
-- (the flight goes on through the loading screen). Once per flight, never in combat, and only when
-- the app's "Offer a reload on flight paths" is on (Settings.lua flightReload).
local askedThisFlight = false

function ns.onFlightStart()
  if askedThisFlight or ns.setting("flightReload") == false or not (UnitOnTaxi and UnitOnTaxi("player")) then return end
  if InCombatLockdown() or not ns.itemsChanged() then return end
  askedThisFlight = true
  askReload("CHARACTERTRACKER_FLIGHT_RELOAD",
    "Character Tracker\n\nYour bags changed since the last save.\nReload now to update the app? (You keep flying.)", "Reload", "Not now")
end

function ns.onFlightEnd() askedThisFlight = false end

-- A line in chat when gear or bags changed since the last save: at good moments (moment = true:
-- after the bank, mailbox, a vendor or the auction house, leaving a dungeon), at most every 10
-- minutes, and as a reminder every 30 minutes. Only when the app's "Remind me in chat to /reload" is
-- on (Settings.lua chatReminder).
local lastReminder = time()
function ns.remindReload(moment)
  if ns.setting("chatReminder") == false or not ns.itemsChanged() then return end
  if time() - lastReminder < (moment and 600 or 1800) then return end
  lastReminder = time()
  ns.say("Your bags changed since the last save. Type /reload to update the app (no logout needed).")
end

local getGroup -- below, with the boss kills

-- Level times ----------------------------------------------------------------
-- WoWCharacterTrackerDB.start[guid] is the total played when the current level began, and
-- startLevel[guid] the level it belongs to; a finished level's time is the difference. The ding is
-- timed right away from the played time already known (ns.playedNow), so posts don't wait on the
-- server; /played answers, when the game sends them, make the next level's start exact.

local function knownEvent(id)
  for _, e in ipairs(WoWCharacterTrackerDB.events) do
    if e.id == id then return true end
  end
  return false
end

-- Times a finished level: newStart is the total played when `level` began. If the saved start
-- belongs to the level before, the level is done (also later, at login, when a /reload or logout
-- came before the game answered the ding's /played request). Returns true when the level is
-- accounted for (recorded now or before). live = right at the ding.
local function finishLevel(guid, level, newStart, total, live)
  local oldStart = WoWCharacterTrackerDB.start[guid]
  local startLevel = WoWCharacterTrackerDB.startLevel[guid]
  -- Level 1 always starts at 0 played, so 1 to 2 never needs a baseline (a brand new character's
  -- login answer can go missing, and the addon may have been installed after creating it).
  if level == 2 and startLevel ~= 2 then oldStart, startLevel = 0, 1 end
  if not (oldStart and newStart >= oldStart and (startLevel == level - 1 or (startLevel == nil and live))) then return false end
  if not knownEvent(guid .. ":" .. level) then
    local seconds = newStart - oldStart
    ns.recordLevel(guid, level, seconds, total)
    ns.say(string.format("Level %d took %s.", level - 1, ns.formatDuration(seconds)))
    if live then ns.offerPost(level) end
  end
  return true
end

-- A ding without a starting point for its level (the addon came in mid-level) is still logged,
-- without a time.
local function logUntimed(guid, level, total)
  if knownEvent(guid .. ":" .. level) then return end
  ns.recordLevel(guid, level, nil, total)
  ns.say(string.format("Level %d logged. Its time is unknown (no starting point for level %d yet).", level, level - 1))
  ns.offerPost(level)
end

local function setStart(guid, level, start)
  WoWCharacterTrackerDB.start[guid] = start
  WoWCharacterTrackerDB.startLevel[guid] = level
end

-- Played time doesn't move while logged out, so where it was at logout is where this session
-- starts. Level times then don't depend on the game answering /played requests, which some clients
-- (Forever) seem to ignore unless the player typed it.
function ns.onLogin()
  local guid = UnitGUID("player")
  local atLogout = WoWCharacterTrackerDB.logoutPlayed[guid]
  WoWCharacterTrackerDB.logoutPlayed[guid] = nil -- only good for the login right after it
  if atLogout then
    ns.lastPlayed, ns.lastPlayedAt = atLogout, time()
  elseif UnitLevel("player") == 1 and not WoWCharacterTrackerDB.chars[guid] then
    ns.lastPlayed, ns.lastPlayedAt = 0, time() -- a brand new character
  end
  C_Timer.After(3, function() ns.requestPlayed("login") end) -- once the world has loaded
end

function ns.onLogout() -- also on /reload, right before the game writes the saved file
  local played = ns.playedNow()
  if played then WoWCharacterTrackerDB.logoutPlayed[UnitGUID("player")] = played end
  ns.saveSnapshot()
end

function ns.onLevelUp(level)
  local guid = UnitGUID("player")
  ns.pendingLevel = level
  local total = ns.playedNow()
  if total then
    if not finishLevel(guid, level, total, total, true) then logUntimed(guid, level, total) end
    setStart(guid, level, total)
  end
  -- Ask the server too, for the exact start of the new level. The answer can be slow or not come at
  -- all, so ask a moment later and again after a while.
  for _, delay in ipairs({ 1, 15, 45 }) do
    C_Timer.After(delay, function() if ns.pendingLevel == level then ns.requestPlayed("level " .. level) end end)
  end
end

function ns.onTimePlayed(total, thisLevel)
  local guid = UnitGUID("player")
  local newStart = total - thisLevel
  ns.lastPlayed, ns.lastPlayedAt = total, time()
  ns.notePlayed(string.format("answer: total %d, this level %d", total, thisLevel))

  local level = ns.pendingLevel or UnitLevel("player") -- UnitLevel can lag right after a ding
  local live = ns.pendingLevel ~= nil
  if not finishLevel(guid, level, newStart, total, live) and live then logUntimed(guid, level, total) end
  ns.pendingLevel = nil
  setStart(guid, level, newStart)
  ns.saveSnapshot()
  -- Not right away: frames registered during this event could still get this same message.
  C_Timer.After(0.5, ns.unmutePlayed)
end
function ns.recordLevel(guid, level, seconds, played)
  local fields = {
    level = level, -- UnitLevel can lag right after the ding
    seconds = seconds, -- nil when the level's start isn't known
    played = played, -- total /played at the ding
  }
  -- Max level gets a big post (always, whatever the level rule says).
  local maxLevel = (GetMaxPlayerLevel and GetMaxPlayerLevel()) or MAX_PLAYER_LEVEL
  if maxLevel and level >= maxLevel then
    fields.maxLevel = true
    fields.group, fields.groupSize = getGroup()
  end
  ns.record("level", fields, tostring(level))
end

-- Party members' specs (Retail), by name: other players' spec needs an inspect, so the party is
-- inspected when a key starts (everyone is close then), one at a time. GetInspectSpecialization also
-- answers from what the client already knows, so it's asked again when the group is recorded.
local groupSpecs, inspectQueue, inspecting = {}, {}, nil

local function specName(unit)
  if not GetInspectSpecialization then return nil end
  local ok, id = pcall(GetInspectSpecialization, unit)
  id = ok and ns.plain(id)
  if type(id) ~= "number" or id <= 0 or not GetSpecializationInfoByID then return nil end
  return ns.plain((select(2, GetSpecializationInfoByID(id))))
end

local function inspectNext()
  inspecting = nil
  while #inspectQueue > 0 do
    local unit = table.remove(inspectQueue, 1)
    if UnitExists(unit) and CanInspect and CanInspect(unit) then
      inspecting = unit
      ns.doing("NotifyInspect")
      NotifyInspect(unit)
      C_Timer.After(2, function() if inspecting == unit then inspectNext() end end) -- no answer: go on
      return
    end
  end
end

function ns.inspectGroup()
  if not (NotifyInspect and IsInGroup and IsInGroup()) or (IsInRaid and IsInRaid()) then return end
  inspectQueue = {}
  for i = 1, 4 do
    local unit = "party" .. i
    local name = UnitExists(unit) and ns.plain(UnitName(unit))
    if name and not groupSpecs[name] then table.insert(inspectQueue, unit) end
  end
  if not inspecting then inspectNext() end
end

function ns.onInspectReady()
  local unit = inspecting
  if not unit then return end
  local name, spec = ns.plain(UnitName(unit)), specName(unit)
  if name and spec then groupSpecs[name] = spec end
  C_Timer.After(1.5, inspectNext) -- the server limits how quickly inspects may follow
end

-- The other people in the party: "Name~level~CLASSFILE~Class~Race~Realm~Spec" per member (spec on
-- Retail when known), joined by commas. Raids list only how many there are, since 40 names don't fit a post.
getGroup = function()
  if not IsInGroup or not IsInGroup() then return nil, 1 end
  local size = GetNumGroupMembers and GetNumGroupMembers() or 1
  if IsInRaid and IsInRaid() then return nil, size end
  local members = {}
  for i = 1, 4 do
    local unit = "party" .. i
    if UnitExists(unit) then
      local name, realm = UnitName(unit)
      if ns.HAS_SURNAMES then -- the second value is their surname here, not a realm
        if realm and realm ~= "" then name = name .. " " .. realm end
        realm = nil
      end
      local className, classFile = UnitClass(unit)
      local raceName = UnitRace(unit)
      local spec = (name and groupSpecs[name]) or specName(unit)
      table.insert(members, table.concat({ name or "?", UnitLevel(unit) or 0, classFile or "", className or "",
        raceName or "", (realm and realm ~= "") and realm or "", spec or "" }, "~"))
    end
  end
  return table.concat(members, ","), size
end

function ns.recordBoss(name)
  -- END_BOSSES is Classic's list; on Retail it only matched old raids soloed for transmog. Retail
  -- records finished Mythic+ runs instead (ns.recordKeyRun).
  if ns.IS_RETAIL then return end
  local instance = END_BOSSES[name]
  if not instance then return end
  local now = time()
  if ns.recentBosses[name] and now - ns.recentBosses[name] < 600 then return end
  ns.recentBosses[name] = now

  local group, groupSize = getGroup()
  ns.record("boss", { boss = name, instance = (GetInstanceInfo and GetInstanceInfo()) or instance,
    group = group, groupSize = groupSize, at = now }, string.format("boss:%s:%d", name, now))
  ns.say(string.format("End boss down: %s.", name))
end

-- Mythic+ runs (Retail) --------------------------------------------------------
-- CHALLENGE_MODE_COMPLETED fires for every finished keystone, in time or not (an abandoned key
-- never finishes). Recorded as kind "keyrun": the dungeon (instance), keyLevel, upgrades (1-3 when
-- timed, 0 over time), seconds (the run's time), rating (the overall score after it) and the group.
-- The app decides what gets posted (a lowest key level, timed only).

-- The finished run, from whichever completion API the client has (a table since 11.0).
local function completionInfo()
  local cm = C_ChallengeMode
  if not cm then return nil end
  if cm.GetChallengeCompletionInfo then
    local ok, info = pcall(cm.GetChallengeCompletionInfo)
    if ok and type(info) == "table" then return info end
  end
  if cm.GetCompletionInfo then
    local ok, mapID, level, ms, onTime, upgrades, practice, _, newScore = pcall(cm.GetCompletionInfo)
    if ok then
      return { mapChallengeModeID = mapID, level = level, time = ms, onTime = onTime,
        keystoneUpgradeLevels = upgrades, practiceRun = practice, newOverallDungeonScore = newScore }
    end
  end
  return nil
end

function ns.recordKeyRun()
  local info = completionInfo()
  local plain = ns.plain
  if not info or plain(info.practiceRun) then return end
  local mapID, keyLevel = plain(info.mapChallengeModeID), plain(info.level)
  if type(mapID) ~= "number" or type(keyLevel) ~= "number" or keyLevel <= 0 then return end

  local dungeon = C_ChallengeMode.GetMapUIInfo and plain((C_ChallengeMode.GetMapUIInfo(mapID)))
  local up = plain(info.keystoneUpgradeLevels) or 0
  local timed = plain(info.onTime)
  if timed == nil then timed = up > 0 end
  local ms, score = plain(info.time), plain(info.newOverallDungeonScore)
  local group, groupSize = getGroup()
  local now = time()
  ns.record("keyrun", {
    instance = dungeon or (GetInstanceInfo and GetInstanceInfo()) or nil,
    keyLevel = keyLevel,
    upgrades = timed and math.max(1, up) or 0,
    seconds = type(ms) == "number" and math.floor(ms / 1000) or nil,
    rating = (type(score) == "number" and score > 0) and math.floor(score) or nil,
    group = group, groupSize = groupSize, at = now,
  }, string.format("keyrun:%d:%d", mapID, now))
  ns.say(string.format("Mythic+ done: +%d %s, %s.", keyLevel, dungeon or "?", timed and ("timed +" .. math.max(1, up)) or "over time"))
end

-- LOOT_ITEM_SELF is "You receive loot: %s." (and a "%sx%d." variant for stacks). Turned into
-- Lua patterns so this works with the client's own language.
local function lootPattern(fmt)
  if type(fmt) ~= "string" then return nil end
  local p = fmt:gsub("[%(%)%.%+%-%*%?%[%]%^%$]", "%%%0")
  p = p:gsub("%%s", "(.+)"):gsub("%%d", "(%%d+)")
  return "^" .. p .. "$"
end
-- (By count, not ipairs: a client without the first string would otherwise lose them all.)
local function patterns(...)
  local list = {}
  for i = 1, select("#", ...) do
    local p = lootPattern((select(i, ...)))
    if p then table.insert(list, p) end
  end
  return list
end
local LOOT_PATTERNS = patterns(LOOT_ITEM_SELF_MULTIPLE, LOOT_ITEM_SELF)
-- "You receive item: %s." covers vendor purchases and quest rewards, "You create: %s." crafting.
local PUSHED_PATTERNS = patterns(LOOT_ITEM_PUSHED_SELF_MULTIPLE, LOOT_ITEM_PUSHED_SELF, LOOT_ITEM_CREATED_SELF_MULTIPLE, LOOT_ITEM_CREATED_SELF)

local QUALITY_BY_COLOR = { ["ff9d9d9d"] = 0, ["ffffffff"] = 1, ["ff1eff00"] = 2, ["ff0070dd"] = 3, ["ffa335ee"] = 4, ["ffff8000"] = 5, ["ffe6cc80"] = 6 }

local function matchAny(list, text)
  for _, p in ipairs(list) do
    local link = text:match(p)
    if link then return link end
  end
  return nil
end

local lastMountAt = 0

-- itemId when the mount came as an item, spellId when it was learned straight into the mount journal.
function ns.recordMount(name, itemId, spellId)
  -- Buying a mount item and then learning it would count twice on clients with a mount journal.
  if time() - lastMountAt < 120 then return end
  lastMountAt = time()

  local now = time()
  ns.record("mount", { mount = name, itemId = itemId, spellId = spellId, bought = ns.merchantOpen or nil, at = now },
    string.format("mount:%s:%d", name, now))
  ns.say(string.format("New mount: %s.", name))
end

-- Mount items are class 15 (miscellaneous), subclass 5 (mount).
local function isMountItem(link)
  local getInstant = (C_Item and C_Item.GetItemInfoInstant) or GetItemInfoInstant
  if not getInstant then return false end
  local _, _, _, _, _, classID, subclassID = getInstant(link)
  return classID == 15 and subclassID == 5
end

function ns.recordLoot(text)
  local link = matchAny(LOOT_PATTERNS, text)
  local pushed = false
  if not link then
    link = matchAny(PUSHED_PATTERNS, text)
    pushed = true
  end
  if not link then return end

  local itemId = tonumber(link:match("item:(%d+)"))
  local name = link:match("%[(.-)%]")
  if not itemId or not name then return end

  if isMountItem(link) then
    ns.recordMount(name, itemId)
    return
  end
  -- Items on the app's followed list count at any quality and however they came.
  local followed = ns.isFollowed(itemId)
  if pushed and not followed then return end -- rare loot means looted, not bought or handed out

  -- Quality is the 3rd value, the bind type the 14th (2 = Bind on Equip). Both nil while the game
  -- doesn't have the item yet: the quality then comes from the link's color, and it doesn't count as BoE.
  local quality, bindType
  if GetItemInfo then
    local info = { GetItemInfo(link) }
    quality, bindType = info[3], info[14]
  end
  if not quality then
    local color = link:match("|c(%x%x%x%x%x%x%x%x)")
    quality = color and QUALITY_BY_COLOR[color:lower()]
  end
  if not followed and (not quality or quality < 3) then return end -- rare (blue) and better only

  local now = time()
  ns.record("loot", { item = name, itemId = itemId, quality = quality, boe = bindType == 2 or nil, at = now },
    string.format("loot:%d:%d", itemId, now))
end

-- Profession milestones: a primary profession's skill passing one of Settings.lua's
-- professionSteps. The first reading of a profession only sets its baseline (like the rating
-- milestones), so skill from before the addon, or a profession just learned, posts nothing.
-- Once per character, profession and step (the id).
local DEFAULT_PROFESSION_STEPS = { 75, 150, 225, 300 }
function ns.checkProfessions()
  local guid = UnitGUID("player")
  if not guid or not WoWCharacterTrackerDB then return end
  local steps = (ns.setting("post") or {}).professionSteps or DEFAULT_PROFESSION_STEPS
  WoWCharacterTrackerDB.professions = WoWCharacterTrackerDB.professions or {}
  local known = WoWCharacterTrackerDB.professions[guid] or {}
  WoWCharacterTrackerDB.professions[guid] = known
  for _, p in ipairs(ns.professionRanks()) do
    local old = known[p.name]
    if p.rank <= 0 then -- hidden right now: keep the baseline
    elseif old then
      local step = ns.stepPassed(old, p.rank, steps)
      if step then
        ns.record("profession", { profession = p.name, step = step, rating = p.rank }, string.format("profession:%s:%d", p.name, step))
      end
    end
    if p.rank > 0 then known[p.name] = p.rank end
  end
end

function ns.recordDeath()
  if not ns.isHardcore() then return end
  -- (No killer: that came from the combat log, which newer clients keep from addons.)
  ns.record("death", { played = ns.playedNow(), gold = GetMoney and GetMoney() or 0 }) -- gold in copper
  ns.saveSnapshot()
end
