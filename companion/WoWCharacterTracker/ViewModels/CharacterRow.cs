using System.Windows.Media;

namespace WoWCharacterTracker.ViewModels;

// One character in the list and its details: the latest snapshot, filled in from its events.
sealed class CharacterRow : Observable
{
    public required string Guid { get; init; }
    public required string Name { get; init; }
    public required string Realm { get; init; }
    public required int Level { get; init; }
    public required string ClassName { get; init; }
    public required string ClassFile { get; init; }
    public required string Race { get; init; }
    public required string RaceFile { get; init; }
    public required string Sex { get; init; }
    public required string Spec { get; init; }
    public required string Role { get; init; }
    public required double ItemLevel { get; init; }
    public required long Played { get; init; }
    public required string Zone { get; init; }
    public required string Professions { get; init; }
    public required string Talents { get; init; }
    public required long LastSeen { get; init; }
    public required bool Hardcore { get; init; }
    public required string Ruleset { get; init; }
    public required bool Dead { get; init; }
    public required long Money { get; init; }   // copper
    public required string Crests { get; init; }
    public string Guild { get; init; } = "";
    public bool HasGuild => Guild.Length > 0;
    public string Surname { get; init; } = "";
    public string TalentPicks { get; init; } = ""; // Forever: "spellID~rank~maxRank~tree,..."
    public IReadOnlyList<IconChip> TalentPickList { get; private set; } = [];
    public bool HasTalentPicks => TalentPickList.Count > 0;
    public string TalentExport { get; init; } = ""; // the game's loadout string (retail)
    public bool HasTalentExport => TalentExport.Length > 0;
    public string Region { get; init; } = ""; // "eu", "us"... for the Retail character render
    // "EU · Tarren Mill" when the region is known, so same-named realms in other regions stand apart.
    public string RealmText => Region.Length > 0 ? $"{Region.ToUpperInvariant()} · {Realm}" : Realm;
    public string TalentCalc { get; init; } = ""; // Forever: Wowhead classic-style code, "--505..."
    public bool HasTalentLink => HasTalentExport || TalentCalc.Length > 0;
    public string Stats { get; init; } = "";        // "Strength~120,Crit~5.2%,..."
    public IReadOnlyList<(string Name, string Value)> StatList => Stats.Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(p => p.Split('~')).Where(p => p.Length == 2).Select(p => (p[0], p[1])).ToList();
    public IReadOnlyList<IconChip> StatChips => StatList.Select(s => new IconChip($"{s.Name} {s.Value}",
        Wowhead.StatIcons.TryGetValue(s.Name, out var icon) ? Wowhead.IconUrl(icon, "small") : null, s.Name)).ToList();
    public bool HasStats => Stats.Length > 0;
    public string FullName => Surname.Length > 0 ? $"{Name} {Surname}" : Name;

