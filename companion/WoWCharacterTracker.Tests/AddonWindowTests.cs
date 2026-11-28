namespace WoWCharacterTracker.Tests;

// Window.lua and Ui.lua: the addon window with its tabs, built from stubbed frames. Every option
// moved in from the old Options page writes the same settings as before.
public class AddonWindowTests
{
    const string Me = "Player-1-0000000A";

    // Frames keep their scripts, checked state, texts and name. A font string's first text is its
    // owner's label, so tests find a control by what it says.
    const string Widgets = """
        UISpecialFrames = {}
        SlashCmdList = {}
        frames, texts, strings = {}, {}, {}
        registered = nil
        Settings = {
          RegisterCanvasLayoutCategory = function(frame, name) return { name = name, GetID = function() return 1 end } end,
          RegisterAddOnCategory = function(c) registered = c.name end,
        }
        local function widget(owner)
          local w = { scripts = {}, shown = true, owner = owner }
          return setmetatable(w, { __index = function(_, k)
            if k:sub(1, 1) == k:sub(1, 1):lower() then return nil end -- fields, not methods
            if k == "SetScript" then return function(s, ev, f) s.scripts[ev] = f end end
            if k == "SetText" then return function(s, t)
              s.text = t
              texts[#texts + 1] = t
              if s.owner and s.owner.label == nil and t ~= "" then s.owner.label = t end
            end end
            if k == "SetWidth" then return function(s, w) s.width = w end end
            if k == "SetPoint" then return function(s, ...) s.point = { ... } end end
            if k == "SetTextColor" then return function(s, r, g, b) s.color = { r, g, b } end end
            if k == "SetTexture" then return function(s, t) s.texture = t end end
            if k == "SetChecked" then return function(s, v) s.checked = v end end
            if k == "GetChecked" then return function(s) return s.checked end end
            if k == "Show" then return function(s) s.shown = true end end
            if k == "Hide" then return function(s) s.shown = false end end
            if k == "IsShown" then return function(s) return s.shown end end
            if k == "SetShown" then return function(s, on) s.shown = on end end
            if k == "CreateTexture" then return function(s) return widget(s) end end
            if k == "CreateFontString" then return function(s)
              local fs = widget(s)
              strings[#strings + 1] = fs
              return fs
            end end
            return function() end
          end })
        end
        function CreateFrame(kind, name, parent)
          local f = widget()
          f.kind, f.name, f.parent = kind, name, parent
          frames[#frames + 1] = f
          return f
        end
        UIParent = widget()
        function named(name)
          for _, f in ipairs(frames) do
            if f.name == name then return f end
          end
        end
        -- Shown, and so is everything it sits in.
        function onScreen(w)
          while w do
            if w.shown == false then return false end
            w = w.parent or w.owner
          end
          return true
        end
        function shownText(s)
          for _, fs in ipairs(strings) do
            if onScreen(fs) and fs.text == s then return fs end
          end
        end
        -- The shown buttons in the row that shows `text`, as "A,B".
        function rowButtons(text)
          local row, out = shownText(text).owner, {}
          for _, f in ipairs(frames) do
            if f.parent == row and f.kind == "Button" and onScreen(f) then out[#out + 1] = f.text end
          end
          return table.concat(out, ",")
        end
        function clickIn(text, label)
          local row = shownText(text).owner
          for _, f in ipairs(frames) do
            if f.parent == row and f.text == label and onScreen(f) then return f.scripts.OnClick(f) end
          end
          error("no " .. label .. " in " .. text)
        end
        function find(label)
          for _, f in ipairs(frames) do
            if f.label == label or f.text == label then return f end
          end
        end
        function click(label)
          local f = find(label)
          f.checked = not f.checked
          f.scripts.OnClick(f)
        end
        """;

