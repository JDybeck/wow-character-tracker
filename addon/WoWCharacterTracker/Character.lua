-- What the addon knows about the character on screen: professions, talents, stats, guild, and the saved snapshot.
local ADDON, ns = ...

-- Numbers like stats can be "secret" on Midnight-era clients (retail 12.x, Forever) at times like a ding or a
-- fight: doing math with them is an error. num() turns those (and anything not a number) into nil,
-- so they're skipped and the snapshot keeps the last readable ones.
local function num(v)
  if type(v) ~= "number" or (issecretvalue and issecretvalue(v)) then return nil end
  return v
end

-- Primary professions in Classic-style clients. Names are matched in English,
-- so on a non-English client the profession list will come back empty.
local CLASSIC_PROFESSIONS = {
  Alchemy = true, Blacksmithing = true, Enchanting = true, Engineering = true,
  Herbalism = true, Leatherworking = true, Mining = true, Skinning = true, Tailoring = true,
}
-- Returns e.g. "Mining 150, Herbalism 75", or "" when there are none (the post then skips the line).
local function getProfessions()
  local found = {}

  if type(GetProfessions) == "function" then
    -- Retail: the two main professions are the first two return values.
    local p1, p2 = GetProfessions()
    for _, idx in ipairs({ p1 or 0, p2 or 0 }) do
      if idx > 0 then
        local name, _, rank = GetProfessionInfo(idx)
        if name then table.insert(found, string.format("%s %d", name, rank or 0)) end
      end
    end
  elseif type(GetNumSkillLines) == "function" then
    -- Classic: scan the skill list for primary professions.
    for i = 1, GetNumSkillLines() do
      local name, isHeader, _, rank = GetSkillLineInfo(i)
      if name and not isHeader and CLASSIC_PROFESSIONS[name] then
        table.insert(found, string.format("%s %d", name, rank or 0))
      end
    end
  end

  return table.concat(found, ", ")
end

-- Retail point split, e.g. "Class 15 / Fury 12 / Slayer 3".
-- The class and spec trees share one trait tree with one currency each (class first),
-- and the hero tree has its own currency. Returns nil if anything is missing.
local function getRetailTalentSplit(specName)
  if not (C_ClassTalents and C_Traits) then return nil end
  local configID = C_ClassTalents.GetActiveConfigID()
  if not configID then return nil end
  local config = C_Traits.GetConfigInfo(configID)
  local treeID = config and config.treeIDs and config.treeIDs[1]
  if not treeID then return nil end

  local currencies = C_Traits.GetTreeCurrencyInfo(configID, treeID, false) or {}
  if not currencies[1] then return nil end

  local heroName, heroCurrency
  local heroID = C_ClassTalents.GetActiveHeroTalentSpec and C_ClassTalents.GetActiveHeroTalentSpec()
  if heroID then
    local sub = C_Traits.GetSubTreeInfo(configID, heroID)
    if sub then heroName, heroCurrency = sub.name, sub.traitCurrencyID end
  end

  local classSpent, specSpent, heroSpent = 0, 0, 0
  local regular = 0
  for _, c in ipairs(currencies) do
    if heroCurrency and c.traitCurrencyID == heroCurrency then
      heroSpent = c.spent or 0
    else
      regular = regular + 1
      if regular == 1 then classSpent = c.spent or 0
      elseif regular == 2 then specSpent = c.spent or 0 end
    end
  end

  if classSpent + specSpent + heroSpent == 0 then return nil end -- nothing to show yet
  local text = string.format("Class %d / %s %d", classSpent, specName, specSpent)
  if heroName then text = text .. string.format(" / %s %d", heroName, heroSpent) end
  return text
end

-- The classic talent trees, left to right as the talent window shows them.
local CLASSIC_TREES = {
  WARRIOR = { "Arms", "Fury", "Protection" }, PALADIN = { "Holy", "Protection", "Retribution" },
  HUNTER = { "Beast Mastery", "Marksmanship", "Survival" }, ROGUE = { "Assassination", "Combat", "Subtlety" },
  PRIEST = { "Discipline", "Holy", "Shadow" }, SHAMAN = { "Elemental", "Enhancement", "Restoration" },
  MAGE = { "Arcane", "Fire", "Frost" }, WARLOCK = { "Affliction", "Demonology", "Destruction" },
  DRUID = { "Balance", "Feral Combat", "Restoration" },
}

