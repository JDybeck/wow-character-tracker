-- Recording what happens: level-ups (with /played), end boss kills, loot, mounts and hardcore deaths.
local ADDON, ns = ...

-- Last boss of each Classic dungeon and raid (English names, as ENCOUNTER_END and the
-- combat log report them). Killing one of these is posted as an end boss kill.
local END_BOSSES = {
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
  ["Balnazzar"] = "Stratholme",
  ["Baron Rivendare"] = "Stratholme",
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
  ns.suppressChat = false
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

function ns.requestPlayed()
  ns.doing("RequestTimePlayed")
  ns.suppressChat = true
  mutePlayed()
  RequestTimePlayed()
  -- Safety net so the player's own /played is never muted for long.
  C_Timer.After(5, ns.unmutePlayed)
end

-- (Older versions also replaced ChatFrame_DisplayTimePlayed to hide it. Replacing Blizzard
-- functions taints the default UI, which on Classic clients blocked Esc and showed "blocked from an
-- action" errors, so that's gone. Muting the chat windows above does the same job safely.)

-- Optional (off by default, /wct popup): offer a reload after a ding, for when the live
-- channel doesn't work on a client.
-- The dialog is only added to Blizzard's StaticPopupDialogs when it's used: anything an addon
-- puts in Blizzard's tables can spread taint, so it stays out unless the player turned this on.
function ns.offerPost(level)
  if not WoWCharacterTrackerDB.popup then return end
  if InCombatLockdown() then
    ns.popupLevel = level -- show it when combat ends
    return
  end
  if not StaticPopupDialogs["CHARACTERTRACKER_POST"] then
    StaticPopupDialogs["CHARACTERTRACKER_POST"] = {
      text = "Ding! Level %d.\n\nReload your UI now to post it to Discord?",
      button1 = "Post now",
      button2 = "Later",
      OnAccept = function() ReloadUI() end,
      timeout = 0,
      whileDead = true,
      hideOnEscape = true,
      preferredIndex = 3,
    }
  end
  StaticPopup_Show("CHARACTERTRACKER_POST", level)
end

local getGroup -- below, with the boss kills

function ns.recordLevel(guid, level, seconds, played)
  local e = ns.characterInfo()
  e.kind = "level"
  e.id = guid .. ":" .. level
  e.level = level -- UnitLevel can lag right after the ding
  e.seconds = seconds -- nil when the level's start isn't known
  -- Max level gets a big post (always, whatever the level rule says).
  local maxLevel = (GetMaxPlayerLevel and GetMaxPlayerLevel()) or MAX_PLAYER_LEVEL
  if maxLevel and level >= maxLevel then
    e.maxLevel = true
    e.group, e.groupSize = getGroup()
  end
  e.played = played -- total /played at the ding
  e.at = time()
  ns.addEvent(e)
end

-- The other people in the party: "Name~level~CLASSFILE~Class~Race~Realm" per member, joined by
-- commas. Raids list only how many there are, since 40 names don't fit a post.
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
      table.insert(members, table.concat({ name or "?", UnitLevel(unit) or 0, classFile or "", className or "",
        raceName or "", (realm and realm ~= "") and realm or "" }, "~"))
    end
  end
  return table.concat(members, ","), size
end

function ns.recordBoss(name)
  local instance = END_BOSSES[name]
  if not instance then return end
  local now = time()
  if ns.recentBosses[name] and now - ns.recentBosses[name] < 600 then return end
  ns.recentBosses[name] = now

  local e = ns.characterInfo()
  e.kind = "boss"
  e.boss = name
  e.instance = (GetInstanceInfo and GetInstanceInfo()) or instance
  e.group, e.groupSize = getGroup()
  e.at = now
  e.id = string.format("%s:boss:%s:%d", e.guid, name, now)
  ns.addEvent(e)
  ns.say(string.format("End boss down: %s.", name))
end

-- LOOT_ITEM_SELF is "You receive loot: %s." (and a "%sx%d." variant for stacks). Turned into
-- Lua patterns so this works with the client's own language.
local function toPattern(fmt)
  if type(fmt) ~= "string" then return nil end
  local p = fmt:gsub("[%(%)%.%+%-%*%?%[%]%^%$]", "%%%0")
  p = p:gsub("%%s", "(.+)"):gsub("%%d", "(%%d+)")
  return "^" .. p .. "$"
end
local function patterns(...)
  local list = {}
  for _, fmt in ipairs({ ... }) do
    local p = toPattern(fmt)
    if p then table.insert(list, p) end
  end
  return list
end
local LOOT_PATTERNS = patterns(LOOT_ITEM_SELF_MULTIPLE, LOOT_ITEM_SELF)
-- "You receive item: %s." covers vendor purchases and quest rewards.
local PUSHED_PATTERNS = patterns(LOOT_ITEM_PUSHED_SELF_MULTIPLE, LOOT_ITEM_PUSHED_SELF)

local QUALITY_BY_COLOR = { ["ff0070dd"] = 3, ["ffa335ee"] = 4, ["ffff8000"] = 5, ["ffe6cc80"] = 6 }

local function matchAny(list, text)
  for _, p in ipairs(list) do
    local link = text:match(p)
    if link then return link end
  end
  return nil
end

local lastMountAt = 0

function ns.recordMount(name, itemId)
  -- Buying a mount item and then learning it would count twice on clients with a mount journal.
  if time() - lastMountAt < 120 then return end
  lastMountAt = time()

  local e = ns.characterInfo()
  e.kind = "mount"
  e.mount = name
  e.itemId = itemId
  e.bought = ns.merchantOpen or nil
  e.at = time()
  e.id = string.format("%s:mount:%s:%d", e.guid, name, e.at)
  ns.addEvent(e)
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
  if pushed then return end -- rare loot means looted, not bought or handed out

  local quality = GetItemInfo and select(3, GetItemInfo(link))
  if not quality then
    local color = link:match("|c(%x%x%x%x%x%x%x%x)")
    quality = color and QUALITY_BY_COLOR[color:lower()]
  end
  if not quality or quality < 3 then return end -- rare (blue) and better only

  local e = ns.characterInfo()
  e.kind = "loot"
  e.item = name
  e.itemId = itemId
  e.quality = quality
  e.at = time()
  e.id = string.format("%s:loot:%d:%d", e.guid, itemId, e.at)
  ns.addEvent(e)
end

function ns.recordDeath()
  if not ns.isHardcore() then return end
  local e = ns.characterInfo()
  e.kind = "death"
  e.played = ns.playedNow()
  e.gold = GetMoney and GetMoney() or 0 -- copper
  -- (No killer: that came from the combat log, which newer clients keep from addons.)
  e.at = time()
  e.id = string.format("%s:death:%d", e.guid, e.at)
  ns.addEvent(e)
  ns.saveSnapshot()
end
