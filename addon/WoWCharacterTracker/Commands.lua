-- The /wct slash commands.
local ADDON, ns = ...

local function showHistory()
  local guid = UnitGUID("player")
  local shown = 0
  for i = #WoWCharacterTrackerDB.events, 1, -1 do
    local e = WoWCharacterTrackerDB.events[i]
    if (e.kind == nil or e.kind == "level") and e.id == guid .. ":" .. e.level then
      local took = e.seconds and ns.formatDuration(e.seconds) or "an unknown time" -- logged without a starting point
      ns.say(string.format("Level %d took %s", e.level - 1, took))
      shown = shown + 1
      if shown >= 10 then break end
    end
  end
  if shown == 0 then ns.say("No level times recorded yet for this character.") end
end

local function sendTest()
  if not ns.hasApp() then
    ns.say("The app isn't set up for this game. Discord posts need the free Character Tracker app: " .. ns.APP_URL .. " /wct has what works without it.")
    return
  end
  if WoWCharacterTrackerDB.live == false then
    ns.say("Instant posting is off. Type /wct live to turn it on.")
    return
  end
  ns.sendLive(ns.liveEvent("test"))
  ns.say("Sent a test event. The companion app should show it within a few seconds.")
end

-- A guild line to see what the game lets through: from the ask box's button (like a real moment), or
-- "now" straight from this slash command. A refused send prints a Blocked line naming "chat GUILD".
local GUILD_TEST_LINE = "Character Tracker guild chat test, please ignore."
local function guildTest(now)
  if not (IsInGuild and IsInGuild()) then return ns.say("You're not in a guild.") end
  if now then
    ns.sayInGuild(GUILD_TEST_LINE)
    ns.say("Sent from the slash command: " .. tostring(ns.lastChatSend))
    return
  end
  ns.offerGuildLine({ kind = "test", at = time() }, GUILD_TEST_LINE)
  ns.say("Click Send in the box. No Blocked line after it and the line in guild chat means clicks work.")
end