    public string MoneyText => Money > 0 ? Format.Money(Money) : "-";
    public bool HasMoney => Money > 0;
    public IReadOnlyList<Coin> MoneyCoins => Coin.Full(Money);
    public IReadOnlyList<Coin> ShortCoins => Coin.Short(Money);
    public bool HasCrests => Crests.Length > 0;
    public string Keystone { get; init; } = "";      // retail: "level~mapID~dungeon", "none"
    public string KeystoneText => Format.Keystone(Keystone);
    public bool HasKeystone => KeystoneText.Length > 0;
    public int MplusScore { get; init; }            // retail: the Mythic+ score, 0 = none
    public string PvpRatings { get; init; } = "";   // retail: "bracket~rating,..."
    public IReadOnlyList<IconChip> RatingChips => Format.Ratings(MplusScore, PvpRatings).Select(r => new IconChip($"{r.Name} {r.Rating}",
        Wowhead.RatingIcons.TryGetValue(r.Name, out var icon) ? Wowhead.IconUrl(icon, "small") : null,
        r.Name == "Mythic+" ? "Mythic+ score" : $"{r.Name} rating")).ToList();
    public string RatingsText => Format.RatingsText(MplusScore, PvpRatings);
    public bool HasRatings => RatingsText.Length > 0;
    // The highest of the Mythic+ score and the PvP ratings, for sorting by rating.
    public int BestRating => Format.Ratings(MplusScore, PvpRatings).Select(r => r.Rating).DefaultIfEmpty(0).Max();
    public IReadOnlyList<IconChip> CrestList { get; private set; } = [];
    public IReadOnlyList<IconChip> ProfessionList { get; private set; } = [];
    public bool HasProfessions => ProfessionList.Count > 0;
    public required IReadOnlyList<LevelRow> Levels { get; init; }
    public required IReadOnlyList<HighlightRow> Highlights { get; init; }
    public Inventory? Inventory { get; init; } // what the character carries, as saved (the Items page)
    // The Gear and bags tab, built the first time it's shown: it makes an item (and a Wowhead
    // lookup) per slot, which for dozens of characters at startup froze the window.
    public Func<CharacterGear>? MakeGear { private get; init; }
    CharacterGear? gear;
    public CharacterGear Gear => gear ??= MakeGear?.Invoke() ?? CharacterGear.Empty;
    public CharacterLeveling Leveling { get; init; } = new();

    // The details are split into tabs: "overview", "gear" (gear and bags) and "leveling".
    string detailTab = "overview";
    public string DetailTab
    {
        get => detailTab;
        set
        {
            if (!Set(ref detailTab, value)) return;
            Raise(nameof(IsOverviewTab)); Raise(nameof(IsGearTab)); Raise(nameof(IsLevelingTab));
        }
    }
    public bool IsOverviewTab { get => DetailTab == "overview"; set { if (value) DetailTab = "overview"; } }
    public bool IsGearTab { get => DetailTab == "gear"; set { if (value) DetailTab = "gear"; } }
    public bool IsLevelingTab { get => DetailTab == "leveling"; set { if (value) DetailTab = "leveling"; } }

    // Fills in icons and tooltips: crests and items from Wowhead, professions from the built-in list.
    // Loaded once, when the character is opened (its overview, card and talent posts use them).
    Task? icons;
    public Task LoadIconsAsync(Flavor flavor) => icons ??= LoadIcons(flavor);

    // The tab's shared lookup of items, crests and talents (FlavorViewModel.InfoFor).
    public Func<string, int, ItemInfo>? Info { private get; init; }

    async Task LoadIcons(Flavor flavor)
    {
        // Crests, talents and loot are things from the game, so they get the same ItemInfo (and the
        // same tooltip and Wowhead link, Ui/ItemTip) as items in bags.
        ItemInfo? Lookup(string type, int id) => id > 0 ? Info?.Invoke(type, id) : null;
        CrestList = CrestChips(Crests);
        foreach (var chip in CrestList) chip.Info = Lookup("currency", chip.Tag);
        ProfessionList = ProfessionChips(Professions);
        TalentPickList = TalentChips(TalentPicks);
        foreach (var chip in TalentPickList) chip.Info = Lookup("spell", chip.Tag);
        foreach (var h in Highlights) h.Info = Lookup("item", h.ItemId);
        Raise(nameof(TalentPickList));
        Raise(nameof(HasTalentPicks));
        Raise(nameof(CrestList));
        Raise(nameof(ProfessionList));
        Raise(nameof(HasProfessions));

        foreach (var chip in CrestList.Where(c => c.Info is not null))
        {
            chip.Info!.Fallback(chip.Tooltip);
            await chip.Info.LoadAsync(flavor);
            chip.IconUrl = chip.Info.IconUrl;
        }
        foreach (var chip in TalentPickList.Where(c => c.Info is not null))
        {
            await chip.Info!.LoadAsync(flavor);
            chip.IconUrl = chip.Info.IconUrl;
            if (chip.Info.Name.Length > 0) chip.Text = $"{chip.Info.Name} {chip.Text}";
        }
        foreach (var h in Highlights.Where(h => h.Info is not null))
        {
            await h.Info!.LoadAsync(flavor);
            h.IconUrl = h.Info.IconUrl;
            if (h.Info.Quality >= 0) h.Brush = h.Info.Brush;
        }
    }

