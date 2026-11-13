using MoonSharp.Interpreter;

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
        foreach (var file in new[] { "Today.lua", "NoteBox.lua", "Ui.lua", "Home.lua", "Window.lua", "Options.lua", "Commands.lua" })
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
            { chars = { ['Player-1-0000000A'] = { char = 'Zeb', ilvl = 35, played = 90000 } }, items = {}, events = {
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
        Assert.True(Bool(c, "shownText('Level 45 Night Elf Warrior') ~= nil"));
        Assert.True(Bool(c, "shownText('44 to 45 took 2h 54m  ·  average 1h 57m') ~= nil"));
        Assert.True(Bool(c, "shownText('Killed Archaedas in Uldaman!') ~= nil"));
        // The level history is folded until opened, and stays as you left it.
        Assert.False(Bool(c, "find('44 to 45') and onScreen(find('44 to 45'))"));
        c.Lua("click('Level history  v')");
        Assert.True(Bool(c, "shownText('44 to 45') ~= nil"));
        Assert.True(Bool(c, "shownText('2h 54m') ~= nil"));
        Assert.True(Bool(c, "WoWCharacterTrackerDB.window.homeHistory == true"));
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
        c.Lua("click('+2 more  v')");
        Assert.True(Bool(c, "shownText('1 |cff9a9a9askins|r') ~= nil"));
        Assert.True(Bool(c, "shownText('5 |cff9a9a9afish|r') ~= nil"));
    }

    [Fact]
    public void The_window_has_four_tabs_and_builds_each_tab_once()
    {
        var c = Load();
        c.Call("openWindow", "general");
        foreach (var tab in new[] { "Sharing", "Friends", "Note", "Settings" })
            Assert.True(Bool(c, $"find('{tab}') ~= nil"), tab);
        Assert.True(Bool(c, "ns.windowShown()"));
        Assert.True(Bool(c, "find('Guild chat') == nil")); // not built until opened

        c.Call("openWindow", "chat");
        var built = Num(c, "#frames");
        Assert.True(Bool(c, "find('Guild chat') ~= nil"));
        c.Call("openWindow", "chat");
        Assert.Equal(built, Num(c, "#frames"));

        Assert.Contains("WoWCharacterTrackerWindow", c.Lua("return table.concat(UISpecialFrames, ',')").String);
        c.Call("toggleWindow");
        Assert.False(Bool(c, "ns.windowShown()"));
    }

    [Fact]
    public void The_moved_options_write_the_same_settings_as_before()
    {
        var c = Load();
        c.Call("openWindow", "chat");
        c.Lua("click('Guild chat')");
        Assert.True(Bool(c, "ns.setting('announce').guild"));

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

        c.Call("openWindow", "friends"); // what all friends get sits under the Friends tab's lists
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
        c.Call("openWindow", "friends");
        Assert.False(Bool(c, "find('Map position').checked"));
        c.Lua("click('Map position')");
        Assert.True(Bool(c, "WoWCharacterTrackerDB.share.defaults.map"));
        Assert.False(Bool(c, "find('Show my map position to the other faction').checked"));
        c.Lua("click('Show my map position to the other faction')");
        Assert.True(Bool(c, "WoWCharacterTrackerDB.share.defaults.otherFaction"));
        c.Lua("local b = find('Map updates: live'); b.scripts.OnClick(b)");
        Assert.Equal(60, Num(c, "WoWCharacterTrackerDB.share.mapRate"));
        Assert.True(Bool(c, "find('Map updates: every minute') ~= nil"));
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
        c.Call("openWindow", "note");
        Assert.Equal("note", c.Lua("return WoWCharacterTrackerDB.window.tab").String);
        c.Lua("""
            local w
            for _, f in ipairs(frames) do if f.name == 'WoWCharacterTrackerWindow' then w = f end end
            rawset(w, 'GetPoint', function() return 'TOPLEFT', nil, 'BOTTOMLEFT', 100, 200 end)
            w.scripts.OnDragStop(w)
            """);
        Assert.Equal("TOPLEFT", c.Lua("return WoWCharacterTrackerDB.window.point").String);
        Assert.Equal("BOTTOMLEFT", c.Lua("return WoWCharacterTrackerDB.window.relative").String);
        Assert.Equal(200, Num(c, "WoWCharacterTrackerDB.window.y"));
        Assert.Equal("note", c.Lua("return WoWCharacterTrackerDB.window.tab").String);

        c.Call("toggleWindow");
        c.Call("openWindow");
        Assert.True(Bool(c, "find('Note box (/wct note)') ~= nil"));
        Assert.Equal("note", c.Lua("return WoWCharacterTrackerDB.window.tab").String);
    }

    [Fact]
    public void The_app_tab_lists_what_the_app_set()
    {
        var c = Load(settings: "{ post = { boss = true }, screenshot = {} }");
        c.Call("openWindow", "app");
        Assert.Contains("Posted to Discord: boss kills", c.Lua("return texts[#texts]").String);
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
        c.Lua("local b = find('Box stays: 10 seconds'); b.scripts.OnClick(b)");
        Assert.Equal(15, Num(c, "ns.setting('askSeconds')"));
        Assert.True(Bool(c, "find('Box stays: 15 seconds') ~= nil"));
    }

    // Reload reminders: one choice over the ding popup, the flight box and the chat line. The
    // defaults (no popup, flight box and chat line on) are Gentle.
    [Fact]
    public void Reload_reminders_are_one_choice_over_three_settings()
    {
        var c = Load();
        c.Call("openWindow", "settings");
        Assert.Equal("gentle", c.Lua("return ns.reloadChoice()").String);
        c.Lua("click('Every chance')");
        Assert.True(Bool(c, "WoWCharacterTrackerDB.popup == true and ns.setting('flightReload') and ns.setting('chatReminder')"));
        c.Lua("click('Off')");
        Assert.True(Bool(c, "WoWCharacterTrackerDB.popup == false and ns.setting('flightReload') == false and ns.setting('chatReminder') == false"));
        c.Lua("WoWCharacterTrackerDB.popup = true");
        Assert.Equal("custom", c.Lua("return ns.reloadChoice()").String);
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

    // Resized wider, Home's card and the moments under it take the room (the split keeps its share).
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
        Assert.Equal(1020, Num(c, "cardOf().width")); // 1100 less the window's edges and the scroll bar
        Assert.Equal(1020, Num(c, "find('Friends tab').point[4]")); // at the right edge of the wider page
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

    // The Settings tab links to the app's page with the app and without it.
    [Fact]
    public void The_settings_tab_links_to_the_apps_page()
    {
        foreach (var settings in new[] { "{}", "{ post = {} }" })
        {
            var c = Load(settings: settings);
            c.Call("openWindow", "settings");
            Assert.True(Bool(c, "(function() for _, f in ipairs(frames) do if f.kind == 'EditBox' and f.text == ns.APP_URL and onScreen(f) then return true end end end)()"));
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
        ns.pendingRequests = function() return requests end
        ns.answerFriendRequest = function(f, choice) call(f.tag .. ' ' .. choice) end
        ns.sharedWithYou = function() return shared end
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
        -- The shown buttons of name's section as "A,B", and a click on one.
        function friendButtons(name)
          local s, out = showFriend(name), {}
          for _, b in ipairs({ s.b1, s.b2 }) do if onScreen(b) then out[#out + 1] = b.text end end
          return table.concat(out, ',')
        end
        function clickFriend(name, label)
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
        c.Lua("openFriend('Cidro')");
        Assert.True(Bool(c, "charRow('Cidrin Frostmane') == nil"));
    }

    // Dragging a friend moves the others aside while you drag; letting go keeps the order (shared
    // with the app), and the grid shows it from then on.
    [Fact]
    public void Dragging_a_friend_sets_the_order()
    {
        var c = FriendsTab();
        c.Lua("local b = find('Find online friends'); b.scripts.OnClick(b)");
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
        c.Lua("local b = find('Find online friends'); b.scripts.OnClick(b)");
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

    // A friend who's offline shows the character seen last; online friends come first.
    [Fact]
    public void An_offline_friend_shows_the_character_seen_last()
    {
        var c = FriendsTab();
        c.Lua("""
            online = { online[2] }
            shared[1].chars[2] = { char = 'Cidra', level = 20, at = clock - 90000, received = clock - 90000 }
            local b = find('Find online friends'); b.scripts.OnClick(b)
            """);
        Assert.True(Bool(c, "friendCard('Cidrin Frostmane') ~= nil"));
        Assert.True(Bool(c, "shownText('Level 60 Frost  ·  Ironforge  ·  last seen 2h ago') ~= nil"));
        Assert.True(Bool(c, "shownText('Level 12  ·  online') ~= nil"));
        Assert.True(Bool(c, "friendCard('Bobo').point[4] < friendCard('Cidrin Frostmane').point[4]")); // online first, top left
    }

    // A click on a character unfolds its info under the row and its gear beside the window; the
    // panel's X closes only the panel, a second click on the row folds both away.
    [Fact]
    public void A_character_unfolds_under_its_row_and_folds_with_its_gear()
    {
        var c = FriendsTab();
        c.Lua("openFriend('Cidro')");
        c.Lua("local r = shownText('Cidrin Frostmane').owner; r.scripts.OnMouseUp(r)");
        Assert.True(Bool(c, "onScreen(named('WoWCharacterTrackerDetailsPanel'))"));
        Assert.True(Bool(c, "shownText('Tailoring 300/300, Enchanting 280/300') ~= nil")); // the info, under the row
        Assert.True(Bool(c, "shownText('Level times') ~= nil"));
        c.Lua("named('WoWCharacterTrackerDetailsPanel'):Hide()"); // its X
        Assert.True(Bool(c, "shownText('Tailoring 300/300, Enchanting 280/300') ~= nil")); // still unfolded
        c.Lua("local r = shownText('Cidrin Frostmane').owner; r.scripts.OnMouseUp(r)");
        Assert.True(Bool(c, "shownText('Tailoring 300/300, Enchanting 280/300') == nil"));
        Assert.False(Bool(c, "onScreen(named('WoWCharacterTrackerDetailsPanel'))"));
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

    // On Forever the realm's name is a type and a layer ("Realm 6 PvP"). The ruleset is the game's
    // rules (Hardcore, Self-found), shown only when it isn't Normal.
    [Theory]
    [InlineData("Normal", null)]
    [InlineData("Hardcore", "Hardcore")]
    public void A_forever_character_shows_its_realm_type(string ruleset, string? shown)
    {
        var c = FriendsTab();
        c.Lua($"shared[1].chars[1].realm = 'CAMWOW EU5 Realm 6 PvP'; shared[1].chars[1].ruleset = '{ruleset}'; ns.friendsChanged()");
        c.Lua("openFriend('Cidro')");
        c.Lua("local r = shownText('Cidrin Frostmane').owner; r.scripts.OnMouseUp(r)");
        Assert.True(Bool(c, "shownText('Realm') ~= nil and shownText('PvP') ~= nil"));
        Assert.True(Bool(c, "shownText('Layer') ~= nil and shownText('6') ~= nil"));
        Assert.True(Bool(c, "shownText('Normal') == nil"));
        Assert.Equal(shown != null, Bool(c, "shownText('Ruleset') ~= nil"));
        if (shown != null) Assert.True(Bool(c, $"shownText('{shown}') ~= nil"));
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
        Assert.True(Bool(c, "shownText('7g 0s 0c') ~= nil"));
    }

    // Home: friends' moments beside yours, and a friend's row opens their details like the Friends tab.
    [Fact]
    public void Home_lists_friends_moments_and_opens_a_friends_character()
    {
        var c = Load();
        c.Lua(FriendStubs);
        c.Lua("""
            shared[1].chars[1].highlights = 'boss~99000~60~Ragnaros~Molten Core~40'
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
        c.Lua("local r = shownText('Killed Ragnaros in Molten Core!').owner; r.scripts.OnMouseUp(r)");
        Assert.True(Bool(c, "onScreen(named('WoWCharacterTrackerDetailsPanel'))"));
        Assert.True(Bool(c, "shownText('Cidrin Frostmane') ~= nil"));
    }

    [Fact]
    public void Find_online_friends_lists_each_friend_with_the_buttons_for_their_state()
    {
        var c = FriendsTab();
        Assert.True(Bool(c, "shownText('Annie') == nil")); // not until you ask for them
        c.Lua("local b = find('Find online friends'); b.scripts.OnClick(b)");
        Assert.Equal("find", Calls(c));

        Assert.Equal("", c.Lua("return friendButtons('Annie')").String);
        Assert.True(Bool(c, "shownText('Checking...') ~= nil")); // the open friend's state
        Assert.Equal("Ask,Share", c.Lua("return friendButtons('Bobo')").String);
        Assert.True(Bool(c, "shownText('Level 12  ·  online') ~= nil")); // level, then the label (nickname) and BattleTag
        Assert.Equal("Sharing,Ask for more", c.Lua("return friendButtons('Cidro')").String); // they share with you already
        Assert.True(Bool(c, "shownText('You share, shares with you') ~= nil"));
        Assert.Equal("Invite", c.Lua("return friendButtons('Danno')").String);
        Assert.Equal("Invited", c.Lua("return friendButtons('Evie')").String);
        Assert.Equal("", c.Lua("return friendButtons('Fayla')").String);
        Assert.True(Bool(c, "shownText('Blocked') ~= nil"));

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
        c.Lua("online[3].sharesWithYou = false; shared = {}; local b = find('Find online friends'); b.scripts.OnClick(b); calls = {}");
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
        c.Lua("local b = find('Find online friends'); b.scripts.OnClick(b)");
        c.Lua("clickFriend('Bobo', 'Ask')");
        Assert.True(Bool(c, "shownText('Ask Bob#2 to share their characters with you.') ~= nil"));
        Assert.True(Bool(c, "shownText('You share') ~= nil and shownText('You ask for') ~= nil"));
        c.Lua("pressAsk()");
        Assert.Equal("gear,talents,location,professions,highlights,leveling", c.Lua("return askedWith.give").String);
        Assert.Equal("gear,talents,location,professions,highlights,leveling", c.Lua("return askedWith.wants").String);
        Assert.True(Bool(c, "shownText('Asked Bob#2.') ~= nil"));

        c.Lua("clickFriend('Bobo', 'Ask')");
        c.Lua("local b = askBox(); b.give.gear = false; b.ask.map = true");
        c.Lua("pressAsk()");
        Assert.Equal("talents,location,professions,highlights,leveling", c.Lua("return askedWith.give").String);
        Assert.EndsWith(",map", c.Lua("return askedWith.wants").String);

        c.Lua("clickFriend('Bobo', 'Ask')");
        c.Lua("click('Send them mine too')");
        c.Lua("pressAsk()");
        Assert.Equal("nothing", c.Lua("return askedWith.give").String);
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
        c.Lua("local b = find('Find online friends'); b.scripts.OnClick(b)");
        var before = Num(c, "#frames");
        c.Call("friendsChanged");
        c.Lua("local b = find('Find online friends'); b.scripts.OnClick(b)");
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
        c.Lua("local b = find('Find online friends'); b.scripts.OnClick(b)");
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
    public void Clicking_a_shared_character_opens_its_gear_in_slot_order()
    {
        var c = GearTab();
        Assert.True(Bool(c, "panel() == nil")); // built on the first click
        c.Lua("clickChar('Cidrin Frostmane')");
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
        c.Lua("clickChar('Cidrin Frostmane')");
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
    public void Clicking_another_character_switches_the_panel_and_the_same_one_closes_it()
    {
        var c = GearTab();
        c.Lua("clickChar('Cidrin Frostmane')");
        var built = Num(c, "#frames");
        c.Lua("clickChar('Cidra')");
        Assert.Equal(built, Num(c, "#frames"));
        Assert.Equal("Head:item 400:", c.Lua("return gearRows()").String);
        Assert.True(Bool(c, "title() == 'Cidra'"));
        
        c.Lua("clickChar('Cidra')");
        Assert.False(Bool(c, "onScreen(panel())"));
        c.Lua("clickChar('Cidrin Frostmane')");
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
        c.Lua("clickChar('Cidrin Frostmane')");
        Assert.True(Bool(c, "shownText('Level 60 Frost Mage') ~= nil"));
        foreach (var (label, value) in new[] {
            ("Friend", "Cid#3"), ("Realm", "Firemaw"), ("Race", "Female Gnome, Alliance"), ("Item level", "112"), ("Zone", "Ironforge"), ("Gold", "123g 45s 67c"),
            ("Professions", "Tailoring 300/300, Enchanting 280/300"), ("Talents", "Arcane 10 / Fire 0 / Frost 41"), ("Updated", "2h ago") })
        {
            Assert.Equal(value, c.Lua($"return factText('{label}').owner.valueText.text").String);
        }
        Assert.Equal(0.25, Num(c, "titleText().color[1]"), 3); // class colored

        c.Lua("clickChar('Cidra')"); // little shared: only what's there
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
            local b = find('Find online friends'); b.scripts.OnClick(b)
            """);
        Assert.True(Bool(c, "friendCard('Cidrin Frostmane') ~= nil")); // the shared one, by its name
        Assert.True(Bool(c, "shownText('Level 60 Frost  ·  Ironforge  ·  online, updated 2h ago') ~= nil")); // when it last arrived
        Assert.True(Bool(c, "charRow('Cidro') == nil")); // shared, so no Battle.net row
        c.Lua("clickChar('Cidrin Frostmane')");
        Assert.True(Bool(c, "title() == 'Cidrin Frostmane'"));
        Assert.StartsWith("Head:Frostfire Circlet", c.Lua("return gearRows()").String);

        c.Lua("openFriend('Bobo')");
        c.Lua("clickChar('Bobo')");
        Assert.True(Bool(c, "shownText('Bobo') ~= nil and shownText('Level 12') ~= nil"));
        Assert.True(Bool(c, "shownText('Bobby (Bob#2)') ~= nil"));
        Assert.True(Bool(c, "shownText('Not shared with you. Ask them on the Friends tab to see their gear and more.') ~= nil"));
        Assert.Equal("", c.Lua("return gearRows()").String);
        Assert.True(Bool(c, "shownText('No gear shared') == nil"));

        c.Call("friendsChanged"); // a redraw keeps it open
        Assert.True(Bool(c, "onScreen(panel())"));
        c.Lua("clickChar('Bobo')");
        Assert.False(Bool(c, "onScreen(panel())"));
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
        c.Lua("clickChar('Cidra')");
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
        c.Lua("local b = find('Find online friends'); b.scripts.OnClick(b)");
        c.Lua("clickFriend('Cidro', 'Sharing')");
        Assert.True(Bool(c, "onScreen(sharingPanel())"));
        Assert.True(Bool(c, "shownText('Cid#3') ~= nil"));
        Assert.Equal("Gear: like all friends (on);Talents: like all friends (on);Location: like all friends (on);Professions: like all friends (on);Highlights: like all friends (on);Level times: like all friends (on);Gold: like all friends (off);Map position: like all friends (off);Map for the other faction: like all friends (off)",
            c.Lua("return sharingButtons()").String);
        Assert.True(Bool(c, "shownText('They get gear, talents, location, professions, highlights, level times.') ~= nil"));

        // Like all friends, on, off, like all friends again.
        c.Lua("clickSharing('Gold:')");
        Assert.True(Bool(c, "ns.shareSettings().contacts['Cid#3'].gold == true"));
        Assert.StartsWith("Gear: like all friends (on);Talents", c.Lua("return sharingButtons()").String);
        Assert.Contains(";Gold: on;Map position", c.Lua("return sharingButtons()").String);
        c.Lua("clickSharing('Gear:')"); c.Lua("clickSharing('Gear: on')");
        Assert.True(Bool(c, "ns.shareSettings().contacts['Cid#3'].gear == false"));
        Assert.True(Bool(c, "shownText('They get talents, location, professions, highlights, level times, gold.') ~= nil"));
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

    // The sharing panel's Nickname box sets the friend's nickname when you leave it (the app reads it).
    [Fact]
    public void The_sharing_panel_sets_a_nickname()
    {
        var c = FriendsTab();
        c.Lua(SharingStubs);
        c.Lua("clickFriend('Cidro', 'Sharing')");
        c.Lua("""
            for _, f in ipairs(frames) do
              if f.parent == sharingPanel() and f.kind == 'EditBox' then box = f end
            end
            box.GetText = function(self) return self.text end
            box.scripts.OnEditFocusGained(box)
            box:SetText('Cidde')
            box.scripts.OnEditFocusLost(box)
            """);
        Assert.Equal("Cidde", c.Lua("return ns.friendNickname('Cid#3')").String);
        Assert.True(Bool(c, "WoWCharacterTrackerDB.friendNames['Cid#3'].at > 0"));
        Assert.True(Bool(c, "shownText('Cidde (Cid#3)') ~= nil")); // the panel's title
    }

    // Typing a nickname for one friend and then opening another saves it to the first, not the second.
    [Fact]
    public void A_nickname_typed_for_one_friend_isnt_saved_to_the_next()
    {
        var c = FriendsTab();
        c.Lua(SharingStubs);
        c.Lua("local b = find('Find online friends'); b.scripts.OnClick(b)");
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
            local b = find('Find online friends'); b.scripts.OnClick(b)
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

    [Fact]
    public void The_sharing_panel_asks_a_friend_for_what_you_picked()
    {
        var c = FriendsTab();
        c.Lua(SharingStubs);
        c.Lua("asked = {}; ns.askFriend = function(tag, wants) asked = { tag = tag, wants = table.concat(wants, ',') }; return 'Asked them.' end");
        c.Lua("local b = find('Find online friends'); b.scripts.OnClick(b)");
        c.Lua("clickFriend('Cidro', 'Sharing')");
        Assert.True(Bool(c, "shownText('Ask them to share:') ~= nil"));
        c.Lua("""
            function chip(label)
              for _, f in ipairs(frames) do
                if f.parent == sharingPanel() and f.text == label then return f.scripts.OnClick(f) end
              end
              error('no ' .. label)
            end
            """);
        c.Lua("chip('Map position')");
        c.Lua("chip('Gold')");
        c.Lua("chip('Gold')"); // picked, then not
        c.Lua("chip('Ask')");
        Assert.Equal("Cid#3", c.Lua("return asked.tag").String);
        Assert.Equal("map", c.Lua("return asked.wants").String);
        Assert.True(Bool(c, "shownText('Asked them.') ~= nil"));
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
        Assert.True(Bool(c, "shownText('Allow gives them 1 of your characters: name, level, class and item level, plus gear, talents, location, professions, highlights, level times.') ~= nil"));
        Assert.True(Bool(c, "shownText('Talents') == nil")); // folded

        c.Lua("press('Choose what they get  v')");
        c.Lua("press('Basics only')");
        Assert.True(Bool(c, "shownText('Allow gives them 1 of your characters: name, level, class and item level.') ~= nil"));
        c.Lua("click('Talents')");
        Assert.True(Bool(c, "shownText('Allow gives them 1 of your characters: name, level, class and item level, plus talents.') ~= nil"));

        c.Lua("press('Allow')");
        Assert.False(Bool(c, "onScreen(requestBox())"));
        c.Lua("zeb = ns.shareSettings().contacts['Zeb#2222']");
        Assert.True(Bool(c, "zeb.share and zeb.receive"));
        Assert.True(Bool(c, "zeb.talents == nil and zeb.gold == nil")); // the same as for all friends: follows it
        Assert.True(Bool(c, "zeb.gear == false and zeb.location == false and zeb.professions == false and zeb.highlights == false and zeb.leveling == false"));
        Assert.Contains("Zeb#2222 can now see your shared characters (talents). Change it on the Friends tab (/wct).", c.Said());
    }

    [Fact]
    public void Like_all_friends_goes_back_to_the_general_choice_and_esc_is_not_now()
    {
        var c = RequestBox();
        c.Lua("press('Choose what they get  v'); press('Everything')");
        Assert.True(Bool(c, "find('Gold').checked"));
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
        Assert.True(Bool(c, "onScreen(panel())"));
        Assert.False(Bool(c, "onScreen(sharingPanel())")); // one side panel at a time
    }

    [Fact]
    public void A_character_without_gear_says_no_gear_shared()
    {
        var c = GearTab();
        c.Lua("shared[1].chars[2].gear = nil");
        c.Call("friendsChanged");
        c.Lua("clickChar('Cidra')");
        Assert.True(Bool(c, "shownText('No gear shared') ~= nil"));
        Assert.Equal("", c.Lua("return gearRows()").String);
        c.Lua("clickChar('Cidrin Frostmane')");
        Assert.True(Bool(c, "shownText('No gear shared') == nil"));
    }

    [Fact]
    public void The_friends_tab_says_when_nobody_is_online_and_nothing_is_shared()
    {
        var c = FriendsTab();
        c.Lua("online, shared, requests = {}, {}, {}");
        c.Lua("local b = find('Find online friends'); b.scripts.OnClick(b)");
        Assert.True(Bool(c, "shownText('No friends here yet. Battle.net friends online in this game show up here, and so does everyone you share with.') ~= nil"));
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
