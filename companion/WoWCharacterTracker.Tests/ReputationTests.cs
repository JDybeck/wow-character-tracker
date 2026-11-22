using MoonSharp.Interpreter;
using WoWCharacterTracker.Services.Discord;

namespace WoWCharacterTracker.Tests;

// Reputations: the addon's Reputation.lua (what it saves and when a milestone is recorded), the app's
// reader (Reputations.cs) and what the milestone looks like in chat, on Discord and in highlights.
public class ReputationTests
{
    static string AddonDir => AddonLuaTests.AddonDir;

    // Shared.lua and Reputation.lua with the game's faction list stubbed; the recorded events come back.
    // setup: the faction API (legacy GetFactionInfo or Retail's C_Reputation), as Lua.
    static (Script Script, Table Ns, List<Table> Recorded) RepScript(string setup)
    {
        var script = new Script();
        script.DoString("""
            function time() return 5000 end
            guid = 'Player-1'
            function UnitGUID() return guid end
            WoWCharacterTrackerDB = { events = {}, chars = { ['Player-1'] = { char = 'Zeb' } } }
            WoWCharacterTrackerSettings = { post = {} }
            """);
        script.DoString(setup);
        var ns = new Table(script);
        var recorded = new List<Table>();
        foreach (var file in new[] { "Shared.lua", "Reputation.lua" })
            script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonDir, file))), "WoWCharacterTracker", ns);
        ns["characterInfo"] = (Func<Table>)(() => script.DoString("return { guid = guid, level = 30 }").Table);
        ns["addEvent"] = (Action<Table>)(e =>
        {
            recorded.Add(e);
            script.Globals.Get("WoWCharacterTrackerDB").Table.Get("events").Table.Append(DynValue.NewTable(e));
        });
        return (script, ns, recorded);
    }

    // Classic-style GetFactionInfo (name, description, standing, barMin, barMax, value, ..., isHeader,
    // isCollapsed, hasRep, ..., factionID), with the Alliance group collapsed in the game's list.
    const string LegacyFactions = """
        factions = {
          { 'Classic', 0, 0, 0, 0, true, false, 1118 },
          { 'Argent Dawn', 6, 9000, 21000, 12000, false, true, 529 },
          { 'Alliance', 0, 0, 0, 0, true, false, 469 },
        }
        hidden = { { 'Stormwind', 7, 21000, 42000, 41000, false, true, 72 } }
        open = false
        folded = 0
        local function rows()
          local out = {}
          for _, f in ipairs(factions) do
            out[#out + 1] = f
            if f[1] == 'Alliance' and open then for _, h in ipairs(hidden) do out[#out + 1] = h end end
          end
          return out
        end
        function GetNumFactions() return #rows() end
        function GetFactionInfo(i)
          local f = rows()[i]
          if not f then return nil end
          return f[1], '', f[2], f[3], f[4], f[5], false, false, f[6], f[1] == 'Alliance' and not open, f[7], false, false, f[8]
        end
        function ExpandFactionHeader(i) if rows()[i][1] == 'Alliance' then open = true end end
        function CollapseFactionHeader(i) if rows()[i][1] == 'Alliance' then open = false; folded = folded + 1 end end
        """;

    static List<string> Milestones(List<Table> recorded) =>
        [.. recorded.Select(e => $"{e.Get("reputation").String}:{(e.Get("step").IsNil() ? "renown " + e.Get("renown").Number : e.Get("step").Number.ToString())}")];

    // The first reading is only the baseline, read through collapsed groups (folded again after) and
    // saved with the character. Then reaching Exalted records it once; lower standings only when the
    // app's Reputation from says so.
    [Fact]
    public void Reaching_exalted_is_recorded_once_and_old_standing_is_only_the_baseline()
    {
        var (script, ns, recorded) = RepScript(LegacyFactions);
        script.Call(ns.Get("checkReputations"));
        Assert.Empty(recorded);
        Assert.Equal("529~6~3000~12000~~Argent Dawn\n72~7~20000~21000~~Stormwind",
            script.DoString("return WoWCharacterTrackerDB.chars['Player-1'].reputations").String);
        Assert.False(script.Globals.Get("open").Boolean); // the group the player had folded is folded again
        Assert.Equal(1, (int)script.Globals.Get("folded").Number);

        script.DoString("hidden[1][2] = 8; hidden[1][5] = 42000; factions[2][2] = 7; factions[2][5] = 21000");
        script.Call(ns.Get("checkReputations"));
        script.Call(ns.Get("checkReputations")); // nothing new
        Assert.Equal(["Stormwind:8"], Milestones(recorded)); // Revered isn't a milestone by default
        Assert.Equal("Player-1:reputation:72:8", recorded[0].Get("id").String);
        Assert.Equal(72, (int)recorded[0].Get("factionId").Number);

        recorded.Clear();
        script.DoString("WoWCharacterTrackerSettings.post.reputationFrom = 6; factions[2][2] = 8");
        script.Call(ns.Get("checkReputations"));
        Assert.Equal(["Argent Dawn:8"], Milestones(recorded));
    }

    // From Honored, passing several standings at once records the highest; one already recorded
    // (Exalted again after a dip) isn't recorded twice.
    [Fact]
    public void A_jump_records_the_highest_standing_and_each_standing_once()
    {
        var (script, ns, recorded) = RepScript(LegacyFactions);
        script.DoString("WoWCharacterTrackerSettings.post.reputationFrom = 6; factions[2][2] = 5");
        script.Call(ns.Get("checkReputations"));
        script.DoString("factions[2][2] = 8");
        script.Call(ns.Get("checkReputations"));
        script.DoString("factions[2][2] = 7");
        script.Call(ns.Get("checkReputations"));
        script.DoString("factions[2][2] = 8");
        script.Call(ns.Get("checkReputations"));
        Assert.Equal(["Argent Dawn:8"], Milestones(recorded));
    }

    // Retail's C_Reputation: renown factions record their top renown, friendships never record (they
    // have ranks, not standings), and warband factions keep one baseline for the account, so a second
    // character doesn't post what the first already did.
    const string RetailFactions = """
        list = {
          { factionID = 2590, name = 'Council of Dornogal', reaction = 8, currentReactionThreshold = 0, nextReactionThreshold = 1, currentStanding = 1 },
          { factionID = 2640, name = 'Brann Bronzebeard', reaction = 5, currentReactionThreshold = 0, nextReactionThreshold = 10, currentStanding = 5 },
          { factionID = 2600, name = 'The Severed Threads', reaction = 7, currentReactionThreshold = 21000, nextReactionThreshold = 42000, currentStanding = 30000 },
          { name = 'Guild', isHeader = true, factionID = 1168 },
        }
        renown = { [2590] = 24 }
        warband = { [2590] = true, [2600] = true }
        C_Reputation = {
          GetNumFactions = function() return #list end,
          GetFactionDataByIndex = function(i) return list[i] end,
          IsMajorFaction = function(id) return renown[id] ~= nil end,
          IsAccountWideReputation = function(id) return warband[id] == true end,
        }
        C_MajorFactions = {
          GetMajorFactionData = function(id) return { renownLevel = renown[id], renownReputationEarned = 500, renownLevelThreshold = 2500 } end,
          GetRenownLevels = function(id) local out = {} for l = 1, 25 do out[l] = { level = l } end return out end,
        }
        C_GossipInfo = {
          GetFriendshipReputation = function(id)
            if id ~= 2640 then return { friendshipFactionID = 0 } end
            return { friendshipFactionID = 2640, reaction = 'Best Friend', standing = 45000, reactionThreshold = 42000, nextThreshold = 50000 }
          end,
          GetFriendshipReputationRanks = function(id) return { currentLevel = 3, maxLevel = 6 } end,
        }
        """;

    [Fact]
    public void Retail_renown_friendships_and_warband_factions()
    {
        var (script, ns, recorded) = RepScript(RetailFactions);
        script.Call(ns.Get("checkReputations"));
        Assert.Empty(recorded);
        Assert.Equal("2590~8~500~2500~R24/25~Council of Dornogal\n2640~5~3000~8000~F3/6:Best Friend~Brann Bronzebeard\n2600~7~9000~21000~~The Severed Threads",
            script.DoString("return WoWCharacterTrackerDB.chars['Player-1'].reputations").String);

        script.DoString("renown[2590] = 25; list[2].reaction = 8; list[3].reaction = 8");
        script.Call(ns.Get("checkReputations"));
        Assert.Equal(["Council of Dornogal:renown 25", "The Severed Threads:8"], Milestones(recorded));
        Assert.Equal("Player-1:reputation:2590:renown", recorded[0].Get("id").String);

        // Another character on the account: the warband factions are already there.
        recorded.Clear();
        script.DoString("guid = 'Player-2'");
        script.Call(ns.Get("checkReputations"));
        Assert.Empty(recorded);
    }

    // The saved text, read back by the addon (its window) and the app, the same way.
    const string Saved = "2590~8~120~2500~R12/25~Council of Dornogal\n2640~5~10~100~F3/6:Best Friend~Brann\nnot a line\n529~6~3000~12000~~Argent Dawn\n72~8~999~1000~~Stormwind";

    [Fact]
    public void The_addon_reads_its_text_back_the_same()
    {
        var (script, ns, _) = RepScript("");
        var list = script.Call(ns.Get("parseReputations"), Saved);
        Assert.Equal(4, list.Table.Length);
        Assert.Equal(Saved.Replace("not a line\n", ""), script.Call(ns.Get("encodeReputations"), list).String);
    }

    [Fact]
    public void The_app_reads_the_saved_text()
    {
        var list = Reputations.Parse(Saved);
        Assert.Equal(["Council of Dornogal", "Brann", "Argent Dawn", "Stormwind"], list.Select(r => r.Name));
        Assert.Equal(new Reputation(2590, "Council of Dornogal", 8, 120, 2500, Renown: 12, RenownMax: 25), list[0]);
        Assert.Equal(new Reputation(2640, "Brann", 5, 10, 100, Rank: 3, RankMax: 6, Label: "Best Friend"), list[1]);
        Assert.Equal(["Renown 12", "Best Friend", "Honored", "Exalted"], list.Select(r => r.StandingText));
        Assert.Equal(["120 / 2500", "10 / 100", "3000 / 12000", ""], list.Select(r => r.ProgressText)); // nothing to fill at Exalted
        Assert.Equal(0.25, list[2].Fraction);
        Assert.Equal(1, list[3].Fraction);
        Assert.Empty(Reputations.Parse(""));
        Assert.Empty(Reputations.Parse(null));
    }

    // Renown first, then the highest standing, then the name: in the app and in the addon's window.
    [Fact]
    public void Renown_comes_first_then_the_highest_standing()
    {
        Assert.Equal(["Council of Dornogal", "Stormwind", "Argent Dawn", "Brann"], Reputations.Sorted(Reputations.Parse(Saved)).Select(r => r.Name));

        var (script, ns, _) = RepScript("");
        script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonDir, "ReputationWindow.lua"))), "WoWCharacterTracker", ns);
        var sorted = script.Call(ns.Get("sortedReputations"), script.Call(ns.Get("parseReputations"), Saved)).Table;
        Assert.Equal(["Council of Dornogal", "Stormwind", "Argent Dawn", "Brann"], sorted.Values.Select(v => v.Table.Get("name").String));
        Assert.Equal(4, (int)script.Call(ns.Get("reputationCount"), script.DoString($"return {{ reputations = [[{Saved}]] }}")).Number);
    }

    // The saved file's character carries its reputations to the app's character row.
    [Fact]
    public void Saved_reputations_reach_the_character_row()
    {
        var file = Path.Combine(Path.GetTempPath(), "wct-rep-" + Guid.NewGuid().ToString("N") + ".lua");
        File.WriteAllText(file, """
            WoWCharacterTrackerDB = { chars = { ["Player-1-0000001"] = {
              char = "Zeb", realm = "R", level = 60, class = "Warrior", classFile = "WARRIOR", race = "Human", lastSeen = 100,
              reputations = "529~6~3000~12000~~Argent Dawn\n72~8~999~1000~~Stormwind",
            } } }
            """);
        try
        {
            var row = CharacterRow.Build(SavedVariables.Read(file), Flavors.Forever, new GameLookup(Flavors.Forever, () => { })).Single();
            Assert.True(row.HasReputations);
            Assert.Equal(["Stormwind", "Argent Dawn"], row.ReputationsShown.Select(r => r.Name));
            Assert.Equal("Honored, 3000 / 12000", row.ReputationList[1].Tooltip);
            Assert.False(row.HasMoreReputations);
        }
        finally { File.Delete(file); }
    }

    // The milestone's words: in chat (the addon), in highlights and on Discord (the app).
    [Theory]
    [InlineData("{ kind = 'reputation', reputation = 'Argent Dawn', factionId = 529, step = 8 }", "Reached Exalted with Argent Dawn!")]
    [InlineData("{ kind = 'reputation', reputation = 'Timbermaw Hold', factionId = 576, step = 7 }", "Reached Revered with Timbermaw Hold!")]
    [InlineData("{ kind = 'reputation', reputation = 'Council of Dornogal', factionId = 2590, renown = 25 }", "Reached Renown 25 with Council of Dornogal!")]
    public void The_chat_line_names_the_standing(string eventLua, string line)
    {
        var script = new Script();
        var ns = new Table(script);
        foreach (var file in new[] { "Shared.lua", "Announce.lua" })
            script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonDir, file))), "WoWCharacterTracker", ns);
        Assert.Equal(line, script.Call(ns.Get("momentLine"), script.DoString("return " + eventLua)).String);
    }

    [Fact]
    public void The_app_posts_and_highlights_the_milestone()
    {
        var e = new Entry { Kind = "reputation", Char = "Zeb", Level = 60, Zone = "Eastern Plaguelands", Reputation = "Argent Dawn", FactionId = 529, Step = 8 };
        Assert.Equal("Reached Exalted with Argent Dawn", HighlightText.Text(e));
        var embed = new EventEmbeds(Flavors.Forever).Build(e, isTest: false);
        Assert.Equal("\U0001F91D Zeb reached Exalted with Argent Dawn!", embed["title"]);
        Assert.Contains("**Exalted** with **[Argent Dawn](https://www.wowhead.com/forever/faction=529)**.", (string)embed["description"]);

        var renown = e with { Reputation = "Council of Dornogal", FactionId = 2590, Step = 0, Renown = 25 };
        Assert.Equal("Reached Renown 25 with Council of Dornogal", HighlightText.Text(renown));
        Assert.True(new FlavorSettings().Wants(e));
        Assert.False(new FlavorSettings { PostReputations = false }.Wants(e));
    }
}
