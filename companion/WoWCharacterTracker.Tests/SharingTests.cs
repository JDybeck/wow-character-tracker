namespace WoWCharacterTracker.Tests;

public class DiscordPausedTests
{
    static Entry Level(int level) => new() { Kind = "level", Guid = "g1", Level = level, At = 1 };

    [Fact]
    public void Paused_tab_wants_nothing()
    {
        var s = Flavors.Forever.Defaults();
        Assert.True(s.Wants(Level(30), []));
        s.DiscordPaused = true;
        Assert.False(s.Wants(Level(30), []));
        Assert.False(s.Wants(new Entry { Kind = "loot", Guid = "g1", Quality = 5, At = 1 }, []));
    }
}

public class PageMatchTests
{
    [Theory]
    [InlineData("items", "characters|items|professions|followed", true)]
    [InlineData("sharing", "sharing", true)]
    [InlineData("friends", "characters|items", false)]
    [InlineData(null, "characters", false)]
    public void A_tab_can_cover_several_pages(string? page, string tab, bool expected) =>
        Assert.Equal(expected, WoWCharacterTracker.Ui.PageMatch.Matches(page, tab));
}

public class MomentsTests
{
    [Fact]
    public void Every_announce_and_ask_kind_has_a_row()
    {
        string[] announced = ["level", "maxlevel", "boss", "keyrun", "loot", "followed", "mount", "duel", "makgora", "bg", "profession", "mplus", "pvp", "death"];
        Assert.Equal(announced.Order(), Moments.All.Select(m => m.Announce).OfType<string>().Order());
        Assert.Equal(FlavorSettings.AllAskKinds.Order(), Moments.All.Select(m => m.Ask).OfType<string>().Order());
    }

    [Fact]
    public void Every_shot_kind_is_in_the_table() =>
        Assert.Equal(EventKinds.ShotKinds.Order(), Moments.All.Select(m => m.Shot).OfType<string>().Distinct().Order());

    [Fact]
    public void Every_alert_is_a_stream_alert_kind() =>
        Assert.All(Moments.All.Select(m => m.Alert).OfType<string>(), a => Assert.Contains(a, StreamOptions.AllAlertKinds));

    [Fact]
    public void Games_get_their_own_rows()
    {
        var forever = Moments.For(Flavors.Forever).Select(m => m.Key).ToList();
        var retail = Moments.For(Flavors.Retail).Select(m => m.Key).ToList();
        Assert.Contains("boss", forever);
        Assert.Contains("death", forever);
        Assert.DoesNotContain("keyrun", forever);
        Assert.Contains("keyrun", retail);
        Assert.Contains("mplus", retail);
        Assert.DoesNotContain("makgora", retail);
    }

    [Fact]
    public void Post_column_reads_and_writes_the_settings_field()
    {
        var s = new FlavorSettings();
        var epic = Moments.All.Single(m => m.Key == "epic");
        epic.SetPost(s, true);
        Assert.True(s.PostEpic);
        Assert.True(epic.GetPost(s));
    }

    [Fact]
    public void Every_row_can_be_tested() =>
        Assert.All(Moments.All, m => Assert.False(string.IsNullOrEmpty(m.Test)));

    [Fact]
    public void Level_rule_reads_like_a_sentence()
    {
        var s = Flavors.Forever.Defaults();
        Assert.Equal("every 5 to 30, then every level", Moments.Rule(Moments.All[0], s));
        (s.LevelEveryBefore, s.LevelEveryAfter) = (1, 1);
        Assert.Equal("every level", Moments.Rule(Moments.All[0], s));
    }
}

public class SharingRowTests
{
    static SharingRow Row(string key, FlavorSettings s, Action? inGame = null, List<string>? alerts = null) =>
        new(Moments.All.Single(m => m.Key == key), s, () => alerts ?? [], () => { }, inGame ?? (() => { }), () => { });

    [Fact]
    public void Guild_box_edits_the_announce_list_and_marks_in_game_changed()
    {
        var s = Flavors.Forever.Defaults();
        s.AnnounceKinds = [];
        var inGame = 0;
        var row = Row("mount", s, () => inGame++);
        row.Guild = true;
        Assert.Contains("mount", s.AnnounceKinds);
        Assert.Equal(1, inGame);
        Assert.False(row.Group);
    }