-- Forever (Classic+) keeps the classic three talent trees, but as one modern trait tree with the
-- three side by side. Nodes are grouped into trees by their X position (a wide gap between
-- columns starts the next tree). Returns "Holy 0 / Protection 0 / Retribution 16", the tree with
-- the most points (shown as the spec), and the picked talents as "spellID~rank~maxRank~tree,...".
local function getTraitTalents()
  if not (C_ClassTalents and C_ClassTalents.GetActiveConfigID and C_Traits and C_Traits.GetTreeNodes) then return nil end
  local configID = C_ClassTalents.GetActiveConfigID()
  if not configID then return nil end
  local config = C_Traits.GetConfigInfo(configID)
  local treeID = config and config.treeIDs and config.treeIDs[1]
  if not treeID then return nil end

  local nodes, xs, seen = {}, {}, {}
  for _, nodeID in ipairs(C_Traits.GetTreeNodes(treeID) or {}) do
    local node = C_Traits.GetNodeInfo(configID, nodeID)
    if node and node.posX then
      nodes[#nodes + 1] = node
      if not seen[node.posX] then seen[node.posX] = true; xs[#xs + 1] = node.posX end
    end
  end
  if #nodes == 0 then return nil end
  table.sort(xs)
  -- Columns inside a tree are about 600 apart; trees are 2000+ apart.
  local treeOfX, tree = {}, 1
  for i, x in ipairs(xs) do
    if i > 1 and x - xs[i - 1] > 1200 then tree = tree + 1 end
    treeOfX[x] = tree
  end

  local points, picks, total = {}, {}, 0
  for i = 1, tree do points[i] = 0 end
  for _, node in ipairs(nodes) do
    local ranks = node.ranksPurchased or node.currentRank or 0
    if ranks > 0 then
      local t = treeOfX[node.posX]
      points[t] = points[t] + ranks
      total = total + ranks
      local entryID = (node.activeEntry and node.activeEntry.entryID) or (node.entryIDs and node.entryIDs[1])
      local entry = entryID and C_Traits.GetEntryInfo and C_Traits.GetEntryInfo(configID, entryID)
      local def = entry and entry.definitionID and C_Traits.GetDefinitionInfo and C_Traits.GetDefinitionInfo(entry.definitionID)
      local spellID = def and (def.spellID or def.overriddenSpellID)
      if spellID then picks[#picks + 1] = string.format("%d~%d~%d~%d", spellID, ranks, node.maxRanks or ranks, t) end
    end
  end
  if total == 0 then return nil end

  local _, classFile = UnitClass("player")
  local names = CLASSIC_TREES[classFile or ""] or {}
  -- Wowhead's classic-style calculator code: per tree, every talent in reading order (top row
  -- first, left to right) as its rank, trailing zeros left out, trees joined by "-".
  local byTree = {}
  for i = 1, tree do byTree[i] = {} end
  for _, node in ipairs(nodes) do table.insert(byTree[treeOfX[node.posX]], node) end
  local codes = {}
  for i = 1, tree do
    table.sort(byTree[i], function(a, b) if a.posY ~= b.posY then return (a.posY or 0) < (b.posY or 0) end return a.posX < b.posX end)
    local digits = {}
    for _, node in ipairs(byTree[i]) do digits[#digits + 1] = tostring(math.min(9, node.ranksPurchased or node.currentRank or 0)) end
    codes[i] = (table.concat(digits):gsub("0+$", ""))
  end
  local calc = (table.concat(codes, "-"):gsub("%-+$", ""))

  local parts, best, bestPoints = {}, nil, -1
  for i = 1, tree do
    local name = (tree == 3 and names[i]) or ("Tree " .. i)
    parts[#parts + 1] = string.format("%s %d", name, points[i])
    if points[i] > bestPoints then best, bestPoints = name, points[i] end
  end
  return table.concat(parts, " / "), best, table.concat(picks, ","), calc
end

-- Returns e.g. "Arms 31 / Fury 20 / Protection 0", or nil when there is nothing to show yet.
-- Also returns the spec name on retail (nil on Classic).
function ns.getTalents()
  if type(GetNumTalentTabs) == "function" and GetNumTalentTabs() > 0 then
    -- Classic: one entry per talent tree with points spent.
    local parts, total = {}, 0
    for i = 1, GetNumTalentTabs() do
      local a, b, c, _, e = GetTalentTabInfo(i)
      local name, points
      if type(a) == "number" then
        name, points = b, e -- newer return order: id, name, description, icon, pointsSpent
      else
        name, points = a, c -- classic era order: name, icon, pointsSpent
      end
      points = points or 0
      total = total + points
      table.insert(parts, string.format("%s %d", name or "?", points))
    end
    if total == 0 then return nil end -- under level 10: nothing to show, so nothing is stored
    return table.concat(parts, " / ")
  elseif type(GetSpecialization) == "function" then
    -- Retail: class, spec and hero trees.
    local spec = GetSpecialization()
    local specName = spec and select(2, GetSpecializationInfo(spec))
    if not specName then return nil end
    local ok, split = pcall(getRetailTalentSplit, specName)
    if ok and split then return split, specName end
    return nil, specName
  end
  local ok, traits, specName, picks, calc = pcall(getTraitTalents)
  if ok then return traits, specName, picks, calc end
  return nil
end

-- Forever has surnames: UnitName gives the surname where other clients give the realm.
ns.HAS_SURNAMES = C_PlayerInfo and C_PlayerInfo.ShouldDisplaySurname ~= nil

local function getSurname()
  if not ns.HAS_SURNAMES then return nil end
  local _, surname = UnitName("player")
  if surname and surname ~= "" then return surname end
  return nil
end

local function getZone()
  local zone = (GetRealZoneText and GetRealZoneText()) or (GetZoneText and GetZoneText())
  if zone == nil or zone == "" then return "an unknown place" end
  return zone
end

-- What kind of place the zone is: dungeon, raid, pvp (battleground or arena), rest (an inn or a
-- city, where the character is resting) or zone. The app tells capital cities from towns by name.
local function getZoneType()
  local inInstance, kind = IsInInstance()
  if inInstance then
    if kind == "raid" then return "raid" end
    if kind == "pvp" or kind == "arena" then return "pvp" end
    return "dungeon"
  end
  if IsResting and IsResting() then return "rest" end
  return "zone"
end

local function getItemLevel()
  if type(GetAverageItemLevel) ~= "function" then return nil end
  local overall, equipped = GetAverageItemLevel()
  local v = num(equipped) or num(overall)
  if v and v > 0 then return math.floor(v * 10 + 0.5) / 10 end
  return nil
end

-- "TANK", "HEALER" or "DAMAGER" on retail, nil where there are no specs.
local function getRole()
  -- Retail only: Forever's single class "spec" always says DAMAGER.
  if type(GetSpecializationRole) ~= "function" or type(GetSpecialization) ~= "function" then return nil end
  local spec = GetSpecialization()
  if spec then
    local ok, role = pcall(GetSpecializationRole, spec)
    if ok then return role end
  end
  return nil
end

-- Character sheet numbers as "Name~value,...": the primary stats, armor, and what the client has
-- of attack power, spell power, crit, haste, mastery and versatility.
local function getStats()
  if type(UnitStat) ~= "function" then return nil end
  local parts = {}
  local function add(name, value)
    if value and value ~= 0 then parts[#parts + 1] = name .. "~" .. value end
  end
  local function pct(v) v = num(v); return v and v > 0 and string.format("%.1f%%", v) or nil end
  for i, name in ipairs({ "Strength", "Agility", "Stamina", "Intellect", "Spirit" }) do
    local ok, _, effective = pcall(UnitStat, "player", i)
    effective = ok and num(effective)
    if effective then add(name, math.floor(effective)) end
  end
  if UnitArmor then local _, armor = UnitArmor("player"); armor = num(armor); add("Armor", armor and math.floor(armor)) end
  if UnitAttackPower then
    local base, pos, neg = UnitAttackPower("player")
    base, pos, neg = num(base), num(pos), num(neg)
    if base then add("Attack Power", math.floor(base + (pos or 0) + (neg or 0))) end
  end
  if GetSpellBonusDamage then
    local best = 0
    for school = 2, 7 do best = math.max(best, num(GetSpellBonusDamage(school)) or 0) end
    add("Spell Power", best)
  end
  if GetCritChance then add("Crit", pct(GetCritChance())) end
  if GetHaste then add("Haste", pct(GetHaste())) end
  if GetMasteryEffect then add("Mastery", pct((GetMasteryEffect()))) end
  if GetCombatRatingBonus and CR_VERSATILITY_DAMAGE_DONE then
    local vers = num(GetCombatRatingBonus(CR_VERSATILITY_DAMAGE_DONE))
    local bonus = GetVersatilityBonus and num(GetVersatilityBonus(CR_VERSATILITY_DAMAGE_DONE)) or 0
    if vers then add("Versatility", pct(vers + (bonus or 0))) end
  end
  if #parts == 0 then return nil end
  return table.concat(parts, ",")
end

-- The game's talent loadout string (what "Export" in the talent window gives), for Wowhead's
-- talent calculator. Retail has it; nil where the client doesn't.
local function getTalentExport()
  if not (C_Traits and C_Traits.GenerateImportString and C_ClassTalents and C_ClassTalents.GetActiveConfigID) then return nil end
  local configID = C_ClassTalents.GetActiveConfigID()
  if not configID then return nil end
  local ok, s = pcall(C_Traits.GenerateImportString, configID)
  if ok and type(s) == "string" and s ~= "" then return s end
  return nil
end

function ns.isHardcore()
  return C_GameRules and C_GameRules.IsHardcoreActive and C_GameRules.IsHardcoreActive() or false
end

-- Self-found shows as an aura on the player (spell 431567 in Classic Era).
local function isSelfFound()
  if C_UnitAuras and C_UnitAuras.GetPlayerAuraBySpellID then
    return C_UnitAuras.GetPlayerAuraBySpellID(431567) ~= nil
  end
  return false
end

-- "Hardcore", "Self-found", "Hardcore, Self-found" or "Normal", for grouping characters in the app.
local function getRuleset()
  local parts = {}
  if ns.isHardcore() then table.insert(parts, "Hardcore") end
  if isSelfFound() then table.insert(parts, "Self-found") end
  if #parts == 0 then return "Normal" end
  return table.concat(parts, ", ")
end

-- Retail crests as "id~quantity~name,..." (the app shows them with their icons), or nil.
-- Read from the game's own Crests section of the currency window, so only this season's crests
-- show up (older ones stay in your currencies but under other sections). A collapsed Crests
-- section can't be read without changing your window, so then the last known crests are kept.
local function getCrests()
  if not (C_CurrencyInfo and C_CurrencyInfo.GetCurrencyListSize and C_CurrencyInfo.GetCurrencyListInfo) then return nil end
  local parts, inCrests = {}, false
  for i = 1, C_CurrencyInfo.GetCurrencyListSize() do
    local info = C_CurrencyInfo.GetCurrencyListInfo(i)
    if info then
      if info.isHeader then
        if inCrests then break end
        inCrests = info.name ~= nil and info.name:lower():find("crest") ~= nil and info.isHeaderExpanded ~= false
      elseif inCrests then
        local link = C_CurrencyInfo.GetCurrencyListLink and C_CurrencyInfo.GetCurrencyListLink(i)
        local id = link and tonumber(link:match("currency:(%d+)")) or 0
        table.insert(parts, string.format("%d~%d~%s", id, info.quantity or 0, (info.name or "?"):gsub("[~,]", " ")))
      end
    end
  end
  if #parts == 0 then return nil end
  return table.concat(parts, ",")
end

-- Retail: the Mythic Keystone in your bags as "level~mapID~dungeon", "none" without one, nil on
-- clients without Mythic+ (Forever).
local function getKeystone()
  if not (C_MythicPlus and C_MythicPlus.GetOwnedKeystoneChallengeMapID) then return nil end
  local mapID = C_MythicPlus.GetOwnedKeystoneChallengeMapID()
  local level = C_MythicPlus.GetOwnedKeystoneLevel and C_MythicPlus.GetOwnedKeystoneLevel()
  if not mapID or not level or level == 0 then return "none" end
  local name = C_ChallengeMode and C_ChallengeMode.GetMapUIInfo and C_ChallengeMode.GetMapUIInfo(mapID)
  return string.format("%d~%d~%s", level, mapID, ((name or "?"):gsub("[~,;=]", " ")))
end

-- Best guess of total /played right now.
function ns.playedNow()
  if not ns.lastPlayed then return nil end
  return ns.lastPlayed + (time() - ns.lastPlayedAt)
end

-- Guild name, "" when not in one, nil when the game doesn't know yet (right after login).
local function getGuild()
  if not (IsInGuild and IsInGuild()) then return "" end
  local name = GetGuildInfo and GetGuildInfo("player")
  return name
end

-- "eu", "us", "kr", "tw" or "cn": the app needs it to find the Retail character render.
local REGIONS = { "us", "kr", "eu", "tw", "cn" }
local function getRegion()
  local id = GetCurrentRegion and GetCurrentRegion()
  return id and REGIONS[id] or nil
end

-- Runs one piece of character info protected: if the game refuses something (a secret value, a
-- function that isn't ready), only that field is left out instead of losing the whole event.
local function safe(fn, ...)
  if type(fn) ~= "function" then return nil end
  local ok, a, b, c, d = pcall(fn, ...)
  if ok then return a, b, c, d end
  ns.lastInfoError = a -- for /wct debug
  return nil
end

-- Everything we show about the character, both in posts and in the companion app.
function ns.characterInfo()
  local className, classFile = safe(UnitClass, "player")
  local raceName, raceFile = safe(UnitRace, "player")
  local sex = safe(UnitSex, "player") -- 2 = male, 3 = female
  local talents, spec, picks, calc = safe(ns.getTalents)
  local info = {
    guid = UnitGUID("player"),
    char = safe(UnitName, "player"),
    surname = safe(getSurname),
    realm = safe(GetRealmName),
    region = safe(getRegion),
    level = safe(UnitLevel, "player"),
    zone = safe(getZone),
    zoneType = safe(getZoneType),
    professions = safe(getProfessions),
    talents = talents,
    talentPicks = picks, -- Forever: "spellID~rank~maxRank~tree,..." (saved file only, too long for the grid)
    talentExport = safe(getTalentExport), -- saved file only
    talentCalc = calc, -- Forever: Wowhead talent calculator code (short, goes live too)
    stats = safe(getStats), -- saved file only
    spec = spec,
    role = safe(getRole),
    ilvl = safe(getItemLevel),
    class = className,
    classFile = classFile,
    race = raceName,
    raceFile = raceFile,
    sex = (sex == 3) and "female" or "male",
    hardcore = safe(ns.isHardcore) or nil,
    ruleset = safe(getRuleset),
    money = safe(GetMoney), -- copper
    crests = safe(getCrests),
    keystone = safe(getKeystone),
    guild = safe(getGuild),
    -- For the app's time-to-max estimate (saved file only).
    xp = num(safe(UnitXP, "player")),
    xpMax = num(safe(UnitXPMax, "player")),
    maxLevel = num(safe(GetMaxPlayerLevel)) or num(MAX_PLAYER_LEVEL),
  }
  -- Secret values can't be saved or sent (and doing anything with them is an error), so none stay.
  if issecretvalue then
    for k, v in pairs(info) do
      if issecretvalue(v) then info[k] = nil end
    end
  end
  return info
end

-- Keeps one up-to-date entry per character in WoWCharacterTrackerDB.chars.
-- Right after login and during logout the game can report no gear, spec or professions yet.
-- Values that come back empty keep what was saved before instead of wiping it.
-- (Money too: during logout GetMoney() comes back as 0.)
local KEEP_IF_EMPTY = { "ilvl", "spec", "role", "talents", "talentPicks", "talentExport", "talentCalc", "stats", "professions", "zone", "played", "crests", "money" }

function ns.saveSnapshot()
  ns.doing("snapshot")
  local guid = UnitGUID("player")
  if not guid then return end
  local info = ns.characterInfo()
  info.played = ns.playedNow()
  info.lastSeen = time()
  local old = WoWCharacterTrackerDB.chars[guid]
  if old then
    -- Talents that can't be read right now (game not ready, logout) keep the old text through
    -- KEEP_IF_EMPTY below. (This used to follow the spec, but Forever has no specs at all.)
    for _, k in ipairs(KEEP_IF_EMPTY) do
      local v = info[k]
      if v == nil or v == "" or v == 0 or v == "an unknown place" or v == "Unknown" then info[k] = old[k] end
    end
    -- Guild: "" means no guild (keep that), nil means not known yet.
    if info.guild == nil then info.guild = old.guild end
  end
  -- Older versions stored the class as Forever's "spec" and a role that means nothing there.
  if info.spec == info.class then info.spec = nil end
  if type(GetSpecialization) ~= "function" then info.role = nil end
  WoWCharacterTrackerDB.chars[guid] = info
  -- The XP each level needs on this client, so the app can estimate time to max level.
  if info.level and info.xpMax and info.xpMax > 0 and WoWCharacterTrackerDB.xpTable then
    WoWCharacterTrackerDB.xpTable[info.level] = info.xpMax
  end
end
