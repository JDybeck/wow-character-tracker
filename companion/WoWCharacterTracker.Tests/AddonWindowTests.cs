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
        function CreateFrame(kind, name)
          local f = widget()
          f.kind, f.name = kind, name
          frames[#frames + 1] = f
          return f
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
