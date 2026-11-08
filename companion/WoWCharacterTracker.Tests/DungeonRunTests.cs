using MoonSharp.Interpreter;
using WoWCharacterTracker.Services.Discord;
using WoWCharacterTracker.Services.LiveChannel;

namespace WoWCharacterTracker.Tests;

// Dungeon runs summed up in one post (Runs.lua and the app's "run" events).
public class DungeonRunTests
{
    // A dungeon with one party member (a tank), a clock (now) and the app's "Dungeon runs" on. (No stack
    // variants of the loot lines: MoonSharp's matcher gives up on them with item links, the game's Lua doesn't.)
    const string Dungeon = """
        now = 5000
        function time() return now end
        LOOT_ITEM_SELF = 'You receive loot: %s.'
        LOOT_ITEM = '%s receives loot: %s.'
        WoWCharacterTrackerSettings = { post = { run = true, boss = true, epic = true, rare = true } }
        WoWCharacterTrackerDB = { events = {} }
        where = 'The Deadmines'
        function UnitGUID() return 'Player-1' end
        function IsInInstance() if where then return true, 'party' end return false, 'none' end
        function GetInstanceInfo() return where end
        function IsInGroup() return true end
        function GetNumGroupMembers() return 2 end
        function UnitExists(u) return u == 'party1' end
        function UnitName() return 'Tanky' end
        function UnitClass() return 'Warrior', 'WARRIOR' end
        function UnitRace() return 'Dwarf' end
        function UnitLevel() return 20 end
        function UnitGroupRolesAssigned() return 'TANK' end
        function GetItemInfo(link) return nil, nil, tonumber(link:match('cnIQ(%d)')) end
        function CreateFrame() return { SetScript = function() end } end
        timers = {}
        C_Timer = { After = function(_, fn) table.insert(timers, fn) end }
        """;

