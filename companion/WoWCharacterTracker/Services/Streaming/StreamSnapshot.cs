namespace WoWCharacterTracker.Services.Streaming;

// What the stream overlay and the stream text files show, for the character played last. The card is
// the character as it is now (item level, played, gold, keystone, ratings, duel and Mak'gora records) next to
// its leveling (latest level times, today); the highlights box is what happened (loot, bosses, duel
// wins...); the alert is the newest big moment (a level-up, boss kill, epic loot...). Plus how the
// overlay should look (StreamOptions). Built from plain data (StreamCharacter and the events), so it
// doesn't depend on the app's view models. StreamOptions.From decides what counts (all time, today,
// or since "Start counting now").
sealed record StreamSnapshot(
    string Character, string Game, int Level, string ClassName, string ClassColor, string Race, string Realm,
    string Zone, string Played, string ItemLevel, IReadOnlyList<StreamLevel> Levels, string LastLevel, string Today,
    IReadOnlyList<StreamHighlight> Highlights, StreamAlert? Alert, long Now,
    StreamRecord? Duels = null, StreamRecord? Makgora = null, StreamLook? Look = null, string SinceText = "",
    string Spec = "", string Gold = "", string Keystone = "", IReadOnlyList<StreamRating>? Ratings = null, StreamRecord? Battlegrounds = null,
    string Talents = "", string LevelPlayed = "")
{
    public static readonly StreamSnapshot Empty = new("", "", 0, "", "#FFFFFF", "", "", "", "", "", [], "", "", [], null, 0, Look: StreamLook.From(new()));

    public bool HasCharacter => Character.Length > 0;

    // "Ronnie Raket, level 21 Paladin"
    public string CharacterLine => HasCharacter ? $"{Character}, level {Level} {ClassName}".TrimEnd() : "";

    const int LevelsShown = 5;
    // Highlights: 3, or with a fixed box height up to 12 for the page to show the newest that fit.
    public const int HighlightsShown = 3;
    const int HighlightsToFit = 12;

    // The character played last of each game's candidates (Empty when there are none: MaxBy on an
    // empty list of tuples would throw).
    public static StreamSnapshot Latest(IEnumerable<(StreamCharacter Character, IReadOnlyList<Entry> Events)> candidates, DateTimeOffset now, StreamOptions? options = null,
        Func<int, Wowhead.Info?>? items = null)
    {
        var list = candidates.ToList();
        if (list.Count == 0) return Empty with { Look = StreamLook.From(options ?? new()) };
        var (character, events) = list.MaxBy(v => v.Character.LastSeen);
        return Build(character, events, now, options, items);
    }

    // items: what Wowhead knows of an item id so far (its icon and quality), for loot and mount lines.
    public static StreamSnapshot Build(StreamCharacter c, IEnumerable<Entry> events, DateTimeOffset now, StreamOptions? options = null,
        Func<int, Wowhead.Info?>? items = null)
    {
        var o = options ?? new();
        var from = o.From(now);
        var mine = events.Where(e => e.Guid == c.Guid && e.At >= from).OrderByDescending(e => e.At).ToList();
        var levels = mine.Where(e => e.Kind == "level").ToList();
        var shown = levels.Take(LevelsShown).Select(e => new StreamLevel(e.Level, Took(e))).ToList();
        var newest = levels.FirstOrDefault();

        // Today: since local midnight (and since "Start counting now", when that's later).
        var midnight = new DateTimeOffset(now.LocalDateTime.Date, now.Offset).ToUnixTimeSeconds();
        var today = levels.Where(e => e.At >= midnight).ToList();
        var timed = today.Where(e => e.Seconds > 0).ToList();
        var todayText = today.Count == 0 ? "No levels yet today"
            : $"{Format.Count(today.Count, "level")} today" + (timed.Count > 0 ? $", {Format.Duration((long)timed.Average(e => e.Seconds))} average" : "");

        StreamRecord? Record(string kind)
        {
            var fights = mine.Where(e => e.Kind == kind).ToList();
            return fights.Count == 0 ? null : new StreamRecord(fights.Count(e => !e.Lost), fights.Count(e => e.Lost));
        }

        var kinds = o.HighlightKinds.ToHashSet();
        var highlights = mine.Where(e => kinds.Contains(e.Kind) && (e.Kind != "loot" || e.Followed || e.PassesRareRule(o.RareHighlightBoeOnly))).Select(e => Highlight(e, items)).Where(h => h.Text.Length > 0)
            .Take(o.HighlightsHeight > 0 ? HighlightsToFit : HighlightsShown).ToList();

        return new StreamSnapshot(
            c.Name, c.Game, c.Level, c.ClassName, c.ClassColor, c.Race, c.Realm, c.Zone, c.Played, c.ItemLevel,
            shown,
            newest is null ? "" : $"{newest.Level - 1} to {newest.Level} took {Took(newest)}",
            todayText,
            highlights,
            mine.Select(e => WantedAlert(e, o)).FirstOrDefault(a => a is not null),
            now.ToUnixTimeSeconds(),
            Record("duel"), Record("makgora"), StreamLook.From(o),
            RangeText(o), c.Spec, c.Gold, c.Keystone, c.Ratings ?? [], Record("bg"),
            Format.TalentNumbers(c.Talents), PlayedThisLevel(c, events));
    }

    // Played since the ding to the character's level ("2h 14m"): its played now minus the played
    // the ding was recorded with. All time, whatever the range; empty when either isn't known.
    static string PlayedThisLevel(StreamCharacter c, IEnumerable<Entry> events)
    {
        var ding = events.Where(e => e.Guid == c.Guid && e.Kind == "level" && e.Level == c.Level && e.Played > 0).MaxBy(e => e.At);
        return ding is not null && c.PlayedSeconds >= ding.Played ? Format.Played(c.PlayedSeconds - ding.Played) : "";
    }

    // Under the card: what it counts, when it's not all time.
    public static string RangeText(StreamOptions o) => o.RangeMode switch
    {
        "today" => "Today",
        "now" => "Since " + DateTimeOffset.FromUnixTimeSeconds(o.Since).LocalDateTime.ToString("MMM d, HH:mm", System.Globalization.CultureInfo.InvariantCulture),
        _ => "",
    };

    // One line per highlight, the same as the app's (Services.HighlightText): "Looted Thunderfury...",
    // "Won a duel against Grimtusk", "Lost Warsong Gulch". Wins and losses both.
    public static string HighlightText(Entry e) => Services.HighlightText.Text(e);

    // A highlight line with its icon: the item's own for loot and mounts once Wowhead has it (with its
    // quality's color as the frame), the rating's for milestones, else one per kind (Wowhead.HighlightIcons).
    public static StreamHighlight Highlight(Entry e, Func<int, Wowhead.Info?>? items = null)
    {
        var item = e.Kind is "loot" or "mount" && e.ItemId > 0 ? items?.Invoke(e.ItemId) : null;
        var icon = item is { Icon.Length: > 0 } ? item.Icon
            : e.Kind switch
            {
                "mplus" or "keyrun" => Wowhead.RatingIcons["Mythic+"],
                "pvp" => Wowhead.RatingIcons.GetValueOrDefault(e.Bracket) ?? Wowhead.RatingIcons["2v2"],
                "profession" => Wowhead.ProfessionIcons.GetValueOrDefault(e.Profession) ?? Wowhead.HighlightIcons["profession"],
                _ => Wowhead.HighlightIcons.GetValueOrDefault(e.Kind),
            };
        var quality = e.Kind == "loot" && e.Quality > 0 ? e.Quality : item?.Quality ?? -1;
        return new(HighlightText(e), icon is null ? "" : Wowhead.IconUrl(icon, "small"),
            e.Kind is "loot" or "mount" ? GameColors.QualityHex(quality) ?? "" : "", e.Kind is "loot" or "mount" ? e.ItemId : 0);
    }

    // The overlay's alert for an event, the way Discord would post it, or null when it's not alert
    // material (a duel or Mak'gora lost, a key over time; rare loot is its own kind). Tone picks the colors on the page.
    public static StreamAlert? AlertFor(Entry e) => (e.Followed ? FollowedAlert(e) : null) ?? MomentAlert(e);

    // The alert this event pops up with these options: a followed item as itself when that's on, else
    // as any other loot. One event, one alert either way.
    static StreamAlert? WantedAlert(Entry e, StreamOptions o)
    {
        if (e.Followed && o.AlertKinds.Contains("followed") && FollowedAlert(e) is { } followed) return followed;
        return e.Kind == "loot" && !e.PassesRareRule(o.RareAlertBoeOnly) ? null : ChosenAlert(e, o.AlertKinds);
    }

    // In its quality's colors; below rare it's gold, there's no tone for those.
    static StreamAlert? FollowedAlert(Entry e) => (e.Kind == "loot" ? e.Item : e.Kind == "mount" ? e.Mount : "") is { Length: > 0 } thing
        ? new(e.Id, "followed", "ITEM FOUND!", thing, "From your followed items", e.Quality switch { >= 5 => "legendary", 4 => "epic", 3 => "rare", _ => "gold" }, e.At)
        : null;

    // A dungeon is completed when its end boss dies: the same boss event, recorded inside a dungeon.
    static StreamAlert? MomentAlert(Entry e) => e.Kind switch
    {
        "boss" when e.Boss.Length > 0 && e.ZoneType == "dungeon" => new(e.Id, "dungeon", "DUNGEON COMPLETE!", e.Instance.Length > 0 ? e.Instance : e.Zone, $"{e.Boss} defeated", "victory", e.At),
        "level" when e.MaxLevel => new(e.Id, "maxlevel", "MAX LEVEL!", $"Level {e.Level}", e.Played > 0 ? $"Total played {Format.Played(e.Played)}" : "", "legendary", e.At),
        "level" => new(e.Id, "level", "DING!", $"Level {e.Level}", e.Seconds > 0 ? $"{e.Level - 1} to {e.Level} took {Took(e)}" : "", "gold", e.At),
        "boss" when e.Boss.Length > 0 => BossAlert(e),
        "keyrun" when e.Timed => new(e.Id, "keyrun", "TIMED!", Format.KeyRun(e), $"+{e.Upgrades}" + (e.Seconds > 0 ? $" in {Format.Clock(e.Seconds)}" : ""), "victory", e.At),
        "loot" when e.Quality >= 5 => new(e.Id, "legendary", "LEGENDARY!", e.Item, "", "legendary", e.At),
        "loot" when e.Quality == 4 => new(e.Id, "epic", "EPIC LOOT!", e.Item, "", "epic", e.At),
        "loot" when e.Quality == 3 => new(e.Id, "rare", "RARE LOOT!", e.Item, "", "rare", e.At),
        "mount" when e.Mount.Length > 0 => new(e.Id, "mount", "NEW MOUNT!", e.Mount, e.Bought ? "Bought" : "", "gold", e.At),
        "duel" when !e.Lost => new(e.Id, "duel", "VICTORY!", e.Fled ? $"{e.Opponent} ran from the duel" : $"Won a duel against {e.Opponent}", Capital(e.OpponentText), "duel", e.At),
        "makgora" when !e.Lost => new(e.Id, "makgora", "MAK'GORA!", $"{e.Opponent} fell to the death", Capital(e.OpponentText), "blood", e.At),
        "bg" when !e.Lost => new(e.Id, "bg", "VICTORY!", e.Instance.Length > 0 ? $"Won {e.Instance}" : "Battleground won", "", "victory", e.At),
        "death" => new(e.Id, "death", "R.I.P.", $"{e.Char} fell at level {e.Level}", e.Played > 0 ? $"After {Format.Played(e.Played)} played" : "", "death", e.At),
        "profession" => new(e.Id, "profession", "MILESTONE!", $"{e.Step} {e.Profession}", e.Rating > 0 ? $"Skill {e.Rating}" : "", "rating", e.At),
        "mplus" => new(e.Id, "mplus", "MILESTONE!", $"{e.Step} Mythic+ rating", e.Rating > 0 ? $"Rating {e.Rating}" : "", "rating", e.At),
        "pvp" => new(e.Id, "pvp", "MILESTONE!", $"{e.Step} in {e.Bracket}", e.Rating > 0 ? $"Rating {e.Rating}" : "", "rating", e.At),
        _ => null,
    };

    static StreamAlert BossAlert(Entry e) => new(e.Id, "boss", "VICTORY!", $"{e.Boss} defeated", e.Instance, "victory", e.At);

    // The event's alert if its kind is picked. With the dungeon alert off, a dungeon's end boss is
    // still a boss kill, so "Boss kills" keeps its VICTORY!.
    static StreamAlert? ChosenAlert(Entry e, List<string> picked)
    {
        var alert = MomentAlert(e);
        if (alert is null || picked.Contains(alert.Kind)) return alert;
        return alert.Kind == "dungeon" && picked.Contains("boss") ? BossAlert(e) : null;
    }

    static string Capital(string s) => s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;

    static string Took(Entry e) => e.Seconds > 0 ? Format.Duration(e.Seconds) : "?";
}