    // The user's tags as on/off chips for this character, and the box for making a new one.
    IReadOnlyList<ToggleOption> tagOptions = [];
    public IReadOnlyList<ToggleOption> TagOptions { get => tagOptions; set { tagOptions = value; Raise(nameof(TagOptions)); } }
    string newTag = "";
    public string NewTag { get => newTag; set => Set(ref newTag, value); }

    // Remove from list asks first, right there in the details.
    bool confirmRemove;
    public bool ConfirmRemove { get => confirmRemove; set => Set(ref confirmRemove, value); }

    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
    public string ItemLevelText => ItemLevel > 0 ? ItemLevel.ToString("0") : "-";
    public string PlayedText => Played > 0 ? Format.Played(Played) : "-";
    public string LastSeenText => Format.Ago(LastSeen) is { Length: > 0 } s ? s : "-";
    public string ProfessionsText => Professions is { Length: > 0 } and not "None" ? Professions : "None yet";
    public string TalentsText => Talents.Length > 0 ? Talents : "-";
    public string ZoneText => Zone.Length > 0 ? Zone : "-";
    public string ZoneType { get; init; } = "";       // dungeon, raid, pvp, rest, zone (Places)
    Places.Kind? ZoneKind => Places.Of(Zone, ZoneType);
    public string? ZoneIconUrl => ZoneKind is { } k ? Wowhead.IconUrl(k.Icon, "small") : null;
    public string ZoneKindName => ZoneKind?.Name ?? "";
    public string Description => string.Join(" ", new[] { Race, Spec, ClassName }.Where(s => s.Length > 0));
    public bool HasLevels => Levels.Count > 0;
    public bool HasHighlights => Highlights.Count > 0;
    public bool IsTest => TestCharacters.IsTest(Guid);
    public string? Tag => IsTest ? "TEST" : Dead ? "DEAD" : Hardcore ? "HARDCORE" : null;

    public SolidColorBrush ClassBrush => ClassColors.Brush(ClassColors.For(ClassFile));
    public SolidColorBrush ClassSoftBrush => ClassColors.Brush(ClassColors.For(ClassFile), 0x26);

    // Tank, healer, or melee/ranged/caster damage (Roles), as a small WoW icon with its name as the tooltip.
    public bool IsRetail { get; init; }
    public string RoleKind => Roles.Of(Role, ClassFile, Spec, IsRetail);
    public string? RoleIconUrl => Roles.Icon(RoleKind) is { } icon ? Wowhead.IconUrl(icon, "small") : null;
    public string RoleName => Roles.Name(RoleKind);
    // Next to the class: the spec's own icon, or the role's when the spec isn't known (SpecIcons).
    public string? SpecIconUrl => SpecIcons.Of(ClassFile, Spec) is { } icon ? Wowhead.IconUrl(icon, "medium") : RoleIconUrl;
    public string SpecIconTip => Spec.Length > 0 && RoleName.Length > 0 ? $"{Spec} ({RoleName})" : RoleName;

    string? pictureUrl;
    public string? PictureUrl { get => pictureUrl; set => Set(ref pictureUrl, value); }

    public PictureSubject Picture => new(Guid, Region, Realm, Name, RaceFile, Sex, ClassFile);

    // The picture by the one rule (CharacterPicture); the class icon while the rest is looked up.
    public async Task LoadPictureAsync(Flavor flavor)
    {
        pictureFlavor = flavor;
        HasGamePortrait = CharacterPicture.GamePortrait(flavor, Guid, standardChosen: false) is not null;
        if (CharacterPicture.GamePortrait(flavor, Guid, useStandard) is { } own) { PictureUrl = own; return; }
        PictureUrl = CharacterArt.ClassIconUrl(ClassFile);
        if (await CharacterPicture.StandardAsync(flavor, Picture) is { } url) PictureUrl = url;
    }

