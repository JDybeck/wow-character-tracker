using MoonSharp.Interpreter;

namespace WoWCharacterTracker.Tests;

// Minimap.lua: the button on the minimap's edge, and Icon.tga, the picture on it.
public class MinimapButtonTests
{
    // Frames that remember their scripts, points and whether they're shown; Minimap is 140 wide with
    // its middle at (500, 400), like a Classic client at scale 1.
    const string Frames = """
        frames = {}
        local function widget()
          local w = { scripts = {}, shown = true }
          return setmetatable(w, { __index = function(_, k)
            if k:sub(1, 1) == k:sub(1, 1):lower() then return nil end
            if k == "SetScript" then return function(s, ev, f) s.scripts[ev] = f end end
            if k == "GetScript" then return function(s, ev) return s.scripts[ev] end end
            if k == "SetPoint" then return function(s, ...) s.point = { ... } end end
            if k == "SetTexture" then return function(s, t) s.texture = t end end
            if k == "Show" then return function(s) s.shown = true end end
            if k == "Hide" then return function(s) s.shown = false end end
            if k == "SetShown" then return function(s, v) s.shown = v and true or false end end
            if k == "IsShown" then return function(s) return s.shown end end
            if k == "CreateTexture" then return function(s) local t = widget(); s.textures = s.textures or {}; s.textures[#s.textures + 1] = t; return t end end
            return function() end
          end })
        end
        function CreateFrame(kind, name, parent) local f = widget(); f.kind, f.parent = kind, parent; frames[#frames + 1] = f; return f end
        Minimap = widget()
        Minimap.GetWidth = function() return 140 end
        Minimap.GetCenter = function() return 500, 400 end
        Minimap.GetEffectiveScale = function() return 1 end
        UIParent = widget()
        UIParent.GetEffectiveScale = function() return 1 end
        cursor = { 500, 400 }
        function GetCursorPosition() return cursor[1], cursor[2] end
        tip = {}
        GameTooltip = { SetOwner = function() end, SetText = function(_, t) tip[#tip + 1] = t end,
          AddLine = function(_, t) tip[#tip + 1] = t end, Show = function() end, Hide = function() end }
        """;