// The character as the stream sees it (filled in from the character list).
sealed record StreamCharacter(string Guid, string Name, string Game, int Level, string ClassName, string ClassColor, string Race,
    string Realm, string Zone, string Played, string ItemLevel, long LastSeen, string Spec = "", string Gold = "", string Keystone = "",
    IReadOnlyList<StreamRating>? Ratings = null, string Talents = "", long PlayedSeconds = 0);

sealed record StreamLevel(int Level, string Took);

// A highlights line: its text, the icon's address ("" = none) and, for items, the quality color for its frame.
// ItemId is the item still to look up for its icon (loot and mounts).
sealed record StreamHighlight(string Text, string Icon = "", string Color = "", int ItemId = 0)
{
    public override string ToString() => Text;
}

// A Retail rating on the card: "Mythic+" 2850, "3v3" 1850 (Format.Ratings).
sealed record StreamRating(string Name, int Rating);

// Duels or Mak'goras won and lost.
sealed record StreamRecord(int Won, int Lost)
{
    public override string ToString() => $"{Won} won, {Lost} lost";
}

// The newest alert-worthy moment; the overlay pops it up when it's new (Id) and recent (At).
// Kind: level, maxlevel, boss, dungeon, keyrun, legendary, epic, rare, followed, mount, duel, makgora, bg, death, mplus, pvp.
sealed record StreamAlert(string Id, string Kind, string Title, string Line, string Sub, string Tone, long At);

