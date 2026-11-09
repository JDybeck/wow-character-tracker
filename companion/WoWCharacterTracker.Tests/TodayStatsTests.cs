using MoonSharp.Interpreter;

namespace WoWCharacterTracker.Tests;

// Home's TODAY row: the addon's counters (Today.lua) and the ranking, the same in Lua and C#.
public class TodayStatsTests
{
    static readonly DateTime Now = new(2026, 11, 9, 15, 0, 0);
    static long At(int hour) => new DateTimeOffset(Now.Date.AddHours(hour)).ToUnixTimeSeconds();

    static (Script Script, Table Ns) Load()
    {
        var script = new Script();
        script.DoString("""
            WoWCharacterTrackerDB = {}
            clock = 100
            lootSource = 'GameObject-0-1-1-1-1-1'
            fishing = false
            function date() return '2026-11-09' end
            function UnitGUID() return 'Player-1-0000000A' end
            function GetTime() return clock end
            function IsFishingLoot() return fishing end
            function GetLootSourceInfo() return lootSource end
            local names = { [2366] = 'Herb Gathering', [2575] = 'Mining', [8613] = 'Skinning', [2576] = 'Mining', [133] = 'Fireball' }
            C_Spell = { GetSpellName = function(id) return names[id] end }
            """);
        var ns = new Table(script);
        foreach (var file in new[] { "Shared.lua", "Today.lua" })
            script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, file))), "WoWCharacterTracker", ns);
        script.Globals["ns"] = ns;
        return (script, ns);
    }

    [Fact]
    public void A_vein_mined_twice_counts_once_and_other_casts_dont_count()
    {
        var (script, _) = Load();
        script.DoString("""
            ns.onSpellcast('player', 'c1', 2576) ns.onLootOpened()            -- a vein
            clock = 110 ns.onSpellcast('player', 'c2', 2576) ns.onLootOpened() -- the same vein again
            clock = 120 lootSource = 'GameObject-0-1-1-1-1-2'
            ns.onLootOpened() ns.onSpellcast('player', 'c3', 2366)            -- a herb, loot seen first
            clock = 130 ns.onSpellcast('player', 'c4', 133) ns.onLootOpened() -- a fireball and a mob's loot
            ns.onSpellcast('party1', 'c5', 2366)                              -- someone else
            fishing = true ns.onLootOpened() ns.onLootOpened()                -- two fish
            ns.todayCount('quests')
            """);
        var t = script.DoString("return WoWCharacterTrackerDB.today['Player-1-0000000A']").Table;
        Assert.Equal((1, 1, 0, 2, 1), ((int)t.Get("ore").Number, (int)t.Get("herbs").Number, (int)t.Get("skins").CastToNumber().GetValueOrDefault(),
            (int)t.Get("fish").Number, (int)t.Get("quests").Number));
    }

    [Fact]
    public void A_new_day_starts_over_and_the_first_snapshot_is_the_start()
    {
        var (script, _) = Load();
        script.DoString("""
            WoWCharacterTrackerDB.today = { ['Player-1-0000000A'] = { day = '2026-11-08', quests = 9, ilvlStart = 20 } }
            ns.todaySnapshot({ guid = 'Player-1-0000000A', ilvl = 30, money = 5000 })
            ns.todaySnapshot({ guid = 'Player-1-0000000A', ilvl = 32.5, money = 25000 })
            """);
        var t = script.DoString("return WoWCharacterTrackerDB.today['Player-1-0000000A']").Table;
        Assert.True(t.Get("quests").IsNil());
        Assert.Equal((30.0, 32.5, 5000.0, 25000.0), (t.Get("ilvlStart").Number, t.Get("ilvl").Number, t.Get("moneyStart").Number, t.Get("money").Number));
    }

    // Main stats at 0 only fill up to four; above 0 goes first, in the same order and wording both sides.
    [Fact]
    public void The_addon_and_the_app_rank_today_the_same()
    {
        var (script, _) = Load();
        script.DoString($$"""
            events = {
              { kind = 'level', at = {{At(10)}} }, { kind = 'level', at = {{At(11)}} },
              { kind = 'loot', quality = 4, at = {{At(12)}} },
              { kind = 'level', at = {{At(-5)}} }, -- yesterday
            }
            today = {
              a = { day = '2026-11-09', ilvlStart = 30, ilvl = 32.5, moneyStart = 10000, money = 135000, quests = 7, herbs = 3 },
              b = { day = '2026-11-09', ore = 2, moneyStart = 50000, money = 10000 }, -- spent: no gold
              c = { day = '2026-11-08', quests = 50 },
            }
            stats = ns.todayStats(events, today, '2026-11-09', {{At(0)}}, false)
            out = {}
            for _, s in ipairs(stats) do out[#out + 1] = ns.todayValue(s) .. ' ' .. s.name end
            """);
        var lua = script.DoString("return table.concat(out, ', ')").String;

        var events = new List<Entry>
        {
            new() { Id = "1", Kind = "level", At = At(10) }, new() { Id = "2", Kind = "level", At = At(11) },
            new() { Id = "3", Kind = "loot", Quality = 4, At = At(12) }, new() { Id = "4", Kind = "level", At = At(-5) },
        };
        var days = new List<DayTotals>
        {
            new("a", "2026-11-09", 30, 32.5, 10000, 135000, 7, 3, 0, 0, 0),
            new("b", "2026-11-09", 0, 0, 50000, 10000, 0, 0, 2, 0, 0),
            new("c", "2026-11-08", 0, 0, 0, 0, 50, 0, 0, 0, 0),
        };
        var app = string.Join(", ", TodayStats.Build(events, days, new HashSet<string>(), keys: false, Now).Select(s => $"{s.Value} {s.What}"));

        Assert.Equal("2 levels, 1 epics, +2.5 item level, 7 quests, 12g gold, 3 herbs, 2 veins", lua);
        Assert.Equal(lua, app);
    }

    [Fact]
    public void A_quiet_day_shows_the_four_main_stats()
    {
        var stats = TodayStats.Build([], [new("a", "2026-11-09", 0, 0, 0, 0, 2, 0, 0, 0, 0)], new HashSet<string>(), keys: true, Now);
        Assert.Equal(["2 quests", "0 levels", "0 keys", "0 epics"], stats.Select(s => $"{s.Value} {s.What}"));
    }

    [Fact]
    public void The_saved_days_are_read()
    {
        var script = new Script();
        var days = SavedVariables.ReadToday(script.DoString("return { ['Player-1-A'] = { day = '2026-11-09', ilvl = 31.5, money = 99, herbs = 4 }, bad = 5 }"));
        var d = Assert.Single(days);
        Assert.Equal(("Player-1-A", "2026-11-09", 31.5, 99L, 4), (d.Guid, d.Day, d.Ilvl, d.Money, d.Herbs));
    }
}
