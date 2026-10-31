using WoWCharacterTracker.Services.LiveChannel;
using WoWCharacterTracker.Services.Streaming;

namespace WoWCharacterTracker.Tests;

public class StreamTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 18, 20, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 18)));
    static readonly StreamCharacter Ronnie = new("P-1", "Ronnie Raket", "Forever", 22, "Paladin", "#F48CBA", "Dwarf", "Classic Beta PvP",
        "Redridge Mountains", "1d 6h", "21", Now.ToUnixTimeSeconds());

    static long Ago(TimeSpan t) => (Now - t).ToUnixTimeSeconds();
    static Entry Level(int level, long seconds, TimeSpan ago, string guid = "P-1") =>
        new() { Id = $"{guid}:{level}", Guid = guid, Kind = "level", Level = level, Seconds = seconds, At = Ago(ago) };
    static Entry Duel(string opponent, bool lost, TimeSpan ago, string kind = "duel") =>
        new() { Id = $"P-1:{kind}:{opponent}:{ago.Ticks}", Guid = "P-1", Kind = kind, Opponent = opponent, Lost = lost, At = Ago(ago) };
    static Entry Loot(string item, TimeSpan ago) => new() { Id = $"P-1:loot:{item}", Guid = "P-1", Kind = "loot", Item = item, Quality = 5, At = Ago(ago) };

    [Fact]
    public void Newest_levels_first_with_the_ding_and_todays_average()
    {
        Entry[] events =
        [
            Level(20, 1300, TimeSpan.FromDays(2)),     // not today
            Level(21, 1500, TimeSpan.FromHours(2)),
            Level(22, 1400, TimeSpan.FromSeconds(5)),
            Level(30, 999, TimeSpan.FromSeconds(1), guid: "P-2"), // someone else
        ];
        var s = StreamSnapshot.Build(Ronnie, events, Now);

        Assert.Equal([22, 21, 20], s.Levels.Select(l => l.Level));
        Assert.Equal("21 to 22 took 23m 20s", s.LastLevel);
        Assert.Equal("2 levels today, 24m 10s average", s.Today);
        Assert.Equal(new StreamAlert("P-1:22", "level", "DING!", "Level 22", "21 to 22 took 23m 20s", "gold", Ago(TimeSpan.FromSeconds(5))), s.Alert);
        Assert.Equal("Ronnie Raket, level 22 Paladin", s.CharacterLine);
    }

    [Fact]
    public void Untimed_levels_and_no_levels()
    {
        var s = StreamSnapshot.Build(Ronnie, [Level(22, 0, TimeSpan.FromMinutes(1))], Now);
        Assert.Equal("21 to 22 took ?", s.LastLevel);
        Assert.Equal("1 level today", s.Today);

        s = StreamSnapshot.Build(Ronnie, [], Now);
        Assert.Equal("No levels yet today", s.Today);
        Assert.Null(s.Alert);
        Assert.Equal("", s.LastLevel);
        Assert.Null(s.Duels);
    }

    [Fact]
    public void Alerts_look_like_the_discord_posts()
    {
        var max = new Entry { Id = "m", Guid = "P-1", Kind = "level", Level = 60, MaxLevel = true, Played = 3 * 86400 + 5 * 3600, At = Ago(TimeSpan.FromSeconds(3)) };
        Assert.Equal(("maxlevel", "MAX LEVEL!", "Level 60", "Total played 3d 5h 0m", "legendary"), Parts(StreamSnapshot.AlertFor(max)!));
        var boss = new Entry { Id = "b", Kind = "boss", Boss = "Edwin VanCleef", Instance = "The Deadmines" };
        Assert.Equal(("boss", "VICTORY!", "Edwin VanCleef defeated", "The Deadmines", "victory"), Parts(StreamSnapshot.AlertFor(boss)!));
        Assert.Equal("epic", StreamSnapshot.AlertFor(new Entry { Kind = "loot", Item = "Sul'thraze", Quality = 4 })!.Tone);
        Assert.Equal("legendary", StreamSnapshot.AlertFor(new Entry { Kind = "loot", Item = "Thunderfury", Quality = 5 })!.Kind);
        Assert.Equal("blood", StreamSnapshot.AlertFor(new Entry { Kind = "makgora", Opponent = "Grimtusk", OpponentInfo = "40~WARRIOR~Warrior~Orc" })!.Tone);
        Assert.Equal("Level 40 Orc Warrior", StreamSnapshot.AlertFor(new Entry { Kind = "duel", Opponent = "Grimtusk", OpponentInfo = "40~WARRIOR~Warrior~Orc" })!.Sub);
        // A fight lost isn't alert material.
        Assert.Null(StreamSnapshot.AlertFor(new Entry { Kind = "duel", Opponent = "Grimtusk", Lost = true }));
    }

    [Fact]
    public void A_dungeons_end_boss_completes_the_dungeon()
    {
        var kill = new Entry { Id = "b", Guid = "P-1", Kind = "boss", Boss = "Edwin VanCleef", Instance = "The Deadmines", ZoneType = "dungeon", At = Ago(TimeSpan.FromSeconds(5)) };
        Assert.Equal(("dungeon", "DUNGEON COMPLETE!", "The Deadmines", "Edwin VanCleef defeated", "victory"), Parts(StreamSnapshot.AlertFor(kill)!));
        // A raid's end boss, or a kill from before the zone type was recorded, stays a boss kill.
        Assert.Equal("boss", StreamSnapshot.AlertFor(kill with { ZoneType = "raid", Boss = "Ragnaros" })!.Kind);
        Assert.Equal("boss", StreamSnapshot.AlertFor(kill with { ZoneType = "" })!.Kind);
    }

    [Theory]
    [InlineData("dungeon,boss", "dungeon")]
    [InlineData("dungeon", "dungeon")]
    [InlineData("boss", "boss")] // dungeon alert off: still a boss kill
    [InlineData("level", null)]
    public void The_dungeon_alert_has_its_own_option(string picked, string? expected)
    {
        var kill = new Entry { Id = "b", Guid = "P-1", Kind = "boss", Boss = "Edwin VanCleef", Instance = "The Deadmines", ZoneType = "dungeon", At = Ago(TimeSpan.FromSeconds(5)) };
        var options = new StreamOptions { AlertKinds = [.. picked.Split(',')] };
        Assert.Equal(expected, StreamSnapshot.Build(Ronnie, [kill], Now, options).Alert?.Kind);
    }

    [Fact]
    public void The_dungeon_alert_needs_only_what_the_live_channel_already_sends()
    {
        var e = Payload.Parse("i=:boss:1;g=P-1;k=boss;n=Zeb;b=Bazil Thredd;I=The Stockade;f=dungeon")!;
        Assert.Equal("dungeon", StreamSnapshot.AlertFor(e)!.Kind);
    }

    [Fact]
    public void Every_option_sits_in_one_part_and_game_parts_match_the_capabilities()
    {
        (string Kind, string Name, string Tip)[][] alerts = [StreamViewModel.AlertNames, StreamViewModel.ForeverAlertNames, StreamViewModel.RetailAlertNames];
        (string Kind, string Name, string Tip)[][] highlights = [StreamViewModel.KindNames, StreamViewModel.ForeverKindNames, StreamViewModel.RetailKindNames];
        (string Kind, string Name, string Tip)[][] fields = [StreamViewModel.FieldNames, StreamViewModel.RetailFieldNames];

        Assert.Equal(StreamOptions.AllAlertKinds.Order(), alerts.SelectMany(p => p).Select(k => k.Kind).Order());
        Assert.Equal(StreamOptions.AllKinds.Order(), highlights.SelectMany(p => p).Select(k => k.Kind).Order());
        Assert.Equal(StreamOptions.AllCardFields.Order(), fields.SelectMany(p => p).Select(k => k.Kind).Order());

        // Nothing marked as needing ratings sits outside Retail's part, or needing none outside Forever's.
        Assert.All(StreamViewModel.RetailAlertNames.Concat(StreamViewModel.RetailKindNames), k => Assert.True(EventKinds.OfferedIn(k.Kind, Flavors.Retail.HasRatings)));
        Assert.All(StreamViewModel.ForeverAlertNames.Concat(StreamViewModel.ForeverKindNames), k => Assert.True(EventKinds.OfferedIn(k.Kind, Flavors.Forever.HasRatings)));
        Assert.All(StreamViewModel.AlertNames.Concat(StreamViewModel.KindNames), k =>
            Assert.True(EventKinds.OfferedIn(k.Kind, hasRatings: true) && EventKinds.OfferedIn(k.Kind, hasRatings: false)));
    }

    [Fact]
    public void New_configs_have_the_dungeon_alert_on()
    {
        Assert.Contains("dungeon", new StreamOptions().AlertKinds);
        Assert.Contains("dungeon", StreamOptions.AllAlertKinds);
    }

    static (string, string, string, string, string) Parts(StreamAlert a) => (a.Kind, a.Title, a.Line, a.Sub, a.Tone);

    [Fact]
    public void The_newest_chosen_kind_is_the_alert()
    {
        Entry[] events = [Level(22, 1400, TimeSpan.FromMinutes(5)), Loot("Coarse Stone", TimeSpan.FromMinutes(1)) with { Quality = 3 }];
        // Rare loot is off by default, so the level-up is still the alert.
        Assert.Equal("level", StreamSnapshot.Build(Ronnie, events, Now).Alert!.Kind);
        Assert.Equal("rare", StreamSnapshot.Build(Ronnie, events, Now, new StreamOptions { AlertKinds = ["level", "rare"] }).Alert!.Kind);
        Assert.Null(StreamSnapshot.Build(Ronnie, events, Now, new StreamOptions { AlertKinds = ["boss"] }).Alert);
    }

    [Fact]
    public void Duel_and_makgora_records_count_wins_and_losses()
    {
        Entry[] events =
        [
            Duel("Grimtusk", lost: false, TimeSpan.FromHours(3)), Duel("Pellia", lost: false, TimeSpan.FromHours(2)),
            Duel("Grimtusk", lost: true, TimeSpan.FromHours(1)), Duel("Brandur", lost: false, TimeSpan.FromMinutes(5), kind: "makgora"),
        ];
        var s = StreamSnapshot.Build(Ronnie, events, Now);
        Assert.Equal(new StreamRecord(2, 1), s.Duels);
        Assert.Equal(new StreamRecord(1, 0), s.Makgora);
        // Wins and losses are both highlights (the newest three).
        Assert.Equal(["Won a Mak'gora against Brandur", "Lost a duel to Grimtusk", "Won a duel against Pellia"], s.Highlights.Select(h => h.Text));
    }

    [Fact]
    public void Battlegrounds_count_wins_and_losses_and_a_win_is_an_alert()
    {
        Entry[] events =
        [
            new() { Id = "bg1", Guid = Ronnie.Guid, Kind = "bg", Instance = "Warsong Gulch", At = Ago(TimeSpan.FromHours(1)) },
            new() { Id = "bg2", Guid = Ronnie.Guid, Kind = "bg", Instance = "Arathi Basin", Lost = true, At = Ago(TimeSpan.FromMinutes(5)) },
        ];
        var s = StreamSnapshot.Build(Ronnie, events, Now);
        Assert.Equal(new StreamRecord(1, 1), s.Battlegrounds);
        Assert.Equal(["Lost Arathi Basin", "Won Warsong Gulch"], s.Highlights.Select(h => h.Text));
        Assert.Equal("Won Warsong Gulch", s.Alert?.Line); // losses aren't alerts
    }

    [Fact]
    public void Highlights_follow_the_chosen_kinds()
    {
        Entry[] events = [Loot("Thunderfury, Blessed Blade of the Windseeker", TimeSpan.FromMinutes(10)), Duel("Pellia", false, TimeSpan.FromMinutes(1))];
        var s = StreamSnapshot.Build(Ronnie, events, Now, new StreamOptions { HighlightKinds = ["loot"] });
        Assert.Equal(["Looted Thunderfury, Blessed Blade of the Windseeker"], s.Highlights.Select(h => h.Text));
    }

    [Fact]
    public void Highlights_carry_an_icon_items_their_own_once_known()
    {
        Entry[] events =
        [
            new() { Id = "l", Guid = "P-1", Kind = "loot", Item = "Verigan's Fist", ItemId = 6953, Quality = 3, At = Ago(TimeSpan.FromMinutes(4)) },
            new() { Id = "m", Guid = "P-1", Kind = "mount", Mount = "Magic Broom", ItemId = 33176, At = Ago(TimeSpan.FromMinutes(3)) },
            new() { Id = "r", Guid = "P-1", Kind = "mplus", Step = 3000, Rating = 3011, At = Ago(TimeSpan.FromMinutes(2)) },
        ];
        // Before Wowhead answers: the kind's icon, the loot's quality frame already known from the event.
        var s = StreamSnapshot.Build(Ronnie, events, Now);
        Assert.Equal(new StreamHighlight("Reached 3000 Mythic+ rating (3011)", Wowhead.IconUrl("inv_relics_hourglass", "small")), s.Highlights[0]);
        Assert.Equal(new StreamHighlight("Got Magic Broom", Wowhead.IconUrl("ability_mount_ridinghorse", "small"), "", 33176), s.Highlights[1]);
        Assert.Equal(("#0070DD", 6953), (s.Highlights[2].Color, s.Highlights[2].ItemId));

        // Once it has: the item's own icon, the mount framed in its quality.
        Wowhead.Info? Items(int id) => id == 33176 ? new("Magic Broom", "inv_misc_broom", 4, "") : null;
        s = StreamSnapshot.Build(Ronnie, events, Now, items: Items);
        Assert.Equal((Wowhead.IconUrl("inv_misc_broom", "small"), "#A335EE"), (s.Highlights[1].Icon, s.Highlights[1].Color));
    }

    [Fact]
    public void A_fixed_highlights_box_gets_more_lines_to_fill_but_the_text_file_keeps_three()
    {
        var events = Enumerable.Range(1, 8).Select(i => Duel($"Foe{i}", false, TimeSpan.FromMinutes(i))).ToArray();
        Assert.Equal(3, StreamSnapshot.Build(Ronnie, events, Now).Highlights.Count);

        var s = StreamSnapshot.Build(Ronnie, events, Now, new StreamOptions { HighlightsWidth = 50, HighlightsHeight = 180 });
        Assert.Equal(8, s.Highlights.Count); // the page shows the newest that fit
        Assert.Equal((200, 180), (s.Look!.HighlightsWidth, s.Look.HighlightsHeight)); // narrowest 200
        var file = StreamFiles.Contents(s).Single(f => f.File == "highlights.txt").Text;
        Assert.Equal(3, file.Split(Environment.NewLine).Length);
    }

    [Fact]
    public void Start_from_now_only_counts_what_came_after()
    {
        Entry[] events =
        [
            Level(21, 1500, TimeSpan.FromHours(2)), Duel("Grimtusk", false, TimeSpan.FromHours(2)),
            Level(22, 1400, TimeSpan.FromMinutes(5)), Duel("Pellia", true, TimeSpan.FromMinutes(4)),
        ];
        var s = StreamSnapshot.Build(Ronnie, events, Now, new StreamOptions { Since = Ago(TimeSpan.FromHours(1)) });
        Assert.Equal([22], s.Levels.Select(l => l.Level));
        Assert.Equal(new StreamRecord(0, 1), s.Duels);
        Assert.Equal(["Lost a duel to Pellia"], s.Highlights.Select(h => h.Text)); // the win was before
        Assert.StartsWith("Since ", s.SinceText);
    }

    [Fact]
    public void Today_counts_since_midnight_and_all_counts_everything()
    {
        Entry[] events = [Level(20, 1300, TimeSpan.FromDays(2)), Duel("Grimtusk", false, TimeSpan.FromDays(2)), Level(21, 1500, TimeSpan.FromMinutes(30))];
        var today = StreamSnapshot.Build(Ronnie, events, Now, new StreamOptions { Range = "today", Since = Ago(TimeSpan.FromMinutes(5)) });
        Assert.Equal([21], today.Levels.Select(l => l.Level)); // Since is only for "now"
        Assert.Null(today.Duels);
        Assert.Equal("Today", today.SinceText);

        var all = StreamSnapshot.Build(Ronnie, events, Now, new StreamOptions { Range = "all", Since = Ago(TimeSpan.FromMinutes(5)) });
        Assert.Equal([21, 20], all.Levels.Select(l => l.Level));
        Assert.Equal("", all.SinceText);
    }

    [Fact]
    public void Old_configs_with_since_keep_counting_from_then()
    {
        Assert.Equal("now", new StreamOptions { Since = 123 }.RangeMode);
        Assert.Equal("all", new StreamOptions().RangeMode);
        Assert.Equal("today", new StreamOptions { Range = "today", Since = 123 }.RangeMode);
    }

    [Fact]
    public void The_card_carries_spec_gold_and_keystone()
    {
        var s = StreamSnapshot.Build(Ronnie with { Spec = "Holy", Gold = "1,204g", Keystone = "+12 Ara-Kara, City of Echoes" }, [], Now);
        Assert.Equal(("Holy", "1,204g", "+12 Ara-Kara, City of Echoes"), (s.Spec, s.Gold, s.Keystone));
    }

    [Fact]
    public void The_card_carries_ratings_and_none_without()
    {
        var s = StreamSnapshot.Build(Ronnie with { Ratings = [new("Mythic+", 2850), new("3v3", 1850)] }, [], Now);
        Assert.Equal([new StreamRating("Mythic+", 2850), new StreamRating("3v3", 1850)], s.Ratings!);
        Assert.Empty(StreamSnapshot.Build(Ronnie, [], Now).Ratings!);
    }

    [Theory]
    [InlineData("12~503~Ara-Kara, City of Echoes", "+12 Ara-Kara, City of Echoes")]
    [InlineData("none", "")]
    [InlineData("", "")]
    [InlineData("0~503~Ara-Kara", "")]
    public void Keystone_text(string saved, string shown) => Assert.Equal(shown, Format.Keystone(saved));

    [Fact]
    public void The_look_comes_from_the_options()
    {
        var s = StreamSnapshot.Build(Ronnie, [], Now, new StreamOptions { CardOpacity = 140, ShowDuels = false });
        Assert.Equal(100, s.Look!.CardOpacity); // clamped
        Assert.False(s.Look.Duels);
    }

    [Fact]
    public void Size_colors_and_fields_come_from_the_options()
    {
        var look = StreamSnapshot.Build(Ronnie, [], Now, new StreamOptions
        {
            Scale = 900, CardWidth = 100, CardHeight = 420, Accent = "3C8CFF", Background = "#abc", ShowBorder = false, CardFields = ["gold"],
        }).Look!;
        Assert.Equal((250, 260, 420), (look.Scale, look.Width, look.Height)); // clamped, a tiny width is the smallest
        Assert.Equal(("#3c8cff", "#aabbcc"), (look.Accent, look.Background));
        Assert.False(look.Border);
        Assert.Equal(["gold"], look.Fields!);

        var plain = StreamSnapshot.Build(Ronnie, [], Now).Look!;
        Assert.Equal((0, 0, "", ""), (plain.Width, plain.Height, plain.Accent, plain.Background)); // auto size, default colors
        Assert.Equal(StreamOptions.AllCardFields, plain.Fields!);
    }

    [Theory]
    [InlineData("#FFD100", "#ffd100")]
    [InlineData(" fd1 ", "#ffdd11")]
    [InlineData("gold", "")]
    [InlineData("#12345", "")]
    [InlineData("", "")]
    public void Hex_colors(string typed, string used) => Assert.Equal(used, StreamLook.Hex(typed));

    [Theory]
    [InlineData("Arms 31 / Fury 20 / Protection 0", "31/20/0")]
    [InlineData("Class 15 / Fury 12 / Slayer 3", "15/12/3")]
    [InlineData("", "")]
    public void Talents_as_numbers(string talents, string shown) => Assert.Equal(shown, Format.TalentNumbers(talents));

    [Fact]
    public void Played_at_this_level_counts_from_the_ding()
    {
        Entry[] events = [Level(22, 1400, TimeSpan.FromHours(3)) with { Played = 100_000 }, Level(21, 1300, TimeSpan.FromHours(4)) with { Played = 98_000 }];
        var s = StreamSnapshot.Build(Ronnie with { PlayedSeconds = 108_100, Talents = "Holy 5 / Protection 0 / Retribution 12" }, events, Now);
        Assert.Equal("2h 15m", s.LevelPlayed);
        Assert.Equal("5/0/12", s.Talents);
        // Not known without the ding's played, or when the saved played is older than the ding.
        Assert.Equal("", StreamSnapshot.Build(Ronnie with { PlayedSeconds = 108_100 }, [Level(22, 1400, TimeSpan.FromHours(3))], Now).LevelPlayed);
        Assert.Equal("", StreamSnapshot.Build(Ronnie with { PlayedSeconds = 99_000 }, events, Now).LevelPlayed);
    }

    [Fact]
    public void No_characters_at_all_is_an_empty_overlay_not_a_crash()
    {
        // A fresh install (no characters on any tab) used to throw "Sequence contains no elements".
        var s = StreamSnapshot.Latest([], Now);
        Assert.False(s.HasCharacter);
        Assert.NotNull(s.Look);
    }

    [Fact]
    public void The_character_played_last_wins_across_games()
    {
        var retail = Ronnie with { Guid = "R-1", Name = "Drygbeck", Game = "Retail", LastSeen = Now.ToUnixTimeSeconds() - 3600 };
        var s = StreamSnapshot.Latest([(retail, []), (Ronnie, [])], Now);
        Assert.Equal("Ronnie Raket", s.Character);
    }

    [Fact]
    public void Text_files_are_empty_without_a_character()
    {
        Assert.All(StreamFiles.Contents(StreamSnapshot.Empty), f => Assert.Equal("", f.Text));
        var files = StreamFiles.Contents(StreamSnapshot.Build(Ronnie, [Level(22, 1400, TimeSpan.FromSeconds(5)), Duel("Pellia", false, TimeSpan.FromSeconds(3))], Now))
            .ToDictionary(f => f.File, f => f.Text);
        Assert.Equal("22", files["level.txt"]);
        Assert.Equal("Level 22: 23m 20s", files["levels.txt"]);
        Assert.Equal("1 won, 0 lost", files["duels.txt"]);
    }
}