// How the overlay looks: the card's and the highlights' backgrounds (0-100), which parts and stats lines it shows, its
// size (scale in percent, card width and smallest height, highlights width and fixed height, in px, 0 = fits), colors ("#rrggbb", empty =
// default) and the border. The brand line is always on.
sealed record StreamLook(int CardOpacity, bool Levels, bool Today, bool Duels, bool Makgora, bool Highlights, bool Stats = true, bool Battlegrounds = true,
    IReadOnlyList<string>? Fields = null, int Scale = 100, int Width = 0, int Height = 0, string Accent = "", string Background = "",
    bool Border = true, int HighlightsWidth = 0, int HighlightsHeight = 0, int HighlightsOpacity = 78)
{
    public static StreamLook From(StreamOptions o) =>
        new(Math.Clamp(o.CardOpacity, 0, 100), o.ShowLevels, o.ShowToday, o.ShowDuels, o.ShowMakgora, o.ShowHighlights, o.ShowStats, o.ShowBattlegrounds,
            [.. o.CardFields], Math.Clamp(o.Scale, 50, 250), o.CardWidth <= 0 ? 0 : Math.Clamp(o.CardWidth, 260, 1000), Math.Clamp(o.CardHeight, 0, 1000),
            Hex(o.Accent), Hex(o.Background), o.ShowBorder,
            o.HighlightsWidth <= 0 ? 0 : Math.Clamp(o.HighlightsWidth, 200, 1000), Math.Clamp(o.HighlightsHeight, 0, 1000),
            Math.Clamp(o.HighlightsOpacity, 0, 100));

    // "#rgb", "rrggbb" or "#RRGGBB" as "#rrggbb"; anything else is "" (the default color).
    public static string Hex(string? s)
    {
        var h = (s ?? "").Trim().TrimStart('#').ToLowerInvariant();
        if (h.Length == 3) h = string.Concat(h.Select(ch => $"{ch}{ch}"));
        return h.Length == 6 && h.All(char.IsAsciiHexDigit) ? "#" + h : "";
    }
}
