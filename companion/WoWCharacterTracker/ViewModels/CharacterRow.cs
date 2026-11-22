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
    // A Battle.net friend's character (the Friends page): shown, never yours to post, tag or remove.
    public bool IsFriend { get; set; }
    public bool IsOwn => !IsFriend;
    public string FriendTag { get; set; } = "";
    string friendName = "";
    public string FriendName { get => friendName; set => Set(ref friendName, value); } // nickname, else the BattleTag
    // When your addon last heard from that friend (their contact's `seen`), shown as "updated 5m ago".
    public long FriendSynced { get; set; }
    public string FriendSharesText { get; set; } = ""; // "Shares with you: gear, talents" on the friend's header
    public string FriendSyncedText => FriendSynced > 0 ? $"updated {Format.Ago(FriendSynced)}" : "not updated yet";
    bool isHiddenFriend;
    public bool IsHiddenFriend { get => isHiddenFriend; set => Set(ref isHiddenFriend, value); } // hidden on the Friends page, shown anyway
    // A friend's character raced against one of yours (FriendsViewModel), null when none share a level.
    public LevelRaceRow? Rival { get; set; }
    public bool HasRival => Rival is not null;
    public bool ShowsGold => IsOwn || HasMoney; // a friend who doesn't share gold has none to show
    public string Guild { get; init; } = "";
    public bool HasGuild => Guild.Length > 0;
    public string Surname { get; init; } = "";
    public string TalentPicks { get; init; } = ""; // Forever: "spellID~rank~maxRank~tree,..."
    public IReadOnlyList<IconChip> TalentPickList { get; private set; } = [];
    public bool HasTalentPicks => TalentPickList.Count > 0;
    public string TalentExport { get; init; } = ""; // the game's loadout string (retail)
    public bool HasTalentExport => TalentExport.Length > 0;
    public string Region { get; set; } = ""; // "eu", "us"... for the Retail render and the armory (friends get yours)
    public string RealmText => Realms.Label(Region, Realm);
    public string ArmoryGame { get; init; } = ""; // Flavor.ArmoryGame
    public string? ArmoryUrl => IsTest ? null : BlizzardArmory.ProfileUrl(ArmoryGame, Region, Name, Surname);
    public bool HasArmoryLink => ArmoryUrl is not null;
    public string TalentCalc { get; init; } = ""; // Forever: Wowhead classic-style code, "--505..."
    public bool HasTalentLink => HasTalentExport || TalentCalc.Length > 0;
    // Forever dual spec: the build that isn't active, null without a second spec (or before both were seen).
    public SpecBuildRow? OtherSpec { get; private set; }
    public bool HasOtherSpec => OtherSpec is not null;
    // With two specs, one build shows at a time: the active one, or the other after the switch button.
    bool showOtherSpec;
    public bool ShowOtherSpec
    {
        get => showOtherSpec;
        set
        {
            if (!Set(ref showOtherSpec, value)) return;
            Raise(nameof(ShowsActiveTalents)); Raise(nameof(TalentsLabel)); Raise(nameof(SwitchSpecText));
        }
    }
    public bool ShowsActiveTalents => !ShowOtherSpec;
    RelayCommand? switchSpec;
    public RelayCommand SwitchSpecCommand => switchSpec ??= new RelayCommand(_ => ShowOtherSpec = !ShowOtherSpec);
    // The active spec's name: the group the other build isn't.
    string ActiveSpecName => OtherSpec?.Build.Group == 1 ? "SECONDARY" : "PRIMARY";
    public string TalentsLabel => OtherSpec switch
    {
        null => "TALENTS",
        { } o when ShowOtherSpec => $"{o.Build.Name.ToUpperInvariant()} TALENTS (INACTIVE)",
        _ => $"ACTIVE TALENTS ({ActiveSpecName})",
    };
    public string SwitchSpecText => ShowOtherSpec ? "Show active spec" : $"Show {OtherSpec?.Build.Name.ToLowerInvariant()} spec";
    public string Stats { get; init; } = "";        // "Strength~120,Crit~5.2%,..."
    public string Recipes { get; init; } = "";      // "Alchemy~Potions=2330/Elixirs=2329" lines (ProfessionSkills.Recipes)
    IReadOnlyDictionary<string, IReadOnlyList<RecipeGroup>>? recipeLists;
    public IReadOnlyList<RecipeGroup> RecipesOf(string profession) =>
        (recipeLists ??= ProfessionSkills.Recipes(Recipes)).GetValueOrDefault(profession) ?? [];
    // Factions and standings (Reputations.Parse), saved file only: renown first, then the highest.
    // The overview shows the first few until "Show all" is clicked.
    public string Reputations { get; init; } = "";
    const int ReputationsShownFirst = 8;
    IReadOnlyList<ReputationRow>? reputationList;
    public IReadOnlyList<ReputationRow> ReputationList => reputationList ??=
        [.. Services.Reputations.Sorted(Services.Reputations.Parse(Reputations)).Select(r => new ReputationRow(r))];
    public bool HasReputations => ReputationList.Count > 0;
    bool allReputations;
    public IReadOnlyList<ReputationRow> ReputationsShown => allReputations ? ReputationList : [.. ReputationList.Take(ReputationsShownFirst)];
    public bool HasMoreReputations => !allReputations && ReputationList.Count > ReputationsShownFirst;
    public string MoreReputationsText => $"Show all {ReputationList.Count}";
    RelayCommand? allReputationsCommand;
    public RelayCommand ShowAllReputationsCommand => allReputationsCommand ??= new RelayCommand(_ =>
    {
        allReputations = true;
        Raise(nameof(ReputationsShown));
        Raise(nameof(HasMoreReputations));
    });
    // The lists below are made once per row (its data never changes; a sync builds new rows), not on
    // every read: bindings, sorting and the card all read them.
    IReadOnlyList<(string Name, string Value)>? statList;
    public IReadOnlyList<(string Name, string Value)> StatList => statList ??= Stats.Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(p => p.Split('~')).Where(p => p.Length == 2).Select(p => (p[0], p[1])).ToList();
    IReadOnlyList<IconChip>? statChips;
    public IReadOnlyList<IconChip> StatChips => statChips ??= StatList.Select(s => new IconChip($"{s.Name} {s.Value}",
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
    IReadOnlyList<IconChip>? ratingChips;
    public IReadOnlyList<IconChip> RatingChips => ratingChips ??= Format.Ratings(MplusScore, PvpRatings).Select(r => new IconChip($"{r.Name} {r.Rating}",
        Wowhead.RatingIcons.TryGetValue(r.Name, out var icon) ? Wowhead.IconUrl(icon, "small") : null,
        r.Name == "Mythic+" ? "Mythic+ score" : $"{r.Name} rating")).ToList();
    string? ratingsText;
    public string RatingsText => ratingsText ??= Format.RatingsText(MplusScore, PvpRatings);
    public bool HasRatings => RatingsText.Length > 0;
    // The highest of the Mythic+ score and the PvP ratings, for sorting by rating.
    int? bestRating;
    public int BestRating => bestRating ??= Format.Ratings(MplusScore, PvpRatings).Select(r => r.Rating).DefaultIfEmpty(0).Max();
    public IReadOnlyList<IconChip> CrestList { get; private set; } = [];
    public IReadOnlyList<IconChip> ProfessionList { get; private set; } = [];
    public bool HasProfessions => ProfessionList.Count > 0;
    // Yours always ("None yet" without any); a friend's only when they share them.
    public bool ShowsProfessions => IsOwn || HasProfessions;
    public required IReadOnlyList<LevelRow> Levels { get; init; }
    public required IReadOnlyList<HighlightRow> Highlights { get; init; }
    public IReadOnlyList<Entry> Events { get; init; } = []; // oldest first (ArmoryExport)
    public Inventory? Inventory { get; init; } // what the character carries, as saved (the Items page)
    // The Gear and bags tab, built the first time it's shown: it makes an item (and a Wowhead
    // lookup) per slot, which for dozens of characters at startup froze the window.
    public Func<CharacterGear>? MakeGear { private get; init; }
    CharacterGear? gear;
    public CharacterGear Gear => gear ??= MakeGear?.Invoke() ?? CharacterGear.Empty;

    // The list is rebuilt on every sync with new data; when this character's saved inventory is the
    // same one (Tracker keeps unchanged files as they were read), the gear tab built for the old row
    // is taken over instead of building every bag and bank slot again.
    public void KeepGearFrom(CharacterRow old)
    {
        if (old.gear is { } built && Inventory is not null && ReferenceEquals(old.Inventory, Inventory)
            && old.ClassFile == ClassFile && old.Spec == Spec)
            gear = built;
    }
    public CharacterLeveling Leveling { get; init; } = new();

    // The details are split into tabs: "overview", "gear" (gear and bags), "leveling", "highlights" and "notes".
    string detailTab = "overview";
    public string DetailTab
    {
        get => detailTab;
        set
        {
            if (!Set(ref detailTab, value)) return;
            Raise(nameof(IsOverviewTab)); Raise(nameof(IsGearTab)); Raise(nameof(IsLevelingTab)); Raise(nameof(IsNotesTab)); Raise(nameof(IsHighlightsTab));
        }
    }
    public bool IsHighlightsTab { get => DetailTab == "highlights"; set { if (value) DetailTab = "highlights"; } }

    // The Overview shows the newest few; the Highlights tab has them all.
    const int HighlightPreview = 5;
    public IReadOnlyList<HighlightRow> RecentHighlights => Highlights.Count > HighlightPreview ? Highlights.Take(HighlightPreview).ToList() : Highlights;
    public bool HasMoreHighlights => Highlights.Count > HighlightPreview;
    public string AllHighlightsText => $"All {Highlights.Count} highlights";
    RelayCommand? showHighlights;
    public RelayCommand ShowHighlightsCommand => showHighlights ??= new RelayCommand(_ => DetailTab = "highlights");
    public bool IsNotesTab { get => DetailTab == "notes"; set { if (value) DetailTab = "notes"; } }

    // The user's own notes for the character (the Notes tab), stored by the app's config.
    string note = "";
    Action<string>? saveNote;
    public string Note
    {
        get => note;
        set { if (Set(ref note, value)) saveNote?.Invoke(value); }
    }
    public void SetNote(string text, Action<string> save) { note = text; saveNote = save; }
    public bool IsOverviewTab { get => DetailTab == "overview"; set { if (value) DetailTab = "overview"; } }
    public bool IsGearTab { get => DetailTab == "gear"; set { if (value) DetailTab = "gear"; } }
    public bool IsLevelingTab { get => DetailTab == "leveling"; set { if (value) DetailTab = "leveling"; } }

    // Fills in icons and tooltips: crests and items from Wowhead, professions from the built-in list.
    // Loaded once, when the character is opened (its overview, card and talent posts use them).
    Task? icons;
    public Task LoadIconsAsync(Flavor flavor) => icons ??= LoadIcons(flavor);

    // The tab's shared lookup of everything from the game (FlavorViewModel.Lookup).
    public GameLookup? Lookup { private get; init; }

    // Crests, talents, loot and recipes are things from the game, so they get the same ItemInfo (and
    // the same tooltip and Wowhead link, Ui/ItemTip) as items in bags.
    public ItemInfo? Find(string type, int id) => id > 0 ? Lookup?.For(type, id) : null;

    async Task LoadIcons(Flavor flavor)
    {
        CrestList = CrestChips(Crests);
        foreach (var chip in CrestList) chip.Info = Find("currency", chip.Tag);
        ProfessionList = ProfessionChips(Professions);
        TalentPickList = TalentChips(TalentPicks);
        foreach (var h in Highlights) h.Info = Find("item", h.ItemId);
        Raise(nameof(TalentPickList));
        Raise(nameof(HasTalentPicks));
        Raise(nameof(CrestList));
        Raise(nameof(ProfessionList));
        Raise(nameof(HasProfessions));
        Raise(nameof(ShowsProfessions));

        foreach (var chip in CrestList.Where(c => c.Info is not null))
        {
            chip.Info!.Fallback(chip.Tooltip);
            await chip.Info.LoadAsync(flavor);
            chip.IconUrl = chip.Info.IconUrl;
        }
        await LoadTalentIconsAsync(TalentPickList, Find, flavor);
        if (OtherSpec is not null) await OtherSpec.LoadIconsAsync(flavor);
        foreach (var h in Highlights) await h.LoadIconAsync(flavor);
    }

    // A talent chip's name and icon from Wowhead ("5/5" becomes "Deflection 5/5").
    internal static async Task LoadTalentIconsAsync(IReadOnlyList<IconChip> chips, Func<string, int, ItemInfo?> find, Flavor flavor)
    {
        foreach (var chip in chips) chip.Info = find("spell", chip.Tag);
        foreach (var chip in chips.Where(c => c.Info is not null))
        {
            await chip.Info!.LoadAsync(flavor);
            chip.IconUrl = chip.Info.IconUrl;
            if (chip.Info.Name.Length > 0) chip.Text = $"{chip.Info.Name} {chip.Text}";
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
    // The XP bar in whole percent (under the level), from the newest snapshot that has it, and when that was.
    public int? LevelPercent { get; init; }
    public long LevelPercentAt { get; init; }
    public string LevelPercentText => LevelPercent is { } p ? $"{p}%" : "";
    public string? LevelPercentTip => LevelPercent is { } p ? $"{p}% into level {Level}, updated {Format.Ago(LevelPercentAt)}" : null;
    public string PlayedText => Played > 0 ? Format.Played(Played) : "-";
    public string PlayedLine => Played > 0 ? $"{Format.Played(Played)} played" : "";
    public string LastSeenText => Format.Ago(LastSeen) is { Length: > 0 } s ? s : "-";
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

    // "Alliance", "Horde" or "" (Factions.Of): the row's edge, the crest on the portrait, the details' glow.
    public string Faction { get; init; } = "";
    public bool HasFaction => Faction.Length > 0;
    public SolidColorBrush? FactionBrush => FactionColors.Edge(Faction);
    public Brush? FactionGlow => FactionColors.Glow(Faction);
    public string? FactionCrestUrl => Factions.CrestIcon(Faction) is { } icon ? Wowhead.IconUrl(icon, "small") : null;

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
        if (IsFriend)
        {
            PictureUrl ??= CharacterArt.ClassIconUrl(ClassFile);
            var armory = await CharacterPicture.ArmoryAsync(flavor, Picture, ArmorySd);
            HasGamePortrait = armory is not null;
            if (armory is not null && !useStandard) { PictureUrl = armory; return; }
        }
        else
        {
            HasGamePortrait = CharacterPicture.GamePortrait(flavor, Guid, standardChosen: false) is not null;
            if (CharacterPicture.GamePortrait(flavor, Guid, useStandard) is { } own) { PictureUrl = own; return; }
            PictureUrl = CharacterArt.ClassIconUrl(ClassFile);
        }
        if (await CharacterPicture.StandardAsync(flavor, Picture) is { } url) PictureUrl = url;
    }

    // Which picture to show when there's more than the standard (race) one: yours on Forever, the
    // in-game portrait once one was captured; a friend's, the armory picture. The choice is saved
    // (Config.StandardPortraits) through the callback.
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
        RaisePortrait();
    }

    void RaisePortrait()
    {
        Raise(nameof(UseGamePortrait)); Raise(nameof(UseStandardPortrait));
        Raise(nameof(UseArmoryHd)); Raise(nameof(UseArmorySd));
        if (pictureFlavor is { } f) _ = LoadPictureAsync(f);
    }

    // A friend's armory picture: old (SD) or new (HD) models. The Friends page's switch sets it for
    // all (Config.ArmorySd), a character's own choice overrides it (Config.ArmoryModels, only kept
    // while it differs, so picking the switch's again follows the switch).
    bool armorySdDefault;
    bool? armorySdOverride;
    Action<bool?>? armoryChosen;
    public bool ArmorySd => armorySdOverride ?? armorySdDefault;
    public bool UseArmoryHd { get => !useStandard && !ArmorySd; set { if (value) ChooseArmory(false); } }
    public bool UseArmorySd { get => !useStandard && ArmorySd; set { if (value) ChooseArmory(true); } }

    public void SetArmoryChoice(bool sdDefault, bool? sdOverride, Action<bool?> chosen) =>
        (armorySdDefault, armorySdOverride, armoryChosen) = (sdDefault, sdOverride, chosen);

    // The switch for all friends moved (its owner drops the saved choices that now match it).
    public void SetArmoryDefault(bool sd)
    {
        if (armorySdDefault == sd) return;
        armorySdDefault = sd;
        if (armorySdOverride == sd) armorySdOverride = null;
        RaisePortrait();
    }

    void ChooseArmory(bool sd)
    {
        if (useStandard) { useStandard = false; portraitChosen?.Invoke(false); }
        armorySdOverride = sd == armorySdDefault ? null : sd;
        armoryChosen?.Invoke(armorySdOverride);
        RaisePortrait();
    }

    // One row per character: the latest snapshot, filled in from events when there isn't one yet.
    // lookup: the tab's shared things from the game (icons and names are looked up once).
    public static List<CharacterRow> Build(TrackerData data, Flavor flavor, GameLookup lookup)
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
        var snapsOf = data.Characters.ToLookup(c => c.Guid);
        string Newest(string guid, Func<Snapshot, string> field) =>
            snapsOf[guid].Where(c => field(c).Length > 0).MaxBy(c => c.LastSeen) is { } n ? field(n) : "";
        Func<CharacterGear> MakeGear(Inventory? inv, string classFile, string spec) => () => CharacterGear.From(inv, lookup, flavor, classFile, spec);

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
            // Played on the level being played: since the ding (played then), else what the addon saved
            // (a friend's shared levelTime). Nothing at max level.
            var played = Math.Max(s?.Played ?? 0, all.Count > 0 ? all.Max(e => e.Played) : 0);
            var ding = levels.Where(e => e.Level == level && e.Played > 0).MaxBy(e => e.At);
            var thisLevel = ding is not null && played > ding.Played ? played - ding.Played : s is { LevelTime: > 0 } && s.Level == level ? s.LevelTime : 0;
            var cap = Math.Max(xpSnaps.GetValueOrDefault(guid)?.MaxLevel ?? 0, s?.MaxLevel ?? 0);
            if (cap > 0 && level >= cap || all.Any(e => e.MaxLevel)) thisLevel = 0;

            var row = new CharacterRow
            {
                Guid = guid,
                Name = s?.Char is { Length: > 0 } n ? n : last?.Char ?? "?",
                Realm = s?.Realm ?? last!.Realm,
                Level = level,
                ClassName = Pick(s?.Class, last?.Class),
                ClassFile = Pick(s?.ClassFile, last?.ClassFile),
                Race = Pick(s?.Race, last?.Race),
                RaceFile = Pick(s?.RaceFile, last?.RaceFile),
                Faction = Factions.Of(Pick(s?.Faction, last?.Faction), Pick(s?.RaceFile, last?.RaceFile), Pick(s?.Race, last?.Race)),
                Sex = Pick(s?.Sex, last?.Sex) is { Length: > 0 } sex ? sex : "male",
                Spec = Pick(s?.Spec, last?.Spec) is var spec && spec != Pick(s?.Class, last?.Class) ? spec : "", // old Forever data had the class as spec
                Role = s?.Role ?? "",
                IsRetail = flavor.HasSpecs,
                ItemLevel = s?.ItemLevel is > 0 ? s.ItemLevel : last?.ItemLevel ?? 0,
                Played = played,
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
                ArmoryGame = flavor.ArmoryGame,
                Stats = Newest(guid, c => c.Stats),
                Recipes = Newest(guid, c => c.Recipes),
                Reputations = Newest(guid, c => c.Reputations),
                Levels = levels
                    .OrderByDescending(e => e.Level)
                    .Select(e => new LevelRow($"{e.Level - 1} to {e.Level}", e.Seconds > 0 ? Format.Duration(e.Seconds) : "?", e.Zone, When(e.At)))
                    .ToList(),
                Highlights = all
                    .Where(e => EventKinds.Highlights.Contains(e.Kind))
                    .OrderByDescending(e => e.At)
                    .Select(HighlightRow.From)
                    .ToList(),
                Events = all,
                Lookup = lookup,
                Inventory = inventories.GetValueOrDefault(guid),
                MakeGear = MakeGear(inventories.GetValueOrDefault(guid), Pick(s?.ClassFile, last?.ClassFile), Pick(s?.Spec, last?.Spec)),
                Leveling = CharacterLeveling.From(all, level, xpSnaps.GetValueOrDefault(guid), data.XpTable, estimate: flavor.EstimatesXp, thisLevel),
                LevelPercent = LevelStats.Percent(xpSnaps.GetValueOrDefault(guid), level),
                LevelPercentAt = xpSnaps.GetValueOrDefault(guid)?.LastSeen ?? 0,
            };
            // The saved file has the other spec, a check-in may already have swapped to it: then it's the active one.
            if (snapsOf[guid].Where(c => c.OtherSpec is not null).MaxBy(c => c.LastSeen)?.OtherSpec is { } other
                && other.TalentCalc != row.TalentCalc)
                row.OtherSpec = new SpecBuildRow(row, other);
            rows.Add(row);
        }

        return rows.OrderByDescending(r => r.LastSeen).ToList();
    }

    // Forever talent picks, "spellID~rank~maxRank~tree,...": a chip per talent ("5/5" until
    // Wowhead gives the name and icon), ordered by tree.
    internal static IReadOnlyList<IconChip> TalentChips(string picks) => picks.Length == 0 ? [] : picks.Split(',')
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

    // "Mining 104" with the profession's icon (the card shows these too), the max on hover.
    static IReadOnlyList<IconChip> ProfessionChips(string professions) => [.. ProfessionSkills.Parse(professions)
        .Select(p => new IconChip(p.Text, ProfessionSkills.IconUrl(p.Name), p.FullText))];

    static string When(long at) => DateTimeOffset.FromUnixTimeSeconds(at).LocalDateTime.ToString("MMM d, HH:mm", System.Globalization.CultureInfo.InvariantCulture); // English months, like the rest of the app
    static string Pick(string? a, string? b) => a is { Length: > 0 } ? a : b ?? "";
}
