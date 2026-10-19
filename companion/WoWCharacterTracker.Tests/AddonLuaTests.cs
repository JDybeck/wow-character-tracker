using MoonSharp.Interpreter;

namespace WoWCharacterTracker.Tests;

// The addon's Lua, checked with MoonSharp (the same interpreter the app reads saved files with).
public class AddonLuaTests
{
    static string AddonDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "addon", "WoWCharacterTracker"))) dir = dir.Parent;
            return Path.Combine(dir!.FullName, "addon", "WoWCharacterTracker");
        }
    }

    public static TheoryData<string> LuaFiles() => [.. Directory.GetFiles(AddonDir, "*.lua").Select(Path.GetFileName)!];

    [Theory]
    [MemberData(nameof(LuaFiles))]
    public void Addon_file_compiles(string file) =>
        new Script().LoadString(File.ReadAllText(Path.Combine(AddonDir, file)), codeFriendlyName: file);

    // Runs Announce.lua with WoW's chat functions stubbed; returns what it sent ("CHANNEL: text").
    static List<string> Announce(string settings, string eventLua, bool inGuild = true, bool inGroup = false, bool inRaid = false)
    {
        var sent = new List<string>();
        var script = new Script();
        script.Globals["SendChatMessage"] = (Action<string, string>)((msg, channel) => sent.Add($"{channel}: {msg}"));
        script.Globals["IsInGuild"] = (Func<bool>)(() => inGuild);
        script.Globals["IsInGroup"] = (Func<bool>)(() => inGroup);
        script.Globals["IsInRaid"] = (Func<bool>)(() => inRaid);
        script.Globals["InCombatLockdown"] = (Func<bool>)(() => false);
        script.DoString("function GetItemInfo(id) return \"Sul'thraze\", '|cffa335ee|Hitem:'..id..\"|h[Sul'thraze the Lasher]|h|r\" end");
        script.DoString("WoWCharacterTrackerSettings = " + settings);
        var ns = new Table(script);
        foreach (var file in new[] { "Shared.lua", "Announce.lua" })
            script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonDir, file))), "WoWCharacterTracker", ns);
        script.Call(ns.Get("announce"), script.DoString("return " + eventLua));
        return sent;
    }

    // ns.record: what characterInfo knows plus the event's fields, and its id. characterInfo's
    // maxLevel (the level cap, for the snapshot) must not make every event a max level ding.
    [Fact]
    public void Recorded_events_are_not_max_level_unless_they_say_so()
    {
        var script = new Script();
        var ns = new Table(script);
        script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonDir, "Shared.lua"))), "WoWCharacterTracker", ns);
        script.DoString("function time() return 1000 end");
        var recorded = new List<Table>();
        ns["characterInfo"] = (Func<Table>)(() => script.DoString("return { guid = 'Player-1', level = 5, maxLevel = 60 }").Table);
        ns["addEvent"] = (Action<Table>)recorded.Add;

        script.Call(ns.Get("record"), "loot", script.DoString("return { item = 'X', at = 900 }"), "loot:1:900");
        script.Call(ns.Get("record"), "level", script.DoString("return { level = 60, maxLevel = true }"), "60");
        script.Call(ns.Get("record"), "death");

        Assert.True(recorded[0].Get("maxLevel").IsNil());
        Assert.Equal("Player-1:loot:1:900", recorded[0].Get("id").String);
        Assert.Equal(900, recorded[0].Get("at").Number);
        Assert.True(recorded[1].Get("maxLevel").Boolean);
        Assert.Equal("Player-1:death:1000", recorded[2].Get("id").String);
    }

    const string GuildOnly = "{ announce = { guild = true, party = false, kinds = { level = true, boss = true, loot = true, duel = true } } }";

    [Fact]
    public void A_level_up_goes_to_guild_chat_with_its_time() =>
        Assert.Equal(["GUILD: Ding! Level 23. 22 to 23 took 41m 12s."], Announce(GuildOnly, "{ kind = 'level', level = 23, seconds = 2472 }"));

    [Fact]
    public void Loot_is_an_item_link_and_only_epic_or_better() =>
        Assert.Equal(["GUILD: Looted |cffa335ee|Hitem:17069|h[Sul'thraze the Lasher]|h|r!"],
            [.. Announce(GuildOnly, "{ kind = 'loot', item = \"Sul'thraze the Lasher\", itemId = 17069, quality = 4 }"),
             .. Announce(GuildOnly, "{ kind = 'loot', item = 'Blue thing', itemId = 1, quality = 3 }")]);

    [Fact]
    public void Boss_kills_leave_the_group_out_and_go_to_the_raid_in_a_raid() =>
        Assert.Equal(["RAID: Killed Edwin VanCleef in The Deadmines!"],
            Announce("{ announce = { party = true, kinds = { boss = true } } }",
                "{ kind = 'boss', boss = 'Edwin VanCleef', instance = 'The Deadmines', group = 'Zeb~40~WARRIOR~Warrior~Orc~' }", inGroup: true, inRaid: true));

    [Fact]
    public void Nothing_for_losses_kinds_left_out_or_no_channel()
    {
        Assert.Empty(Announce(GuildOnly, "{ kind = 'duel', opponent = 'Grimtusk', lost = true }"));
        Assert.Empty(Announce(GuildOnly, "{ kind = 'mount', mount = 'Swift Palomino' }"));
        Assert.Empty(Announce("{ announce = { guild = false, party = false, kinds = { level = true } } }", "{ kind = 'level', level = 5 }"));
        Assert.Empty(Announce("{ }", "{ kind = 'level', level = 5 }")); // Settings.lua from an older app
    }

    // /wct note: the note comes from the Settings.lua the app writes (quotes, new lines and "|" kept
    // as text), the box remembers open/locked, and a locked box lets clicks through.
    [Fact]
    public void The_note_box_shows_the_characters_note_from_the_apps_settings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wct-note-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Interface", "AddOns", "WoWCharacterTracker"));
        try
        {
            GameNote[] notes = [new("Player-1", "Farm \"Sul'thraze\"\nthen | the bank", 1000), new("Player-2", "Other", 1000)];
            WoWCharacterTracker.Services.AddonInstaller.WriteSettings(dir, new FlavorSettings(), notes);
            var script = new Script();
            script.DoString(File.ReadAllText(Path.Combine(dir, "Interface", "AddOns", "WoWCharacterTracker", "Settings.lua")));
            script.DoString("""
                WoWCharacterTrackerDB = {}
                function UnitGUID() return "Player-1" end
                function UnitName() return "Ronnie" end
                UIParent = {}
                -- Any frame method does nothing, except the ones the box reads back.
                local function widget()
                  local w = { shown = false }
                  return setmetatable(w, { __index = function(_, k)
                    if k:sub(1, 1) == k:sub(1, 1):lower() then return nil end -- fields, not methods
                    if k == "Show" then return function(s) s.shown = true end end
                    if k == "Hide" then return function(s) s.shown = false end end
                    if k == "IsShown" then return function(s) return s.shown end end
                    if k == "SetText" then return function(s, t) s.text = t end end
                    if k == "EnableMouse" then return function(s, on) s.mouse = on end end
                    if k == "SetColorTexture" then return function(s, r, g, b, a) s.alpha = a end end
                    if k == "GetStringHeight" then return function() return 40 end end
                    if k == "IsMouseOver" then return function() return mouseOver end end
                    if k == "SetShown" then return function(s, on) s.shown = on end end
                    if k == "SetScript" then return function(s, ev, f) rawset(s, "on" .. ev, f) end end
                    if k == "CreateTexture" or k == "CreateFontString" then return function() return widget() end end
                    return function() end
                  end })
                end
                local frames = {}
                function CreateFrame() local f = widget(); frames[#frames + 1] = f; return f end
                -- The box is the first frame, its X the second.
                function WoWCharacterTrackerNoteBoxButtonsShown() return frames[2].shown end
                function WoWCharacterTrackerNoteBoxTick() frames[1].onOnUpdate(frames[1], 1) end
                """);
            var ns = new Table(script);
            ns["say"] = (Action<string>)(_ => { });
            foreach (var file in new[] { "Shared.lua", "NoteBox.lua" })
                script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonDir, file))), "WoWCharacterTracker", ns);

            script.Call(ns.Get("toggleNoteBox"));
            // Buttons hidden until the mouse is over the box, or always shown with the option off.
            Assert.False(script.DoString("return WoWCharacterTrackerNoteBoxButtonsShown()").Boolean);
            script.DoString("mouseOver = true");
            script.DoString("WoWCharacterTrackerNoteBoxTick()");
            Assert.True(script.DoString("return WoWCharacterTrackerNoteBoxButtonsShown()").Boolean);
            script.DoString("mouseOver = false");
            script.Call(ns.Get("setNoteButtonsOnHover"), false);
            Assert.True(script.DoString("return WoWCharacterTrackerNoteBoxButtonsShown()").Boolean);
            Assert.Equal("Farm \"Sul'thraze\"\nthen || the bank", script.Call(ns.Get("currentNote")).String);

            // Edited in game later: that one wins, until the app writes a newer one.
            script.DoString("function time() return 2000 end");
            script.Call(ns.Get("saveNote"), "Edited in game");
            Assert.Equal("Edited in game", script.Call(ns.Get("currentNote")).String);
            script.DoString("WoWCharacterTrackerSettings.notes['Player-1'] = { text = 'From the app', at = 3000 }");
            Assert.Equal("From the app", script.Call(ns.Get("currentNote")).String);
            Assert.True(script.DoString("return WoWCharacterTrackerDB.noteBox.shown").Boolean);

            script.Call(ns.Get("lockNoteBox"), true);
            Assert.True(script.DoString("return WoWCharacterTrackerDB.noteBox.locked").Boolean);
            Assert.Equal(0.6, script.Call(ns.Get("noteAlpha")).Number); // until set in the options
            script.Call(ns.Get("setNoteAlpha"), 0.25);
            Assert.Equal(0.25, script.DoString("return WoWCharacterTrackerDB.noteBox.alpha").Number);

            script.Call(ns.Get("toggleNoteBox"));
            Assert.False(script.DoString("return WoWCharacterTrackerDB.noteBox.shown").Boolean);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // Options.lua: registers a page under the game's AddOns options, and lists what the app set
    // (from the Settings.lua it writes) when the page opens.
    [Fact]
    public void The_options_page_registers_and_lists_the_apps_settings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wct-options-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Interface", "AddOns", "WoWCharacterTracker"));
        try
        {
            WoWCharacterTracker.Services.AddonInstaller.WriteSettings(dir, new FlavorSettings { AnnounceGuild = true, PostBosses = true });
            var script = new Script();
            script.DoString(File.ReadAllText(Path.Combine(dir, "Interface", "AddOns", "WoWCharacterTracker", "Settings.lua")));
            script.DoString("""
                WoWCharacterTrackerDB = { live = false }
                UIParent = {}
                frames, texts = {}, {}
                registered = nil
                Settings = {
                  RegisterCanvasLayoutCategory = function(frame, name) return { name = name, GetID = function() return 1 end } end,
                  RegisterAddOnCategory = function(c) registered = c.name end,
                }
                local function widget()
                  local w = { scripts = {} }
                  return setmetatable(w, { __index = function(_, k)
                    if k:sub(1, 1) == k:sub(1, 1):lower() then return nil end -- fields, not methods
                    if k == "SetScript" then return function(s, ev, f) s.scripts[ev] = f end end
                    if k == "SetText" then return function(s, t) s.text = t; texts[#texts + 1] = t end end
                    if k == "SetChecked" then return function(s, v) s.checked = v end end
                    if k == "CreateTexture" or k == "CreateFontString" then return function() return widget() end end
                    return function() end
                  end })
                end
                function CreateFrame() local f = widget(); frames[#frames + 1] = f; return f end
                """);
            var ns = new Table(script);
            ns["say"] = (Action<string>)(_ => { });
            foreach (var file in new[] { "Shared.lua", "NoteBox.lua", "Options.lua" })
                script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonDir, file))), "WoWCharacterTracker", ns);

            Assert.Equal("WoW Character Tracker", script.DoString("return registered").String);
            script.DoString("frames[1].scripts.OnShow(frames[1])");
            var summary = script.DoString("return texts[#texts]").String;
            Assert.Contains("boss kills", summary);
            Assert.Contains("Told in chat: guild", summary);
            Assert.False(script.DoString("return frames[2].checked").Boolean); // instant posting, off in the saved file
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // Inventory.lua's scanGearEffects over stubbed tooltips: head and legs are tier (the legs a
    // catalysted piece, which the game's tooltip counts all the same), the chest has an Equip line.
    [Fact]
    public void Set_bonuses_and_effects_come_from_the_gear_tooltips()
    {
        var script = new Script();
        script.DoString("""
            -- As the game shows it: active bonuses without their count, others with it, per spec on tier.
            local set = { "|cffffd100Baleful Grave-Knight's Battlegear (2/5)|r", "  Baleful Grave-Knight's Casque",
              "|cff00ff00Set: Heart Strike deals 10% more damage.|r", "(2) Set Frost: Remorseless Winter hits harder.",
              "(4) Set Blood: Your Bone Shield lasts longer.", "Sets the item level: not a bonus." }
            function GetSpecialization() return 1 end
            function GetSpecializationInfo() return 250, "Blood" end
            local tips = {
              [1] = { "Baleful Grave-Knight's Casque", "Item Level 321", unpack(set) },
              [5] = { "Plain Breastplate", "Enchanted: Crystalline Radiance |A:Professions-ChatIcon-Quality-Tier3:20:20|a",
                "Equip: Your attacks have a chance to heal you." },
              [7] = { "Catalysted Greaves", unpack(set) },
            }
            function GetInventoryItemID(_, slot) return tips[slot] and (1000 + slot) or nil end
            function GetInventoryItemLink(_, slot) return slot == 5 and "|cffffffff|Hitem:1005:7980::::::::80:::::|h[Plain Breastplate]|h|r" or nil end
            C_TooltipInfo = { GetInventoryItem = function(_, slot)
              local lines = {}
              for i, t in ipairs(tips[slot] or {}) do lines[i] = { leftText = t } end
              return { lines = lines }
            end }
            """);
        var ns = new Table(script);
        foreach (var file in new[] { "Character.lua", "Inventory.lua" })
            script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonDir, file))), "WoWCharacterTracker", ns);
        var result = script.Call(ns.Get("scanGearEffects")).Tuple;
        Assert.Equal("Baleful Grave-Knight's Battlegear~2~5~0~Heart Strike deals 10% more damage.\n"
            + "Baleful Grave-Knight's Battlegear~2~5~4~Your Bone Shield lasts longer.", result[0].String);
        Assert.Equal("5~1005~Equip: Your attacks have a chance to heal you.", result[1].String);
        Assert.Equal("5~1005~Crystalline Radiance~7980", result[2].String); // the enchant id from the item link
    }
}
