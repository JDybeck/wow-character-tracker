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
    public void Key_runs_go_out_from_the_posts_key_level()
    {
        const string settings = "{ announce = { guild = true, kinds = { keyrun = true } }, post = { keyrun = { on = true, min = 10 } } }";
        Assert.Equal(["GUILD: Timed +12 Ara-Kara, City of Echoes (+2)!", "GUILD: Finished +11 The Rookery."],
            [.. Announce(settings, "{ kind = 'keyrun', instance = 'Ara-Kara, City of Echoes', keyLevel = 12, upgrades = 2 }"),
             .. Announce(settings, "{ kind = 'keyrun', instance = 'The Rookery', keyLevel = 11, upgrades = 0 }"),
             .. Announce(settings, "{ kind = 'keyrun', instance = 'The Rookery', keyLevel = 7, upgrades = 1 }")]);
    }

    // Events.lua on Retail: a finished key is recorded from the completion info (the table API, or
    // the older one with separate values), practice runs aren't, and end boss kills aren't either.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_finished_key_is_recorded_as_a_key_run(bool tableApi)
    {
        var script = new Script();
        script.DoString("""
            function time() return 5000 end
            -- One party member, whose spec the client knows (else the group says only the class).
            function IsInGroup() return true end
            function GetNumGroupMembers() return 2 end
            function UnitExists(u) return u == 'party1' end
            function UnitName(u) return 'Dreadus', 'ArgentDawn' end
            function UnitLevel() return 90 end
            function UnitClass() return 'Demon Hunter', 'DEMONHUNTER' end
            function UnitRace() return 'Night Elf' end
            function GetInspectSpecialization(u) return 577 end
            function GetSpecializationInfoByID(id) return id, 'Havoc' end
            C_ChallengeMode = { GetMapUIInfo = function(id) return id == 503 and 'Ara-Kara, City of Echoes' or nil end }
            """);
        script.DoString(tableApi
            ? "C_ChallengeMode.GetChallengeCompletionInfo = function() return { mapChallengeModeID = 503, level = 12, time = 1601500, onTime = true, keystoneUpgradeLevels = 2, practiceRun = practice, newOverallDungeonScore = 2847.6 } end"
            : "C_ChallengeMode.GetCompletionInfo = function() return 503, 12, 2400000, false, 0, practice, 2800, 2810 end");
        var ns = new Table(script);
        var recorded = new List<Table>();
        ns["say"] = (Action<string>)(_ => { });
        ns["characterInfo"] = (Func<Table>)(() => script.DoString("return { guid = 'Player-1', level = 90 }").Table);
        ns["addEvent"] = (Action<Table>)recorded.Add;
        ns["IS_RETAIL"] = true;
        foreach (var file in new[] { "Shared.lua", "Events.lua" })
            script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonDir, file))), "WoWCharacterTracker", ns);

        script.Call(ns.Get("recordKeyRun"));
        script.Call(ns.Get("recordBoss"), "Ragnaros"); // an old raid soloed on Retail: not a moment there
        script.DoString("practice = true");
        script.Call(ns.Get("recordKeyRun"));

        var e = Assert.Single(recorded);
        Assert.Equal("keyrun", e.Get("kind").String);
        Assert.Equal("Player-1:keyrun:503:5000", e.Get("id").String);
        Assert.Equal("Ara-Kara, City of Echoes", e.Get("instance").String);
        Assert.Equal(12, e.Get("keyLevel").Number);
        Assert.Equal(tableApi ? 2 : 0, e.Get("upgrades").Number);
        Assert.Equal(tableApi ? 1601 : 2400, e.Get("seconds").Number);
        Assert.Equal(tableApi ? 2847 : 2810, e.Get("rating").Number);
        Assert.Equal("Dreadus~90~DEMONHUNTER~Demon Hunter~Night Elf~ArgentDawn~Havoc", e.Get("group").String);
    }

    // Events.lua with the game's skill list stubbed (Classic style); the recorded events come back.
    static (Script Script, Table Ns, List<Table> Recorded) ProfessionScript()
    {
        var script = new Script();
        script.DoString("""
            function time() return 5000 end
            function UnitGUID() return 'Player-1' end
            skills = { { 'Mining', 140 }, { 'Herbalism', 70 } }
            function GetNumSkillLines() return #skills end
            function GetSkillLineInfo(i) return skills[i][1], false, nil, skills[i][2] end
            WoWCharacterTrackerDB = {}
            WoWCharacterTrackerSettings = { post = { professionSteps = { 75, 150, 225, 300 } } }
            """);
        var ns = new Table(script);
        var recorded = new List<Table>();
        ns["say"] = (Action<string>)(_ => { });
        foreach (var file in new[] { "Shared.lua", "Character.lua", "Events.lua" })
            script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonDir, file))), "WoWCharacterTracker", ns);
        ns["characterInfo"] = (Func<Table>)(() => script.DoString("return { guid = 'Player-1', level = 30 }").Table);
        ns["addEvent"] = (Action<Table>)recorded.Add;
        return (script, ns, recorded);
    }

    // The first reading is only the baseline; after that, passing a step records it once, with the
    // highest step passed when several were.
    [Fact]
    public void Profession_milestones_post_a_passed_step_once()
    {
        var (script, ns, recorded) = ProfessionScript();
        script.Call(ns.Get("checkProfessions")); // baseline: Mining 140, Herbalism 70
        Assert.Empty(recorded);

        script.DoString("skills = { { 'Mining', 151 }, { 'Herbalism', 230 }, { 'Skinning', 1 } }");
        script.Call(ns.Get("checkProfessions"));
        script.Call(ns.Get("checkProfessions")); // nothing new

        Assert.Equal(["Mining:150:151", "Herbalism:225:230"],
            recorded.Select(e => $"{e.Get("profession").String}:{e.Get("step").Number}:{e.Get("rating").Number}"));
        Assert.Equal("Player-1:profession:Mining:150", recorded[0].Get("id").String);
        Assert.Equal("profession", recorded[0].Get("kind").String);
    }

    // Shared.lua's stepPassed (profession and rating milestones): a step counts when the value goes
    // from below it to it or past it. Starting on a step, staying put or going down never counts.
    [Theory]
    [InlineData(199, 200, 200)]  // reaching the step
    [InlineData(150, 200, 200)]  // from the step below
    [InlineData(190, 210, 200)]  // jumping past it
    [InlineData(74, 75, 75)]
    [InlineData(0, 75, 75)]
    [InlineData(140, 310, 300)]  // past several: the highest
    [InlineData(200, 201, 0)]    // already on the step
    [InlineData(200, 200, 0)]
    [InlineData(201, 250, 0)]    // between steps
    [InlineData(199, 199, 0)]
    [InlineData(75, 76, 0)]
    [InlineData(210, 190, 0)]    // going down
    [InlineData(300, 400, 0)]    // past the last step
    public void A_step_counts_only_when_crossed_from_below(int from, int to, int expected)
    {
        var script = new Script();
        var ns = new Table(script);
        script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonDir, "Shared.lua"))), "WoWCharacterTracker", ns);
        var step = script.Call(ns.Get("stepPassed"), from, to, script.DoString("return { 75, 150, 200, 300 }"));
        Assert.Equal(expected, step.IsNil() ? 0 : (int)step.Number);
    }

    // The same through the profession check, skill-up by skill-up: one post per step reached,
    // nothing for the skill-ups after it.
    [Fact]
    public void Profession_skill_ups_post_only_when_a_step_is_reached()
    {
        var (script, ns, recorded) = ProfessionScript();
        script.DoString("WoWCharacterTrackerSettings.post.professionSteps = { 200, 300 }");
        foreach (var skill in new[] { 190, 199, 200, 201, 202, 250, 299, 300, 301, 310 })
        {
            script.DoString($"skills = {{ {{ 'Mining', {skill} }} }}");
            script.Call(ns.Get("checkProfessions"));
        }
        Assert.Equal(["200 at 200", "300 at 300"], recorded.Select(e => $"{e.Get("step").Number} at {e.Get("rating").Number}"));
    }

    // Loot remembers whether the item binds when equipped (GetItemInfo's 14th value), for the
    // "Bind on Equip only" rare rule.
    [Theory]
    [InlineData(2, true)]
    [InlineData(1, false)]
    public void Looted_rares_say_whether_they_bind_on_equip(int bindType, bool boe)
    {
        var script = new Script();
        script.DoString($$"""
            LOOT_ITEM_SELF = "You receive loot: %s."
            function time() return 5000 end
            function GetItemInfo(link) return 'Thrash Blade', link, 3, 53, 48, 'Weapon', 'Swords', 1, 'INVTYPE_WEAPON', 0, 0, 2, 7, {{bindType}} end
            """);
        var ns = new Table(script);
        var recorded = new List<Table>();
        ns["say"] = (Action<string>)(_ => { });
        foreach (var file in new[] { "Shared.lua", "Events.lua" })
            script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonDir, file))), "WoWCharacterTracker", ns);
        ns["characterInfo"] = (Func<Table>)(() => script.DoString("return { guid = 'Player-1', level = 50 }").Table);
        ns["addEvent"] = (Action<Table>)recorded.Add;

        script.Call(ns.Get("recordLoot"), "You receive loot: |cff0070dd|Hitem:17705::::::::50:::::|h[Thrash Blade]|h|r.");

        var e = Assert.Single(recorded);
        Assert.Equal(3, e.Get("quality").Number);
        Assert.Equal(boe, e.Get("boe").CastToBool());
    }

    // Loads Events.lua after the test's own Lua setup; recorded collects what it records.
    static (Script Script, Table Ns, List<Table> Recorded) LoadEvents(string setup)
    {
        var script = new Script();
        script.DoString("LOOT_ITEM_SELF = 'You receive loot: %s'\nfunction time() return now or 5000 end\n" + setup);
        var ns = new Table(script);
        var recorded = new List<Table>();
        ns["say"] = (Action<string>)(_ => { });
        foreach (var file in new[] { "Shared.lua", "Events.lua" })
            script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonDir, file))), "WoWCharacterTracker", ns);
        ns["characterInfo"] = (Func<Table>)(() => script.DoString("return { guid = 'Player-1', level = 50 }").Table);
        ns["addEvent"] = (Action<Table>)recorded.Add;
        return (script, ns, recorded);
    }

    // Forever's links name the quality ("|cnIQ4:") and a new drop isn't known to the game yet: it gets
    // a second look, and without an answer the quality comes from the link.
    [Fact]
    public void A_new_drop_on_forever_is_kept_with_the_quality_from_its_link()
    {
        var (script, ns, recorded) = LoadEvents("""
            function GetItemInfo() return nil end
            timers = {}
            C_Timer = { After = function(_, fn) table.insert(timers, fn) end }
            """);

        script.Call(ns.Get("recordLoot"), "You receive loot: |cnIQ4:|Hitem:17069::::::::|h[Sul'thraze]|h|r");
        Assert.Empty(recorded);
        script.DoString("timers[1]()");

        Assert.Equal(4, Assert.Single(recorded).Get("quality").Number);
    }

    // During a boss fight the client hands addons secret text, which can't be used at all.
    [Fact]
    public void Secret_chat_text_is_skipped()
    {
        var (script, ns, recorded) = LoadEvents("function issecretvalue() return true end");
        script.Call(ns.Get("recordLoot"), "You receive loot: |cnIQ4:|Hitem:17069::::::::|h[Sul'thraze]|h|r");
        Assert.Empty(recorded);
    }

    const string HardcoreDeath = """
        function GetMoney() return 7 end
        C_Timer = { After = function(_, fn) fn() end }
        """;

    static Table[] Die(string setup, int times = 1)
    {
        var (script, ns, recorded) = LoadEvents(HardcoreDeath + "\n" + setup);
        ns["isHardcore"] = (Func<bool>)(() => true);
        ns["playedNow"] = (Func<int>)(() => 100);
        ns["saveSnapshot"] = (Action)(() => { });
        for (var i = 0; i < times; i++) script.Call(ns.Get("recordDeath"));
        return [.. recorded];
    }

    [Fact]
    public void A_death_names_the_last_attacker_from_the_recap() =>
        Assert.Equal("Clattering Scorpid", Assert.Single(Die("""
            function DeathRecap_HasEvents() return true end
            function DeathRecap_GetEvents() return { { sourceName = 'Young Wolf' }, { sourceName = 'Clattering Scorpid' } } end
            """)).Get("killer").String);

    [Fact]
    public void A_death_to_the_world_says_what_it_was() =>
        Assert.Equal("Falling", Assert.Single(Die("""
            function DeathRecap_HasEvents() return true end
            function DeathRecap_GetEvents() return { { environmentalType = 'FALLING' } } end
            """)).Get("killer").String);

    [Fact]
    public void A_death_without_a_recap_still_counts() =>
        Assert.True(Assert.Single(Die("")).Get("killer").IsNil());

    [Fact]
    public void One_death_firing_twice_is_one_death() => Assert.Single(Die("", times: 2));

    [Fact]
    public void Feign_death_is_no_death() => Assert.Empty(Die("function UnitIsFeignDeath() return true end"));

    [Fact]
    public void A_death_line_names_the_killer() =>
        Assert.Equal(["GUILD: Fell at level 34 to Hogger. Rest in peace."],
            Announce("{ announce = { guild = true, kinds = { death = true } } }", "{ kind = 'death', level = 34, killer = 'Hogger' }"));

    // Loot that isn't a weapon (class 2) or armor (4) is marked, so a legendary one isn't always
    // posted. An unknown class counts as gear.
    [Theory]
    [InlineData("2", false)]
    [InlineData("4", false)]
    [InlineData("7", true)]
    [InlineData("nil", false)]
    public void Looted_items_that_cant_be_worn_are_marked(string classId, bool mat)
    {
        var script = new Script();
        script.DoString($$"""
            LOOT_ITEM_SELF = "You receive loot: %s."
            function time() return 5000 end
            function GetItemInfo(link) return 'Elementium Bar', link, 5 end
            function GetItemInfoInstant(link) return 17771, 'x', 'y', 'z', 'i', {{classId}}, 0 end
            """);
        var ns = new Table(script);
        var recorded = new List<Table>();
        ns["say"] = (Action<string>)(_ => { });
        foreach (var file in new[] { "Shared.lua", "Events.lua" })
            script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonDir, file))), "WoWCharacterTracker", ns);
        ns["characterInfo"] = (Func<Table>)(() => script.DoString("return { guid = 'Player-1', level = 50 }").Table);
        ns["addEvent"] = (Action<Table>)recorded.Add;

        script.Call(ns.Get("recordLoot"), "You receive loot: |cffff8000|Hitem:17771::::::::50:::::|h[Elementium Bar]|h|r.");

        Assert.Equal(mat, Assert.Single(recorded).Get("mat").CastToBool());
    }

    // Retail is told by the interface number, not by C_MythicPlus: Forever (16001) has that API too,
    // and must still record end boss kills; Retail (120105) records key runs instead.
    [Theory]
    [InlineData(16001, true)]
    [InlineData(120105, false)]
    public void End_boss_kills_are_recorded_on_forever_but_not_on_retail(int interfaceNumber, bool recorded)
    {
        var script = new Script();
        script.DoString($$"""
            function time() return 5000 end
            function GetBuildInfo() return "x", "1", "Oct 1 2026", {{interfaceNumber}} end
            C_MythicPlus = { GetOwnedKeystoneChallengeMapID = function() return nil end }
            """);
        var ns = new Table(script);
        var events = new List<Table>();
        ns["say"] = (Action<string>)(_ => { });
        ns["recentBosses"] = new Table(script);
        foreach (var file in new[] { "Shared.lua", "Character.lua", "Events.lua" })
            script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonDir, file))), "WoWCharacterTracker", ns);
        ns["characterInfo"] = (Func<Table>)(() => script.DoString("return { guid = 'Player-1', level = 20 }").Table);
        ns["addEvent"] = (Action<Table>)events.Add;

        script.Call(ns.Get("recordBoss"), "Edwin VanCleef");

        Assert.Equal(!recorded, ns.Get("IS_RETAIL").Boolean);
        if (!recorded) { Assert.Empty(events); return; }
        var e = Assert.Single(events);
        Assert.Equal("boss", e.Get("kind").String);
        Assert.Equal("The Deadmines", e.Get("instance").String);
    }

    // The stream's DUNGEON COMPLETE! tells a dungeon from a raid by the zone type the kill is recorded with.
    [Theory]
    [InlineData("party", "dungeon")]
    [InlineData("raid", "raid")]
    public void End_boss_kills_carry_the_zone_type(string instanceType, string zoneType)
    {
        var script = new Script();
        script.DoString($$"""
            function time() return 5000 end
            function GetBuildInfo() return "x", "1", "Oct 1 2026", 16001 end
            function IsInInstance() return true, "{{instanceType}}" end
            function UnitGUID() return "Player-1" end
            """);
        var ns = new Table(script);
        var events = new List<Table>();
        ns["say"] = (Action<string>)(_ => { });
        ns["recentBosses"] = new Table(script);
        foreach (var file in new[] { "Shared.lua", "Character.lua", "Events.lua" })
            script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonDir, file))), "WoWCharacterTracker", ns);
        ns["addEvent"] = (Action<Table>)events.Add;

        script.Call(ns.Get("recordBoss"), "Edwin VanCleef");

        Assert.Equal(zoneType, Assert.Single(events).Get("zoneType").String);
    }

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
            Assert.Contains("Saved history: newest 300", summary);
            Assert.False(script.DoString("return frames[2].checked").Boolean); // instant posting, off in the saved file
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // In game page, Keep everything: Settings.lua tells the addon not to trim its saved history.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Keep_all_history_reaches_the_addon(bool keepAll)
    {
        var dir = Path.Combine(Path.GetTempPath(), "wct-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Interface", "AddOns", "WoWCharacterTracker"));
        try
        {
            WoWCharacterTracker.Services.AddonInstaller.WriteSettings(dir, new FlavorSettings { KeepAllHistory = keepAll });
            var script = new Script();
            script.DoString(File.ReadAllText(Path.Combine(dir, "Interface", "AddOns", "WoWCharacterTracker", "Settings.lua")));
            var ns = new Table(script);
            script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonDir, "Shared.lua"))), "WoWCharacterTracker", ns);
            Assert.Equal(keepAll, script.Call(ns.Get("setting"), "keepAllHistory").CastToBool());
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
        foreach (var file in new[] { "Shared.lua", "Character.lua", "Inventory.lua" })
            script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonDir, file))), "WoWCharacterTracker", ns);
        var result = script.Call(ns.Get("scanGearEffects")).Tuple;
        Assert.Equal("Baleful Grave-Knight's Battlegear~2~5~0~Heart Strike deals 10% more damage.\n"
            + "Baleful Grave-Knight's Battlegear~2~5~4~Your Bone Shield lasts longer.", result[0].String);
        Assert.Equal("5~1005~Equip: Your attacks have a chance to heal you.", result[1].String);
        Assert.Equal("5~1005~Crystalline Radiance~7980", result[2].String); // the enchant id from the item link
    }

    // ns.trimEvents: the newest 300 loot, duels and battlegrounds per character stay, in order; level-ups,
    // boss kills and other characters' events aren't touched.
    [Fact]
    public void Saved_events_keep_the_newest_300_loot_duels_and_battlegrounds_per_character()
    {
        var script = new Script();
        var ns = new Table(script);
        script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonDir, "Shared.lua"))), "WoWCharacterTracker", ns);
        script.Globals["trim"] = ns.Get("trimEvents");
        var result = script.DoString("""
            local events = {}
            local function add(guid, kind, at) events[#events + 1] = { guid = guid, kind = kind, at = at } end
            for i = 1, 350 do add("A", "loot", i) end
            for i = 1, 310 do add("A", "duel", 1000 + i) end
            for i = 1, 20 do add("A", "level", 2000 + i) end
            for i = 1, 400 do add("A", "boss", 3000 + i) end
            for i = 1, 5 do add("B", "loot", 4000 + i) end
            add("A", "loot", 5000) -- the newest loot
            local removed = trim(events)
            local count, inOrder, oldestLoot = {}, true, nil
            for i, e in ipairs(events) do
              local key = e.guid .. e.kind
              count[key] = (count[key] or 0) + 1
              if i > 1 and e.at < events[i - 1].at then inOrder = false end
              if e.guid == "A" and e.kind == "loot" and not oldestLoot then oldestLoot = e.at end
            end
            return removed, count.Aloot, count.Aduel, count.Alevel, count.Aboss, count.Bloot, inOrder, oldestLoot, trim(events)
            """).Tuple;
        Assert.Equal(51 + 10, result[0].Number); // 351 loot and 310 duels down to 300 each
        Assert.Equal(300, result[1].Number);
        Assert.Equal(300, result[2].Number);
        Assert.Equal(20, result[3].Number);
        Assert.Equal(400, result[4].Number); // boss kills are all kept
        Assert.Equal(5, result[5].Number);
        Assert.True(result[6].Boolean);
        Assert.Equal(52, result[7].Number); // loot 1-51 went, the newest stayed
        Assert.Equal(0, result[8].Number); // nothing more to trim the next time
    }

    // ns.saveSnapshot at logout, where the game reports 0 of 0 XP: the saved bar stays, but only one
    // from the same level; a fresh level's 0 XP with a real bar is saved as it is.
    [Theory]
    [InlineData(11, 0, 0, 3000, 7600)] // logout: keep the bar
    [InlineData(12, 0, 0, 0, 0)] // dinged since: the old level's bar doesn't carry over
    [InlineData(11, 0, 7600, 0, 7600)] // just dinged: 0 XP is real
    public void Logout_keeps_the_xp_bar_of_the_same_level(int level, int xp, int xpMax, int savedXp, int savedXpMax)
    {
        var script = new Script();
        script.DoString("""
            function time() return 1000 end
            function UnitGUID() return "Player-1" end
            WoWCharacterTrackerDB = { xpTable = {}, chars = { ["Player-1"] = { level = 11, xp = 3000, xpMax = 7600 } } }
            """);
        var ns = new Table(script);
        ns["doing"] = (Action<string>)(_ => { });
        foreach (var file in new[] { "Shared.lua", "Character.lua" })
            script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonDir, file))), "WoWCharacterTracker", ns);
        ns["playedNow"] = (Func<double>)(() => 5000);
        ns["characterInfo"] = (Func<Table>)(() => script.DoString($"return {{ guid = 'Player-1', level = {level}, xp = {xp}, xpMax = {xpMax} }}").Table);

        script.Call(ns.Get("saveSnapshot"));

        var saved = script.DoString("return WoWCharacterTrackerDB.chars['Player-1']").Table;
        Assert.Equal(savedXp, saved.Get("xp").Number);
        Assert.Equal(savedXpMax, saved.Get("xpMax").Number);
    }

    // ns.debounce: many calls while it waits are one run, and it can run again after.
    [Fact]
    public void Debounce_runs_once_for_a_burst()
    {
        var script = new Script();
        script.DoString("""
            timers, runs = {}, 0
            C_Timer = { After = function(_, fn) timers[#timers + 1] = fn end }
            """);
        var ns = new Table(script);
        script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonDir, "Shared.lua"))), "WoWCharacterTracker", ns);
        script.Globals["soon"] = script.Call(ns.Get("debounce"), 3, script.DoString("return function() runs = runs + 1 end"));
        script.DoString("for _ = 1, 19 do soon() end"); // a whole equipment set
        Assert.Equal(1, script.DoString("return #timers").Number);
        script.DoString("timers[1]()");
        Assert.Equal(1, script.DoString("return runs").Number);
        script.DoString("soon()");
        Assert.Equal(2, script.DoString("return #timers").Number);
    }

    // Bag changes while looting don't read the gear's tooltips again: only new gear (another link), a
    // new spec, or five minutes passing does.
    [Fact]
    public void Gear_tooltips_are_only_read_again_when_the_gear_changes()
    {
        var script = new Script();
        script.DoString("""
            now, reads, chest = 1000, 0, "|Hitem:1005:7980|h"
            function time() return now end
            function UnitGUID() return "Player-1" end
            function InCombatLockdown() return false end
            function GetInventoryItemID(_, slot) return slot == 5 and 1005 or nil end
            function GetInventoryItemLink(_, slot) return slot == 5 and chest or nil end
            C_TooltipInfo = { GetInventoryItem = function() reads = reads + 1; return { lines = { { leftText = "Equip: Heals." } } } end }
            WoWCharacterTrackerDB = {}
            """);
        var ns = new Table(script);
        ns["doing"] = (Action<string>)(_ => { });
        foreach (var file in new[] { "Shared.lua", "Character.lua", "Inventory.lua" })
            script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonDir, file))), "WoWCharacterTracker", ns);
        double Reads() => script.Globals.Get("reads").Number;

        script.Call(ns.Get("scanInventory"));
        Assert.Equal(1, Reads());
        script.DoString("now = now + 10");
        script.Call(ns.Get("scanInventory")); // looting: same gear
        Assert.Equal(1, Reads());
        script.DoString("chest = '|Hitem:1005:9999|h'"); // a new enchant
        script.Call(ns.Get("scanInventory"));
        Assert.Equal(2, Reads());
        script.DoString("now = now + 300");
        script.Call(ns.Get("scanInventory"));
        Assert.Equal(3, Reads());
        Assert.Equal("5~1005~Equip: Heals.", script.DoString("return WoWCharacterTrackerDB.items['Player-1'].effects").String);
    }

    // Inventory.lua's scanEquipSets over a stubbed C_EquipmentSet: where each piece is comes from the
    // location's bits (bank and bag items carry the player bit too), or the game's sentinels.
    [Fact]
    public void Equipment_sets_are_saved_with_where_each_piece_is()
    {
        var script = new Script();
        script.DoString("""
            local PLAYER, BAGS, BANK = 0x00100000, 0x00200000, 0x00400000
            function GetInventoryItemID(_, slot) return slot == 1 and 1001 or nil end
            C_EquipmentSet = {
              GetEquipmentSetIDs = function() return { 3, 7 } end,
              GetEquipmentSetInfo = function(id)
                if id == 3 then return "ret", 134400, 3, true end
                return "prot\nline", 132624, 7, false
              end,
              GetItemIDs = function(id)
                if id == 3 then return { [1] = 1001, [4] = 1 } end
                return { [1] = 2001, [5] = 2005, [16] = 2016, [17] = 2017, [18] = 0 }
              end,
              GetItemLocations = function(id)
                if id == 3 then return { [1] = PLAYER + 1, [4] = 1 } end
                return { [1] = PLAYER + BAGS + 2 * 256 + 4, [5] = PLAYER + BANK + 12, [16] = PLAYER + BANK + BAGS + 6 * 256 + 1, [17] = -1, [18] = 0 }
              end,
            }
            """);
        var ns = new Table(script);
        foreach (var file in new[] { "Shared.lua", "Inventory.lua" })
            script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonDir, file))), "WoWCharacterTracker", ns);
        Assert.Equal("134400~1~1:1001:equipped,4:0:ignored~ret\n"
            + "132624~0~1:2001:bags,5:2005:bank,16:2016:bank,17:2017:missing,18:0:empty~prot line",
            script.Call(ns.Get("scanEquipSets")).String);

        script.DoString("C_EquipmentSet = nil");
        Assert.True(script.Call(ns.Get("scanEquipSets")).IsNil()); // keeps what the saved file had
    }
}