    // Forever: which picture to show once an in-game portrait was captured, the in-game one or the
    // standard (race) one. The choice is saved (Config.StandardPortraits) through the callback.
    Flavor? pictureFlavor;
    bool useStandard;
    Action<bool>? portraitChosen;
    bool hasGamePortrait;
    public bool HasGamePortrait { get => hasGamePortrait; private set => Set(ref hasGamePortrait, value); }
    public bool UseGamePortrait { get => !useStandard; set { if (value) ChoosePortrait(false); } }
    public bool UseStandardPortrait { get => useStandard; set { if (value) ChoosePortrait(true); } }

    public void SetPortraitChoice(bool standard, Action<bool> chosen) => (useStandard, portraitChosen) = (standard, chosen);

    void ChoosePortrait(bool standard)
    {
        if (useStandard == standard) return;
        useStandard = standard;
        portraitChosen?.Invoke(standard);
        Raise(nameof(UseGamePortrait)); Raise(nameof(UseStandardPortrait));
        if (pictureFlavor is { } f) _ = LoadPictureAsync(f);
    }

    // One row per character: the latest snapshot, filled in from events when there isn't one yet.
    // info: the tab's shared info per Wowhead type and id (icons and names are looked up once).
    public static List<CharacterRow> Build(TrackerData data, Flavor flavor, Func<string, int, ItemInfo> info)
    {
        var inventories = data.Inventories.GroupBy(i => i.Guid).ToDictionary(g => g.Key, g => g.MaxBy(i => Math.Max(i.ScannedAt, i.BankAt))!);
        // XP only comes with the saved file, so the newest snapshot that has it.
        var xpSnaps = data.Characters.Where(c => c.XpMax > 0).GroupBy(c => c.Guid).ToDictionary(g => g.Key, g => g.MaxBy(c => c.LastSeen)!);
        var events = data.Events.GroupBy(e => e.Guid).ToDictionary(g => g.Key, g => g.OrderBy(e => e.At).ToList());
        var snaps = data.Characters.GroupBy(c => c.Guid).ToDictionary(g => g.Key, g => g.MaxBy(c => c.LastSeen)!);
        // Live check-ins leave the guild out when there's none, so take the newest snapshot that knows it.
        var guilds = data.Characters.Where(c => c.Guild is not null).GroupBy(c => c.Guid)
            .ToDictionary(g => g.Key, g => g.MaxBy(c => c.LastSeen)!.Guild!);
        // A live check-in may carry the talent code before the saved file has it, or the other way round.
        // Some things only come with the saved file (talent picks, stats) or only with some check-ins
        // (the talent code), so each is taken from the newest snapshot that has it.
        string Newest(string guid, Func<Snapshot, string> field) =>
            data.Characters.Where(c => c.Guid == guid && field(c).Length > 0).MaxBy(c => c.LastSeen) is { } n ? field(n) : "";
        Func<CharacterGear> MakeGear(Inventory? inv, string classFile, string spec) => () => CharacterGear.From(inv, info, flavor, classFile, spec);

        var rows = new List<CharacterRow>();
        foreach (var guid in snaps.Keys.Union(events.Keys))
        {
            snaps.TryGetValue(guid, out var s);
            var all = events.GetValueOrDefault(guid) ?? [];
            var levels = all.Where(e => e.Kind == "level").ToList();
            var last = all.LastOrDefault();
            if (s is null && last is null) continue;
            var hardcore = (s?.Hardcore ?? false) || all.Any(e => e.Hardcore);
            var level = Math.Max(s?.Level ?? 0, all.Count > 0 ? all.Max(e => e.Level) : 0);
            // The newest of the snapshot and the last event says what the character has now.
            var newer = last is not null && last.At > (s?.LastSeen ?? 0) ? last : null;

            rows.Add(new CharacterRow
            {
                Guid = guid,
                Name = s?.Char is { Length: > 0 } n ? n : last?.Char ?? "?",
                Realm = s?.Realm ?? last!.Realm,
                Level = level,
                ClassName = Pick(s?.Class, last?.Class),
                ClassFile = Pick(s?.ClassFile, last?.ClassFile),
                Race = Pick(s?.Race, last?.Race),
                RaceFile = Pick(s?.RaceFile, last?.RaceFile),
                Sex = Pick(s?.Sex, last?.Sex) is { Length: > 0 } sex ? sex : "male",
                Spec = Pick(s?.Spec, last?.Spec) is var spec && spec != Pick(s?.Class, last?.Class) ? spec : "", // old Forever data had the class as spec
                Role = s?.Role ?? "",
                IsRetail = flavor == Flavors.Retail,
                ItemLevel = s?.ItemLevel is > 0 ? s.ItemLevel : last?.ItemLevel ?? 0,
                Played = Math.Max(s?.Played ?? 0, all.Count > 0 ? all.Max(e => e.Played) : 0),
                Zone = Pick(s?.Zone, last?.Zone),
                ZoneType = s?.Zone is { Length: > 0 } ? s.ZoneType : last?.ZoneType ?? "", // from where the zone came from
                Professions = Pick(s?.Professions, last?.Professions),
                Talents = Pick(s?.Talents, last?.Talents),
                LastSeen = Math.Max(s?.LastSeen ?? 0, last?.At ?? 0),
                Hardcore = hardcore,
                Ruleset = Pick(s?.Ruleset, last?.Ruleset) is { Length: > 0 } rs ? rs : hardcore ? "Hardcore" : "Normal",
                Dead = all.Any(e => e.Kind == "death"),
                Money = newer is { Money: > 0 } ? newer.Money : s?.Money ?? last?.Money ?? 0,
                Crests = Pick(s?.Crests, last?.Crests),
                Keystone = newer is { Keystone.Length: > 0 } ? newer.Keystone : Pick(s?.Keystone, last?.Keystone),
                MplusScore = newer is { MplusScore: > 0 } ? newer.MplusScore : s?.MplusScore is > 0 ? s.MplusScore : last?.MplusScore ?? 0,
                PvpRatings = newer is { PvpRatings.Length: > 0 } ? newer.PvpRatings : Pick(s?.PvpRatings, last?.PvpRatings),
                Guild = guilds.GetValueOrDefault(guid) ?? all.LastOrDefault(e => e.Guild is not null)?.Guild ?? "",
                Surname = Pick(s?.Surname, last?.Surname),
                TalentPicks = Newest(guid, c => c.TalentPicks),
                TalentExport = Newest(guid, c => c.TalentExport),
                TalentCalc = Newest(guid, c => c.TalentCalc),
                Region = Newest(guid, c => c.Region),
                Stats = Newest(guid, c => c.Stats),
                Levels = levels
                    .OrderByDescending(e => e.Level)
                    .Select(e => new LevelRow($"{e.Level - 1} to {e.Level}", e.Seconds > 0 ? Format.Duration(e.Seconds) : "?", e.Zone, When(e.At)))
                    .ToList(),
                Highlights = all
                    .Where(e => HighlightText.Kinds.Contains(e.Kind))
                    .OrderByDescending(e => e.At)
                    .Select(Highlight)
                    .ToList(),
                Info = info,
                Inventory = inventories.GetValueOrDefault(guid),
                MakeGear = MakeGear(inventories.GetValueOrDefault(guid), Pick(s?.ClassFile, last?.ClassFile), Pick(s?.Spec, last?.Spec)),
                Leveling = CharacterLeveling.From(all, level, xpSnaps.GetValueOrDefault(guid), data.XpTable, estimate: flavor == Flavors.Forever),
            });
        }

        return rows.OrderByDescending(r => r.LastSeen).ToList();
    }