    static (Script Script, Table Ns, List<Table> Recorded) Load(string setup = "")
    {
        var script = new Script();
        script.DoString(Dungeon + "\n" + setup);
        var ns = new Table(script);
        var recorded = new List<Table>();
        ns["say"] = (Action<string>)(_ => { });
        ns["recentBosses"] = new Table(script);
        foreach (var file in new[] { "Shared.lua", "LiveGrid.lua", "Events.lua", "Runs.lua" })
            script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, file))), "WoWCharacterTracker", ns);
        ns["characterInfo"] = (Func<Table>)(() => script.DoString("return { guid = 'Player-1', level = 20 }").Table);
        ns["addEvent"] = (Action<Table>)recorded.Add;
        return (script, ns, recorded);
    }

    static Table RunOf(List<Table> recorded) => Assert.Single(recorded, e => e.Get("kind").String == "run");

    [Fact]
    public void A_cleared_dungeon_is_one_run_with_its_bosses_party_and_loot()
    {
        var (script, ns, recorded) = Load();
        script.Call(ns.Get("checkRun")); // the loading screen into the dungeon
        script.DoString("now = 5600");
        script.Call(ns.Get("recordBoss"), "Rhahk'Zor");
        script.Call(ns.Get("runLoot"), "Tanky receives loot: |cnIQ3:|Hitem:872::::::::|h[Rockslicer]|h|r.");
        script.Call(ns.Get("runLoot"), "Tanky receives loot: |cnIQ1:|Hitem:2589::::::::|h[Linen Cloth]|h|r."); // too plain
        script.DoString("now = 7000");
        script.Call(ns.Get("recordBoss"), "Edwin VanCleef");
        var loot = "You receive loot: |cnIQ4:|Hitem:5191::::::::|h[Cruel Barb]|h|r.";
        script.Call(ns.Get("recordLoot"), loot);
        script.Call(ns.Get("runLoot"), loot);
        Assert.DoesNotContain(recorded, e => e.Get("kind").String == "run");

        script.DoString("now = 7200");
        script.DoString("timers[#timers]()"); // a few minutes after the end boss

        var run = RunOf(recorded);
        Assert.Equal("The Deadmines", run.Get("instance").String);
        Assert.Equal(2000, run.Get("seconds").Number); // in to the end boss
        Assert.Equal("Rhahk'Zor^Edwin VanCleef", run.Get("bosses").String);
        Assert.Equal("Tanky~872~3~Rockslicer^~5191~4~Cruel Barb", run.Get("drops").String);
        Assert.True(run.Get("cleared").Boolean);
        Assert.Equal("Tanky~20~WARRIOR~Warrior~Dwarf~~~TANK", run.Get("group").String);
        Assert.Equal("Player-1:run:5000", run.Get("id").String);

        // The end boss and the loot belong to the run, and with runs on they wait for its post.
        var boss = Assert.Single(recorded, e => e.Get("kind").String == "boss");
        var drop = Assert.Single(recorded, e => e.Get("kind").String == "loot");
        Assert.Equal(5000, boss.Get("run").Number);
        Assert.Equal(5000, drop.Get("run").Number);
        Assert.False(script.Call(ns.Get("wanted"), boss).Boolean);
        Assert.False(script.Call(ns.Get("wanted"), drop).Boolean);
        Assert.True(script.Call(ns.Get("wanted"), run).Boolean);
    }

    [Fact]
    public void A_corpse_run_is_still_the_same_run()
    {
        var (script, ns, recorded) = Load("dead = false function UnitIsDeadOrGhost() return dead end");
        script.Call(ns.Get("checkRun"));
        script.Call(ns.Get("recordBoss"), "Rhahk'Zor");
        script.DoString("where = nil dead = true now = 5300");
        script.Call(ns.Get("checkRun")); // released and ran back as a ghost
        script.DoString("where = 'The Deadmines' dead = false now = 5500");
        script.Call(ns.Get("checkRun"));
        script.Call(ns.Get("recordBoss"), "Edwin VanCleef");
        script.DoString("where = nil now = 5600");
        script.Call(ns.Get("checkRun")); // out after the end boss: done

        Assert.Equal("Rhahk'Zor^Edwin VanCleef", RunOf(recorded).Get("bosses").String);
    }

    [Fact]
    public void Leaving_without_the_end_boss_ends_the_run_after_a_while()
    {
        var (script, ns, recorded) = Load();
        script.Call(ns.Get("checkRun"));
        script.Call(ns.Get("recordBoss"), "Rhahk'Zor");
        script.DoString("where = nil now = 5100");
        script.Call(ns.Get("checkRun"));
        Assert.DoesNotContain(recorded, e => e.Get("kind").String == "run");

        script.DoString("now = 6100");
        script.DoString("timers[#timers]()");
        var run = RunOf(recorded);
        Assert.True(run.Get("cleared").IsNil());
        Assert.False(script.Call(ns.Get("wanted"), run).Boolean); // one boss and no loot: not worth a post
    }

    [Fact]
    public void Nothing_is_tracked_with_runs_off()
    {
        var (script, ns, recorded) = Load("WoWCharacterTrackerSettings.post.run = false");
        script.Call(ns.Get("checkRun"));
        script.Call(ns.Get("recordBoss"), "Edwin VanCleef");
        Assert.True(Assert.Single(recorded).Get("run").IsNil());
        Assert.True(script.DoString("return WoWCharacterTrackerDB.runs['Player-1'] == nil").Boolean);
    }

    [Fact]
    public void A_run_reads_from_the_live_channel()
    {
        var e = Payload.Parse("i=:run:5000;g=1403-0A1B2C3D;k=run;n=Zeb;I=The Deadmines;d=2000;cl=1;bs=Rhahk'Zor^Edwin VanCleef;dr=Tanky~872~3~Rockslicer^~5191~4~Cruel Barb;P=Tanky~20~WARRIOR~Warrior~Dwarf~~~TANK")!;
        Assert.True(e.Cleared);
        Assert.Equal(["Rhahk'Zor", "Edwin VanCleef"], e.BossList);
        Assert.Equal([new RunDrop("Tanky", 872, 3, "Rockslicer"), new RunDrop("", 5191, 4, "Cruel Barb")], e.DropList);
        Assert.Equal("TANK", Assert.Single(e.Members).Role);
    }

    // With runs on, a run's kills and loot wait for its post (legendaries still post on their own);
    // the addon's wanted() agrees.
    [Theory]
    [InlineData("boss", 0, 5000, false)]
    [InlineData("boss", 0, 0, true)]
    [InlineData("loot", 4, 5000, false)]
    [InlineData("loot", 4, 0, true)]
    [InlineData("loot", 5, 5000, true)]
    public void A_runs_kills_and_loot_wait_for_its_post(string kind, int quality, long run, bool posted)
    {
        var s = new FlavorSettings { PostRuns = true, PostBosses = true, PostEpic = true, FirstBossKillOnly = false };
        var e = new Entry { Kind = kind, Level = 20, Quality = quality, Run = run };
        Assert.Equal(posted, s.Wants(e));
        Assert.Equal(posted, SharedRulesTests.AddonWants(s, e));
    }

    [Theory]
    [InlineData(true, "", true)]
    [InlineData(false, "~5191~4~Cruel Barb", true)]
    [InlineData(false, "", false)]
    public void Runs_post_when_cleared_or_with_loot(bool cleared, string drops, bool posted)
    {
        var s = new FlavorSettings { PostRuns = true };
        var e = new Entry { Kind = "run", Level = 20, Cleared = cleared, Drops = drops };
        Assert.Equal(posted, s.Wants(e));
        Assert.Equal(posted, SharedRulesTests.AddonWants(s, e));
        s.PostRuns = false;
        Assert.False(s.Wants(e));
        Assert.False(SharedRulesTests.AddonWants(s, e));
    }

    [Fact]
    public void The_post_lists_the_bosses_the_party_with_roles_and_who_got_what()
    {
        var e = new Entry
        {
            Kind = "run", Char = "Zeb", Level = 20, Race = "Human", Class = "Priest", ClassFile = "PRIEST", Role = "HEALER",
            Instance = "The Deadmines", Seconds = 2000, Cleared = true, Bosses = "Rhahk'Zor^Edwin VanCleef",
            Drops = "Tanky~872~3~Rockslicer^~5191~4~Cruel Barb", Group = "Tanky~20~WARRIOR~Warrior~Dwarf~~~TANK", GroupSize = 2,
        };
        var embed = new EventEmbeds(Flavors.Forever).Build(e, isTest: false);
        Assert.Equal("Zeb cleared The Deadmines!", embed["title"]);
        var text = (string)embed["description"];
        Assert.Contains("Cleared in **33m 20s**, 2 bosses down.", text);
        Assert.Contains("Rhahk'Zor, Edwin VanCleef", text);
        Assert.Contains("\U0001F6E1️ Tanky, 20 Dwarf Warrior", text);
        Assert.Contains("\U0001F49A Zeb, 20 Human Priest", text);
        Assert.Contains("Tanky: [Rockslicer](https://www.wowhead.com/forever/item=872)", text);
        Assert.Contains("Zeb: [Cruel Barb](https://www.wowhead.com/forever/item=5191)", text);
    }
}
