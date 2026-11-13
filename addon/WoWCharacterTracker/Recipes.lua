-- The recipes each character knows, per profession, under the game's category headers (Camping,
-- Reagents, Plate Helmets...). The game lists them only while that profession's window is open
-- (C_TradeSkillUI, the same on Forever and Retail), so they're read then and kept in
-- WoWCharacterTrackerDB.recipes[guid][profession] as "Camping=2660/Reagents=3115,3116": categories
-- in the window's order, recipe (spell) ids in it. New ones are added on NEW_RECIPE_LEARNED. The
-- snapshot's `recipes` (ns.recipeText) is what the app and friends get: "Blacksmithing~..." lines.
local ADDON, ns = ...

local cleanCategory = ns.cleanRecipeCategory

local function joinGroups(groups)
  local parts = {}
  for _, g in ipairs(groups) do
    if #g.ids > 0 then parts[#parts + 1] = cleanCategory(g.name) .. "=" .. table.concat(g.ids, ",") end
  end
  return table.concat(parts, "/")
end

-- What a scan found ({ { name, ids } } in the window's order) added to what was saved: the scan's
-- order first, then what only the old text had, each id once (a recipe that moved goes with the scan).
function ns.mergeRecipes(text, found)
  local seen, groups, byName = {}, {}, {}
  local function add(name, id)
    id = tonumber(id)
    if not id or id <= 0 or id ~= math.floor(id) or seen[id] then return end
    seen[id] = true
    name = cleanCategory(name)
    local g = byName[name]
    if not g then
      g = { name = name, ids = {} }
      byName[name] = g
      groups[#groups + 1] = g
    end
    g.ids[#g.ids + 1] = id
  end
  for _, g in ipairs(found or {}) do
    for _, id in ipairs(g.ids) do add(g.name, id) end
  end
  for _, g in ipairs(ns.recipeGroups(text)) do
    for _, id in ipairs(g.ids) do add(g.name, id) end
  end
  return joinGroups(groups)
end

-- lists: [profession] = text. keep: the professions the character still has ([name] = true), or nil
-- when the game hasn't said yet. "Alchemy~Potions=1,2\nCooking~Meat=3", by profession name; "" without any.
function ns.recipeText(lists, keep)
  local names = {}
  for name, text in pairs(lists or {}) do
    if type(text) == "string" and text ~= "" and (not keep or keep[name]) then names[#names + 1] = name end
  end
  table.sort(names)
  local lines = {}
  for _, name in ipairs(names) do lines[#lines + 1] = name .. "~" .. lists[name] end
  return table.concat(lines, "\n")
end

-- The names of every profession the character has (main ones, cooking, fishing...), nil when the
-- game doesn't list them (yet).
local function knownProfessions()
  if type(GetProfessions) ~= "function" or type(GetProfessionInfo) ~= "function" then return nil end
  local known, any = {}, false
  for _, idx in pairs({ GetProfessions() }) do
    local name = type(idx) == "number" and ns.plain((GetProfessionInfo(idx)))
    if type(name) == "string" and name ~= "" then
      known[name] = true
      any = true
    end
  end
  return any and known or nil
end

-- This character's recipes as text, for the snapshot.
function ns.ownRecipeText()
  local db = WoWCharacterTrackerDB
  local guid = UnitGUID("player")
  return ns.recipeText(db and db.recipes and db.recipes[guid], knownProfessions())
end

-- The recipe lists saved for the character being played.
local function ownLists()
  local db = WoWCharacterTrackerDB
  local guid = db and UnitGUID("player")
  if not guid then return nil end
  db.recipes = db.recipes or {}
  db.recipes[guid] = db.recipes[guid] or {}
  return db.recipes[guid], guid
end

-- Puts the new text on the saved snapshot right away, so friends get it with the next send.
local function updateSnapshot(guid)
  local saved = WoWCharacterTrackerDB.chars and WoWCharacterTrackerDB.chars[guid]
  if not saved then return end
  local text = ns.ownRecipeText()
  if text ~= "" then saved.recipes = text end
end

-- The open window's profession, or nil when it shows someone else's list (a link, the guild's, an
-- NPC crafter) or is still loading.
local function openProfession()
  local ui = C_TradeSkillUI
  if not (ui and ui.GetBaseProfessionInfo and ui.GetRecipeInfo) then return nil end
  for _, check in ipairs({ "IsTradeSkillLinked", "IsTradeSkillGuild", "IsTradeSkillGuildMember", "IsNPCCrafting", "IsRuneforging", "IsDataSourceChanging" }) do
    if ui[check] and ui[check]() then return nil end
  end
  local info = ui.GetBaseProfessionInfo()
  local name = info and ns.plain(info.professionName)
  return type(name) == "string" and name ~= "" and name or nil
end

-- A recipe's category header as the window shows it, "" when the game gives none.
local function categoryOf(info)
  local ui = C_TradeSkillUI
  local id = ns.plain(info.categoryID)
  local category = type(id) == "number" and ui.GetCategoryInfo and ui.GetCategoryInfo(id)
  return category and ns.plain(category.name) or ""
end

-- Reads the open profession's learned recipes. Added to what's saved, never replacing it: the
-- fallback list (GetFilteredRecipeIDs, when GetAllRecipeIDs is missing) follows the window's filters.
function ns.scanRecipes()
  local profession = openProfession()
  local lists, guid = ownLists()
  if not (profession and lists) then return end
  local ui = C_TradeSkillUI
  local all = ui.GetAllRecipeIDs or ui.GetFilteredRecipeIDs
  if not all then return end
  local found, byName = {}, {}
  for _, id in ipairs(all() or {}) do
    id = ns.plain(id)
    local info = type(id) == "number" and ui.GetRecipeInfo(id)
    if info and info.learned and not info.isDummyRecipe and not info.isRecraft then
      local name = categoryOf(info)
      local g = byName[name]
      if not g then
        g = { name = name, ids = {} }
        byName[name] = g
        found[#found + 1] = g
      end
      g.ids[#g.ids + 1] = id
    end
  end
  if #found == 0 then return end
  local before = lists[profession]
  lists[profession] = ns.mergeRecipes(before, found)
  if lists[profession] ~= before then updateSnapshot(guid) end
end

-- A recipe learned from a trainer or a book: added to its profession's list, but only once that list
-- has been read from the window (one recipe alone would read as all they know).
function ns.addLearnedRecipe(recipeID)
  recipeID = ns.plain(recipeID)
  local ui = C_TradeSkillUI
  local lists, guid = ownLists()
  if type(recipeID) ~= "number" or not (lists and ui and ui.GetProfessionInfoByRecipeID) then return end
  local info = ui.GetProfessionInfoByRecipeID(recipeID)
  local name = info and ns.plain(info.parentProfessionName or info.professionName)
  if type(name) ~= "string" or not lists[name] then return end
  local recipe = ui.GetRecipeInfo and ui.GetRecipeInfo(recipeID)
  local category = recipe and categoryOf(recipe) or ""
  -- Old ones first here, so the new one goes at the end of its category.
  local groups = ns.recipeGroups(lists[name])
  groups[#groups + 1] = { name = category, ids = { recipeID } }
  lists[name] = ns.mergeRecipes("", groups)
  updateSnapshot(guid)
end