    // A highlight row, keeping its event so it can be posted from the details. Boss kills say who
    // was there, deaths what was lost.
    static HighlightRow Highlight(Entry e)
    {
        var row = Row(e);
        row.Source = e;
        row.Detail = e.Kind switch
        {
            "boss" when e.Members.Count > 0 => "With " + string.Join(", ", e.Members.Select(m => $"{m.Name} ({m.Level} {m.Class})".Replace(" ()", ""))),
            "boss" when e.GroupSize > 5 => $"With a raid of {e.GroupSize}",
            "duel" or "makgora" when e.OpponentText.Length > 0 => char.ToUpperInvariant(e.OpponentText[0]) + e.OpponentText[1..],
            "death" => string.Join(", ", new[] { e.Played > 0 ? $"{Format.Played(e.Played)} played" : "", $"lost {Format.Money(e.Gold)}", e.Zone }.Where(p => p.Length > 0)),
            _ => "",
        };
        return row;
    }

    // The text is HighlightText's (shared with the stream overlay); the glyph and color are the app's.
    static HighlightRow Row(Entry e)
    {
        var (verb, subject) = HighlightText.Of(e);
        var (glyph, color) = e.Kind switch
        {
            "boss" => ("", 0xFBBF24),
            "loot" => ("", 0),
            "duel" when e.Lost => ("", 0x94A3B8),
            "duel" => ("", 0x93C5FD),
            "bg" when e.Lost => ("", 0x94A3B8),
            "bg" => ("", 0x93C5FD),
            "makgora" when e.Lost => ("", 0x94A3B8),
            "mplus" => ("", 0xC084FC),
            "pvp" => ("", 0xF87171),
            "mount" => ("", 0x4ADE80),
            _ => ("", 0xF87171), // Mak'gora wins, deaths
        };
        var brush = e.Kind == "loot" ? HighlightRow.QualityBrush(e.Quality) ?? ClassColors.Hex(0x0070DD) : ClassColors.Hex(color);
        return new(glyph, brush, verb, subject, When(e.At), e.Kind is "loot" or "mount" ? e.ItemId : 0);
    }

