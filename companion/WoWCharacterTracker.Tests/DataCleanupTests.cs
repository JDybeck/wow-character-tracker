using MoonSharp.Interpreter;
using WoWCharacterTracker.Services;

namespace WoWCharacterTracker.Tests;

// The Data page's cleanups: the app writes an order into Settings.lua, the addon applies it once
// (Cleanup.lua applyCleanups), and until then the app hides the same things (DataCleanup.Hide).
public class DataCleanupTests
{
    const string A = "Player-1-0000000A"; // removed
    const string B = "Player-1-0000000B"; // stays, old history goes

    const string Db = $$"""
        { chars = {
            ['{{A}}'] = { guid = '{{A}}', char = 'Gone', realm = 'Firemaw', level = 10, lastSeen = 1000 },
            ['{{B}}'] = { guid = '{{B}}', char = 'Stays', realm = 'Firemaw', level = 20, lastSeen = 5000 } },
          items = { ['{{A}}'] = { gear = '1~2' }, ['{{B}}'] = { gear = '1~3' } },
          notes = { ['{{A}}'] = { text = 'x', at = 1 } },
          start = { ['{{A}}'] = 1 }, logoutPlayed = { ['{{A}}'] = 2 },
          recipes = { ['{{A}}'] = { Mining = 'x' } }, runs = { ['{{A}}'] = { id = 1 } },
          today = { ['{{A}}'] = { day = '2026-11-01' }, ['{{B}}'] = { day = '2026-11-14', quests = 3 } },
          days = { ['{{B}}'] = { ['2026-10-01'] = { quests = 1 }, ['2026-11-10'] = { quests = 2 } } },
          events = {
            { id = 'a1', guid = '{{A}}', char = 'Gone', kind = 'level', level = 10, at = 900 },
            { id = 'b1', guid = '{{B}}', char = 'Stays', kind = 'loot', at = 100 },
            { id = 'b2', guid = '{{B}}', char = 'Stays', kind = 'loot', at = 4000 },
            { id = 'b3', guid = '{{B}}', char = 'Stays', kind = 'level', level = 20, at = 100 },
            { id = 'b4', guid = '{{B}}', char = 'Stays', kind = 'ask', askedKind = 'loot', at = 100 } },
          friends = { ['Zeb#2222'] = { chars = {
            ['Player-1-0000000F'] = { char = 'Old', at = 100 },
            ['Player-1-00000010'] = { char = 'New', at = 4000 } } } } }
        """;

    static readonly CleanupOrder Order = new()
    {
        At = 2000, Chars = [A], Before = 1000, Kinds = ["loot"], BeforeDay = "2026-11-05", FriendsBefore = 1000,
    };

