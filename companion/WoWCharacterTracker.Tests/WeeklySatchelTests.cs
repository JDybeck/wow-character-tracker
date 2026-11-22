using MoonSharp.Interpreter;
using WoWCharacterTracker.Services;
using WoWCharacterTracker.Services.Discord;
using WoWCharacterTracker.Services.Streaming;

namespace WoWCharacterTracker.Tests;

// Forever's weekly In Demand quest (Booty Bay) and its Undermine Satchel (Weekly.lua): opening the
// satchel records what came out, what it sells for and the week's quest; the app posts it.
public class WeeklySatchelTests
{
    // A game with an Undermine Satchel in the bags, a loot window holding a Gold Ring and some bars,
    // and the In Demand reward panel. bags/loot/source are globals the test changes.
    const string Game = """
        function time() return now or 5000 end
        function UnitGUID() return 'Player-1' end
        WoWCharacterTrackerDB = { events = {} }
        bags = { [271994] = 1 }
        function GetItemCount(id) return bags[id] or 0 end
        prices = { [272001] = 1000000, [12359] = 500 }
        names = { [271994] = 'Undermine Satchel' }
        function GetItemInfo(x)
          local id = type(x) == 'number' and x or tonumber(x:match('item:(%d+)'))
          return names[id], nil, 2, nil, nil, nil, nil, nil, nil, nil, prices[id]
        end
        loot = { '|cff1eff00|Hitem:272001::::::::|h[Gold Ring]|h|r', '|cffffffff|Hitem:12359::::::::|h[Thorium Bar]|h|r' }
        quantities = { 1, 3 }
        source = 'Item-1-0-ABC'
        function GetNumLootItems() return #loot end
        function GetLootSlotLink(i) return loot[i] end
        function GetLootSlotInfo(i) return 'icon', 'name', quantities[i] end
        function GetLootSourceInfo() return source end
        questId, title = 96075, 'In Demand - Blackrock Legal Records'
        function GetQuestID() return questId end
        function GetTitleText() return title end
        function GetNumQuestRewards() return 1 end
        function GetNumQuestChoices() return 0 end
        function GetQuestItemLink() return '|Hitem:' .. (rewardId or 271994) .. '::|h[Satchel]|h' end
        """;

