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
}
