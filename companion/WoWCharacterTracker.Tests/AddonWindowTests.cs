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
        UIParent = {}
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
        foreach (var file in new[] { "NoteBox.lua", "Ui.lua", "Window.lua", "Options.lua", "Commands.lua" })
            c.Script.Call(c.Script.LoadString(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, file)), codeFriendlyName: file), "WoWCharacterTracker", c.Ns);
        return c;
    }

    static double Num(FriendsLuaTests.Client c, string expr) => c.Lua("return " + expr).Number;
    static bool Bool(FriendsLuaTests.Client c, string expr) => c.Lua("return " + expr).CastToBool();

    [Fact]
    public void The_window_has_seven_tabs_and_builds_each_tab_once()
    {
        var c = Load();
        c.Call("openWindow", "general");
        foreach (var tab in new[] { "General", "Chat", "Highlights", "Note", "Friends", "Sharing", "App" })
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

        c.Call("openWindow", "sharing");
        Assert.False(Bool(c, "find('Gold').checked"));
        c.Lua("click('Gold')");
        Assert.True(Bool(c, "WoWCharacterTrackerDB.share.defaults.gold"));
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
        Assert.Contains(c.Said(), s => s.StartsWith("/wct times", StringComparison.Ordinal));
        c.Lua("SlashCmdList.CHARACTERTRACKER('friends Ask Zeb#2222')");
        Assert.Equal(" Ask Zeb#2222", c.Lua("return gotFriends").String);
        Assert.Equal(1, Num(c, "opened"));
    }

    // Highlights: how long the post box stays, 10 by default, cycling through the choices.
    [Fact]
    public void The_post_box_time_can_be_changed_on_the_highlights_tab()
    {
        var c = Load();
        c.Call("openWindow", "highlights");
        c.Lua("local b = find('Box stays: 10 seconds'); b.scripts.OnClick(b)");
        Assert.Equal(15, Num(c, "ns.setting('askSeconds')"));
        Assert.True(Bool(c, "find('Box stays: 15 seconds') ~= nil"));
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

    // Friends.lua's side of the Friends tab, stubbed: fixed rows, and every call recorded in `calls`.
    const string FriendStubs = """
        clock = 100000
        calls = {}
        RAID_CLASS_COLORS = { MAGE = { r = 0.25, g = 0.78, b = 0.92 } }
        local function call(s) calls[#calls + 1] = s end
        online = {
          { tag = 'Ann#1', account = 1, label = 'Ann#1', character = 'Annie', classFile = 'MAGE', level = 60, zone = 'Orgrimmar' },
          { tag = 'Bob#2', account = 2, label = 'Bobby', character = 'Bobo', level = 12, hasAddon = true },
          { tag = 'Cid#3', account = 3, label = 'Cid#3', character = 'Cidro', hasAddon = true, contact = { share = true }, sharesWithYou = true },
          { tag = 'Dan#4', account = 4, label = 'Dan#4', character = 'Danno', hasAddon = false },
          { tag = 'Eve#5', account = 5, label = 'Eve#5', character = 'Evie', hasAddon = false, invitedAt = clock - 3600 },
          { tag = 'Fay#6', account = 6, label = 'Fay#6', character = 'Fayla', hasAddon = true, blocked = true },
        }
        requests = { { tag = 'Zeb#2222', account = 9, label = 'Zebbe', at = clock - 5 } }
        shared = {
          { tag = 'Cid#3', label = 'Cid#3', chars = {
            { char = 'Cidrin', surname = 'Frostmane', realm = 'Firemaw', level = 60, classFile = 'MAGE', spec = 'Frost', ilvl = 112, zone = 'Ironforge',
              gear = '1~100,5~200', talents = 'Arcane 10 / Fire 0 / Frost 41', at = clock - 7200, received = clock - 7200 },
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
        """;

    static FriendsLuaTests.Client FriendsTab()
    {
        var c = Load();
        c.Lua(FriendStubs);
        c.Call("openWindow", "friends");
        return c;
    }

    static string Calls(FriendsLuaTests.Client c) => c.Lua("return table.concat(calls, ';')").String;

    [Fact]
    public void Find_online_friends_lists_each_friend_with_the_buttons_for_their_state()
    {
        var c = FriendsTab();
        Assert.True(Bool(c, "shownText('Annie') == nil")); // not until you ask for them
        c.Lua("local b = find('Find online friends'); b.scripts.OnClick(b)");
        Assert.Equal("find", Calls(c));

        Assert.True(Bool(c, "shownText('Checking...') ~= nil"));
        Assert.Equal("", c.Lua("return rowButtons('Annie')").String);
        Assert.Equal("Ask,Share", c.Lua("return rowButtons('Bobo')").String);
        Assert.True(Bool(c, "shownText('Bobby') ~= nil")); // the label (nickname)
        Assert.Equal("Ask again", c.Lua("return rowButtons('Cidro')").String);
        Assert.True(Bool(c, "shownText('You share, shares with you') ~= nil"));
        Assert.Equal("Invite", c.Lua("return rowButtons('Danno')").String);
        Assert.Equal("Invited", c.Lua("return rowButtons('Evie')").String);
        Assert.Equal("", c.Lua("return rowButtons('Fayla')").String);
        Assert.True(Bool(c, "shownText('Blocked') ~= nil"));

        // The class color from RAID_CLASS_COLORS, white without one.
        Assert.Equal(0.25, Num(c, "shownText('Annie').color[1]"), 3);
        Assert.Equal("1,1,1", c.Lua("return table.concat(shownText('Bobo').color, ',')").String);
    }

    [Fact]
    public void Each_friend_button_calls_friends_with_their_tag()
    {
        var c = FriendsTab();
        c.Lua("local b = find('Find online friends'); b.scripts.OnClick(b); calls = {}");
        c.Lua("clickIn('Bobo', 'Ask')");
        c.Lua("clickIn('Bobo', 'Share')");
        c.Lua("clickIn('Cidro', 'Ask again')");
        c.Lua("clickIn('Danno', 'Invite')");
        c.Lua("clickIn('Evie', 'Invited')"); // invited within a day: does nothing
        Assert.Equal("ask Bob#2;share Bob#2;ask Cid#3;invite Dan#4", Calls(c));
        Assert.True(Bool(c, "shownText('Invited Dan#4.') ~= nil")); // what it said, in the tab
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
    public void Shared_with_you_lists_characters_with_their_gear_on_hover()
    {
        var c = FriendsTab();
        c.Lua("""
            tip = { lines = {} }
            GameTooltip = {
              SetOwner = function(self, owner) self.owner = owner; self.lines = {} end,
              AddLine = function(self, s) table.insert(self.lines, s) end,
              Show = function(self) self.up = true end,
              Hide = function(self) self.up = false end,
            }
            ns.itemInfo = function(id) if id == 100 then return 'Frostfire Circlet', nil, 4 end end
            """);
        c.Call("friendsChanged");
        Assert.True(Bool(c, "shownText('Cidrin Frostmane') ~= nil")); // with the surname
        Assert.True(Bool(c, "shownText('level 60, Frost, ilvl 112, Ironforge, 2h ago') ~= nil"));

        c.Lua("line = shownText('Cidrin Frostmane').owner; line.scripts.OnEnter(line)");
        Assert.True(Bool(c, "GameTooltip.up and GameTooltip.owner == line"));
        var lines = c.Lua("return table.concat(GameTooltip.lines, ';')").String;
        Assert.Contains("Frostfire Circlet", lines);
        Assert.Contains("item 200", lines); // not cached yet
        Assert.Contains("Arcane 10 / Fire 0 / Frost 41", lines);
        c.Lua("line.scripts.OnLeave(line)");
        Assert.False(Bool(c, "GameTooltip.up"));
    }

    [Fact]
    public void The_friends_tab_says_when_nobody_is_online_and_nothing_is_shared()
    {
        var c = FriendsTab();
        c.Lua("online, shared, requests = {}, {}, {}");
        c.Lua("local b = find('Find online friends'); b.scripts.OnClick(b)");
        Assert.True(Bool(c, "shownText('None of your Battle.net friends are online in this game.') ~= nil"));
        Assert.True(Bool(c, "shownText('Nothing shared with you yet. Ask a friend above.') ~= nil"));
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
