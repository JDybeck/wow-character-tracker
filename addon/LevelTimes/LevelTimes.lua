-- LevelTimes: records how long each character level takes.
-- Data lives in LevelTimesDB (SavedVariables), which the companion app reads.

local ADDON = "LevelTimes"

local frame = CreateFrame("Frame")
frame:RegisterEvent("ADDON_LOADED")
frame:RegisterEvent("PLAYER_LOGIN")
frame:RegisterEvent("PLAYER_LEVEL_UP")
frame:RegisterEvent("TIME_PLAYED_MSG")

local pendingLevel  -- set on level up, cleared when /played data arrives
local suppressChat = false

local function say(msg)
  print("|cff00ccff[LevelTimes]|r " .. msg)
end

local function formatDuration(s)
  local h = math.floor(s / 3600)
  local m = math.floor((s % 3600) / 60)
  local sec = s % 60
  if h > 0 then return string.format("%dh %dm", h, m) end
  if m > 0 then return string.format("%dm %ds", m, sec) end
  return string.format("%ds", sec)
end

local function requestPlayed()
  suppressChat = true
  RequestTimePlayed()
  -- Safety net so we never swallow the player's own /played forever.
  C_Timer.After(5, function() suppressChat = false end)
end

-- Hide the /played chat spam that our own requests would cause.
if type(ChatFrame_DisplayTimePlayed) == "function" then
  local original = ChatFrame_DisplayTimePlayed
  ChatFrame_DisplayTimePlayed = function(...)
    if suppressChat then return end
    return original(...)
  end
end

frame:SetScript("OnEvent", function(_, event, ...)
  if event == "ADDON_LOADED" then
    if (...) ~= ADDON then return end
    LevelTimesDB = LevelTimesDB or {}
    LevelTimesDB.start = LevelTimesDB.start or {}   -- [guid] = total played at the start of the current level
    LevelTimesDB.events = LevelTimesDB.events or {} -- one entry per finished level

  elseif event == "PLAYER_LOGIN" then
    requestPlayed() -- sets the baseline for the current level

  elseif event == "PLAYER_LEVEL_UP" then
    pendingLevel = (...) -- the level just reached
    requestPlayed()

  elseif event == "TIME_PLAYED_MSG" then
    local total, thisLevel = ...
    local guid = UnitGUID("player")
    local newStart = total - thisLevel
    local oldStart = LevelTimesDB.start[guid]

    if pendingLevel then
      if oldStart and newStart >= oldStart then
        local seconds = newStart - oldStart
        table.insert(LevelTimesDB.events, {
          id = guid .. ":" .. pendingLevel,
          char = UnitName("player"),
          realm = GetRealmName(),
          level = pendingLevel,
          seconds = seconds,
          at = time(),
        })
        say(string.format("Level %d took %s.", pendingLevel - 1, formatDuration(seconds)))
      else
        say("No baseline for the previous level (addon was installed mid-level), skipping.")
      end
      pendingLevel = nil
    end

    LevelTimesDB.start[guid] = newStart
    suppressChat = false
  end
end)

SLASH_LEVELTIMES1 = "/leveltimes"
SlashCmdList["LEVELTIMES"] = function()
  local guid = UnitGUID("player")
  local shown = 0
  for i = #LevelTimesDB.events, 1, -1 do
    local e = LevelTimesDB.events[i]
    if e.id == guid .. ":" .. e.level then
      say(string.format("Level %d took %s", e.level - 1, formatDuration(e.seconds)))
      shown = shown + 1
      if shown >= 10 then break end
    end
  end
  if shown == 0 then say("No level times recorded yet for this character.") end
end
