using MoonSharp.Interpreter;

namespace WoWCharacterTracker.Tests;

// MapPins.lua with a stand-in world map: where pins land, and what the provider puts on the map.
public class MapPinsLuaTests
{
    // Zone 1433 sits at 1000..1100 (x) and 0..100 (y) on continent 0, which spans 0..2000 on map 13.
    const string Map = """
        Enum = { UIMapType = { Cosmic = 0, World = 1, Continent = 2, Zone = 3, Dungeon = 4, Micro = 5, Orphan = 6 } }
        function CreateVector2D(x, y) return { x = x, y = y } end
        maps = { [1433] = { mapType = 3, name = 'Redridge Mountains' }, [13] = { mapType = 2 }, [1429] = { mapType = 3 }, [1581] = { mapType = 4 } }
        C_Map = {
          GetMapInfo = function(id) return maps[id] end,
          GetWorldPosFromMapPos = function(id, pos) return 0, { x = 1000 + pos.x * 100, y = pos.y * 100 } end,
          GetMapPosFromWorldPos = function(continent, world, shown)
            if shown == 13 then return 13, { x = world.x / 2000, y = world.y / 2000 } end
            return shown, { x = world.x / 100, y = world.y / 100 } -- another zone: far off it
          end,
        }
        function Mixin(t, ...) for i = 1, select('#', ...) do for k, v in pairs((select(i, ...))) do t[k] = v end end return t end
        function CreateFromMixins(...) return Mixin({}, ...) end
        MapCanvasPinMixin = { CheckMouseButtonPassthrough = function() error('SetPassThroughButtons') end }
        MapCanvasDataProviderMixin = { OnAdded = function(self, map) self.map = map end, GetMap = function(self) return self.map end }
        acquired = {}
        WorldMapFrame = { mapID = 1433, hooks = {}, shown = false,
          AddDataProvider = function(self, p) self.provider = p; p:OnAdded(self) end,
          HookScript = function(self, event, fn) self.hooks[event] = fn end,
          IsShown = function(self) return self.shown end,
          GetMapID = function(self) return self.mapID end,
          RemoveAllPinsByTemplate = function() acquired = {} end,
          AcquirePin = function(self, template, friend, x, y) acquired[#acquired + 1] = { template = template, friend = friend, x = x, y = y } end,
        }
        """;