    static FriendsLuaTests.Client Load(string db = "{ chars = {}, items = {}, events = {} }", string settings = "{}")
    {
        var c = new FriendsLuaTests.Client(Me, "{}", db, settings);
        c.Lua(Widgets);
        foreach (var file in new[] { "Today.lua", "NoteBox.lua", "Ui.lua", "RecipeSkills.lua", "RecipeWindow.lua", "Shell.lua", "Home.lua", "Window.lua", "Options.lua", "Commands.lua" })
            c.Script.Call(c.Script.LoadString(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, file)), codeFriendlyName: file), "WoWCharacterTracker", c.Ns);
        return c;
    }

    static double Num(FriendsLuaTests.Client c, string expr) => c.Lua("return " + expr).Number;
    static bool Bool(FriendsLuaTests.Client c, string expr) => c.Lua("return " + expr).CastToBool();

    // /wct opens Home: the character being played with its level times, today's moments, the newest
    // moments and friends.
    [Fact]
    public void Home_shows_the_character_with_its_level_times()
    {
        var c = Load("""
            { chars = { ['Player-1-0000000A'] = { char = 'Zeb', ilvl = 35, played = 90000 }, ['Player-1-0000000B'] = { char = 'Alt', money = 12000000 } }, items = {}, events = {
              { kind = 'level', guid = 'Player-1-0000000A', char = 'Zeb', level = 44, seconds = 3600, at = 10 },
              { kind = 'level', guid = 'Player-1-0000000A', char = 'Zeb', level = 45, seconds = 10440, at = 20 },
              { kind = 'boss', guid = 'Player-1-0000000A', char = 'Zeb', level = 45, boss = 'Archaedas', instance = 'Uldaman', at = 30 } } }
            """);
        c.Lua("""
            function UnitName() return 'Zeb' end
            function UnitLevel() return 45 end
            function UnitClass() return 'Warrior', 'WARRIOR' end
            function UnitRace() return 'Night Elf' end
            function UnitXP() return 1000 end
            function GetMoney() return 3400000 end
            function UnitXPMax() return 4000 end
            function GetMaxPlayerLevel() return 60 end
            function date() return { year = 2026, month = 11, day = 9 } end
            ns.playedNow = function() return 100000 end -- Character.lua's
            ns.levelTimeNow = function() return 1200 end
            """);
        c.Script.Call(c.Script.LoadString(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, "Announce.lua"))), "WoWCharacterTracker", c.Ns); // the moment lines
        c.Lua("SlashCmdList.CHARACTERTRACKER('')");
        Assert.True(Bool(c, "shownText('PLAYING NOW') ~= nil"));
        Assert.True(Bool(c, "shownText('Zeb') ~= nil"));
        Assert.True(Bool(c, "shownText('Night Elf Warrior') ~= nil"));
        Assert.True(Bool(c, "shownText(45) ~= nil")); // the level, on the portrait's badge
        Assert.True(Bool(c, "shownText('25% to 46  ·  20m 0s on this level') ~= nil")); // under the XP bar
        Assert.True(Bool(c, "shownText('1,540g account') ~= nil")); // 340g here and 1,200g on the alt
        Assert.True(Bool(c, "shownText('44 to 45 took 2h 54m  ·  average 1h 57m') ~= nil"));
        Assert.True(Bool(c, "shownText('Killed Archaedas in Uldaman!') ~= nil"));
        // The level history is folded until opened, and stays as you left it.
        Assert.False(Bool(c, "find('44 to 45') and onScreen(find('44 to 45'))"));
        c.Lua("click('Level history')");
        Assert.True(Bool(c, "shownText('44 to 45') ~= nil"));
        Assert.True(Bool(c, "shownText('2h 54m') ~= nil"));
        Assert.True(Bool(c, "WoWCharacterTrackerDB.window.homeHistory == true"));
    }

    const string TooltipStub = """
        GameTooltip = {
          SetOwner = function(self, owner, anchor) self.owner, self.anchor, self.link, self.text = owner, anchor, nil, nil end,
          SetHyperlink = function(self, link) self.link = link end,
          SetText = function(self, s) self.text = s end,
          Show = function(self) self.up = true end,
          Hide = function(self) self.up = false end,
        }
        """;

    // A loot or mount moment on Home shows the item's own tooltip from the game, any other moment its whole line.
    [Fact]
    public void Home_moments_show_the_items_tooltip()
    {
        var c = Load("""
            { chars = {}, items = {}, events = {
              { kind = 'loot', guid = 'Player-1-0000000A', char = 'Zeb', level = 45, item = 'Krol Blade', itemId = 2244, quality = 4, at = 10 },
              { kind = 'boss', guid = 'Player-1-0000000A', char = 'Zeb', level = 45, boss = 'Archaedas', instance = 'Uldaman', at = 20 } } }
            """);
        c.Lua("""
            function UnitName() return 'Zeb' end
            function UnitLevel() return 45 end
            function UnitClass() return 'Warrior', 'WARRIOR' end
            function UnitRace() return 'Night Elf' end
            function date() return { year = 2026, month = 11, day = 9 } end
            ns.playedNow = function() return 100000 end
            ns.levelTimeNow = function() return 1200 end
            """);
        c.Lua(TooltipStub);
        c.Script.Call(c.Script.LoadString(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, "Announce.lua"))), "WoWCharacterTracker", c.Ns);
        c.Lua("SlashCmdList.CHARACTERTRACKER('')");

        // The item isn't in the stub's cache, so the line carries a link built from its id (Announce.lua itemLink).
        c.Lua("r = shownText('Looted |cffffffff|Hitem:2244::::::::|h[Krol Blade]|h|r!').owner; r.scripts.OnEnter(r)");
        Assert.True(Bool(c, "GameTooltip.up and GameTooltip.owner == r"));
        Assert.Equal("item:2244:", c.Lua("return GameTooltip.link").String);

        c.Lua("r = shownText('Killed Archaedas in Uldaman!').owner; r.scripts.OnEnter(r)");
        Assert.True(Bool(c, "GameTooltip.link == nil"));
        Assert.Equal("Zeb: Killed Archaedas in Uldaman!", c.Lua("return GameTooltip.text").String);
    }

    // Item links in chat show their tooltip on hover (the game only does it on a click), hooked once.
    [Fact]
    public void Item_links_in_chat_show_their_tooltip_on_hover()
    {
        var c = Load();
        c.Lua(TooltipStub);
        c.Lua("""
            hooks, hooked = {}, 0
            NUM_CHAT_WINDOWS = 2
            ChatFrame1 = { HookScript = function(self, name, fn) hooks[name] = fn; hooked = hooked + 1 end }
            ns.ui.hookChatItemTips()
            ns.ui.hookChatItemTips()
            """);
        Assert.Equal(2, Num(c, "hooked")); // enter and leave, once, and no ChatFrame2 to hook
        c.Lua("hooks.OnHyperlinkEnter(ChatFrame1, 'player:Zeb')");
        Assert.False(Bool(c, "GameTooltip.up == true"));
        c.Lua("hooks.OnHyperlinkEnter(ChatFrame1, 'item:19019:0:0:0')");
        Assert.True(Bool(c, "GameTooltip.up and GameTooltip.owner == ChatFrame1"));
        Assert.Equal("item:19019:0:0:0", c.Lua("return GameTooltip.link").String);
        c.Lua("hooks.OnHyperlinkLeave(ChatFrame1)");
        Assert.False(Bool(c, "GameTooltip.up"));
    }

    // Latest moments lists what goes to Discord: not the levels the level steps skip, not a boss
    // killed before with first kills only, and a moment posted from the ask box as that moment.
    [Fact]
    public void Home_lists_only_the_moments_that_are_posted()
    {
        var c = Load("""
            { chars = {}, items = {}, events = {
              { kind = 'boss', guid = 'Player-1-0000000A', char = 'Zeb', level = 40, boss = 'Archaedas', instance = 'Uldaman', at = 5 },
              { kind = 'level', guid = 'Player-1-0000000A', char = 'Zeb', level = 44, at = 10 },
              { kind = 'level', guid = 'Player-1-0000000A', char = 'Zeb', level = 45, at = 20 },
              { kind = 'boss', guid = 'Player-1-0000000A', char = 'Zeb', level = 45, boss = 'Archaedas', instance = 'Uldaman', at = 30 },
              { kind = 'ask', askedKind = 'boss', guid = 'Player-1-0000000A', char = 'Zeb', level = 45, boss = 'Ironaya', instance = 'Uldaman', at = 40 } } }
            """, settings: "{ post = { level = { on = true, split = 30, before = 5, after = 5 }, boss = false }, filters = { split = 30, before = 5, after = 5, firstKill = true } }");
        foreach (var file in new[] { "LiveGrid.lua", "Announce.lua" })
            c.Script.Call(c.Script.LoadString(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, file))), "WoWCharacterTracker", c.Ns);
        c.Lua("""
            function UnitName() return 'Zeb' end
            function UnitLevel() return 45 end
            function UnitClass() return 'Warrior', 'WARRIOR' end
            function date() return { year = 2026, month = 11, day = 9 } end
            ns.playedNow = function() return 100000 end -- Character.lua's
            ns.levelTimeNow = function() return 1200 end
            function shownCount(s)
              local n = 0
              for _, fs in ipairs(strings) do if onScreen(fs) and fs.text == s then n = n + 1 end end
              return n
            end
            """);
        c.Lua("SlashCmdList.CHARACTERTRACKER('')");
        Assert.True(Bool(c, "shownText('Ding! Level 45.') ~= nil"));
        Assert.True(Bool(c, "shownText('Ding! Level 44.') == nil"));
        Assert.Equal(0, Num(c, "shownCount('Killed Archaedas in Uldaman!')")); // bosses aren't posted here
        Assert.True(Bool(c, "shownText('Killed Ironaya in Uldaman!') ~= nil")); // but this one was, from the ask box
    }

    // The level steps and filters on the Sharing tab change the In game setting the app takes on sync.
    [Fact]
    public void The_sharing_tab_changes_the_level_steps_and_filters()
    {
        var c = Load(settings: "{ post = { level = { on = true } }, filters = { split = 30, before = 5, after = 1, rareBoe = true, firstKill = true } }");
        c.Call("openWindow", "sharing");
        Assert.True(Bool(c, "shownText('Posts at levels 5, 10, 15, 20, 25, 30, 31, 32, 33, 34, 35, 36 and so on.') ~= nil"));
        c.Lua("""
            for _, f in ipairs(frames) do
              if f.kind == 'EditBox' and f.key == 'before' then
                f.GetText = function(s) return s.text end
                f:SetText('2')
                f.scripts.OnEditFocusLost(f)
              end
            end
            click('Rares: Bind on Equip only')
            """);
        Assert.Equal(2, Num(c, "WoWCharacterTrackerDB.gameSettings.filters.before"));
        Assert.True(Bool(c, "WoWCharacterTrackerDB.gameSettings.filters.rareBoe == false"));
        Assert.True(Bool(c, "WoWCharacterTrackerDB.gameSettings.filters.firstKill == true"));
        Assert.True(Bool(c, "shownText('Posts at levels 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 22, 24 and so on.') ~= nil"));
    }

    // TODAY shows the four that matter most; the rest unfold with "+N more".
    [Fact]
    public void Home_folds_the_rest_of_today_behind_more()
    {
        var c = Load("""
            { chars = {}, items = {}, events = {}, today = { ['Player-1-0000000A'] =
              { day = '2026-11-09', quests = 4, herbs = 2, ore = 3, skins = 1, fish = 5, ilvlStart = 30, ilvl = 31 } } }
            """);
        c.Lua("""
            function UnitClass() return 'Warrior', 'WARRIOR' end
            function UnitLevel() return 45 end
            function date(f) if f == '*t' then return { year = 2026, month = 11, day = 9 } end return '2026-11-09' end
            ns.playedNow = function() return 100 end
            ns.levelTimeNow = function() return 10 end
            """);
        c.Lua("SlashCmdList.CHARACTERTRACKER('')");
        Assert.True(Bool(c, "shownText('+1 |cff9a9a9aitem level|r') ~= nil"));
        Assert.True(Bool(c, "shownText('3 |cff9a9a9aveins|r') ~= nil"));
        Assert.True(Bool(c, "shownText('5 |cff9a9a9afish|r') == nil"));
        Assert.True(Bool(c, "shownText('?') == nil")); // unknown values hide instead of showing "?"
        Assert.True(Bool(c, "shownText('Item level') == nil")); // and an unknown tile hides
        c.Lua("click('+2 more')");
        Assert.True(Bool(c, "shownText('1 |cff9a9a9askin|r') ~= nil")); // one of a count is singular
        Assert.True(Bool(c, "shownText('5 |cff9a9a9afish|r') ~= nil"));
    }

    // A quiet day: one line that says so, no row of zeros.
    [Fact]
    public void A_quiet_day_shows_no_today_instead_of_zeros()
    {
        var c = Load();
        c.Lua("""
            function UnitClass() return 'Warrior', 'WARRIOR' end
            function UnitLevel() return 45 end
            function date(f) if f == '*t' then return { year = 2026, month = 11, day = 9 } end return '2026-11-09' end
            ns.playedNow = function() return 100 end
            ns.levelTimeNow = function() return 10 end
            """);
        c.Lua("SlashCmdList.CHARACTERTRACKER('')");
        Assert.True(Bool(c, "shownText('TODAY') == nil"));
        Assert.True(Bool(c, "shownText('0 |cff9a9a9alevels|r') == nil"));
    }

    [Fact]
    public void The_window_has_five_tabs_and_builds_each_tab_once()
    {
        var c = Load();
        c.Call("openWindow", "general");
        foreach (var tab in new[] { "Home", "Characters", "Friends", "Sharing", "Settings" })
            Assert.True(Bool(c, $"find('{tab}') ~= nil"), tab);
        Assert.True(Bool(c, "find('Note') == nil")); // the note box's options are a section of Settings
        Assert.True(Bool(c, "ns.windowShown()"));
        Assert.True(Bool(c, "find('Posts and chat') == nil")); // not built until opened

        c.Call("openWindow", "chat");
        var built = Num(c, "#frames");
        Assert.True(Bool(c, "find('Posts and chat') ~= nil"));
        Assert.True(Bool(c, "find('Guild chat') == nil")); // guild lines are off for now (Announce.lua GUILD_CHAT_ON)
        c.Call("openWindow", "chat");
        Assert.Equal(built, Num(c, "#frames"));

        Assert.Contains("WoWCharacterTrackerWindow", c.Lua("return table.concat(UISpecialFrames, ',')").String);
        c.Call("toggleWindow");
        Assert.False(Bool(c, "ns.windowShown()"));
    }

    // The tabs are a bar of icons down the left: the open one lit, its name over the page, and a
    // count on Friends while friend requests wait.
    [Fact]
    public void The_tab_bar_lights_the_open_tab_and_counts_friend_requests()
    {
        var c = Load();
        c.Lua("ns.pendingRequests = function() return { {}, {} } end");
        c.Call("openWindow", "sharing");
        Assert.True(Bool(c, "find('Sharing').bg.shown and not find('Home').bg.shown"));
        Assert.Contains("INV_Misc_Gear_01", c.Lua("return find('Settings').icon.texture").String);
        c.Lua("rail = find('Home').parent; rail.scripts.OnUpdate(rail, 2)");
        Assert.True(Bool(c, "shownText('2') ~= nil")); // the badge on Friends
        c.Lua("find('Settings').scripts.OnClick(find('Settings'))");
        Assert.True(Bool(c, "find('Settings').bg.shown and not find('Sharing').bg.shown"));
    }

    [Fact]
    public void The_moved_options_write_the_same_settings_as_before()
    {
        var c = Load(settings: "{ post = {} }"); // Instant posting needs the app
        c.Call("openWindow", "chat");
        c.Lua("click('Party / raid chat')");
        Assert.True(Bool(c, "ns.setting('announce').party"));

        c.Call("openWindow", "general");
        c.Lua("click('Keep all history')");
        Assert.True(Bool(c, "ns.setting('keepAllHistory')"));
        Assert.True(Bool(c, "find('Instant posting').checked")); // on until turned off
        c.Lua("click('Instant posting')");
        Assert.False(Bool(c, "WoWCharacterTrackerDB.live"));

        c.Lua("ns.setNoteAlpha = function(a) gotAlpha = a end");
        c.Call("openWindow", "note");
        c.Lua("local s = find('Background opacity'); s.scripts.OnValueChanged(s, 30)");
        Assert.Equal(0.3, Num(c, "gotAlpha"), 3);

        c.Call("openWindow", "sharing"); // what all friends get is a section of the Sharing tab
        Assert.False(Bool(c, "find('Gold').checked"));
        c.Lua("click('Gold')");
        Assert.True(Bool(c, "WoWCharacterTrackerDB.share.defaults.gold"));
        Assert.True(Bool(c, "find('Professions').checked")); // on by default
        c.Lua("click('Professions')");
        Assert.False(Bool(c, "WoWCharacterTrackerDB.share.defaults.professions"));
    }

    [Fact]
    public void What_you_share_sets_map_position_the_other_faction_and_the_map_rate()
    {
        var c = Load();
        c.Call("showShareDefaults"); // the Sharing tab's Friends view
        Assert.False(Bool(c, "find('Map position').checked"));
        c.Lua("click('Map position')");
        Assert.True(Bool(c, "WoWCharacterTrackerDB.share.defaults.map"));
        Assert.False(Bool(c, "find('Show my map position to the other faction').checked"));
        c.Lua("click('Show my map position to the other faction')");
        Assert.True(Bool(c, "WoWCharacterTrackerDB.share.defaults.otherFaction"));
        Assert.True(Bool(c, "shownText('Map updates') ~= nil"));
        c.Lua("local b = find('Every minute'); b.scripts.OnClick(b)");
        Assert.Equal(60, Num(c, "WoWCharacterTrackerDB.share.mapRate"));
        c.Lua("local b = find('Every 5 min'); b.scripts.OnClick(b)");
        Assert.Equal(300, Num(c, "WoWCharacterTrackerDB.share.mapRate"));
    }

    // The App tab's Ignore this character is the same In game setting the app's Ignore tracking writes.
    [Fact]
    public void Ignore_this_character_writes_the_in_game_setting()
    {
        var c = Load();
        c.Call("openWindow", "app");
        Assert.False(Bool(c, "find('Ignore this character').checked"));
        c.Lua("click('Ignore this character')");
        Assert.True(Bool(c, $"WoWCharacterTrackerDB.gameSettings.ignored['{Me}'] == true"));
        Assert.True(Bool(c, $"ns.isIgnored('{Me}')"));
        c.Lua("click('Ignore this character')");
        Assert.False(Bool(c, $"ns.isIgnored('{Me}')"));
    }

    [Fact]
    public void The_minimap_checkbox_uses_the_minimap_button_when_there_is_one()
    {
        var c = Load();
        c.Call("openWindow", "general");
        c.Lua("click('Show the minimap button')"); // no minimap button loaded: nothing fails

        c.Lua("""
            ns.minimapButtonShown = function() return true end
            ns.setMinimapButtonShown = function(on) gotShown = on end
            """);
        c.Call("toggleWindow");
        c.Call("openWindow", "general");
        Assert.True(Bool(c, "find('Show the minimap button').checked"));
        c.Lua("click('Show the minimap button')");
        Assert.False(Bool(c, "gotShown"));
    }

    [Fact]
    public void The_window_saves_its_position_and_last_tab()
    {
        var c = Load();
        c.Call("openWindow", "note"); // the Note tab's options moved to Settings
        Assert.Equal("settings", c.Lua("return WoWCharacterTrackerDB.window.tab").String);
        c.Lua("""
            local w
            for _, f in ipairs(frames) do if f.name == 'WoWCharacterTrackerWindow' then w = f end end
            rawset(w, 'GetPoint', function() return 'TOPLEFT', nil, 'BOTTOMLEFT', 100, 200 end)
            w.scripts.OnDragStop(w)
            """);
        Assert.Equal("TOPLEFT", c.Lua("return WoWCharacterTrackerDB.window.point").String);
        Assert.Equal("BOTTOMLEFT", c.Lua("return WoWCharacterTrackerDB.window.relative").String);
        Assert.Equal(200, Num(c, "WoWCharacterTrackerDB.window.y"));
        Assert.Equal("settings", c.Lua("return WoWCharacterTrackerDB.window.tab").String);

        c.Call("toggleWindow");
        c.Call("openWindow");
        Assert.True(Bool(c, "shownText('Note box') ~= nil"));
        Assert.Equal("settings", c.Lua("return WoWCharacterTrackerDB.window.tab").String);
    }

    [Fact]
    public void The_app_section_says_where_the_apps_settings_are()
    {
        var c = Load(settings: "{ post = { boss = true }, screenshot = {} }");
        c.Call("openWindow", "app");
        Assert.Contains("What gets posted to Discord is on the Sharing tab", c.Lua("return texts[#texts]").String);
    }

    [Fact]
    public void Wct_alone_opens_the_window_and_times_shows_the_level_times()
    {
        var c = Load();
        c.Lua("""
            opened = 0
            ns.openWindow = function() opened = opened + 1 end
            ns.friendsCommand = function(args) gotFriends = args end
            """);
        c.Lua("SlashCmdList.CHARACTERTRACKER('')");
        Assert.Equal(1, Num(c, "opened"));
        c.Lua("SlashCmdList.CHARACTERTRACKER('times')");
        Assert.Contains("No level times recorded yet for this character.", c.Said());
        c.Lua("SlashCmdList.CHARACTERTRACKER('help')");
        Assert.Contains(c.Said(), s => s.StartsWith("/wct note", StringComparison.Ordinal));
        c.Lua("SlashCmdList.CHARACTERTRACKER('friends Ask Zeb#2222')");
        Assert.Equal(" Ask Zeb#2222", c.Lua("return gotFriends").String);
        Assert.Equal(1, Num(c, "opened"));
    }

    // Highlights: how long the post box stays, 10 by default, cycling through the choices.
    [Fact]
    public void The_post_box_time_can_be_changed_on_the_sharing_tab()
    {
        var c = Load(settings: "{ post = {} }"); // the ask box needs the app
        c.Call("openWindow", "sharing");
        c.Lua("local b = find('15 sec'); b.scripts.OnClick(b)");
        Assert.Equal(15, Num(c, "ns.setting('askSeconds')"));
    }

    // Settings.lua as the app writes it for Forever with the Normal preset, edit applied first: the
    // table literal the tests load as WoWCharacterTrackerSettings.
    static string AppSettings(Action<FlavorSettings>? edit = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "wct-window-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(AddonInstaller.TargetDir(dir));
        try
        {
            var s = Flavors.Forever.Defaults();
            SharingPresets.Apply(s, Flavors.Forever, SharingPresets.Normal);
            edit?.Invoke(s);
            AddonInstaller.WriteSettings(dir, s, preset: SharingPresets.Detect(s, Flavors.Forever), flavor: Flavors.Forever);
            var lua = File.ReadAllText(Path.Combine(AddonInstaller.TargetDir(dir), "Settings.lua"));
            return lua[(lua.IndexOf("= {", StringComparison.Ordinal) + 2)..];
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // The Sharing table's box for a row and column (1 Discord, 2 Screenshot, 3 Group, 4 Ask first),
    // nil where that moment can't go.
    const string CellLua = """
        function cell(row, column)
          local name = shownText(row)
          for _, f in ipairs(frames) do
            if f.point and f.point[2] == name and f.point[4] == 206 + (column - 1) * 105 then return f end
          end
        end
        function clickCell(row, column) local f = cell(row, column); f.checked = not f.checked; f.scripts.OnClick(f) end
        """;

    // The app's table in game: its rows, and the Discord and screenshot boxes go in as In game settings
    // that change what's posted right away.
    [Fact]
    public void The_sharing_table_changes_discord_and_screenshots()
    {
        var c = Load(settings: AppSettings(s => (s.PostEpic, s.PostMounts) = (true, false)));
        c.Lua(CellLua);
        c.Call("openWindow", "sharing");
        Assert.True(Bool(c, "shownText('Level-ups') ~= nil and shownText('End boss kills') ~= nil"));
        Assert.True(Bool(c, "shownText('Mythic+ runs') == nil")); // Retail's
        Assert.True(Bool(c, "shownText('Guild') == nil")); // guild chat is off until it works
        Assert.Equal("custom", c.Lua("return ns.currentPreset()").String); // the edit isn't Normal
        Assert.True(Bool(c, "cell('Epic loot', 1).checked and not cell('New mounts', 1).checked"));
        Assert.True(Bool(c, "cell('Followed items', 2) == nil and cell('Dungeon runs', 3) == nil")); // nothing to pick there

        c.Lua("clickCell('New mounts', 1); clickCell('Epic loot', 1)");
        Assert.True(Bool(c, "WoWCharacterTrackerDB.gameSettings.posts.mount == true and WoWCharacterTrackerDB.gameSettings.posts.epic == false"));
        Assert.True(Bool(c, "ns.postRules().mount and not ns.postRules().epic"));
        Assert.Equal("custom", c.Lua("return WoWCharacterTrackerDB.gameSettings.preset").String);

        var shot = Bool(c, "cell('Rare loot', 2).checked");
        c.Lua("clickCell('Rare loot', 2)");
        Assert.Equal(!shot, Bool(c, "WoWCharacterTrackerDB.gameSettings.screenshot.loot == true"));
        Assert.Equal(!shot, Bool(c, "cell('Epic loot', 2).checked == true")); // one screenshot setting for all loot

        // The app takes it all on sync.
        var s = Flavors.Forever.Defaults();
        Assert.True(s.TakeInGame(SavedVariables.ReadInGame(c.Lua("return WoWCharacterTrackerDB.gameSettings"))));
        Assert.True(s.PostMounts);
        Assert.False(s.PostEpic);
        Assert.Equal(!shot, s.Shoots("loot"));
    }

    // A preset picked in game goes in right away, and the app reads back the same preset.
    [Fact]
    public void A_preset_picked_in_game_goes_in_right_away()
    {
        var c = Load(settings: AppSettings());
        c.Lua(CellLua);
        c.Call("openWindow", "sharing");
        Assert.Equal("normal", c.Lua("return ns.currentPreset()").String);
        c.Lua("click('Quiet')");
        Assert.Equal("quiet", c.Lua("return ns.currentPreset()").String);
        Assert.Equal("quiet", c.Lua("return WoWCharacterTrackerDB.gameSettings.preset").String);
        Assert.True(Bool(c, "not cell('End boss kills', 1).checked and cell('Level-ups', 1).checked"));
        Assert.Equal(10, Num(c, "ns.filters().before"));

        var s = Flavors.Forever.Defaults();
        SharingPresets.Apply(s, Flavors.Forever, SharingPresets.Normal);
        Assert.True(SharingPresets.TakeInGame(s, Flavors.Forever, SavedVariables.ReadInGame(c.Lua("return WoWCharacterTrackerDB.gameSettings")), out var applied));
        Assert.Null(applied); // nothing left for the app to apply: the game's change is the preset
        Assert.Equal(SharingPresets.Quiet, SharingPresets.Detect(s, Flavors.Forever));
    }

    // The ask checkbox turns boss kills and gear on and off, and keeps what else the app's table picked.
    [Fact]
    public void The_ask_checkbox_keeps_the_other_moments_the_app_picked()
    {
        var c = Load(settings: "{ post = {}, ask = { boss = true, level = true } }");
        c.Call("openWindow", "sharing");
        Assert.True(Bool(c, "find('Offer to post what Discord skips').checked"));
        c.Lua("click('Offer to post what Discord skips')");
        Assert.True(Bool(c, "ns.setting('ask').boss == nil and ns.setting('ask').level == true"));
        c.Lua("click('Offer to post what Discord skips')");
        Assert.True(Bool(c, "ns.setting('ask').boss and ns.setting('ask').rare and ns.setting('ask').epic and ns.setting('ask').level"));
    }

    // The Group column is the only switch for group chat: a box shows while group chat is on, ticking
    // one turns it on with that moment alone (old ticks don't come back), unticking the last turns it off.
    [Fact]
    public void The_group_column_is_the_only_switch_for_group_chat()
    {
        var c = Load(settings: AppSettings(s => (s.AnnounceParty, s.AnnouncePartyKinds) = (false, ["level", "boss"])));
        c.Lua(CellLua);
        c.Call("openWindow", "sharing");
        Assert.True(Bool(c, "find('Party / raid chat') == nil"));
        Assert.False(Bool(c, "cell('Level-ups', 3).checked")); // in the list, but nothing is said while it's off

        c.Lua("clickCell('End boss kills', 3)");
        Assert.True(Bool(c, "ns.setting('announce').party == true"));
        Assert.True(Bool(c, "ns.setting('announce').partyKinds.boss == true and ns.setting('announce').partyKinds.level == nil"));
        Assert.True(Bool(c, "cell('End boss kills', 3).checked and not cell('Level-ups', 3).checked"));

        c.Lua("clickCell('End boss kills', 3)");
        Assert.True(Bool(c, "ns.setting('announce').party == false"));
    }

    // Two views on the Sharing tab: posts and chat first, What you share opens the Friends one.
    [Fact]
    public void The_sharing_tab_switches_between_posts_and_friends()
    {
        var c = Load();
        c.Call("openWindow", "sharing");
        Assert.True(Bool(c, "shownText('Incognito') ~= nil and shownText('What you share with friends') == nil"));
        c.Call("showShareDefaults");
        Assert.True(Bool(c, "shownText('Incognito') == nil and shownText('What you share with friends') ~= nil"));
        Assert.True(Bool(c, "find('Share with friends').checked ~= nil")); // drawn when shown
        c.Lua("local b = find('Posts and chat'); b.scripts.OnClick(b)");
        Assert.True(Bool(c, "shownText('Incognito') ~= nil and shownText('What you share with friends') == nil"));
    }

    // While Incognito is on, the header says until when on every tab, and a click on it ends it.
    [Fact]
    public void Incognito_shows_in_the_header_and_a_click_ends_it()
    {
        var c = Load();
        c.Call("openWindow", "settings");
        c.Lua("""
            rail = find('Home').parent
            function tick() rail.scripts.OnUpdate(rail, 2) end
            function chip()
              for _, f in ipairs(frames) do
                if f.kind == 'Button' and type(f.text) == 'string' and f.text:find('^Incognito until') and onScreen(f) then return f end
              end
            end
            tick()
            """);
        Assert.True(Bool(c, "chip() == nil"));
        c.Lua("ns.goIncognito(1800); tick()");
        Assert.True(Bool(c, "chip() ~= nil"));
        c.Lua("local b = chip(); b.scripts.OnClick(b); tick()");
        Assert.True(Bool(c, "ns.incognitoActive() == nil and chip() == nil"));
    }

    // No reload reminder setting any more: the chat line is rare and only for the app (Events.lua).
    [Fact]
    public void The_settings_have_no_reload_reminders()
    {
        var c = Load(settings: "{ post = {} }");
        c.Call("openWindow", "settings");
        Assert.True(Bool(c, "shownText('Reload reminders') == nil and find('Every chance') == nil and find('Offer a reload on flight paths') == nil"));
    }

    // With the app: the preset is picked here (an In game setting the app applies on sync), and the
    // per-moment chat table is the app's. Without it: no preset, the whole chat table.
    [Fact]
    public void The_sharing_tab_picks_a_preset_with_the_app_and_shows_the_chat_table_without()
    {
        var c = Load(settings: "{ post = {}, preset = 'normal' }");
        c.Call("openWindow", "sharing");
        Assert.True(Bool(c, "shownText('Level-ups') == nil"));
        c.Lua("click('Quiet')");
        Assert.Equal("quiet", c.Lua("return WoWCharacterTrackerDB.gameSettings.preset").String);
        Assert.Equal("quiet", c.Lua("return ns.setting('preset')").String);

        var alone = Load();
        alone.Call("openWindow", "sharing");
        Assert.True(Bool(alone, "find('Quiet') == nil"));
        Assert.True(Bool(alone, "shownText('Level-ups') ~= nil"));
    }

    // A last tab saved by an older version (or a /wct command) opens where its options went.
    [Fact]
    public void Old_tab_names_open_the_new_tabs()
    {
        var c = Load();
        c.Call("openWindow", "highlights");
        Assert.Equal("sharing", c.Lua("return WoWCharacterTrackerDB.window.tab").String);
        c.Call("openWindow", "app");
        Assert.Equal("settings", c.Lua("return WoWCharacterTrackerDB.window.tab").String);
    }

    // The window opens in combat, so /wct options doesn't need to wait for it to end.
    [Fact]
    public void Wct_options_opens_the_window_even_in_combat()
    {
        var c = Load();
        c.Lua("inCombat = true; opened = 0; ns.openWindow = function() opened = opened + 1 end");
        c.Lua("SlashCmdList.CHARACTERTRACKER('options')");
        Assert.Equal(1, Num(c, "opened"));
    }

    // Long hints and checkbox tips wrap inside the window instead of running past its edge.
    [Fact]
    public void Long_hints_and_tips_get_a_width_so_they_wrap()
    {
        var c = Load();
        foreach (var tab in new[] { "general", "chat", "highlights", "note", "friends", "sharing" }) c.Call("openWindow", tab);
        var unwrapped = c.Lua("""
            local bad = {}
            for _, s in ipairs(strings) do
              if s.text and #s.text > 60 and not s.width then bad[#bad + 1] = s.text end
            end
            return table.concat(bad, " / ")
            """).String;
        Assert.Equal("", unwrapped);
    }

    // The window open on Home, with the player stubbed.
    static FriendsLuaTests.Client Home(string settings)
    {
        var home = Load(settings: settings);
        home.Lua("""
            function UnitName() return 'Zeb' end
            function UnitLevel() return 45 end
            function UnitClass() return 'Warrior', 'WARRIOR' end
            function UnitRace() return 'Night Elf' end
            function UnitXP() return 1000 end
            function UnitXPMax() return 4000 end
            function GetMaxPlayerLevel() return 60 end
            function date() return { year = 2026, month = 11, day = 9 } end
            ns.playedNow = function() return 100000 end
            ns.levelTimeNow = function() return 1200 end
            """);
        home.Script.Call(home.Script.LoadString(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, "Announce.lua"))), "WoWCharacterTracker", home.Ns);
        home.Call("openWindow", "home");
        return home;
    }

    // Resized wider, Home's two columns take the room.
    [Fact]
    public void Home_follows_the_windows_width()
    {
        var c = Home("{ post = {} }");
        c.Lua("""
            function cardOf() return shownText('PLAYING NOW').owner end
            win = named('WoWCharacterTrackerWindow')
            """);
        c.Lua("""
            win.GetWidth = function() return 1100 end
            win.GetHeight = function() return 700 end
            for _, f in ipairs(frames) do
              if f.parent == win and f.kind == 'Button' and f.scripts.OnMouseDown then grip = f end
            end
            grip.scripts.OnMouseUp(grip)
            """);
        // 952 wide (1100 less the tab bar on the left, the window's edges and the scroll bar): the
        // character's column takes a bit under half, the moments the rest, 10 between them.
        Assert.Equal(452, Num(c, "cardOf().width"));
        Assert.Equal(490, Num(c, "shownText('Latest moments').owner.parent.width"));
    }

    // Without the app, Home says what it adds and where to get it, until Got it; with it, nothing.
    [Fact]
    public void Home_points_to_the_app_when_there_is_none()
    {
        const string hint = "Discord posts, screenshots and the stream overlay need the free Character Tracker app on your PC. Get it here (click the link, then Ctrl+C):";
        var c = Home("{}");
        Assert.True(Bool(c, $"shownText('{hint}') ~= nil"));
        Assert.True(Bool(c, "(function() for _, f in ipairs(frames) do if f.kind == 'EditBox' and f.text == ns.APP_URL and onScreen(f) then return true end end end)()"));
        c.Lua("local b = find('Got it'); b.scripts.OnClick(b)");
        Assert.True(Bool(c, $"shownText('{hint}') == nil"));
        Assert.True(Bool(c, "WoWCharacterTrackerDB.window.appHintHidden == true"));

        var withApp = Home("{ post = {} }");
        Assert.True(Bool(withApp, $"shownText('{hint}') == nil"));
    }

    // The window's version says what's newer, and a click asks friends' addons for theirs.
    [Fact]
    public void The_version_says_whats_newer_and_a_click_checks()
    {
        var c = Load(settings: "{ post = {}, appUpdate = '0.40.0' }");
        c.Lua("pinged = 0; ns.findOnlineFriends = function() pinged = pinged + 1 end");
        c.Call("openWindow", "settings");
        Assert.True(Bool(c, "shownText('v0.33.0  ·  |cffffd100app v0.40.0 is out|r') ~= nil"));
        c.Lua("ns.noteAddonVersion('0.41.2')");
        Assert.True(Bool(c, "shownText('v0.33.0  ·  |cffffd100app v0.40.0 is out|r  ·  |cffffd100addon v0.41.2 seen|r') ~= nil"));
        c.Lua("local b = shownText('v0.33.0  ·  |cffffd100app v0.40.0 is out|r  ·  |cffffd100addon v0.41.2 seen|r').owner; b.scripts.OnClick(b)");
        Assert.Equal(1, Num(c, "pinged"));
    }

    [Fact]
    public void The_settings_tab_shows_what_needs_the_app_only_with_the_app()
    {
        var c = Load();
        c.Call("openWindow", "settings");
        foreach (var label in new[] { "Instant posting", "Show a small label next to the pattern" })
            Assert.True(Bool(c, $"shownText('{label}') == nil"), label);
        Assert.True(Bool(c, "shownText('Keep all history') ~= nil and shownText('Show the minimap button') ~= nil"));
        Assert.True(Bool(c, "shownText('Character Tracker app') ~= nil"));

        c = Load(settings: "{ post = {} }");
        c.Call("openWindow", "settings");
        foreach (var label in new[] { "Instant posting", "Show a small label next to the pattern", "Pattern corner", "Character Tracker app" })
            Assert.True(Bool(c, $"shownText('{label}') ~= nil"), label);
    }

    // The Settings tab links to the app's page and the tip page, with the app and without it.
    [Fact]
    public void The_settings_tab_links_to_the_apps_page_and_tips()
    {
        Assert.Equal("https://ko-fi.com/jdybeck", Load().Lua("return ns.TIP_URL").String);
        foreach (var settings in new[] { "{}", "{ post = {} }" })
        {
            var c = Load(settings: settings);
            c.Call("openWindow", "settings");
            Assert.True(Bool(c, "(function() for _, f in ipairs(frames) do if f.kind == 'EditBox' and f.text == ns.APP_URL and onScreen(f) then return true end end end)()"));
            Assert.True(Bool(c, "(function() for _, f in ipairs(frames) do if f.kind == 'EditBox' and f.text == ns.TIP_URL and onScreen(f) then return true end end end)()"));
        }
    }

    // Friends.lua's side of the Friends tab, stubbed: fixed rows, and every call recorded in `calls`.
    const string FriendStubs = """
        clock = 100000
        calls = {}
        RAID_CLASS_COLORS = { MAGE = { r = 0.25, g = 0.78, b = 0.92 } }
        local function call(s) calls[#calls + 1] = s end
        online = {
          { tag = 'Ann#1', account = 1, label = 'Ann#1', character = 'Annie', classFile = 'MAGE', level = 60, zone = 'Orgrimmar' },
          { tag = 'Bob#2', account = 2, label = 'Bobby (Bob#2)', character = 'Bobo', level = 12, hasAddon = true },
          { tag = 'Cid#3', account = 3, label = 'Cid#3', character = 'Cidro', hasAddon = true, contact = { share = true }, sharesWithYou = true },
          { tag = 'Dan#4', account = 4, label = 'Dan#4', character = 'Danno', hasAddon = false },
          { tag = 'Eve#5', account = 5, label = 'Eve#5', character = 'Evie', hasAddon = false, invitedAt = clock - 3600 },
          { tag = 'Fay#6', account = 6, label = 'Fay#6', character = 'Fayla', hasAddon = true, blocked = true },
        }
        requests = { { tag = 'Zeb#2222', account = 9, label = 'Zebbe (Zeb#2222)', at = clock - 5 } }
        shared = {
          { tag = 'Cid#3', label = 'Cid#3', chars = {
            { char = 'Cidrin', surname = 'Frostmane', realm = 'Firemaw', level = 60, classFile = 'MAGE', spec = 'Frost', ilvl = 112, zone = 'Ironforge',
              gear = '1~100,5~200', talents = 'Arcane 10 / Fire 0 / Frost 41', professions = 'Tailoring 300/300, Enchanting 280/300',
              at = clock - 7200, received = clock - 7200 },
          } },
        }
        ns.findOnlineFriends = function() call('find') end
        ns.onlineFriends = function() return online end
        ns.askFriend = function(tag) call('ask ' .. tag) return 'Asked ' .. tag .. '.' end
        ns.shareWithFriend = function(tag) call('share ' .. tag) return 'Sharing with ' .. tag .. '.' end
        ns.inviteFriend = function(tag) call('invite ' .. tag) return true, 'Invited ' .. tag .. '.' end
        ns.unblockFriend = function(tag) call('unblock ' .. tag) online[6].blocked = nil return tag .. ' can ask again.' end
        ns.pendingRequests = function() return requests end
        ns.answerFriendRequest = function(f, choice) call(f.tag .. ' ' .. choice) end
        ns.sharedWithYou = function() return shared end
        -- Find online friends, also while it says Checking...
        function findFriendsButton()
          for _, f in ipairs(frames) do
            if f.kind == 'Button' and f.text and (f.text == 'Find online friends' or f.text:find('^Checking')) then return f end
          end
        end
        -- A friend's card in the grid (it has the online dot) by the character name on it, and a click on it.
        function friendCard(name)
          for _, fs in ipairs(strings) do
            if onScreen(fs) and fs.text == name and fs.owner.dot then return fs.owner end
          end
        end
        function openFriend(name) local card = friendCard(name); card.scripts.OnMouseUp(card) end
        -- The open friend's section (it has the nickname box), opened for name when it isn't yet.
        function section()
          for _, f in ipairs(frames) do
            if f.nickname and onScreen(f) then return f end
          end
        end
        function showFriend(name)
          local card = friendCard(name)
          local s = section()
          if not (s and s.tag == card.tag) then card.scripts.OnMouseUp(card) end
          return section()
        end
        -- The shown buttons of name as "A,B", and a click on one: a friend in the grid has them in their
        -- section, one found with Find online friends on their row.
        function friendButtons(name)
          if not friendCard(name) then return rowButtons(name) end
          local s, out = showFriend(name), {}
          for _, b in ipairs({ s.b1, s.b2 }) do if onScreen(b) then out[#out + 1] = b.text end end
          return table.concat(out, ',')
        end
        function clickFriend(name, label)
          if not friendCard(name) then return clickIn(name, label) end
          local s = showFriend(name)
          for _, b in ipairs({ s.b1, s.b2 }) do
            if onScreen(b) and b.text == label then return b.scripts.OnClick(b) end
          end
          error('no ' .. label .. ' for ' .. name)
        end
        -- A character row (it has the when text) by its name.
        function charRow(name)
          for _, fs in ipairs(strings) do
            if onScreen(fs) and fs.text == name and fs.owner.when then return fs.owner end
          end
        end
        """;

    static FriendsLuaTests.Client FriendsTab()
    {
        var c = Load();
        c.Lua(FriendStubs);
        c.Call("openWindow", "friends");
        return c;
    }

    static string Calls(FriendsLuaTests.Client c) => c.Lua("return table.concat(calls, ';')").String;

    // One card per friend with the character they play; a click unfolds their characters under it,
    // a second click folds them, and opening another friend folds the first.
    [Fact]
    public void A_friend_unfolds_their_characters_on_a_click()
    {
        var c = FriendsTab();
        Assert.True(Bool(c, "friendCard('Cidro') ~= nil")); // a contact online: shown before Find
        Assert.True(Bool(c, "charRow('Cidrin Frostmane') == nil"));
        c.Lua("openFriend('Cidro')");
        Assert.True(Bool(c, "charRow('Cidrin Frostmane') ~= nil"));
        Assert.True(Bool(c, "charRow('Cidro') ~= nil")); // the one being played, not shared, first
        Assert.Equal("not shared", c.Lua("return charRow('Cidro').when.text").String);
        Assert.Equal("They share with you: gear, talents, location, professions.", c.Lua("return section().theirs.text").String);
        c.Lua("openFriend('Cidro')");
        Assert.True(Bool(c, "charRow('Cidrin Frostmane') == nil"));
    }

    // Dragging a friend moves the others aside while you drag; letting go keeps the order (shared
    // with the app), and the grid shows it from then on.
    [Fact]
    public void Dragging_a_friend_sets_the_order()
    {
        var c = FriendsTab();
        c.Lua("online[1].contact = {}; online[2].contact = {}"); // friends here, so in the grid
        c.Lua("local b = findFriendsButton(); b.scripts.OnClick(b)");
        c.Lua("""
            function order()
              local out = {}
              for _, name in ipairs({ 'Annie', 'Bobo', 'Cidro' }) do out[#out + 1] = name .. '@' .. friendCard(name).point[4] end
              return table.concat(out, ' ')
            end
            page = friendCard('Annie').parent
            page.GetLeft = function() return 0 end
            page.GetTop = function() return 1000 end
            cursorX, cursorY = 0, 0
            function GetCursorPosition() return cursorX, cursorY end
            """);
        var before = c.Lua("return order()").String;
        c.Lua("""
            local cid = friendCard('Cidro')
            gridTop = -cid.point[5]
            cid.scripts.OnDragStart(cid)
            cursorX, cursorY = 20, 1000 - gridTop - 10 -- over the first slot, top left
            cid.scripts.OnUpdate(cid)
            moved = order() -- while dragging: Annie and Bobo moved aside
            cid.scripts.OnDragStop(cid)
            cid.scripts.OnMouseUp(cid) -- the click that ends a drag doesn't open the friend
            """);
        Assert.NotEqual(before, c.Lua("return moved").String);
        Assert.True(Bool(c, "friendCard('Cidro').point[4] < friendCard('Annie').point[4]")); // Cidro first now
        Assert.Equal("Cid#3", c.Lua("return WoWCharacterTrackerDB.friendOrder.tags[1]").String);
        Assert.True(Bool(c, "section() == nil"));
        c.Call("friendsChanged");
        Assert.True(Bool(c, "friendCard('Cidro').point[4] < friendCard('Annie').point[4]")); // kept
    }

    // The grip resizes the window: the size is kept, and the Friends grid fits more columns.
    [Fact]
    public void Resizing_the_window_keeps_the_size_and_widens_the_friends_grid()
    {
        var c = FriendsTab();
        c.Lua("for _, f in ipairs(online) do f.contact = f.contact or {} end"); // all in the grid
        c.Lua("local b = findFriendsButton(); b.scripts.OnClick(b)");
        Assert.True(Bool(c, "friendCard('Danno').point[5] ~= friendCard('Annie').point[5]")); // 3 columns: Danno on row 2
        c.Lua("""
            win = named('WoWCharacterTrackerWindow')
            win.GetWidth = function() return 1240 end
            win.GetHeight = function() return 800 end
            for _, f in ipairs(frames) do
              if f.parent == win and f.kind == 'Button' and f.scripts.OnMouseDown then grip = f end
            end
            grip.scripts.OnMouseDown(grip)
            grip.scripts.OnMouseUp(grip)
            """);
        Assert.Equal(1240, Num(c, "WoWCharacterTrackerDB.window.width"));
        Assert.Equal(800, Num(c, "WoWCharacterTrackerDB.window.height"));
        Assert.True(Bool(c, "friendCard('Danno').point[5] == friendCard('Annie').point[5]")); // 5 columns: all on row 1
    }

    // A plain click on the grip leaves the size alone; dragging it sizes the window with the cursor.
    // The window fades a little while the mouse is away, and is solid again under it.
    [Fact]
    public void The_window_fades_while_the_mouse_is_away()
    {
        var c = FriendsTab();
        c.Lua("""
            win = named('WoWCharacterTrackerWindow')
            over = false
            win.IsMouseOver = function() return over end
            function alphaOf(f) f.SetAlpha = function(s, a) s.shownAlpha = a end f.GetAlpha = function(s) return s.shownAlpha or 1 end end
            alphaOf(win)
            -- The panels beside it fade with it, even the ones on UIParent, and ignore the window's alpha.
            recipes = CreateFrame('Frame', 'WoWCharacterTrackerRecipes', UIParent)
            _G.WoWCharacterTrackerRecipes = recipes
            recipes.IsMouseOver = function() return false end
            alphaOf(recipes)
            function tick() win.fadeTicker.scripts.OnUpdate(win.fadeTicker, 1) end
            tick()
            """);
        Assert.Equal(0.9, Num(c, "win.shownAlpha"), 3);
        Assert.Equal(0.9, Num(c, "recipes.shownAlpha"), 3);
        c.Lua("over = true tick()");
        Assert.Equal(1, Num(c, "win.shownAlpha"), 3);
        Assert.Equal(1, Num(c, "recipes.shownAlpha"), 3);
    }

    [Fact]
    public void Clicking_the_resize_grip_does_not_resize_and_dragging_does()
    {
        var c = FriendsTab();
        c.Lua("""
            win = named('WoWCharacterTrackerWindow')
            win.GetLeft = function() return 100 end
            win.GetTop = function() return 900 end
            UIParent.GetRight = function() return 1900 end
            win.GetWidth = function() return 828 end
            win.GetHeight = function() return 590 end
            win.SetSize = function(s, w, h) s.sized = { w, h } end
            cursor = { 500, 300 }
            function GetCursorPosition() return cursor[1], cursor[2] end
            function IsMouseButtonDown() return true end
            for _, f in ipairs(frames) do
              if f.parent == win and f.kind == 'Button' and f.scripts.OnMouseDown then grip = f end
            end
            grip.scripts.OnMouseDown(grip)
            cursor = { 502, 299 } -- a hand's wobble during a click
            win.scripts.OnUpdate(win, 0.02)
            """);
        Assert.True(Bool(c, "win.sized == nil"));
        c.Lua("""
            cursor = { 700, 200 }
            win.scripts.OnUpdate(win, 0.02)
            grip.scripts.OnMouseUp(grip)
            """);
        Assert.Equal(1028, Num(c, "win.sized[1]")); // 200 wider
        Assert.Equal(690, Num(c, "win.sized[2]"));  // 100 taller
        Assert.True(Bool(c, "win.scripts.OnUpdate == nil")); // stopped on letting go
    }

    // Sizing starts from the top left and stops at the screen's edge, so the clamp never pushes the
    // title bar off the top. The new anchor is saved.
    [Fact]
    public void Resizing_grows_from_the_top_left_and_only_as_far_as_the_screen()
    {
        var c = FriendsTab();
        c.Lua("""
            win = named('WoWCharacterTrackerWindow')
            win.GetLeft = function() return 300 end
            win.GetTop = function() return 700 end
            win.GetWidth = function() return 900 end
            win.GetHeight = function() return 650 end
            UIParent.GetRight = function() return 1366 end
            win.SetResizeBounds = function(_, ...) bounds = { ... } end
            win.SetPoint = function(s, ...) s.point = { ... } end
            for _, f in ipairs(frames) do
              if f.parent == win and f.kind == 'Button' and f.scripts.OnMouseDown then grip = f end
            end
            grip.scripts.OnMouseDown(grip)
            win.GetPoint = function(s) return s.point[1], s.point[2], s.point[3], s.point[4], s.point[5] end
            grip.scripts.OnMouseUp(grip)
            """);
        Assert.Equal(1066, Num(c, "bounds[3]")); // 1366 - 300
        Assert.Equal(700, Num(c, "bounds[4]"));  // down to the screen's bottom
        Assert.True(Bool(c, "WoWCharacterTrackerDB.window.point == 'TOPLEFT'"));
        Assert.True(Bool(c, "WoWCharacterTrackerDB.window.relative == 'BOTTOMLEFT'"));
        Assert.Equal(700, Num(c, "WoWCharacterTrackerDB.window.y"));
    }

    // Online and Offline split the grid, with their counts; an offline friend shows the character seen last.
    [Fact]
    public void An_offline_friend_shows_the_character_seen_last()
    {
        var c = FriendsTab();
        c.Lua("""
            online = { online[2] }
            online[1].contact = {}
            shared[1].chars[2] = { char = 'Cidra', level = 20, at = clock - 90000, received = clock - 90000 }
            local b = findFriendsButton(); b.scripts.OnClick(b)
            """);
        Assert.True(Bool(c, "find('Online (1)') ~= nil and find('Offline (1)') ~= nil"));
        Assert.True(Bool(c, "shownText('Level 12  ·  online') ~= nil"));
        Assert.True(Bool(c, "friendCard('Cidrin Frostmane') == nil")); // Online is picked first
        c.Lua("local b = find('Offline (1)'); b.scripts.OnClick(b)");
        Assert.True(Bool(c, "friendCard('Cidrin Frostmane') ~= nil and friendCard('Bobo') == nil"));
        Assert.True(Bool(c, "shownText('Level 60 Frost  ·  Ironforge  ·  last seen 2h ago') ~= nil"));
        Assert.Equal("offline", c.Lua("return WoWCharacterTrackerDB.window.friendsView").String);
    }

    // Nobody in the picked view says so; Home's link to an offline friend's character switches to Offline.
    [Fact]
    public void The_friends_view_says_when_its_empty_and_follows_a_reveal()
    {
        var c = FriendsTab();
        c.Lua("online = {}; ns.friendsChanged(); local b = find('Online (0)'); b.scripts.OnClick(b)");
        Assert.True(Bool(c, "shownText('None of them are online in this game right now.') ~= nil"));
        Assert.True(Bool(c, "friendCard('Cidrin Frostmane') == nil"));
        c.Call("toggleFriendDetails", c.Lua("return shared[1].chars[1]"), "Cid#3");
        Assert.True(Bool(c, "friendCard('Cidrin Frostmane') ~= nil"));
        Assert.Equal("offline", c.Lua("return WoWCharacterTrackerDB.window.friendsView").String);
    }

    // Offline picked last time doesn't stick: opening the tab again shows Online while anyone is online,
    // and Offline only when nobody is (until someone comes online).
    [Fact]
    public void The_friends_tab_opens_on_online_when_anyone_is_online()
    {
        var c = FriendsTab();
        c.Lua("bob = online[2]; bob.contact = {}; online = { bob }; ns.friendsChanged(); local b = find('Offline (1)'); b.scripts.OnClick(b)");
        Assert.True(Bool(c, "friendCard('Cidrin Frostmane') ~= nil"));
        c.Call("openWindow", "friends");
        Assert.True(Bool(c, "shownText('Level 12  ·  online') ~= nil and friendCard('Cidrin Frostmane') == nil"));

        c.Lua("online = {}");
        c.Call("openWindow", "friends");
        Assert.True(Bool(c, "friendCard('Cidrin Frostmane') ~= nil")); // nobody online: Offline
        c.Lua("online = { bob }; ns.friendsChanged()");
        Assert.True(Bool(c, "shownText('Level 12  ·  online') ~= nil and friendCard('Cidrin Frostmane') == nil")); // back to Online
    }

    [Fact]
    public void Unblock_lets_a_blocked_friend_ask_again()
    {
        var c = FriendsTab();
        c.Lua("local b = findFriendsButton(); b.scripts.OnClick(b); calls = {}");
        c.Lua("clickFriend('Fayla', 'Unblock')");
        Assert.Equal("unblock Fay#6", Calls(c));
        Assert.Equal("Ask,Share", c.Lua("return friendButtons('Fayla')").String);
    }

    // A click on a character unfolds its info under the row and its gear beside the window; the
    // panel's X closes only the panel, a second click on the row folds both away.
    [Fact]
    public void The_professions_overview_lists_everyone_by_profession_highest_first()
    {
        var c = Load("""
            { chars = { ['Player-1-0000000A'] = { char = 'Zeb', level = 30, classFile = 'WARRIOR', lastSeen = 1, professions = 'Tailoring 150/300, Mining 120/150' } },
              items = {}, events = {} }
            """);
        c.Lua(FriendStubs);
        c.Lua("ns.openProfessions()");
        Assert.True(Bool(c, "shownText('Tailoring  (2)') ~= nil"));
        Assert.True(Bool(c, "shownText('Mining  (1)') ~= nil"));
        Assert.True(Bool(c, "shownText('Enchanting  (1)') ~= nil"));
        Assert.True(Bool(c, "shownText('300/300') ~= nil and shownText('150/300') ~= nil"));
        // Highest first under a profession: the friend's 300 above your 150.
        Assert.True(Bool(c, "shownText('300/300').owner.point[5] > shownText('150/300').owner.point[5]"));
        // A search narrows it to professions or people.
        c.Lua("local s = named('WoWCharacterTrackerRecipes').search; s.GetText = function(self) return self.text end; s:SetText('min'); s.scripts.OnTextChanged(s, true)");
        Assert.True(Bool(c, "shownText('Mining  (1)') ~= nil and shownText('Tailoring  (2)') == nil"));
    }

    [Fact]
    public void A_character_unfolds_under_its_row_and_folds_with_its_gear()
    {
        var c = GearTab();
        c.Lua("clickChar('Cidrin Frostmane')");
        Assert.True(Bool(c, "shownText('280 / 300') ~= nil")); // the info, under the row
        Assert.True(Bool(c, "shownText('Arcane 10  ·  Frost 41') ~= nil")); // talents, without the empty tree
        Assert.True(Bool(c, "panel() == nil")); // one place: no panel with it
        c.Lua("openGear()"); // its Gear button opens the panel
        Assert.True(Bool(c, "onScreen(panel())"));
        c.Lua("panel():Hide()"); // its X
        Assert.True(Bool(c, "shownText('280 / 300') ~= nil")); // still unfolded
        c.Lua("openGear()");
        c.Lua("clickChar('Cidrin Frostmane')");
        Assert.True(Bool(c, "shownText('280 / 300') == nil"));
        Assert.False(Bool(c, "onScreen(named('WoWCharacterTrackerDetailsPanel'))"));
    }

    // The game's side of recipes: names, the schematic (reagents and the made item) and item info.
    const string RecipeStubs = """
        spells = { [3915] = 'Bolt of Linen Cloth', [2385] = 'Brown Linen Vest', [3758] = 'Green Woolen Bag', [7863] = 'Enchant Boots - Minor Stamina',
          [2963] = 'Bolt of Linen Cloth' }
        items = { [2589] = { 'Linen Cloth', 1, 1, 5 }, [2996] = { 'Bolt of Linen Cloth', 1, 1, 5 }, [2568] = { 'Brown Linen Vest', 1, 1, 10, 5 } }
        C_Spell = { GetSpellName = function(id) return spells[id] end, GetSpellTexture = function() return 1 end }
        C_Item = { GetItemInfo = function(id) local i = items[id]; if i then return i[1], nil, i[2], i[4], i[5] end end,
          GetItemIconByID = function(id) return 'item-' .. id end }
        C_TradeSkillUI = { GetRecipeSchematic = function(id)
          if id == 3915 then return { outputItemID = 2996, reagentSlotSchematics = { { reagentType = 1, quantityRequired = 2, reagents = { { itemID = 2589 } } } } } end
          if id == 2385 then return { outputItemID = 2568, reagentSlotSchematics = { { reagentType = 1, quantityRequired = 3, reagents = { { itemID = 2996 } } } } } end
          return { reagentSlotSchematics = {} }
        end }
        shared[1].chars[1].recipes = 'Tailoring~Cloth=3915,2385/Bags=3758\nEnchanting~Boots=7863'
        """;

    // A character's unfolded info has a button per profession with recipes; it opens the recipe window
    // with the game's headers, the first recipe picked with its reagents and what it makes. A header folds.
    [Fact]
    public void A_characters_recipes_open_like_the_profession_window()
    {
        var c = FriendsTab();
        c.Lua(RecipeStubs);
        c.Lua("openFriend('Cidro')");
        c.Lua("local r = shownText('Cidrin Frostmane').owner; r.scripts.OnMouseUp(r)");
        Assert.True(Bool(c, "find('Enchanting (1)') ~= nil"));
        c.Lua("local b = find('Tailoring (3)'); b.scripts.OnClick(b)");
        Assert.True(Bool(c, "onScreen(named('WoWCharacterTrackerRecipes'))"));
        Assert.True(Bool(c, "shownText('Tailoring') ~= nil and shownText('300/300') ~= nil"));
        Assert.True(Bool(c, "shownText('Cloth') ~= nil and shownText('Bags') ~= nil and shownText('Green Woolen Bag') ~= nil"));
        Assert.True(Bool(c, "shownText('Linen Cloth') ~= nil and shownText('2') ~= nil")); // Bolt of Linen Cloth's reagent
        Assert.True(Bool(c, "shownText('Item level 5') ~= nil"));
        // Rows show the made item's icon like the game's window, the spell's when it makes nothing.
        Assert.Equal("item-2996", c.Lua("""
            for _, fs in ipairs(strings) do
              if onScreen(fs) and fs.text == 'Bolt of Linen Cloth' and fs.owner.sign then return fs.owner.icon.texture end
            end
            """).String);

        c.Lua("local r = shownText('Brown Linen Vest').owner; r.scripts.OnClick(r)");
        Assert.True(Bool(c, "shownText('Item level 10, Requires level 5') ~= nil"));
        c.Lua("local r = shownText('Cloth').owner; r.scripts.OnClick(r)"); // folds Cloth
        // The list's rows (they have a sign for headers): Cloth's recipes are gone, Bags' stay.
        c.Lua("""
            function listed(s)
              for _, fs in ipairs(strings) do
                if onScreen(fs) and fs.text == s and fs.owner.sign then return true end
              end
              return false
            end
            """);
        Assert.False(Bool(c, "listed('Brown Linen Vest')"));
        Assert.True(Bool(c, "listed('Green Woolen Bag')"));
    }

    // /wct recipes finds who knows a recipe among your characters and what friends share; a click on
    // someone opens their recipes on it.
    [Fact]
    public void Recipe_search_finds_who_knows_it()
    {
        var c = FriendsTab();
        c.Lua(RecipeStubs);
        c.Lua("WoWCharacterTrackerDB.chars['Player-1-0000000A'] = { char = 'Zeb', classFile = 'WARRIOR', professions = 'Tailoring 75/150', recipes = 'Tailoring~Cloth=2963' }");
        c.Lua("SlashCmdList.CHARACTERTRACKER('recipes linen')");
        Assert.True(Bool(c, "shownText('Find a recipe') ~= nil"));
        Assert.True(Bool(c, "shownText('Brown Linen Vest') ~= nil and shownText('Bolt of Linen Cloth') ~= nil"));
        Assert.True(Bool(c, "shownText('Known by:') ~= nil"));
        Assert.Equal(1, Num(c, "#ns.findRecipes({ { c = WoWCharacterTrackerDB.chars['Player-1-0000000A'] } }, 'BOLT', function(id) return spells[id] end)"));

        c.Lua("local r = shownText('Brown Linen Vest').owner; r.scripts.OnClick(r)");
        c.Lua("local b = find('Cidrin Frostmane (Cid#3)  300/300'); b.scripts.OnClick(b)");
        Assert.True(Bool(c, "shownText('Tailoring') ~= nil and shownText('Cidrin Frostmane  (Cid#3)') ~= nil"));
        Assert.Equal("75/150", c.Lua("return ns.professionSkill('Tailoring 75/150, First Aid 100', 'Tailoring')").String);
        Assert.Equal("100", c.Lua("return ns.professionSkill('Tailoring 75/150, First Aid 100', 'First Aid')").String);
    }

    // Hide in a character's info takes it out of the list, "show them" brings it back dimmed, and
    // Show again undoes it.
    [Fact]
    public void A_character_can_be_hidden_and_shown_again()
    {
        var c = FriendsTab();
        c.Lua("openFriend('Cidro')");
        c.Lua("local r = shownText('Cidrin Frostmane').owner; r.scripts.OnMouseUp(r)");
        c.Lua("local b = find('Hide'); b.scripts.OnClick(b)");
        Assert.True(Bool(c, "shownText('Cidrin Frostmane') == nil"));
        Assert.True(Bool(c, "find('1 hidden character, show them') ~= nil"));
        c.Lua("local b = find('1 hidden character, show them'); b.scripts.OnClick(b)");
        Assert.True(Bool(c, "shownText('Cidrin Frostmane') ~= nil"));
        c.Lua("local r = shownText('Cidrin Frostmane').owner; r.scripts.OnMouseUp(r)");
        c.Lua("local b = find('Show again'); b.scripts.OnClick(b)");
        Assert.True(Bool(c, "next(WoWCharacterTrackerDB.window.hiddenChars) == nil"));
    }

    // On Forever the realm's name is only a ruleset ("Realm 6 PvP" -> "PvP"): no Realm or Layer line,
    // and the game's rules (Hardcore, Self-found) join it when they aren't Normal.
    [Theory]
    [InlineData("Normal", "PvP")]
    [InlineData("Hardcore", "PvP, Hardcore")]
    public void A_forever_character_shows_its_ruleset(string ruleset, string shown)
    {
        var c = FriendsTab();
        c.Lua($"shared[1].chars[1].realm = 'CAMWOW EU5 Realm 6 PvP'; shared[1].chars[1].ruleset = '{ruleset}'; ns.friendsChanged()");
        c.Lua("openFriend('Cidro')");
        c.Lua("local r = shownText('Cidrin Frostmane').owner; r.scripts.OnMouseUp(r)");
        Assert.True(Bool(c, $"shownText('Ruleset') ~= nil and shownText('{shown}') ~= nil"));
        Assert.True(Bool(c, "shownText('Realm') == nil and shownText('Layer') == nil"));
        Assert.True(Bool(c, "shownText('realm 6') == nil and shownText('PvP, realm 6') == nil"));
    }

    // A fit page (Sharing, Settings) takes the height of what's shown on it, so it doesn't scroll
    // into empty space; hidden things and a page not laid out yet leave it as it is.
    [Fact]
    public void A_fit_page_takes_the_height_of_whats_shown_on_it()
    {
        var c = Load();
        c.Lua("""
            local function at(bottom, shown)
              return { GetBottom = function() return bottom end, IsShown = function() return shown end }
            end
            page = { GetTop = function() return 900 end,
              GetRegions = function() return at(700, true), at(100, false) end,
              GetChildren = function() return at(450, true) end,
              SetHeight = function(_, h) height = h end }
            ns.ui.fitHeight(page)
            """);
        Assert.Equal(470, Num(c, "height"));
        c.Lua("height = nil; page.GetTop = function() return nil end; ns.ui.fitHeight(page)");
        Assert.True(Bool(c, "height == nil"));
    }

    // Settings, Look: picking a look repaints what's built and is kept; Classic gold until then.
    [Fact]
    public void A_look_repaints_the_window_and_is_kept()
    {
        var c = Load();
        c.Lua("ns.ui.applyTheme()");
        c.Call("openWindow", "settings");
        Assert.Equal("classic", c.Lua("return ns.ui.theme()").String);
        c.Lua("heading = shownText('Look')");
        Assert.Equal(0xFC / 255.0, Num(c, "heading.color[1]"), 3); // the app's #FCD34D
        c.Lua("local b = find('Forever wisp'); b.scripts.OnClick(b)");
        Assert.Equal("wisp", c.Lua("return WoWCharacterTrackerDB.window.theme").String);
        Assert.Equal(0x67 / 255.0, Num(c, "heading.color[1]"), 3); // #67E8F9, repainted in place
        Assert.Equal(0x67 / 255.0, Num(c, "ns.ui.COLOR.accent[1]"), 3);
    }

    // Characters: your own characters from the saved file, newest first, opening the details panel
    // with their gear and level times. Ignored ones stay out.
    [Fact]
    public void The_characters_tab_lists_your_own_characters()
    {
        var c = Load("""
            { chars = {
                ['Player-1-0000000A'] = { char = 'Zeb', realm = 'Firemaw', level = 45, class = 'Warrior', classFile = 'WARRIOR', race = 'Night Elf', ilvl = 35, zone = 'Tanaris', lastSeen = 200, money = 70000 },
                ['Player-1-0000000C'] = { char = 'Alt', realm = 'Firemaw', level = 12, class = 'Mage', classFile = 'MAGE', race = 'Gnome', lastSeen = 100 } },
              items = { ['Player-1-0000000A'] = { gear = '1~100' } },
              events = { { kind = 'level', guid = 'Player-1-0000000A', level = 45, seconds = 10440, at = 150 } } }
            """, "{ ignored = { ['Player-1-0000000C'] = true } }");
        c.Call("openWindow", "characters");
        Assert.True(Bool(c, "shownText('Zeb') ~= nil"));
        Assert.True(Bool(c, "shownText('Alt') == nil")); // ignored
        c.Lua("local r = shownText('Zeb').owner; r.scripts.OnMouseUp(r)");
        Assert.True(Bool(c, "onScreen(named('WoWCharacterTrackerDetailsPanel'))"));
        Assert.True(Bool(c, "shownText('44 to 45 took 2h 54m') ~= nil")); // its level times
        Assert.True(Bool(c, "shownText('7g') ~= nil"));
    }

    // With room to spare the rows grow (a bigger portrait, a line more with guild, gold and played);
    // a list that fills the page stays compact.
    [Fact]
    public void Character_rows_grow_while_they_fit_and_stay_compact_when_they_dont()
    {
        string Db(int count)
        {
            var chars = string.Join(",\n", Enumerable.Range(1, count).Select(i =>
                $"['Player-1-{i:D8}'] = {{ char = 'Char{i}', level = 60, classFile = 'MAGE', lastSeen = {i}, money = 12345678, played = 360000, guild = 'Zebs' }}"));
            return $"{{ chars = {{ {chars} }}, items = {{}}, events = {{}} }}";
        }
        var few = Load(Db(3));
        few.Call("openWindow", "characters");
        Assert.True(Bool(few, "shownText('Char1').owner.portrait.width == 48"));
        Assert.True(Bool(few, "shownText('<Zebs>  ·  1,234g  ·  4d 4h 0m played') ~= nil"));

        var many = Load(Db(10));
        many.Call("openWindow", "characters");
        Assert.True(Bool(many, "shownText('Char1').owner.portrait.width == 28"));
        Assert.True(Bool(many, "shownText('<Zebs>  ·  1,234g  ·  4d 4h 0m played') == nil"));
    }

    // A row's portrait: the app's picture (Portraits.lua's slot) over the class icon; without one the
    // class icon, and the character being played gets its live portrait.
    [Fact]
    public void Character_rows_show_the_apps_portrait_or_the_class_icon()
    {
        var c = Load("""
            { chars = {
                ['Player-1-0000000A'] = { char = 'Zeb', level = 45, class = 'Warrior', classFile = 'WARRIOR', lastSeen = 300 },
                ['Player-1-0000000B'] = { char = 'Bea', level = 30, class = 'Priest', classFile = 'PRIEST', lastSeen = 200 },
                ['Player-1-0000000C'] = { char = 'Alt', level = 12, class = 'Mage', classFile = 'MAGE', lastSeen = 100 } },
              items = {}, events = {} }
            """);
        c.Lua("""
            WoWCharacterTrackerPortraits = { ['Player-1-0000000B'] = 7 }
            CLASS_ICON_TCOORDS = { WARRIOR = { 0, 0.25, 0, 0.25 }, PRIEST = { 0.5, 0.75, 0.25, 0.5 }, MAGE = { 0.25, 0.5, 0, 0.25 } }
            live = {}
            function SetPortraitTexture(t, unit) live[#live + 1] = unit; t.texture = 'live:' .. unit end
            function face(name) return shownText(name).owner.portrait.face end
            function class(name) return shownText(name).owner.portrait.class end
            """);
        c.Call("openWindow", "characters");
        Assert.Equal(@"Interface\AddOns\WoWCharacterTracker\Portraits\7", c.Lua("return face('Bea').texture").String);
        Assert.True(Bool(c, "face('Bea').shown"));
        Assert.Equal(@"Interface\TargetingFrame\UI-Classes-Circles", c.Lua("return class('Bea').texture").String); // under it
        Assert.True(Bool(c, "face('Alt').shown == false")); // no portrait: the class icon
        Assert.True(Bool(c, "class('Alt').shown"));
        Assert.Equal("live:player", c.Lua("return face('Zeb').texture").String); // the one being played
    }

    // Home: friends' moments beside yours, and a friend's row opens their details like the Friends tab.
    [Fact]
    public void Home_lists_friends_moments_and_opens_a_friends_character()
    {
        var c = Load();
        c.Lua(FriendStubs);
        c.Lua("""
            shared[1].chars[1].highlights = 'boss~99000~60~Ragnaros~Molten Core~40'
            shared[1].chars[1].levels, shared[1].chars[1].maxLevel = '60~9960~98000', 60
            shared[1].chars[1].guid = 'Player-1-0000000F'
            WoWCharacterTrackerPortraits = { ['Player-1-0000000F'] = 3 }
            function UnitLevel() return 10 end
            function UnitClass() return 'Mage', 'MAGE' end
            function date() return { year = 2026, month = 11, day = 9 } end
            ns.playedNow = function() return 1000 end
            ns.levelTimeNow = function() return nil end
            """);
        c.Script.Call(c.Script.LoadString(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, "Announce.lua"))), "WoWCharacterTracker", c.Ns);
        c.Call("openWindow", "home");
        Assert.True(Bool(c, "shownText(\"Friends' moments\") ~= nil"));
        Assert.True(Bool(c, "shownText('Killed Ragnaros in Molten Core!') ~= nil"));
        // A moment that isn't about an item shows the character's portrait.
        Assert.Equal(@"Interface\AddOns\WoWCharacterTracker\Portraits\3", c.Lua("return shownText('Killed Ragnaros in Molten Core!').owner.portrait.face.texture").String);
        Assert.False(Bool(c, "shownText('Killed Ragnaros in Molten Core!').owner.icon.shown"));
        // At max level the hover is their latest moment, not how long 59 to 60 took.
        var tip = c.Lua("return shownText('Cidrin Frostmane').owner.full").String;
        Assert.StartsWith("Cidrin Frostmane: Killed Ragnaros in Molten Core!", tip);
        Assert.DoesNotContain("took", tip);
        Assert.True(Bool(c, "shownText('Cid#3') ~= nil")); // no nickname: the battletag
        // With a nickname, only the nickname: no battletag beside it.
        c.Lua("ns.friendNickname = function(tag) return tag == 'Cid#3' and 'Cidi' or '' end");
        c.Call("openWindow", "home");
        Assert.True(Bool(c, "shownText('Cidi') ~= nil"));
        Assert.True(Bool(c, "shownText('Cidi (Cid#3)') == nil"));
        c.Lua("local r = shownText('Killed Ragnaros in Molten Core!').owner; r.scripts.OnMouseUp(r)");
        Assert.True(Bool(c, "onScreen(named('WoWCharacterTrackerDetailsPanel'))"));
        Assert.True(Bool(c, "shownText('Cidrin Frostmane') ~= nil"));
    }

    // Your character's Highlights button opens its highlights beside the window, newest first, losses
    // too; a second click closes them.
    [Fact]
    public void A_characters_highlights_open_beside_the_window()
    {
        var c = Load("""
            { chars = { ['Player-1-0000000A'] = { char = 'Zeb', level = 45, classFile = 'WARRIOR', lastSeen = 200 } }, items = {}, events = {
              { kind = 'level', guid = 'Player-1-0000000A', level = 45, seconds = 10440, at = 150 },
              { kind = 'boss', guid = 'Player-1-0000000A', level = 45, boss = 'Archaedas', instance = 'Uldaman', at = 160 },
              { kind = 'duel', guid = 'Player-1-0000000A', level = 45, opponent = 'Bob', lost = true, at = 170 },
              { kind = 'boss', guid = 'Player-1-0000000C', level = 20, boss = 'Mutanus', instance = 'Wailing Caverns', at = 180 } } }
            """);
        c.Script.Call(c.Script.LoadString(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, "Announce.lua"))), "WoWCharacterTracker", c.Ns);
        c.Call("openWindow", "characters");
        c.Lua("local r = shownText('Zeb').owner; r.scripts.OnMouseUp(r)");
        c.Lua("local b = find('Highlights (2)'); b.scripts.OnClick(b)");
        Assert.True(Bool(c, "onScreen(named('WoWCharacterTrackerHighlightsPanel'))"));
        Assert.True(Bool(c, "named('WoWCharacterTrackerDetailsPanel') == nil")); // no gear panel with it
        Assert.True(Bool(c, "shownText('2 highlights, newest first') ~= nil"));
        Assert.True(Bool(c, "shownText('Lost a duel to Bob.').owner.point[5] == 0")); // newest on top
        Assert.True(Bool(c, "shownText('Killed Archaedas in Uldaman!') ~= nil"));
        Assert.True(Bool(c, "shownText('Killed Mutanus in Wailing Caverns!') == nil")); // another character's
        c.Lua("local b = find('Highlights (2)'); b.scripts.OnClick(b)");
        Assert.False(Bool(c, "onScreen(named('WoWCharacterTrackerHighlightsPanel'))"));
    }

    // A friend's character has the button too, with what they share.
    [Fact]
    public void A_friends_character_shows_their_shared_highlights()
    {
        var c = FriendsTab();
        c.Lua("shared[1].chars[1].highlights = 'boss~99000~60~Ragnaros~Molten Core~40\\nmount~98000~60~Swift Razzashi Raptor~19872~'");
        c.Script.Call(c.Script.LoadString(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, "Announce.lua"))), "WoWCharacterTracker", c.Ns);
        c.Lua("openFriend('Cidro')");
        c.Lua("local r = shownText('Cidrin Frostmane').owner; r.scripts.OnMouseUp(r)");
        c.Lua("local b = find('Highlights (2)'); b.scripts.OnClick(b)");
        Assert.True(Bool(c, "shownText('Killed Ragnaros in Molten Core!') ~= nil"));
        Assert.True(Bool(c, "shownText('Got a new mount: |cffffffff|Hitem:19872::::::::|h[Swift Razzashi Raptor]|h|r!').owner.itemId == 19872")); // its game tooltip on hover
    }

    // Home keeps more moments than it shows: the wheel scrolls the column under the cursor.
    [Fact]
    public void Home_moments_scroll_with_the_wheel()
    {
        var events = string.Join(",\n", Enumerable.Range(1, 8).Select(i =>
            $"{{ kind = 'boss', guid = 'Player-1-0000000A', char = 'Zeb', level = 45, boss = 'Boss {i}', instance = 'Uldaman', at = {i} }}"));
        var c = Load("{ chars = {}, items = {}, events = {\n" + events + " } }");
        c.Lua("""
            function UnitClass() return 'Warrior', 'WARRIOR' end
            function UnitLevel() return 45 end
            function date() return { year = 2026, month = 11, day = 9 } end
            function GetCursorPosition() return 10, 0 end
            ns.playedNow = function() return 100 end
            ns.levelTimeNow = function() return 10 end
            """);
        c.Script.Call(c.Script.LoadString(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, "Announce.lua"))), "WoWCharacterTracker", c.Ns);
        c.Call("openWindow", "home");
        Assert.True(Bool(c, "shownText('1-6 of 8') ~= nil"));
        Assert.True(Bool(c, "shownText('Killed Boss 8 in Uldaman!') ~= nil and shownText('Killed Boss 2 in Uldaman!') == nil"));
        c.Lua("""
            for _, f in ipairs(frames) do if f.scripts.OnMouseWheel then moments = f end end
            moments.GetLeft = function() return 0 end
            moments.scripts.OnMouseWheel(moments, -1)
            moments.scripts.OnMouseWheel(moments, -1)
            moments.scripts.OnMouseWheel(moments, -1) -- past the end: stays
            """);
        Assert.True(Bool(c, "shownText('3-8 of 8') ~= nil"));
        Assert.True(Bool(c, "shownText('Killed Boss 1 in Uldaman!') ~= nil and shownText('Killed Boss 8 in Uldaman!') == nil"));

        // A taller window fills its bottom with more moments: all eight fit, nothing left to scroll.
        c.Lua("named('WoWCharacterTrackerWindow').GetHeight = function() return 1400 end");
        c.Call("openWindow", "home");
        Assert.True(Bool(c, "shownText('Killed Boss 8 in Uldaman!') ~= nil and shownText('Killed Boss 1 in Uldaman!') ~= nil"));
        Assert.True(Bool(c, "shownText('1-8 of 8') == nil and shownText('3-8 of 8') == nil"));
    }

    [Fact]
    public void Find_online_friends_lists_each_friend_with_the_buttons_for_their_state()
    {
        var c = FriendsTab();
        Assert.True(Bool(c, "shownText('Annie') == nil")); // not until you ask for them
        c.Lua("local b = findFriendsButton(); b.scripts.OnClick(b)");
        Assert.Equal("find", Calls(c));

        // Your friends in the grid, everyone else in lists by whether they have the addon.
        Assert.True(Bool(c, "friendCard('Cidro') ~= nil and friendCard('Bobo') == nil"));
        Assert.True(Bool(c, "shownText('Have the addon (2)') ~= nil and shownText('Still checking (1)') ~= nil and shownText('No addon (2)') ~= nil"));
        Assert.Equal("Checking... (1 left)", c.Lua("return findFriendsButton().text").String);
        Assert.Equal("", c.Lua("return friendButtons('Annie')").String);
        Assert.True(Bool(c, "shownText('Ann#1  ·  Level 60  ·  Orgrimmar  ·  Checking...') ~= nil"));
        Assert.Equal("Ask,Share", c.Lua("return friendButtons('Bobo')").String);
        Assert.True(Bool(c, "shownText('Bobby (Bob#2)  ·  Level 12') ~= nil")); // the label (nickname and BattleTag), then the level
        Assert.Equal("Sharing,Ask for more", c.Lua("return friendButtons('Cidro')").String); // they share with you already
        Assert.True(Bool(c, "shownText('You share, shares with you') ~= nil"));
        Assert.Equal("Invite", c.Lua("return friendButtons('Danno')").String);
        Assert.Equal("Invited", c.Lua("return friendButtons('Evie')").String);
        Assert.Equal("Unblock", c.Lua("return friendButtons('Fayla')").String);
        Assert.True(Bool(c, "shownText('Fay#6  ·  Blocked') ~= nil")); // still says why, next to the button

        // The class color from RAID_CLASS_COLORS, white without one.
        Assert.Equal(0.25, Num(c, "shownText('Annie').color[1]"), 3);
        Assert.Equal("1,1,1", c.Lua("return table.concat(shownText('Bobo').color, ',')").String);
    }

    // Ask opens the ask box; its Ask asks with what you send along and what you ask for.
    const string AskStubs = """
        function askBox() return named('WoWCharacterTrackerAskBox') end
        function pressAsk()
          for _, f in ipairs(frames) do
            if f.parent == askBox() and f.text == 'Ask' then return f.scripts.OnClick(f) end
          end
          error('no Ask')
        end
        ns.askFriend = function(tag, wants, give)
          local g = {}
          for _, k in ipairs(ns.SHARE_CATEGORIES) do if give and give[k] then g[#g + 1] = k end end
          call('ask ' .. tag)
          askedWith = { wants = table.concat(wants, ','), give = give and table.concat(g, ',') or 'nothing' }
          return 'Asked ' .. tag .. '.'
        end
        """;

    [Fact]
    public void Each_friend_button_calls_friends_with_their_tag()
    {
        var c = FriendsTab();
        c.Lua(AskStubs.Replace("call(", "table.insert(calls, "));
        c.Lua("online[3].sharesWithYou = false; shared = {}; local b = findFriendsButton(); b.scripts.OnClick(b); calls = {}");
        Assert.Equal("Sharing,Ask again", c.Lua("return friendButtons('Cidro')").String);
        c.Lua("clickFriend('Bobo', 'Ask')");
        Assert.True(Bool(c, "onScreen(askBox())"));
        c.Lua("pressAsk()");
        Assert.False(Bool(c, "onScreen(askBox())"));
        c.Lua("clickFriend('Bobo', 'Share')");
        c.Lua("clickFriend('Cidro', 'Ask again')");
        c.Lua("pressAsk()");
        c.Lua("clickFriend('Danno', 'Invite')");
        c.Lua("clickFriend('Evie', 'Invited')"); // invited within a day: does nothing
        Assert.Equal("ask Bob#2;share Bob#2;ask Cid#3;invite Dan#4", Calls(c));
        Assert.True(Bool(c, "shownText('Invited Dan#4.') ~= nil")); // what it said, in the tab
    }

    // The ask box starts as a swap of what friends get from you; unticking changes either side, and
    // Send them mine too off sends nothing along.
    [Fact]
    public void The_ask_box_sends_what_you_share_and_what_you_ask_for()
    {
        var c = FriendsTab();
        c.Lua(AskStubs.Replace("call(", "table.insert(calls, "));
        c.Lua("local b = findFriendsButton(); b.scripts.OnClick(b)");
        c.Lua("clickFriend('Bobo', 'Ask')");
        Assert.True(Bool(c, "shownText('Ask Bob#2 to share their characters with you.') ~= nil"));
        Assert.True(Bool(c, "shownText('You share') ~= nil and shownText('You ask for') ~= nil"));
        c.Lua("pressAsk()");
        Assert.Equal("gear,talents,location,professions,reputations,highlights,leveling", c.Lua("return askedWith.give").String);
        Assert.Equal("gear,talents,location,professions,reputations,highlights,leveling", c.Lua("return askedWith.wants").String);
        Assert.True(Bool(c, "shownText('Asked Bob#2.') ~= nil"));

        c.Lua("clickFriend('Bobo', 'Ask')");
        c.Lua("local b = askBox(); b.give.gear = false; b.ask.map = true");
        c.Lua("pressAsk()");
        Assert.Equal("talents,location,professions,reputations,highlights,leveling", c.Lua("return askedWith.give").String);
        Assert.EndsWith(",map", c.Lua("return askedWith.wants").String);

        c.Lua("clickFriend('Bobo', 'Ask')");
        c.Lua("click('Send them mine too')");
        c.Lua("pressAsk()");
        Assert.Equal("nothing", c.Lua("return askedWith.give").String);
    }

    // Map for the other faction is a row too: shared with them, asked of them, or both.
    [Fact]
    public void The_ask_box_has_map_for_the_other_faction()
    {
        var c = FriendsTab();
        c.Lua(AskStubs.Replace("call(", "table.insert(calls, "));
        c.Lua("local b = findFriendsButton(); b.scripts.OnClick(b)");
        c.Lua("clickFriend('Bobo', 'Ask')");
        Assert.True(Bool(c, "shownText('Map for the other faction') ~= nil"));
        c.Lua("local b = askBox(); b.ask.map = true; b.ask.otherFaction = true; b.give.otherFaction = true");
        c.Lua("pressAsk()");
        Assert.EndsWith(",map,otherFaction", c.Lua("return askedWith.wants").String);
        Assert.True(Bool(c, "askBox().give.otherFaction"));
    }

    [Fact]
    public void Requests_are_listed_with_allow_not_now_and_never()
    {
        var c = FriendsTab();
        const string line = "Zebbe (Zeb#2222) wants to see your characters";
        Assert.True(Bool(c, $"shownText('{line}') ~= nil"));
        c.Lua($"clickIn('{line}', 'Allow')");
        c.Lua($"clickIn('{line}', 'Not now')");
        c.Lua($"clickIn('{line}', 'Never')");
        Assert.Equal("Zeb#2222 allow;Zeb#2222 notnow;Zeb#2222 never", Calls(c));

        c.Lua("requests = {}");
        c.Call("friendsChanged");
        Assert.True(Bool(c, $"shownText('{line}') == nil"));
        Assert.True(Bool(c, "shownText('Requests') == nil"));
    }

    [Fact]
    public void Redrawing_reuses_the_row_frames()
    {
        var c = FriendsTab();
        c.Lua("local b = findFriendsButton(); b.scripts.OnClick(b)");
        var before = Num(c, "#frames");
        c.Call("friendsChanged");
        c.Lua("local b = findFriendsButton(); b.scripts.OnClick(b)");
        Assert.Equal(before, Num(c, "#frames"));

        c.Lua("table.remove(online, 1)"); // fewer rows: the spare one hides
        c.Call("friendsChanged");
        Assert.Equal(before, Num(c, "#frames"));
        Assert.True(Bool(c, "shownText('Annie') == nil"));
        Assert.True(Bool(c, "shownText('Bobo') ~= nil"));
    }

    [Fact]
    public void Friends_changed_redraws_only_while_the_tab_is_shown()
    {
        var c = FriendsTab();
        c.Lua("local b = findFriendsButton(); b.scripts.OnClick(b)");
        c.Lua("online[2].character = 'Bobbo'");
        c.Call("friendsChanged");
        Assert.True(Bool(c, "shownText('Bobbo') ~= nil"));

        c.Call("openWindow", "general");
        c.Lua("online[2].character = 'Bobber'; drew = 0; local f = ns.onlineFriends; ns.onlineFriends = function() drew = drew + 1 return f() end");
        c.Call("friendsChanged");
        Assert.Equal(0, Num(c, "drew"));
        c.Call("openWindow", "friends");
        Assert.True(Bool(c, "shownText('Bobber') ~= nil"));
    }

    [Fact]
    public void A_friends_characters_are_listed_like_the_app()
    {
        var c = FriendsTab();
        c.Lua("openFriend('Cidro')");
        Assert.True(Bool(c, "charRow('Cidrin Frostmane') ~= nil")); // with the surname
        Assert.True(Bool(c, "shownText('Ironforge') ~= nil")); // under the name
        Assert.True(Bool(c, "shownText('60') ~= nil and shownText('112') ~= nil")); // level and item level pills
        Assert.True(Bool(c, "shownText('2h ago') ~= nil"));
    }

    // A second character, items 100 (cached, epic), 200 and 300 (not cached), and a tooltip that
    // records what it was given. gearRows() is the shown panel rows as "slot:name:enchant".
    const string GearStubs = """
        local cid = shared[1].chars
        cid[1].gear = '5~200,1~100,16~300'
        cid[1].enchants = '1~100~+8 Stamina~1234\n16~300~Crusader~0'
        cid[2] = { char = 'Cidra', realm = 'Firemaw', level = 20, gear = '1~400' }
        ITEM_QUALITY_COLORS = { [4] = { r = 0.64, g = 0.21, b = 0.93 } }
        ns.itemInfo = function(id) if id == 100 then return 'Frostfire Circlet', nil, 4 end end
        GameTooltip = {
          SetOwner = function(self, owner) self.owner = owner; self.lines = {}; self.link = nil end,
          AddLine = function(self, s) table.insert(self.lines, s) end,
          SetHyperlink = function(self, link) self.link = link end,
          Show = function(self) self.up = true end,
          Hide = function(self) self.up = false end,
        }
        function panel()
          for _, f in ipairs(frames) do if f.name == 'WoWCharacterTrackerDetailsPanel' then return f end end
        end
        function gearRows()
          local out = {}
          for _, f in ipairs(frames) do
            if f.parent == panel() and f.slotText and onScreen(f) then
              out[#out + 1] = f.slotText.text .. ':' .. f.nameText.text .. ':' .. (f.enchantText.shown and f.enchantText.text or '')
            end
          end
          return table.concat(out, ';')
        end
        function gearRow(i)
          local n = 0
          for _, f in ipairs(frames) do
            if f.parent == panel() and f.slotText and onScreen(f) then
              n = n + 1
              if n == i then return f end
            end
          end
        end
        function titleText()
          for _, s in ipairs(strings) do if s.owner == panel() then return s end end
        end
        function title() return titleText().text end
        function clickChar(name) local l = charRow(name); l.scripts.OnMouseUp(l, 'LeftButton') end
        -- The unfolded character's Gear (n) button, which opens the panel; nil when it has none.
        function gearButton()
          for _, f in ipairs(frames) do
            if f.kind == 'Button' and onScreen(f) and type(f.text) == 'string' and f.text:find('^Gear %(') then return f end
          end
        end
        function openGear() local b = gearButton(); b.scripts.OnClick(b) end
        function clickGear(name) clickChar(name); openGear() end
        -- A label in the details panel (What you share, under the lists, has some of the same words).
        function factText(s)
          for _, fs in ipairs(strings) do
            if onScreen(fs) and fs.text == s and fs.owner.valueText then return fs end
          end
        end
        """;

    static FriendsLuaTests.Client GearTab()
    {
        var c = FriendsTab();
        c.Lua(GearStubs);
        c.Call("friendsChanged");
        c.Lua("openFriend('Cidro')");
        return c;
    }

    [Fact]
    public void A_shared_characters_gear_button_opens_its_gear_in_slot_order()
    {
        var c = GearTab();
        c.Lua("clickChar('Cidrin Frostmane')");
        Assert.True(Bool(c, "panel() == nil")); // built on the first open, not by the unfold
        Assert.Equal("Gear (3)", c.Lua("return gearButton().text").String);
        c.Lua("openGear()");
        Assert.True(Bool(c, "onScreen(panel())"));
        Assert.True(Bool(c, "title() == 'Cidrin Frostmane'"));
        Assert.Equal("Head:Frostfire Circlet:+8 Stamina;Chest:item 200:;Main Hand:item 300:Crusader", c.Lua("return gearRows()").String);
        Assert.Equal(0.64, Num(c, "gearRow(1).nameText.color[1]"), 3);
        Assert.Equal("1,1,1", c.Lua("return table.concat(gearRow(2).nameText.color, ',')").String);
        Assert.Contains("WoWCharacterTrackerDetailsPanel", c.Lua("return table.concat(UISpecialFrames, ',')").String);
    }

    [Fact]
    public void Hovering_a_gear_row_shows_the_real_item_tooltip()
    {
        var c = GearTab();
        c.Lua("clickGear('Cidrin Frostmane')");
        c.Lua("r = gearRow(1); r.scripts.OnEnter(r)");
        Assert.True(Bool(c, "GameTooltip.up and GameTooltip.owner == r"));
        Assert.Equal("item:100:1234", c.Lua("return GameTooltip.link").String);
        c.Lua("r.scripts.OnLeave(r)");
        Assert.False(Bool(c, "GameTooltip.up"));

        c.Lua("r = gearRow(2); r.scripts.OnEnter(r)");
        Assert.Equal("item:200:", c.Lua("return GameTooltip.link").String);
        c.Lua("r = gearRow(3); r.scripts.OnEnter(r)");
        Assert.Equal("item:300:", c.Lua("return GameTooltip.link").String); // enchant id 0: none known
    }

    [Fact]
    public void Clicking_another_character_closes_the_panel_and_its_gear_button_reopens_it()
    {
        var c = GearTab();
        c.Lua("clickGear('Cidrin Frostmane')");
        var built = Num(c, "#frames");
        c.Lua("clickChar('Cidra')");
        Assert.False(Bool(c, "onScreen(panel())")); // it showed another character
        c.Lua("openGear()");
        Assert.Equal(built, Num(c, "#frames"));
        Assert.Equal("Head:item 400:", c.Lua("return gearRows()").String);
        Assert.True(Bool(c, "title() == 'Cidra'"));
        c.Lua("openGear()"); // the same button closes it
        Assert.False(Bool(c, "onScreen(panel())"));

        c.Lua("openGear()");
        c.Lua("clickChar('Cidra')"); // folding the row closes it too
        Assert.False(Bool(c, "onScreen(panel())"));
        c.Lua("clickGear('Cidrin Frostmane')");
        Assert.Equal("Head:Frostfire Circlet:+8 Stamina;Chest:item 200:;Main Hand:item 300:Crusader", c.Lua("return gearRows()").String);
        Assert.Equal(built, Num(c, "#frames"));
    }

    // Everything shared about the character, each line only when it's known.
    [Fact]
    public void The_details_list_what_the_friend_shared()
    {
        var c = GearTab();
        c.Lua("""
            local cid = shared[1].chars[1]
            cid.class, cid.race, cid.sex, cid.faction, cid.money = 'Mage', 'Gnome', 'female', 'Alliance', 1234567
            """);
        c.Call("friendsChanged");
        c.Lua("clickGear('Cidrin Frostmane')");
        Assert.True(Bool(c, "shownText('Level 60 Frost Mage') ~= nil")); // the gear panel's headline
        // Icon tiles for what the row above doesn't already say; no empty talent trees.
        foreach (var (label, value) in new[] { ("Realm", "Firemaw"), ("Gold", "123g"), ("Talents", "Arcane 10  ·  Frost 41") })
        {
            Assert.Equal(value, c.Lua($"return factText('{label}').owner.valueText.text").String);
        }
        // The faction is a crest after the name on the row, not a tile.
        Assert.Contains("UI-PVP-Alliance", c.Lua("return shownText('Cidrin Frostmane').owner.faction.texture").String);
        foreach (var label in new[] { "Friend", "Faction", "Race", "Item level", "Zone", "Updated", "Professions" })
            Assert.True(Bool(c, $"factText('{label}') == nil"), label); // on the row or the section already
        // A row per profession, with its rank and a bar.
        Assert.Equal("300 / 300", c.Lua("return shownText('Tailoring').owner.rank.text").String);
        Assert.Equal("280 / 300", c.Lua("return shownText('Enchanting').owner.rank.text").String);
        Assert.Equal(0.25, Num(c, "titleText().color[1]"), 3); // class colored

        c.Lua("clickGear('Cidra')"); // little shared: only what's there
        Assert.True(Bool(c, "shownText('Level 20') ~= nil"));
        Assert.True(Bool(c, "factText('Gold') == nil and factText('Talents') == nil"));
    }

    // An online friend's card shows their shared character when they share the one they're on,
    // else what Battle.net says, which opens with a hint to ask.
    [Fact]
    public void Clicking_an_online_friend_opens_their_character()
    {
        var c = GearTab();
        c.Lua("""
            shared[1].chars[1].guid = 'Player-1-00000C1D'
            online[3].guid = 'Player-1-00000C1D' -- Cidro is on Cidrin
            online[2].guid = 'Player-1-00000B0B'
            online[2].contact = {} -- Bobo is a friend here
            local b = findFriendsButton(); b.scripts.OnClick(b)
            """);
        Assert.True(Bool(c, "friendCard('Cidrin Frostmane') ~= nil")); // the shared one, by its name
        Assert.True(Bool(c, "shownText('Level 60 Frost  ·  Ironforge  ·  online, updated 2h ago') ~= nil")); // when it last arrived
        Assert.True(Bool(c, "charRow('Cidro') == nil")); // shared, so no Battle.net row
        c.Lua("clickGear('Cidrin Frostmane')");
        Assert.True(Bool(c, "title() == 'Cidrin Frostmane'"));
        Assert.StartsWith("Head:Frostfire Circlet", c.Lua("return gearRows()").String);

        c.Lua("openFriend('Bobo')");
        c.Lua("clickChar('Bobo')");
        Assert.True(Bool(c, "shownText('Bobo') ~= nil"));
        Assert.True(Bool(c, "shownText('Bobby (Bob#2)') ~= nil"));
        const string notShared = "shownText('Not shared with you. Ask them to see their gear and more.')";
        Assert.True(Bool(c, notShared + " ~= nil")); // under the row
        Assert.True(Bool(c, "gearButton() == nil and not onScreen(panel())")); // no gear to show

        c.Call("friendsChanged"); // a redraw keeps it open
        Assert.True(Bool(c, notShared + " ~= nil"));
        c.Lua("clickChar('Bobo')");
        Assert.True(Bool(c, notShared + " == nil"));
    }

    // Near the right edge of the screen the panel goes left of the window instead of being pushed
    // over it, and it switches sides while the window is dragged.
    [Fact]
    public void The_details_panel_opens_on_the_side_with_room()
    {
        var c = GearTab();
        c.Lua("""
            UIParent.GetRight = function() return 1920 end
            win = named('WoWCharacterTrackerWindow')
            win.GetRight = function() return 1000 end
            """);
        c.Lua("clickGear('Cidra')");
        Assert.Equal("TOPLEFT", c.Lua("return panel().point[1]").String);

        c.Lua("win.GetRight = function() return 1800 end");
        c.Call("friendsChanged");
        Assert.Equal("TOPRIGHT", c.Lua("return panel().point[1]").String);

        c.Lua("win.scripts.OnDragStart(win)");
        Assert.True(Bool(c, "win.scripts.OnUpdate ~= nil"));
        c.Lua("win.GetRight = function() return 900 end; win.scripts.OnUpdate()");
        Assert.Equal("TOPLEFT", c.Lua("return panel().point[1]").String);
        c.Lua("win.scripts.OnDragStop(win)");
        Assert.True(Bool(c, "win.scripts.OnUpdate == nil"));
    }

    // Cid#3 a real contact (Friends.lua's own functions), and helpers for the sharing panel.
    const string SharingStubs = """
        WoWCharacterTrackerDB.share = { enabled = true, at = 1, contacts = { ['Cid#3'] = { share = true, receive = true } } }
        function sharingPanel() return named('WoWCharacterTrackerSharingPanel') end
        function sharingButtons()
          local out = {}
          for _, f in ipairs(frames) do
            if f.parent == sharingPanel() and f.kind == 'Button' and f.text and f.text:find(':') then out[#out + 1] = f.text end
          end
          return table.concat(out, ';')
        end
        function clickSharing(prefix)
          for _, f in ipairs(frames) do
            if f.parent == sharingPanel() and f.text and f.text:sub(1, #prefix) == prefix then return f.scripts.OnClick(f) end
          end
          error('no ' .. prefix)
        end
        """;

    [Fact]
    public void Sharing_opens_what_you_share_with_that_friend_and_changes_it()
    {
        var c = FriendsTab();
        c.Lua(SharingStubs);
        c.Lua("local b = findFriendsButton(); b.scripts.OnClick(b)");
        c.Lua("clickFriend('Cidro', 'Sharing')");
        Assert.True(Bool(c, "onScreen(sharingPanel())"));
        Assert.True(Bool(c, "shownText('Cid#3') ~= nil"));
        Assert.Equal("Gear: like all friends (on);Talents: like all friends (on);Location: like all friends (on);Professions: like all friends (on);Reputations: like all friends (on);Highlights: like all friends (on);Level times: like all friends (on);Gold: like all friends (off);Map position: like all friends (off);Map for the other faction: like all friends (off)",
            c.Lua("return sharingButtons()").String);
        Assert.True(Bool(c, "shownText('They get gear, talents, location, professions, reputations, highlights, level times.') ~= nil"));

        // Like all friends, on, off, like all friends again.
        c.Lua("clickSharing('Gold:')");
        Assert.True(Bool(c, "ns.shareSettings().contacts['Cid#3'].gold == true"));
        Assert.StartsWith("Gear: like all friends (on);Talents", c.Lua("return sharingButtons()").String);
        Assert.Contains(";Gold: on;Map position", c.Lua("return sharingButtons()").String);
        c.Lua("clickSharing('Gear:')"); c.Lua("clickSharing('Gear: on')");
        Assert.True(Bool(c, "ns.shareSettings().contacts['Cid#3'].gear == false"));
        Assert.True(Bool(c, "shownText('They get talents, location, professions, reputations, highlights, level times, gold.') ~= nil"));
        c.Lua("clickSharing('Gear: off')");
        Assert.True(Bool(c, "ns.shareSettings().contacts['Cid#3'].gear == nil"));

        c.Lua("click('Share your characters')");
        Assert.False(Bool(c, "ns.shareSettings().contacts['Cid#3'].share"));
        Assert.True(Bool(c, "shownText(\"They get nothing while you don't share with them.\") ~= nil"));
        c.Lua("click('Keep what they share with you')");
        Assert.False(Bool(c, "ns.shareSettings().contacts['Cid#3'].receive"));

        // The same button again closes it; a character's details take its place.
        c.Lua("clickFriend('Cidro', 'Sharing')");
        Assert.False(Bool(c, "onScreen(sharingPanel())"));
    }

    // The friend's nickname and asking them for more are in their section on the Friends tab only:
    // the sharing panel has neither again.
    [Fact]
    public void The_sharing_panel_has_no_nickname_box_or_ask_chips_of_its_own()
    {
        var c = FriendsTab();
        c.Lua(SharingStubs);
        c.Lua("local b = findFriendsButton(); b.scripts.OnClick(b)");
        c.Lua("clickFriend('Cidro', 'Sharing')");
        Assert.True(Bool(c, "onScreen(sharingPanel())"));
        Assert.Equal(0, c.Lua("""
            local n = 0
            for _, f in ipairs(frames) do if f.parent == sharingPanel() and f.kind == 'EditBox' then n = n + 1 end end
            return n
            """).Number);
        Assert.True(Bool(c, "shownText('Ask them to share:') == nil and shownText('Nickname') ~= nil")); // the section's box
    }

    // Typing a nickname for one friend and then opening another saves it to the first, not the second.
    [Fact]
    public void A_nickname_typed_for_one_friend_isnt_saved_to_the_next()
    {
        var c = FriendsTab();
        c.Lua(SharingStubs);
        c.Lua("online[2].contact = {}; local b = findFriendsButton(); b.scripts.OnClick(b)");
        c.Lua("""
            local box = showFriend('Cidro').nickname
            focused = false
            box.HasFocus = function() return focused end
            box.ClearFocus = function(self) focused = false; self.scripts.OnEditFocusLost(self) end
            box.GetText = function(self) return self.text end
            focused = true
            box.scripts.OnEditFocusGained(box)
            box:SetText('Cidde')
            showFriend('Bobo') -- still typing, another friend opens
            """);
        Assert.Equal("Cidde", c.Lua("return ns.friendNickname('Cid#3')").String);
        Assert.Equal("", c.Lua("return ns.friendNickname('Bob#2')").String);
        Assert.True(Bool(c, "section().nickname.text == ''")); // Bobo's (none) shows now
    }

    // A drag cut short by the window closing ends without saving, and the tab draws again.
    [Fact]
    public void A_drag_cut_short_by_closing_ends_and_the_tab_draws_again()
    {
        var c = FriendsTab();
        c.Lua("""
            online[2].contact = {}
            local b = findFriendsButton(); b.scripts.OnClick(b)
            function GetCursorPosition() return 0, 0 end
            local cid = friendCard('Cidro')
            cid.scripts.OnDragStart(cid)
            cid.scripts.OnHide(cid) -- Esc closed the window
            online[2].character = 'Bobber'
            ns.friendsChanged()
            """);
        Assert.True(Bool(c, "friendCard('Bobber') ~= nil"));
        Assert.True(Bool(c, "WoWCharacterTrackerDB.friendOrder == nil"));
    }

    // The panel says what they already share.
    [Fact]
    public void The_sharing_panel_shows_what_a_friend_already_shares()
    {
        var c = FriendsTab();
        c.Lua(SharingStubs);
        c.Lua("local b = findFriendsButton(); b.scripts.OnClick(b)");
        c.Lua("clickFriend('Cidro', 'Sharing')");
        Assert.True(Bool(c, "shownText('Nothing from them yet.') ~= nil"));
        c.Lua("""
            WoWCharacterTrackerDB.friends = { ['Cid#3'] = { chars = { ['Player-1-0000000C'] = { guid = 'Player-1-0000000C', char = 'Cidro', gear = '1~19019', money = 50 } } } }
            ns.friendsChanged()
            clickFriend('Cidro', 'Sharing'); clickFriend('Cidro', 'Sharing') -- closed, opened again
            """);
        Assert.True(Bool(c, "shownText('They share with you: gear, gold.') ~= nil"));
        c.Lua("WoWCharacterTrackerDB.share.contacts['Cid#3'].receive = false; clickFriend('Cidro', 'Sharing'); clickFriend('Cidro', 'Sharing')");
        Assert.True(Bool(c, "shownText(\"You don't keep what they share, so it doesn't show here.\") ~= nil"));
    }

    [Fact]
    public void A_request_for_more_has_no_never_button()
    {
        var c = Load();
        c.Lua("""
            WoWCharacterTrackerDB.share = { enabled = true, at = 1, contacts = { ['Zeb#2222'] = { share = true, receive = true } } }
            ns.offerFriendRequest({ tag = 'Zeb#2222', account = 9, wants = { 'map' }, more = true })
            """);
        Assert.True(Bool(c, "shownText('Zeb#2222 asks to also see your map position.') ~= nil"));
        Assert.False(Bool(c, "(function() for _, f in ipairs(frames) do if f.text == 'Never' and onScreen(f) then return true end end return false end)()"));
    }

    // A request from Zeb#2222 through the real Friends.lua, shown in the window's request box.
    static FriendsLuaTests.Client RequestBox()
    {
        var c = Load();
        c.Lua("""
            function requestBox() return named('WoWCharacterTrackerRequestBox') end
            function boxButton(label)
              for _, f in ipairs(frames) do
                if f.text == label and f.kind == 'Button' and onScreen(f) then return f end
              end
              error('no ' .. label)
            end
            function press(label) local b = boxButton(label); b.scripts.OnClick(b) end
            ns.offerFriendRequest({ tag = 'Zeb#2222', account = 9 })
            """);
        return c;
    }

    [Fact]
    public void A_request_says_what_allow_gives_and_keeps_only_the_changed_choices()
    {
        var c = RequestBox();
        Assert.True(Bool(c, "onScreen(requestBox())"));
        Assert.Equal(0, Num(c, "#shown")); // not the Blizzard popup
        Assert.True(Bool(c, "shownText('Zeb#2222 wants to see your characters.') ~= nil"));
        Assert.True(Bool(c, "shownText('Allow gives them 1 of your characters: name, level, class and item level, plus gear, talents, location, professions, reputations, highlights, level times.') ~= nil"));
        Assert.True(Bool(c, "shownText('Talents') == nil")); // folded

        c.Lua("press('Choose what they get')");
        c.Lua("press('Basics only')");
        Assert.True(Bool(c, "shownText('Allow gives them 1 of your characters: name, level, class and item level.') ~= nil"));
        c.Lua("click('Talents')");
        Assert.True(Bool(c, "shownText('Allow gives them 1 of your characters: name, level, class and item level, plus talents.') ~= nil"));

        c.Lua("press('Allow')");
        Assert.False(Bool(c, "onScreen(requestBox())"));
        c.Lua("zeb = ns.shareSettings().contacts['Zeb#2222']");
        Assert.True(Bool(c, "zeb.share and zeb.receive"));
        Assert.True(Bool(c, "zeb.talents == nil and zeb.gold == nil")); // the same as for all friends: follows it
        Assert.True(Bool(c, "zeb.gear == false and zeb.location == false and zeb.professions == false and zeb.reputations == false and zeb.highlights == false and zeb.leveling == false"));
        Assert.Contains("Zeb#2222 can now see your shared characters (talents). Change it on the Friends tab (/wct).", c.Said());
    }

    [Fact]
    public void Like_all_friends_goes_back_to_the_general_choice_and_esc_is_not_now()
    {
        var c = RequestBox();
        c.Lua("press('Choose what they get'); press('Everything')");
        Assert.True(Bool(c, "find('Gold').checked"));
        Assert.True(Bool(c, "find('Map for the other faction').checked"));
        c.Lua("press('Like all friends')");
        Assert.False(Bool(c, "find('Gold').checked"));

        c.Lua("requestBox():Hide(); requestBox().scripts.OnHide(requestBox())"); // Esc
        c.Run(1);
        Assert.Equal(0, Num(c, "#ns.pendingRequests()"));
        Assert.True(Bool(c, "ns.shareSettings().contacts['Zeb#2222'] == nil"));

        c.Lua("ns.offerFriendRequest({ tag = 'Al#4444', account = 8 })");
        Assert.True(Bool(c, "shownText('Al#4444 wants to see your characters.') ~= nil"));
        c.Lua("ns.answerFriendRequest(ns.pendingRequests()[1], 'never')"); // from the Friends tab's list
        Assert.False(Bool(c, "onScreen(requestBox())"));
    }

    [Fact]
    public void Shared_with_you_has_your_sharing_for_each_friend()
    {
        var c = GearTab();
        c.Lua(SharingStubs);
        c.Call("friendsChanged");
        c.Lua("clickFriend('Cidro', 'Sharing')");
        Assert.True(Bool(c, "onScreen(sharingPanel())"));
        c.Lua("clickChar('Cidra')");
        Assert.True(Bool(c, "onScreen(sharingPanel())")); // unfolding a character leaves it
        c.Lua("openGear()");
        Assert.True(Bool(c, "onScreen(panel())"));
        Assert.False(Bool(c, "onScreen(sharingPanel())")); // one side panel at a time
    }

    [Fact]
    public void A_character_without_gear_has_no_gear_button()
    {
        var c = GearTab();
        c.Lua("shared[1].chars[2].gear = nil");
        c.Call("friendsChanged");
        c.Lua("clickChar('Cidra')");
        Assert.True(Bool(c, "gearButton() == nil")); // nothing to show, so no button saying so
        c.Lua("clickChar('Cidrin Frostmane')");
        Assert.Equal("Gear (3)", c.Lua("return gearButton().text").String);
    }

    [Fact]
    public void The_friends_tab_says_when_nobody_is_online_and_nothing_is_shared()
    {
        var c = FriendsTab();
        c.Lua("online, shared, requests = {}, {}, {}");
        c.Lua("local b = findFriendsButton(); b.scripts.OnClick(b)");
        Assert.True(Bool(c, "shownText('No friends here yet. Battle.net friends online in this game show up here, and so does everyone you share with.') ~= nil"));
    }

    // The Friends tab's What you share opens the Sharing tab, where what all friends get is.
    [Fact]
    public void What_you_share_on_the_friends_tab_opens_the_sharing_tab()
    {
        var c = FriendsTab();
        Assert.True(Bool(c, "shownText('What you share with friends') == nil"));
        c.Lua("local b = find('What you share'); b.scripts.OnClick(b)");
        Assert.Equal("sharing", c.Lua("return WoWCharacterTrackerDB.window.tab").String);
        Assert.True(Bool(c, "shownText('What you share with friends') ~= nil"));
    }

    // Without Friends.lua's new functions (an older Friends.lua), the tab still opens.
    [Fact]
    public void The_friends_tab_opens_without_the_friend_functions()
    {
        var c = Load();
        c.Lua("ns.onlineFriends, ns.findOnlineFriends, ns.pendingRequests, ns.sharedWithYou = nil, nil, nil, nil");
        c.Call("openWindow", "friends");
        c.Lua("local b = find('Find online friends'); b.scripts.OnClick(b)");
        c.Call("friendsChanged");
    }

    [Fact]
    public void The_short_options_page_registers_and_opens_the_window()
    {
        var c = Load();
        Assert.Equal("WoW Character Tracker", c.Lua("return registered").String);
        c.Lua("opened = 0; ns.openWindow = function() opened = opened + 1 end");
        c.Lua("local b = find('Open the Character Tracker window'); b.scripts.OnClick(b)");
        Assert.Equal(1, Num(c, "opened"));
        Assert.True(Bool(c, "find('Instant posting') == nil")); // the old controls are gone
    }
}