    static (Script Script, Table Ns) Load(string db = "{}")
    {
        var script = new Script();
        script.DoString(Frames);
        script.DoString($"WoWCharacterTrackerDB = {db}");
        var ns = new Table(script);
        script.Globals["ns"] = ns;
        script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, "Minimap.lua"))), "WoWCharacterTracker", ns);
        return (script, ns);
    }

    static double Number(Script s, string expr) => s.DoString("return " + expr).Number;

    [Fact]
    public void The_button_is_made_once_on_the_minimap_at_the_saved_angle()
    {
        var (s, _) = Load("{ minimap = { angle = 90 } }");
        s.DoString("b1 = ns.createMinimapButton(); b2 = ns.createMinimapButton()");
        Assert.Equal(1, Number(s, "#frames"));
        Assert.True(s.DoString("return b1 == b2 and b1.parent == Minimap").Boolean);
        // 90 degrees: straight up from the middle, past the edge.
        Assert.Equal("CENTER", s.DoString("return b1.point[1]").String);
        Assert.Equal(0, Number(s, "b1.point[4]"), 3);
        Assert.True(Number(s, "b1.point[5]") > 70);
        Assert.Contains(@"Interface\AddOns\WoWCharacterTracker\Icon", s.DoString("local t = {} for _, x in ipairs(b1.textures) do t[#t + 1] = tostring(x.texture) end return table.concat(t, ';')").String);
    }

    [Fact]
    public void Without_a_saved_angle_it_sits_bottom_left()
    {
        var (s, _) = Load();
        s.DoString("b = ns.createMinimapButton()");
        Assert.True(Number(s, "b.point[4]") < 0);
        Assert.True(Number(s, "b.point[5]") < 0);
        Assert.Equal(Number(s, "b.point[4]"), Number(s, "b.point[5]"), 3);
    }

    [Fact]
    public void Dragging_moves_it_around_the_edge_and_saves_the_angle()
    {
        var (s, _) = Load();
        s.DoString("""
            b = ns.createMinimapButton()
            b.scripts.OnDragStart(b)
            cursor = { 400, 400 } -- left of the middle
            b.scripts.OnUpdate(b, 0.02)
            b.scripts.OnDragStop(b)
            """);
        Assert.Equal(180, Number(s, "WoWCharacterTrackerDB.minimap.angle"), 3);
        Assert.True(Number(s, "b.point[4]") < -70);
        Assert.Equal(0, Number(s, "b.point[5]"), 3);
        Assert.True(s.DoString("return b.scripts.OnUpdate == nil").Boolean); // stops following the cursor
    }

    [Fact]
    public void A_click_opens_the_window_when_there_is_one()
    {
        var (s, _) = Load();
        s.DoString("b = ns.createMinimapButton(); b.scripts.OnClick(b, 'LeftButton')"); // no window yet: no error
        s.DoString("toggled = 0; ns.toggleWindow = function() toggled = toggled + 1 end; b.scripts.OnClick(b, 'LeftButton')");
        Assert.Equal(1, Number(s, "toggled"));
    }

    [Fact]
    public void The_tooltip_says_what_it_is_and_how_to_use_it()
    {
        var (s, _) = Load();
        s.DoString("b = ns.createMinimapButton(); b.scripts.OnEnter(b)");
        Assert.Equal("WoW Character Tracker", s.DoString("return tip[1]").String);
        Assert.Equal("Click to open. Drag to move.", s.DoString("return tip[2]").String);
    }

    [Fact]
    public void Hiding_and_showing_is_saved_and_kept_at_the_next_login()
    {
        var (s, _) = Load();
        s.DoString("b = ns.createMinimapButton()");
        Assert.True(s.DoString("return ns.minimapButtonShown()").Boolean);
        s.DoString("ns.setMinimapButtonShown(false)");
        Assert.True(s.DoString("return WoWCharacterTrackerDB.minimap.hidden").Boolean);
        Assert.False(s.DoString("return b.shown").Boolean);
        Assert.False(s.DoString("return ns.minimapButtonShown()").Boolean);
        s.DoString("ns.setMinimapButtonShown(true)");
        Assert.False(s.DoString("return WoWCharacterTrackerDB.minimap.hidden").Boolean);
        Assert.True(s.DoString("return b.shown").Boolean);

        var (next, _) = Load("{ minimap = { hidden = true } }");
        next.DoString("b = ns.createMinimapButton()");
        Assert.False(next.DoString("return b.shown").Boolean);
    }

    [Fact]
    public void Turning_it_on_before_login_makes_it()
    {
        var (s, _) = Load("{ minimap = { hidden = true } }");
        s.DoString("ns.setMinimapButtonShown(true)");
        Assert.Equal(1, Number(s, "#frames"));
        Assert.True(s.DoString("return frames[1].shown").Boolean);
    }

    [Fact]
    public void The_icon_is_a_64_pixel_32_bit_uncompressed_tga()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AddonLuaTests.AddonDir, "Icon.tga"));
        Assert.Equal(18 + 64 * 64 * 4, bytes.Length);
        Assert.Equal(0, bytes[1]); // no color map
        Assert.Equal(2, bytes[2]); // uncompressed true color
        Assert.Equal(64, BitConverter.ToUInt16(bytes, 12));
        Assert.Equal(64, BitConverter.ToUInt16(bytes, 14));
        Assert.Equal(32, bytes[16]);
        Assert.Equal(8, bytes[17] & 0x0F); // 8 alpha bits
        Assert.Contains(bytes.Skip(18).Where((_, i) => i % 4 == 3), a => a == 0); // see-through corners
        Assert.Contains(bytes.Skip(18).Where((_, i) => i % 4 == 3), a => a == 255);
    }

    [Fact]
    public void The_toc_loads_the_minimap_button_and_login_makes_it()
    {
        var lines = File.ReadAllLines(Path.Combine(AddonLuaTests.AddonDir, "WoWCharacterTracker.toc")).ToList();
        Assert.Contains("Minimap.lua", lines);
        Assert.True(lines.IndexOf("Minimap.lua") < lines.IndexOf("WoWCharacterTracker.lua"));
        Assert.Contains("ns.createMinimapButton()", File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, "WoWCharacterTracker.lua")));
    }
}