    static Script Load()
    {
        var script = new Script();
        script.DoString(AddonLuaTests.FakeTimers);
        script.DoString(Map);
        var ns = new Table(script);
        script.Globals["ns"] = ns;
        foreach (var file in new[] { "Leveling.lua", "MapPins.lua" })
            script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, file)), codeFriendlyName: file), "WoWCharacterTracker", ns);
        script.DoString("""
            opened = nil
            ns.setMapOpen = function(open) opened = open end
            ns.friendPins = function() return { { label = 'Zeb#2222', mapID = 1433, x = 0.5, y = 0.5, age = 3 } } end
            """);
        return script;
    }

    [Theory]
    [InlineData(1433, 0.5, 0.5)]   // the friend's own map
    [InlineData(13, 0.525, 0.025)] // the continent above it
    public void A_pin_lands_on_its_map_and_the_maps_above(int shown, double x, double y)
    {
        var script = Load();
        var spot = script.DoString($"return {{ ns.pinSpot({{ mapID = 1433, x = 0.5, y = 0.5 }}, {shown}) }}").Table;
        Assert.Equal(x, spot.Get(1).Number, 3);
        Assert.Equal(y, spot.Get(2).Number, 3);
    }

    [Theory]
    [InlineData(1429)] // another zone
    [InlineData(1581)] // a dungeon map
    [InlineData(999)]  // a map the game doesn't know
    public void A_pin_stays_off_other_zones_and_dungeon_maps(int shown) =>
        Assert.True(Load().DoString($"return ns.pinSpot({{ mapID = 1433, x = 0.5, y = 0.5 }}, {shown})").IsNil());

    [Fact]
    public void The_pins_join_the_world_map_and_say_when_it_opens()
    {
        var script = Load();
        Assert.True(script.DoString("return ns.attachMapPins()").Boolean);
        Assert.False(script.DoString("return ns.attachMapPins()").Boolean); // once
        script.DoString("WorldMapFrame.shown = true; WorldMapFrame.hooks.OnShow()");
        Assert.True(script.DoString("return opened").Boolean);
        script.DoString("WorldMapFrame.provider:RefreshAllData()");
        Assert.Equal(1, script.DoString("return #acquired").Number);
        Assert.Equal("WoWCharacterTrackerFriendPinTemplate", script.DoString("return acquired[1].template").String);
        Assert.Equal(0.5, script.DoString("return acquired[1].x").Number, 3);
        script.DoString("WorldMapFrame.mapID = 1581; WorldMapFrame.provider:RefreshAllData()");
        Assert.Equal(0, script.DoString("return #acquired").Number);
        script.DoString("WorldMapFrame.hooks.OnHide()");
        Assert.False(script.DoString("return opened").Boolean);
    }

    [Fact]
    public void The_test_pin_goes_next_to_you_and_away_again()
    {
        var script = Load();
        script.DoString("""
            ns.myMapPosition = function() return 1433, 0.4, 0.6 end
            function UnitClass() return 'Warrior', 'WARRIOR' end
            function UnitLevel() return 42 end
            ns.attachMapPins()
            WorldMapFrame.shown = true
            """);
        Assert.True(script.DoString("return ns.toggleTestPin()").Boolean);
        Assert.Equal(2, script.DoString("return #acquired").Number);
        Assert.Equal(0.43, script.DoString("return acquired[2].x").Number, 3);
        Assert.Equal("WARRIOR", script.DoString("return acquired[2].friend.classFile").String);
        Assert.False(script.DoString("return ns.toggleTestPin()").Boolean);
        Assert.Equal(1, script.DoString("return #acquired").Number);
        script.DoString("ns.myMapPosition = function() return nil end"); // in an instance
        Assert.True(script.DoString("return ns.toggleTestPin()").IsNil());
    }

    // The map's AcquirePin calls this on every pin; Blizzard's calls SetPassThroughButtons, ours nothing.
    [Fact]
    public void Our_pins_never_set_pass_through_buttons()
    {
        var script = Load();
        script.DoString("ns.attachMapPins()");
        script.DoString("WoWCharacterTrackerFriendPinMixin.CheckMouseButtonPassthrough({}, 'RightButton')");
    }

    [Fact]
    public void A_pin_shows_the_class_icon_where_the_game_has_it()
    {
        var script = Load();
        script.DoString("""
            function GetClassAtlas(class) return 'classicon-' .. class end
            C_Texture = { GetAtlasInfo = function(atlas) if atlas == 'classicon-warrior' then return { width = 64 } end end }
            """);
        Assert.Equal("classicon-warrior", script.DoString("return ns.pinIcon('WARRIOR')").String);
        Assert.True(script.DoString("return ns.pinIcon('EVOKER')").IsNil()); // not on this client: the colored dot
        Assert.True(script.DoString("return ns.pinIcon(nil)").IsNil());
        script.DoString("C_Texture = nil");
        Assert.True(script.DoString("return ns.pinIcon('WARRIOR')").IsNil());
    }

    // Hovering a pin: the character with what they share of it, the zone, the layer, the age.
    [Fact]
    public void A_pins_tooltip_tells_about_the_character_and_its_layer()
    {
        var script = Load();
        script.DoString("""
            function time() return 1000 end
            ns.fullName = function(c) return c.surname and (c.char .. ' ' .. c.surname) or c.char end
            ns.ago = function(at) return (1000 - at) .. 's ago' end
            RAID_CLASS_COLORS = { WARRIOR = { r = 0.78, g = 0.61, b = 0.43 } }
            function lines(f)
              local out = {}
              for _, l in ipairs(ns.pinLines(f, 'Felwood')) do out[#out + 1] = l[1] end
              return table.concat(out, '|')
            end
            shared = { char = 'Zeb', surname = 'Drygbeck', class = 'Warrior', spec = 'Arms', level = 54, ilvl = 50.9,
              xp = 4500, xpMax = 10000, maxLevel = 60, professions = 'Blacksmithing 162, Mining 145' }
            """);
        Assert.Equal("Zeb Drygbeck, level 54 Arms Warrior|Felwood|Item level 51  ·  45% into level 54|Blacksmithing 162, Mining 145|On another layer|3s ago",
            script.DoString("return lines({ character = 'Zeb', classFile = 'WARRIOR', level = 54, shared = shared, realmNote = \"On another layer\", age = 3 })").String);
        Assert.Equal("Zeb, level 12|Felwood|Last seen 40s ago", // nothing shared, no class name known
            script.DoString("return lines({ character = 'Zeb', level = 12, age = 40, faded = true })").String);
    }

    // Zone 1433 is 100 x 100 yards with east along world x and south along world y (see Map).
    [Theory]
    [InlineData(0.5, 0.5, 0.5, 0.1, 40, "north")]
    [InlineData(0.1, 0.9, 0.5, 0.5, 56.6, "northeast")]
    [InlineData(0.9, 0.5, 0.1, 0.5, 80, "west")]
    [InlineData(0.5, 0.1, 0.5, 0.9, 80, "south")]
    public void The_distance_is_measured_in_yards_with_a_compass_direction(double myX, double myY, double theirX, double theirY, double yards, string direction)
    {
        var r = Load().DoString(FormattableString.Invariant($"return {{ ns.friendDistance(1433, {myX}, {myY}, {{ mapID = 1433, x = {theirX}, y = {theirY} }}) }}")).Table;
        Assert.Equal(yards, r.Get(1).Number, 1);
        Assert.Equal(direction, r.Get(2).String);
    }

    [Fact]
    public void No_distance_to_another_continent()
    {
        var script = Load();
        script.DoString("maps[1945] = { mapType = 3 }; local w = C_Map.GetWorldPosFromMapPos; C_Map.GetWorldPosFromMapPos = function(id, pos) if id == 1945 then return 1, pos end return w(id, pos) end");
        Assert.True(script.DoString("return ns.friendDistance(1433, 0.5, 0.5, { mapID = 1945, x = 0.5, y = 0.5 })").IsNil());
    }

    [Theory]
    [InlineData(10, "meters", "Right next to you")]
    [InlineData(380, "meters", "About 350 m north")]
    [InlineData(1500, "meters", "About 1.4 km north")]
    [InlineData(380, "yards", "About 380 yd north")]
    [InlineData(2345, "yards", "About 2,350 yd north")]
    public void Distances_read_in_meters_or_yards(double yards, string unit, string text) =>
        Assert.Equal(text, Load().DoString(FormattableString.Invariant($"return ns.distanceText({yards}, 'north', '{unit}')")).String);

    // Auto: yards in the Americas region (1), meters elsewhere; a picked unit wins either way.
    [Theory]
    [InlineData(null, 1, "yards")]
    [InlineData(null, 3, "meters")]
    [InlineData("auto", 1, "yards")]
    [InlineData("meters", 1, "meters")]
    [InlineData("yards", 3, "yards")]
    public void The_unit_follows_the_setting_or_the_region(string? setting, int region, string unit)
    {
        var script = Load();
        script.DoString($"function GetCurrentRegion() return {region} end; ns.setting = function(key) if key == 'distanceUnit' then return {(setting is null ? "nil" : $"'{setting}'")} end end");
        Assert.Equal(unit, script.DoString("return ns.distanceUnit()").String);
    }

    [Fact]
    public void The_tooltip_says_how_far_only_on_your_continent()
    {
        var script = Load();
        script.DoString("""
            function time() return 1000 end
            ns.ago = function(at) return (1000 - at) .. 's ago' end
            ns.setting = function(key) if key == 'distanceUnit' then return 'yards' end end
            ns.myMapPosition = function() return 1433, 0.5, 0.9 end
            """);
        Assert.Equal("About 40 yd north", script.DoString("return ns.pinDistance({ mapID = 1433, x = 0.5, y = 0.5 })").String);
        Assert.Equal("Felwood|About 40 yd north", script.DoString("local l = ns.pinLines({ character = 'Zeb', level = 1, age = 0 }, 'Felwood', 'About 40 yd north'); return l[2][1] .. '|' .. l[3][1]").String);
        script.DoString("ns.myMapPosition = function() return nil end"); // you're in an instance
        Assert.True(script.DoString("return ns.pinDistance({ mapID = 1433, x = 0.5, y = 0.5 })").IsNil());
    }

    [Fact]
    public void Nothing_happens_without_a_world_map()
    {
        var script = Load();
        script.DoString("WorldMapFrame = nil");
        Assert.False(script.DoString("return ns.attachMapPins()").Boolean);
    }

    [Theory]
    [InlineData(10, "just now")]
    [InlineData(180, "3m ago")]
    [InlineData(7200, "2h ago")]
    [InlineData(90000, "1d ago")]
    public void Ages_read_like_the_window_shows_them(int seconds, string text)
    {
        var script = new Script();
        var ns = new Table(script);
        script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, "Shared.lua"))), "WoWCharacterTracker", ns);
        script.DoString("function time() return 100000 end");
        Assert.Equal(text, script.Call(ns.Get("ago"), 100000 - seconds).String);
    }
}
