using System.Globalization;
using MoonSharp.Interpreter;

namespace WoWCharacterTracker.Tests;

// Sharing with Battle.net friends (FriendData.lua, Comms.lua, Friends.lua) in MoonSharp, with
// Battle.net stubbed. Two clients can talk: what one sends with SendGameData is handed to the
// other's BN_CHAT_MSG_ADDON handler.
public class FriendsLuaTests
{
    const string Stubs = """
        function time() return math.floor(clock) end
        function strtrim(s) return (s:gsub('^%s+', ''):gsub('%s+$', '')) end
        inCombat, lockdown, groupGuids, sendResult = false, false, {}, 0
        function InCombatLockdown() return inCombat end
        function IsEncounterInProgress() return false end
        C_ChatInfo = { RegisterAddonMessagePrefix = function() end, InChatMessagingLockdown = function() return lockdown end }
        BNET_CLIENT_WOW, WOW_PROJECT_ID = 'WoW', 5
        sent, shown, said = {}, {}, {}
        function BNGetNumFriends() return #friends end
        C_BattleNet = {
          GetFriendAccountInfo = function(i) return friends[i] and { battleTag = friends[i].tag } end,
          GetFriendNumGameAccounts = function(i) return friends[i] and 1 or 0 end,
          GetFriendGameAccountInfo = function(i)
            local f = friends[i]
            return f and { isOnline = f.online ~= false, clientProgram = 'WoW', wowProjectID = 5, gameAccountID = f.account, playerGuid = f.guid }
          end,
          SendGameData = function(to, prefix, text) table.insert(sent, { to = to, prefix = prefix, text = text }) return sendResult end,
        }
        function IsInGroup() return #groupGuids > 0 end
        function IsInRaid() return false end
        function GetNumGroupMembers() return #groupGuids end
        function UnitGUID(u)
          if u == 'player' then return MY_GUID end
          local i = tonumber(u:match('^party(%d+)$') or '')
          return i and groupGuids[i] or nil
        end
        StaticPopupDialogs = {}
        function StaticPopup_Show(name, arg, _, data) table.insert(shown, { name = name, arg = arg, data = data }) return {} end
        function GetAddOnMetadata() return '0.33.0' end
        """;

    internal static readonly string[] AllFiles = ["Shared.lua", "FriendData.lua", "Comms.lua", "Friends.lua"];

    // One game client. me: this player's GUID; friendsLua: the Battle.net friends list as a Lua
    // table of { tag, account, guid, online }; db and settings: the saved file and Settings.lua.
    internal sealed class Client
    {
        public Script Script { get; } = new();
        public Table Ns { get; }
        public Table Comms => Ns.Get("comms").Table;

        public Client(string me, string friendsLua, string db = "{ chars = {}, items = {} }", string settings = "{}", string[]? files = null)
        {
            Script.DoString(AddonLuaTests.FakeTimers);
            Script.DoString(Stubs);
            Script.DoString($"MY_GUID = '{me}'; friends = {friendsLua}; WoWCharacterTrackerDB = {db}; WoWCharacterTrackerSettings = {settings}");
            Ns = new Table(Script);
            Script.Globals["ns"] = Ns;
            Ns["say"] = (Action<string>)(s => Script.Globals.Get("said").Table.Append(DynValue.NewString(s)));
            foreach (var file in files ?? AllFiles)
                Script.Call(Script.LoadString(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, file)), codeFriendlyName: file), "WoWCharacterTracker", Ns);
        }

        public DynValue Lua(string code) => Script.DoString(code);
        public DynValue Call(string name, params object[] args) => Script.Call(Ns.Get(name), args);
        public void Run(double seconds) => Script.DoString($"runTimers(clock + {seconds.ToString(CultureInfo.InvariantCulture)})");
        public List<Table> Sent() => [.. Script.Globals.Get("sent").Table.Values.Select(v => v.Table)];
        public List<string> Said() => [.. Script.Globals.Get("said").Table.Values.Select(v => v.String)];
        public void Receive(string text, int fromAccount, string prefix = "WCTF") =>
            Script.Call(Comms.Get("receive"), prefix, text, "WHISPER", fromAccount);
    }

    static string Str(Client c, string expr) => c.Lua("return " + expr).String;

    [Fact]
    public void A_popup_is_added_to_the_games_list_once_and_gets_its_data()
    {
        var c = new Client("Player-1-0000000A", "{}", files: ["Shared.lua"]);
        c.Lua("""
            ns.popup('WCT_TEST', { text = 'first %s', button1 = 'OK' }, 'x', { n = 1 })
            ns.popup('WCT_TEST', { text = 'second %s', button1 = 'OK' }, 'y', { n = 2 })
            """);
        Assert.Equal("first %s", Str(c, "StaticPopupDialogs.WCT_TEST.text"));
        Assert.True(c.Lua("return StaticPopupDialogs.WCT_TEST.hideOnEscape").Boolean);
        Assert.Equal(2, c.Lua("return #shown").Number);
        Assert.Equal("y", Str(c, "shown[2].arg"));
        Assert.Equal(2, c.Lua("return shown[2].data.n").Number);
    }

    [Fact]
    public void A_copy_is_deep_and_separate()
    {
        var c = new Client("Player-1-0000000A", "{}", files: ["Shared.lua"]);
        c.Lua("original = { a = { b = 1 } }; copied = ns.copy(original); copied.a.b = 2");
        Assert.Equal(1, c.Lua("return original.a.b").Number);
        Assert.Equal(5, c.Lua("return ns.copy(5)").Number);
    }
}