    static (Script Script, Table Ns, List<Table> Recorded, List<string> Said) Load(string setup = "")
    {
        var script = new Script();
        script.DoString(Game + "\n" + setup);
        var ns = new Table(script);
        var said = new List<string>();
        ns["say"] = (Action<string>)said.Add;
        foreach (var file in new[] { "Shared.lua", "Events.lua", "Weekly.lua" })
            script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, file))), "WoWCharacterTracker", ns);
        var recorded = new List<Table>();
        ns["characterInfo"] = (Func<Table>)(() => script.DoString("return { guid = 'Player-1', level = 60 }").Table);
        ns["addEvent"] = (Action<Table>)recorded.Add;
        return (script, ns, recorded, said);
    }

    static void TurnIn(Script script, Table ns)
    {
        script.Call(ns.Get("onQuestComplete"));
        script.Call(ns.Get("onWeeklyTurnedIn"), script.Globals.Get("questId"));
    }

    static void Open(Script script, Table ns)
    {
        script.Call(ns.Get("onWeeklyLootOpened"));
        script.Call(ns.Get("checkWeeklySatchel")); // the window is open, the satchel still there
        script.DoString("bags[271994] = (bags[271994] or 0) - 1");
        script.Call(ns.Get("checkWeeklySatchel"));
    }

    [Fact]
    public void Opening_the_satchel_records_the_valuable_its_price_and_the_quest()
    {
        var (script, ns, recorded, said) = Load();
        TurnIn(script, ns);
        script.Call(ns.Get("onWeeklyLootOpened"));
        script.Call(ns.Get("checkWeeklySatchel"));
        Assert.Empty(recorded); // not before the satchel is gone from the bags

        script.DoString("bags[271994] = 0");
        script.Call(ns.Get("checkWeeklySatchel"));

        var e = Assert.Single(recorded);
        Assert.Equal("weekly", e.Get("kind").String);
        Assert.Equal("Gold Ring", e.Get("item").String);
        Assert.Equal(272001, e.Get("itemId").Number);
        Assert.Equal(1000000, e.Get("sell").Number);
        Assert.Equal("In Demand - Blackrock Legal Records", e.Get("quest").String);
        Assert.Equal("Undermine Satchel", e.Get("bag").String);
        Assert.Equal("Thorium Bar x3", e.Get("extras").String);
        Assert.Equal("Weekly satchel: |cff1eff00|Hitem:272001::::::::|h[Gold Ring]|h|r, sells for 100g.", Assert.Single(said));
        // The quest is used up: the next satchel (another week) doesn't name it.
        script.DoString("bags[271994] = 1");
        Open(script, ns);
        Assert.Equal(DataType.Nil, recorded[1].Get("quest").Type);
    }

    // Another container (a clam, a lockbox) while a satchel is in the bags, or a corpse: nothing.
    [Fact]
    public void Other_loot_while_holding_a_satchel_is_not_it()
    {
        var (script, ns, recorded, _) = Load();
        script.Call(ns.Get("onWeeklyLootOpened"));
        script.Call(ns.Get("checkWeeklySatchel"));
        Assert.Empty(recorded);

        script.DoString("source = 'Creature-0-1-0-0-1234-0'");
        Open(script, ns);
        Assert.Empty(recorded);
    }

    // A week's quest with a title the addon doesn't list, and a satchel with another id: the title's
    // "In Demand" and the reward panel's item still make it the weekly. The price comes from the
    // game, else the known one (an item the game doesn't have yet).
    [Fact]
    public void A_new_week_with_an_unknown_satchel_still_counts()
    {
        var (script, ns, recorded, _) = Load("""
            questId, title, rewardId = 99999, 'In Demand - Something New', 300000
            bags = { [300000] = 1 }
            names[300000] = 'Undermine Satchel'
            prices = {}
            """);
        TurnIn(script, ns);
        script.Call(ns.Get("onWeeklyLootOpened"));
        script.DoString("bags[300000] = 0");
        script.Call(ns.Get("checkWeeklySatchel"));

        var e = Assert.Single(recorded);
        Assert.Equal("In Demand - Something New", e.Get("quest").String);
        Assert.Equal(1000000, e.Get("sell").Number); // the Gold Ring's known price
    }

    [Fact]
    public void Other_quests_are_not_remembered()
    {
        var (script, ns, _, _) = Load("questId, title = 123, 'The Defias Brotherhood'");
        TurnIn(script, ns);
        Assert.Equal(DataType.Nil, script.DoString("return (WoWCharacterTrackerDB.weekly or {})['Player-1']").Type);
    }

    [Theory]
    [InlineData(1000000, "100g")]
    [InlineData(125000, "12g 50s")]
    [InlineData(7500, "75s")]
    [InlineData(42, "42c")]
    public void Prices_read_like_a_vendor(long copper, string text)
    {
        Assert.Equal(text, Format.Price(copper));
        var (script, ns, _, _) = Load();
        Assert.Equal(text, script.Call(ns.Get("goldText"), copper).String);
    }

    static readonly Entry Ring = new()
    {
        Kind = "weekly", Char = "Zeb", Item = "Gold Ring", ItemId = 272001, Quality = 2, Sell = 1_000_000,
        Quest = "In Demand - Blackrock Legal Records", Bag = "Undermine Satchel", Extras = "Thorium Bar x3^Black Lotus", At = 1,
    };

    [Fact]
    public void The_discord_post_names_the_item_its_price_and_the_quest()
    {
        var post = new EventEmbeds(Flavors.Forever).Build(Ring, isTest: false);
        Assert.Equal("\U0001F4B0 Zeb opened the Undermine Satchel: Gold Ring (100g)!", post["title"]);
        var text = (string)post["description"];
        Assert.Contains("Out came **[Gold Ring](https://www.wowhead.com/", text);
        Assert.Contains("it sells for **100g**.", text);
        Assert.Contains("The reward for **In Demand - Blackrock Legal Records** in Booty Bay.", text);
        Assert.Contains("Also inside: Thorium Bar x3, Black Lotus.", text);
        Assert.Equal(("item", 272001), EventEmbeds.WowheadThing(Ring));

        var bare = new EventEmbeds(Flavors.Forever).Build(Ring with { Sell = 0, Quest = "", Bag = "", Extras = "" }, isTest: false, itemPicture: true);
        Assert.Equal("\U0001F4B0 Zeb opened the weekly satchel: Gold Ring!", bare["title"]);
        Assert.Equal("The reward for this week's In Demand quest in Booty Bay.", bare["description"]);
    }

    [Fact]
    public void It_is_posted_by_its_own_switch_and_reads_from_the_saved_file()
    {
        var s = Flavors.Forever.Defaults();
        Assert.True(s.Wants(Ring));
        s.PostWeekly = false;
        Assert.False(s.Wants(Ring));
        Assert.Contains("weekly", FlavorSettings.DefaultAnnounceKinds);
        Assert.Contains(Moments.All, m => m.Key == "weekly" && m.Alert == "weekly");
        Assert.Equal("WEEKLY LOOT!", StreamSnapshot.AlertFor(Ring)?.Title);
    }
}