    // The addon with Settings.lua as the app writes it for these orders.
    static Script Addon(params CleanupOrder[] orders)
    {
        var dir = Path.Combine(Path.GetTempPath(), "wct-cleanup-" + Guid.NewGuid().ToString("N"));
        var addon = Path.Combine(dir, "Interface", "AddOns", "WoWCharacterTracker");
        Directory.CreateDirectory(addon);
        try
        {
            AddonInstaller.WriteSettings(dir, new FlavorSettings { Cleanups = [.. orders] });
            var script = new Script();
            script.DoString(File.ReadAllText(Path.Combine(addon, "Settings.lua")));
            script.DoString("WoWCharacterTrackerDB = " + Db);
            var ns = new Table(script);
            script.Globals["ns"] = ns;
            foreach (var file in new[] { "Shared.lua", "Cleanup.lua" })
                script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, file))), "WoWCharacterTracker", ns);
            return script;
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    static double Apply(Script s) => s.DoString("return ns.applyCleanups(WoWCharacterTrackerDB, ns.setting('cleanup'))").Number;
    static bool Gone(Script s, string expr) => s.DoString("return " + expr).IsNil();

    [Fact]
    public void The_addon_removes_what_the_order_says_once()
    {
        var s = Addon(Order);
        Assert.Equal(1, Apply(s));
        foreach (var table in new[] { "chars", "items", "notes", "start", "logoutPlayed", "recipes", "runs", "today" })
            Assert.True(Gone(s, $"WoWCharacterTrackerDB.{table}['{A}']"), table);
        Assert.False(Gone(s, $"WoWCharacterTrackerDB.chars['{B}']"));
        Assert.False(Gone(s, $"WoWCharacterTrackerDB.items['{B}']"));
        Assert.Equal("b2,b3", s.DoString("local ids = {} for _, e in ipairs(WoWCharacterTrackerDB.events) do ids[#ids + 1] = e.id end return table.concat(ids, ',')").String);
        Assert.False(Gone(s, $"WoWCharacterTrackerDB.today['{B}']")); // today is newer than the cutoff
        Assert.True(Gone(s, $"WoWCharacterTrackerDB.days['{B}']['2026-10-01']"));
        Assert.False(Gone(s, $"WoWCharacterTrackerDB.days['{B}']['2026-11-10']"));
        Assert.True(Gone(s, "WoWCharacterTrackerDB.friends['Zeb#2222'].chars['Player-1-0000000F']"));
        Assert.False(Gone(s, "WoWCharacterTrackerDB.friends['Zeb#2222'].chars['Player-1-00000010']"));
        Assert.Equal(2000, s.DoString("return WoWCharacterTrackerDB.cleanedAt").Number);

        // Played again afterwards: the next load doesn't remove it a second time.
        s.DoString($"WoWCharacterTrackerDB.chars['{A}'] = {{ char = 'Gone', lastSeen = 3000 }}");
        Assert.Equal(0, Apply(s));
        Assert.False(Gone(s, $"WoWCharacterTrackerDB.chars['{A}']"));
    }

    // Removed while being played: saved at the /reload after the order, so it stays as it is now and
    // starts fresh, like the app shows it (A_removed_character_played_again_...).
    [Fact]
    public void The_addon_keeps_a_character_played_since_with_only_whats_newer()
    {
        var day = DataCleanup.CharsDay(Order);
        var s = Addon(Order);
        s.DoString($$"""
            local db = WoWCharacterTrackerDB
            db.chars['{{A}}'].lastSeen = 3000
            db.days['{{A}}'] = { ['{{day}}'] = { quests = 1 }, ['2099-01-01'] = { quests = 2 } }
            db.today['{{A}}'] = { day = '{{day}}' }
            table.insert(db.events, { id = 'a2', guid = '{{A}}', kind = 'level', level = 11, at = 2500 })
            """);
        Assert.Equal(1, Apply(s));
        Assert.False(Gone(s, $"WoWCharacterTrackerDB.chars['{A}']"));
        Assert.False(Gone(s, $"WoWCharacterTrackerDB.start['{A}']")); // the session's level timing stays
        Assert.Equal("b2,b3,a2", s.DoString("local ids = {} for _, e in ipairs(WoWCharacterTrackerDB.events) do ids[#ids + 1] = e.id end return table.concat(ids, ',')").String);
        Assert.True(Gone(s, $"WoWCharacterTrackerDB.days['{A}']['{day}']"));
        Assert.False(Gone(s, $"WoWCharacterTrackerDB.days['{A}']['2099-01-01']"));
        Assert.True(Gone(s, $"WoWCharacterTrackerDB.today['{A}']"));
    }

    [Fact]
    public void Only_orders_newer_than_the_last_one_done_apply()
    {
        var s = Addon(Order, Order with { At = 6000, Chars = [B], Before = 0, Kinds = [], BeforeDay = "", FriendsBefore = 0 });
        s.DoString("WoWCharacterTrackerDB.cleanedAt = 2000");
        Assert.Equal(1, Apply(s));
        Assert.False(Gone(s, $"WoWCharacterTrackerDB.chars['{A}']")); // the first was done already
        Assert.True(Gone(s, $"WoWCharacterTrackerDB.chars['{B}']"));
        Assert.Equal(6000, s.DoString("return WoWCharacterTrackerDB.cleanedAt").Number);
    }

    [Fact]
    public void Without_orders_nothing_changes()
    {
        var s = Addon();
        Assert.Equal(0, Apply(s));
        Assert.Equal(5, s.DoString("return #WoWCharacterTrackerDB.events").Number);
        Assert.True(Gone(s, "WoWCharacterTrackerDB.cleanedAt"));
    }

    // The same saved file, read by the app: Hide leaves what the addon leaves.
    static TrackerData Read()
    {
        var file = Path.Combine(Path.GetTempPath(), "wct-cleanup-" + Guid.NewGuid().ToString("N") + ".lua");
        File.WriteAllText(file, "WoWCharacterTrackerDB = " + Db);
        try { return SavedVariables.Read(file); }
        finally { File.Delete(file); }
    }

    [Fact]
    public void The_app_hides_what_the_addon_removes()
    {
        var shown = DataCleanup.Hide(Read(), [Order]);
        Assert.Equal(["b2", "b3"], shown.Events.Select(e => e.Id).Order());
        Assert.Equal([B], shown.Characters.Select(c => c.Guid).Distinct());
        Assert.DoesNotContain(shown.Inventories, i => i.Guid == A);
        Assert.Equal([B], shown.Today.Select(d => d.Guid));
        Assert.Equal(["2026-11-10"], shown.Days.Select(d => d.Day));
    }

    [Fact]
    public void A_removed_character_played_again_shows_with_only_whats_newer()
    {
        var data = Read();
        var day = DataCleanup.CharsDay(Order);
        data = data with
        {
            Characters = [.. data.Characters.Select(c => c.Guid == A ? c with { LastSeen = 3000 } : c)],
            Events = [.. data.Events, new Entry { Id = "a2", Guid = A, Kind = "level", Level = 11, At = 2500 }],
            Days = [.. data.Days, new DayTotals(A, day, 0, 0, 0, 0, 1, 0, 0, 0, 0), new DayTotals(A, "2099-01-01", 0, 0, 0, 0, 2, 0, 0, 0, 0)],
        };
        var shown = DataCleanup.Hide(data, [Order]);
        Assert.Contains(shown.Characters, c => c.Guid == A);
        Assert.Contains(shown.Events, e => e.Id == "a2");
        Assert.DoesNotContain(shown.Events, e => e.Id == "a1");
        // Its old days stay hidden, so the app's day history can't keep them again.
        Assert.Equal(["2099-01-01"], shown.Days.Where(d => d.Guid == A).Select(d => d.Day));
    }

    [Fact]
    public void Friends_characters_heard_from_before_the_cutoff_are_hidden()
    {
        Snapshot Friend(string guid, long at) => Read().Characters[0] with { Guid = guid, LastSeen = at };
        var contact = new FriendContact("Zeb#2222", 0, "", 0, false, [Friend("F-old", 100), Friend("F-new", 4000)], [], [])
        {
            Days = [new DayTotals("F-old", "2026-11-01", 0, 0, 0, 0, 1, 0, 0, 0, 0)],
        };
        var shown = DataCleanup.Hide(TrackerData.Empty with { Friends = [contact] }, [new CleanupOrder { At = 5000, FriendsBefore = 1000 }]);
        Assert.Equal(["F-new"], shown.Friends[0].Characters.Select(c => c.Guid));
        Assert.Empty(shown.Friends[0].Days);
    }

    [Fact]
    public void An_order_stays_until_every_account_has_done_it()
    {
        CleanupOrder[] orders = [Order, Order with { At = 3000 }];
        Assert.Equal(2, DataCleanup.StillPending(orders, 0).Count);
        Assert.Equal([3000L], DataCleanup.StillPending(orders, 2000).Select(o => o.At));
        Assert.Empty(DataCleanup.StillPending(orders, 3000));
    }

    [Fact]
    public void The_count_matches_what_goes()
    {
        var data = Read();
        var (chars, events, days, _) = DataCleanup.Count(data, data.Days, Order);
        Assert.Equal(1, chars);
        Assert.Equal(3, events); // a1 (the removed character's), b1 and the ask about loot
        Assert.Equal(1, days);   // 2026-10-01
    }
}