-- What this client's functions return, for tracking down problems on new clients. Saved as
-- WoWCharacterTrackerDB.debug (the SavedVariables file is written on /reload or logout).
local function saveDebug()
  local out = {}
  local function add(s) out[#out + 1] = s end
  local function dump(...)
    local t = {}
    for i = 1, select("#", ...) do t[#t + 1] = tostring((select(i, ...))) end
    return table.concat(t, ", ")
  end

  add("build: " .. dump(GetBuildInfo()))
  add("level: " .. tostring(UnitLevel("player")))
  add("played now (estimate): " .. tostring(ns.playedNow()))
  for _, line in ipairs(WoWCharacterTrackerDB.playedLog or {}) do add("played log: " .. line) end
  add("last character info error: " .. tostring(ns.lastInfoError))
  for _, line in ipairs(WoWCharacterTrackerDB.blocked or {}) do add("blocked: " .. line) end
  add("talents now: " .. dump(ns.getTalents()))
  -- The trait tree as the addon reads it: currencies, and where each node sits (posX,posY:ranks),
  -- which decides the Wowhead talent calculator code.
  pcall(function()
    local configID, treeID, config = ns.activeTraitTree()
    add("trait config " .. tostring(configID) .. ", trees " .. table.concat(config.treeIDs or {}, ","))
    for _, c in ipairs(C_Traits.GetTreeCurrencyInfo(configID, treeID, false) or {}) do
      add(string.format("currency %s: spent %s of %s", tostring(c.traitCurrencyID), tostring(c.spent), tostring(c.maxQuantity)))
    end
    local pos = {}
    for _, nodeID in ipairs(C_Traits.GetTreeNodes(treeID) or {}) do
      local n = C_Traits.GetNodeInfo(configID, nodeID)
      if n then pos[#pos + 1] = string.format("%s,%s:%s", tostring(n.posX), tostring(n.posY), tostring(n.ranksPurchased or 0)) end
    end
    add("nodes: " .. table.concat(pos, " "))
  end)
  add("UnitName: " .. dump(UnitName("player")) .. ", surnames: " .. tostring(ns.HAS_SURNAMES))
  add("guild: " .. dump(IsInGuild and IsInGuild(), GetGuildInfo and GetGuildInfo("player")))
  add("hardcore: " .. dump(ns.isHardcore()))
  add("comms error: " .. tostring(ns.lastCommsError)) -- the last friend message our code couldn't handle
  add(string.format("guild/group chat: last %s, waiting %d, locked %s", tostring(ns.lastChatSend),
    ns.pendingAnnouncements and ns.pendingAnnouncements() or 0, tostring(ns.chatLocked())))
  if UpdateAddOnMemoryUsage and GetAddOnMemoryUsage then
    UpdateAddOnMemoryUsage()
    add(string.format("memory: %d KB, %d events", GetAddOnMemoryUsage(ADDON) or 0, #(WoWCharacterTrackerDB.events or {})))
  end

  WoWCharacterTrackerDB.debug = out
  for _, s in ipairs(out) do if #s < 250 then ns.say(s) end end
  ns.say("Saved. /reload to write it to the SavedVariables file.")
end

-- Incognito (Shared.lua) with a chat line, and one more when it runs out. seconds nil: off now.
local INCOGNITO_TIMES = { ["30m"] = 1800, ["1h"] = 3600, ["2h"] = 7200, ["4h"] = 14400 }
function ns.goIncognito(seconds, friends)
  if not seconds then
    if ns.incognitoActive() then
      ns.endIncognito()
      ns.say("Incognito is off. New moments post and share as usual; what happened meanwhile stays unposted.")
    end
    return
  end
  ns.startIncognito(seconds, friends)
  local w = ns.incognitoActive()
  ns.say(string.format("Incognito until %s: nothing is posted to Discord or said in chat%s, and none of it is posted later.",
    date("%H:%M", w["until"]), friends and ", and friends don't get these highlights" or ""))
  C_Timer.After(seconds + 1, function()
    if not ns.incognitoActive() and w["until"] <= time() and not w.said then
      w.said = true
      ns.say("Incognito is over. New moments post and share as usual.")
    end
  end)
end

local function incognitoCommand(arg)
  local word, friends = arg:match("^(%S*)%s*(%S*)$")
  if word == "off" then return ns.goIncognito(nil) end
  local seconds = INCOGNITO_TIMES[word or ""]
  if not seconds then
    local w = ns.incognitoActive()
    ns.say(w and string.format("Incognito until %s. /wct incognito off ends it.", date("%H:%M", w["until"]))
      or "/wct incognito 30m, 1h, 2h or 4h (add friends to keep those highlights from friends too), /wct incognito off.")
    return
  end
  ns.goIncognito(seconds, friends == "friends")
end
ns.INCOGNITO_TIMES = INCOGNITO_TIMES

SLASH_CHARACTERTRACKER1 = "/charactertracker"
SLASH_CHARACTERTRACKER2 = "/wct"
SlashCmdList["CHARACTERTRACKER"] = function(msg)
  local typed = strtrim(msg or "")
  msg = typed:lower()
  if msg == "" or msg == "home" then
    ns.openWindow("home")
  elseif msg == "times" then
    showHistory()
  elseif msg == "friends" or msg:match("^friends%s") then
    ns.friendsCommand(typed:sub(8)) -- BattleTags keep their case
  elseif msg == "recipes" or msg:match("^recipes%s") then
    ns.openRecipeSearch(strtrim(typed:sub(8))) -- who knows a recipe, among your characters and friends
  elseif msg == "incognito" or msg:match("^incognito%s") then
    incognitoCommand(strtrim(msg:sub(10)))
  elseif msg == "popup" then
    WoWCharacterTrackerDB.popup = not WoWCharacterTrackerDB.popup
    ns.say("Reload popup after a ding is now " .. (WoWCharacterTrackerDB.popup and "on." or "off."))
  elseif msg == "live" then
    WoWCharacterTrackerDB.live = WoWCharacterTrackerDB.live == false
    ns.say("Instant posting is now " .. (WoWCharacterTrackerDB.live and "on." or "off. Posts arrive on /reload or logout."))
  elseif msg == "test" then
    sendTest()
  elseif msg == "guildtest" or msg == "guildtest now" then
    guildTest(msg == "guildtest now")
  elseif msg == "debug" then
    saveDebug()
  elseif msg == "options" or msg == "config" then
    ns.openWindow() -- every option is in the window, which also opens in combat
  elseif msg == "note" then
    ns.toggleNoteBox()
  elseif msg == "note lock" then
    ns.lockNoteBox(true)
  elseif msg == "note unlock" then
    ns.lockNoteBox(false)
  elseif msg == "note reset" then
    ns.resetNoteBox()
    ns.say("Note box moved back to the middle of the screen.")
  elseif msg == "help" then
    -- The four most need; the rest (times, live, popup, options, debug, note lock/reset) still work.
    ns.say("/wct opens the addon's window on Home: the character you play, its level times and your friends. The minimap button opens it too.")
    ns.say("/wct note shows or hides this character's note.")
    ns.say("/wct friends shares your characters with Battle.net friends who have the addon. /wct friends help for more.")
    ns.say("/wct recipes <name> finds who knows a recipe, among your characters and what friends share.")
    ns.say("/wct test checks that instant posting reaches the companion app.")
    ns.say("/wct incognito 1h stops posting and chat lines for an hour (30m, 2h, 4h; off ends it). Nothing from it is posted later.")
  else
    ns.say("Unknown command. /wct help lists them.")
  end
end