    // Forever talent picks, "spellID~rank~maxRank~tree,...": a chip per talent ("5/5" until
    // Wowhead gives the name and icon), ordered by tree.
    static IReadOnlyList<IconChip> TalentChips(string picks) => picks.Length == 0 ? [] : picks.Split(',')
        .Select(p => p.Split('~'))
        .Where(p => p.Length >= 4 && int.TryParse(p[0], out _))
        .OrderBy(p => int.TryParse(p[3], out var t) ? t : 0)
        .Select(p => new IconChip($"{p[1]}/{p[2]}") { Tag = int.Parse(p[0]) })
        .ToList();

    // "id~qty~name,..." from newer addons; older ones wrote "Name 45, Name 120" (no icons then).
    static IReadOnlyList<IconChip> CrestChips(string crests)
    {
        if (crests.Length == 0) return [];
        if (!crests.Contains('~'))
            return crests.Split(", ").Select(c => new IconChip(c)).ToList();
        return crests.Split(',').Select(c => c.Split('~')).Where(p => p.Length >= 3)
            .Select(p => new IconChip(p[1], null, p[2]) { Tag = int.TryParse(p[0], out var id) ? id : 0 })
            .ToList();
    }

    // "Mining 104, Blacksmithing 96" with each profession's icon.
    static IReadOnlyList<IconChip> ProfessionChips(string professions)
    {
        if (professions is "" or "None") return [];
        return professions.Split(", ").Select(p =>
        {
            var space = p.LastIndexOf(' ');
            var name = space > 0 ? p[..space] : p;
            var icon = Wowhead.ProfessionIcons.TryGetValue(name, out var i) ? Wowhead.IconUrl(i, "small") : null;
            return new IconChip(p, icon, name);
        }).ToList();
    }

    static string When(long at) => DateTimeOffset.FromUnixTimeSeconds(at).LocalDateTime.ToString("MMM d, HH:mm");
    static string Pick(string? a, string? b) => a is { Length: > 0 } ? a : b ?? "";
}
