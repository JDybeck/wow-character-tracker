using System.Text.RegularExpressions;
using MoonSharp.Interpreter;
using WoWCharacterTracker.Services.LiveChannel;

namespace WoWCharacterTracker.Tests;

// Rules that live in one place in the app, and the ones the addon has to keep the same in Lua.
public class SharedRulesTests
{
    static string AddonFile(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "addon", "WoWCharacterTracker"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "addon", "WoWCharacterTracker", name));
    }

    // The live channel's one-letter keys: the addon's SHORT and the app's Payload.ShortKeys.
    [Fact]
    public void Short_keys_match_the_addon()
    {
        var block = Regex.Match(AddonFile("LiveGrid.lua"), @"local SHORT = \{(.*?)\n\}", RegexOptions.Singleline).Groups[1].Value;
        var lua = Regex.Matches(block, @"(\w+) = ""(\w+)""").ToDictionary(m => m.Groups[2].Value, m => m.Groups[1].Value);
        Assert.NotEmpty(lua);
        Assert.Equal(lua.OrderBy(p => p.Key), Payload.ShortKeys.OrderBy(p => p.Key));
    }

    // Whether an event gets posted: the addon's wanted() (what it sends live and screenshots) and the
    // app's FlavorSettings.Wants, for the same settings.
    [Theory]
    [InlineData("level", 10, 0, false, false)]
    [InlineData("level", 11, 0, false, false)]
    [InlineData("level", 31, 0, false, false)]
    [InlineData("level", 33, 0, true, false)]
    [InlineData("loot", 20, 3, false, false)]
    [InlineData("loot", 20, 4, false, false)]
    [InlineData("loot", 20, 5, false, false)]
    [InlineData("mount", 20, 0, false, false)]
    [InlineData("boss", 20, 0, false, false)]
    [InlineData("death", 20, 0, false, false)]
    [InlineData("duel", 20, 0, false, false)]
    [InlineData("duel", 20, 0, false, true)]
    [InlineData("makgora", 20, 0, false, true)]
    [InlineData("bg", 20, 0, false, false)]
    [InlineData("mplus", 70, 0, false, false)]
    [InlineData("pvp", 70, 0, false, false)]
    public void Addon_and_app_agree_on_what_is_posted(string kind, int level, int quality, bool maxLevel, bool lost)
    {
        foreach (var settings in new[] { Flavors.Forever.Defaults(), Flavors.Retail.Defaults(), new FlavorSettings { PostDuels = true, PostRare = true, PostMaxLevel = false } })
        {
            var e = new Entry { Kind = kind, Level = level, Quality = quality, MaxLevel = maxLevel, Lost = lost };
            Assert.Equal(settings.Wants(e), AddonWants(settings, e));
        }
    }

    // Runs LiveGrid.lua's wanted() through ns.addEvent with the Settings.lua the app would write.
    static bool AddonWants(FlavorSettings s, Entry e)
    {
        var dir = Path.Combine(Path.GetTempPath(), "wct-wanted-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Interface", "AddOns", "WoWCharacterTracker"));
        try
        {
            AddonInstaller.WriteSettings(dir, s);
            var script = new Script();
            script.DoString(File.ReadAllText(Path.Combine(dir, "Interface", "AddOns", "WoWCharacterTracker", "Settings.lua")));
            script.DoString("WoWCharacterTrackerDB = { events = {}, live = false } function CreateFrame() return { SetScript = function() end } end Screenshot = nil");
            var ns = new Table(script);
            foreach (var file in new[] { "Shared.lua", "LiveGrid.lua" })
                script.Call(script.LoadString(AddonFile(file)), "WoWCharacterTracker", ns);
            var ev = script.DoString($"return {{ kind = '{e.Kind}', level = {e.Level}, quality = {e.Quality}, maxLevel = {(e.MaxLevel ? "true" : "nil")}, lost = {(e.Lost ? "true" : "nil")} }}");
            return script.Call(ns.Get("wanted"), ev).Boolean;
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Only_a_characters_first_kill_of_a_boss_is_posted()
    {
        var s = new FlavorSettings { PostBosses = true };
        Entry Kill(string guid, string boss, long at) => new() { Kind = "boss", Guid = guid, Boss = boss, At = at };
        var first = Kill("a", "Ragnaros", 100);
        var again = Kill("a", "Ragnaros", 200);
        List<Entry> history = [first, again, Kill("b", "Ragnaros", 150), Kill("a", "Onyxia", 50)];

        Assert.True(s.Wants(first, history));
        Assert.False(s.Wants(again, history));
        Assert.True(s.Wants(Kill("b", "Ragnaros", 150), history)); // another character's first
        s.FirstBossKillOnly = false;
        Assert.True(s.Wants(again, history));
    }

    [Fact]
    public void Webhooks_follow_rulesets_and_tags()
    {
        var config = new Config();
        var all = new WebhookInfo { Name = "all" };
        var hardcore = new WebhookInfo { Name = "hc", ForeverRulesets = ["Hardcore"] };
        var mains = new WebhookInfo { Name = "mains", Tags = ["Main"] };
        config.Webhooks.AddRange([all, hardcore, mains]);
        foreach (var w in config.Webhooks) { config.SetActive(Flavors.Forever, w, true); config.SetActive(Flavors.Retail, w, true); }
        config.CharacterTags["main"] = ["Main"];

        Assert.Equal(["all", "hc"], config.WebhooksFor(Flavors.Forever, "alt", "", hardcore: true).Select(w => w.Name));
        Assert.Equal(["all", "mains"], config.WebhooksFor(Flavors.Forever, "main", "Normal", hardcore: false).Select(w => w.Name));
        Assert.Equal(["all", "hc", "mains"], config.WebhooksFor(Flavors.Retail, "main", "", hardcore: false).Select(w => w.Name)); // no rulesets on Retail
    }

    [Fact]
    public void Console_line_is_the_highlight_with_the_name()
    {
        Assert.Equal("Zeb killed Edwin VanCleef (The Deadmines)", Tracker.Describe(new Entry { Kind = "boss", Char = "Zeb", Boss = "Edwin VanCleef", Instance = "The Deadmines" }));
        Assert.Equal("Zeb won a duel against Grimtusk", Tracker.Describe(new Entry { Kind = "duel", Char = "Zeb", Opponent = "Grimtusk" }));
        Assert.Equal("Zeb dinged level 12 in 18m 4s", Tracker.Describe(new Entry { Kind = "level", Char = "Zeb", Level = 12, Seconds = 18 * 60 + 4 }));
    }

    [Fact]
    public void Kinds_only_with_ratings_are_left_out_elsewhere()
    {
        Assert.False(EventKinds.OfferedIn("mplus", Flavors.Forever.HasRatings));
        Assert.True(EventKinds.OfferedIn("mplus", Flavors.Retail.HasRatings));
        Assert.True(EventKinds.OfferedIn("legendary", hasRatings: false)); // an alert kind, not an event kind
    }
}