    [Fact]
    public void Group_box_starts_from_the_guild_list_in_old_configs()
    {
        var s = new FlavorSettings { AnnounceKinds = ["level", "mount"], AnnouncePartyKinds = null };
        var row = Row("level", s);
        Assert.True(row.Group);
        row.Group = false;
        Assert.Equal(["mount"], s.AnnouncePartyKinds);
        Assert.Contains("level", s.AnnounceKinds);
    }

    [Fact]
    public void Shot_box_shoots_and_attaches()
    {
        var s = new FlavorSettings();
        var row = Row("boss", s);
        row.Shot = true;
        Assert.True(s.PostsShot("boss"));
    }

    [Fact]
    public void Alert_box_edits_the_stream_list()
    {
        List<string> alerts = [];
        Row("duel", new FlavorSettings(), alerts: alerts).Alert = true;
        Assert.Equal(["duel"], alerts);
    }

    [Fact]
    public void Rows_without_a_column_say_so()
    {
        var rare = Row("rare", new FlavorSettings());
        Assert.False(rare.HasShot);
        Assert.False(rare.HasAnnounce);
        Assert.True(rare.HasAsk);
    }
}

public class SharingPresetsTests
{
    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (var f in Flavors.All)
            foreach (var p in SharingPresets.Keys) data.Add(f.Key, p);
        return data;
    }

    static Flavor F(string key) => Flavors.All.Single(f => f.Key == key);

    [Theory, MemberData(nameof(Cases))]
    public void Applied_preset_is_detected(string flavor, string preset)
    {
        var s = F(flavor).Defaults();
        SharingPresets.Apply(s, F(flavor), preset);
        Assert.Equal(preset, SharingPresets.Detect(s, F(flavor)));
    }

    [Theory, MemberData(nameof(Cases))]
    public void Preset_leaves_personal_settings_alone(string flavor, string preset)
    {
        var s = F(flavor).Defaults();
        s.ActiveWebhookIds = ["a"];
        s.AnnounceGuild = true;
        s.DiscordPaused = true;
        s.NameStyle = "first";
        s.FollowedItems = [new FollowedItem(19019, "Thunderfury")];
        s.Ignored = ["g"];
        s.Path = "x";
        SharingPresets.Apply(s, F(flavor), preset);
        Assert.Equal(["a"], s.ActiveWebhookIds);
        Assert.True(s.AnnounceGuild);
        Assert.True(s.DiscordPaused);
        Assert.Equal("first", s.NameStyle);
        Assert.Single(s.FollowedItems);
        Assert.Contains("g", s.Ignored);
        Assert.Equal("x", s.Path);
    }

    [Fact]
    public void Retail_presets_never_turn_on_forever_kinds()
    {
        foreach (var p in SharingPresets.Keys)
        {
            var s = Flavors.Retail.Defaults();
            SharingPresets.Apply(s, Flavors.Retail, p);
            Assert.False(s.PostBosses || s.PostDeaths || s.PostMakgora);
            Assert.Empty(s.AskKinds!);
            Assert.DoesNotContain("boss", s.AnnounceKinds);
        }
    }

    [Fact]
    public void The_game_defaults_are_normal()
    {
        foreach (var f in Flavors.All)
        {
            var s = f.Defaults();
            Assert.Equal(SharingPresets.Normal, SharingPresets.Detect(s, f));
        }
    }

    [Fact]
    public void A_mix_is_custom()
    {
        var s = Flavors.Forever.Defaults();
        SharingPresets.Apply(s, Flavors.Forever, SharingPresets.Quiet);
        s.PostDuels = true;
        Assert.Equal(SharingPresets.Custom, SharingPresets.Detect(s, Flavors.Forever));
    }

    [Fact]
    public void Everything_posts_every_level()
    {
        var s = Flavors.Forever.Defaults();
        SharingPresets.Apply(s, Flavors.Forever, SharingPresets.Everything);
        Assert.All(Enumerable.Range(2, 59), l => Assert.True(s.LevelWanted(l)));
    }

    [Fact]
    public void Quiet_posts_every_tenth_level()
    {
        var s = Flavors.Forever.Defaults();
        SharingPresets.Apply(s, Flavors.Forever, SharingPresets.Quiet);
        Assert.True(s.LevelWanted(20));
        Assert.False(s.LevelWanted(25));
        Assert.True(s.PostEpic);
        Assert.False(s.PostMounts);
    }
}
