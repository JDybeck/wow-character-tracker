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
        script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonDir, "Announce.lua"))), "WoWCharacterTracker", ns);
        script.Call(ns.Get("announce"), script.DoString("return " + eventLua));
        return sent;
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
    public void Nothing_for_losses_kinds_left_out_or_no_channel()
    {
        Assert.Empty(Announce(GuildOnly, "{ kind = 'duel', opponent = 'Grimtusk', lost = true }"));
        Assert.Empty(Announce(GuildOnly, "{ kind = 'mount', mount = 'Swift Palomino' }"));
        Assert.Empty(Announce("{ announce = { guild = false, party = false, kinds = { level = true } } }", "{ kind = 'level', level = 5 }"));
        Assert.Empty(Announce("{ }", "{ kind = 'level', level = 5 }")); // Settings.lua from an older app
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
            C_TooltipInfo = { GetInventoryItem = function(_, slot)
              local lines = {}
              for i, t in ipairs(tips[slot] or {}) do lines[i] = { leftText = t } end
              return { lines = lines }
            end }
            """);
        var ns = new Table(script);
        foreach (var file in new[] { "Character.lua", "Inventory.lua" })
            script.Call(script.LoadString(File.ReadAllText(Path.Combine(AddonDir, file))), "WoWCharacterTracker", ns);
        var result = script.Call(ns.Get("scanGearEffects")).Tuple;
        Assert.Equal("Baleful Grave-Knight's Battlegear~2~5~0~Heart Strike deals 10% more damage.\n"
            + "Baleful Grave-Knight's Battlegear~2~5~4~Your Bone Shield lasts longer.", result[0].String);
        Assert.Equal("5~1005~Equip: Your attacks have a chance to heal you.", result[1].String);
        Assert.Equal("5~1005~Crystalline Radiance", result[2].String);
    }
}
