using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WoWCharacterTracker.ViewModels;

sealed class CharacterRow : Observable
{
    string? pictureUrl;

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
    public string TalentCalc { get; init; } = ""; // Forever: Wowhead classic-style code, "--505..."
    public bool HasTalentLink => HasTalentExport || TalentCalc.Length > 0;
    public string Stats { get; init; } = "";        // "Strength~120,Crit~5.2%,..."
    public IReadOnlyList<(string Name, string Value)> StatList => Stats.Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(p => p.Split('~')).Where(p => p.Length == 2).Select(p => (p[0], p[1])).ToList();
    public IReadOnlyList<IconChip> StatChips => StatList.Select(s => new IconChip($"{s.Name} {s.Value}")).ToList();
    public bool HasStats => Stats.Length > 0;
    public string FullName => Surname.Length > 0 ? $"{Name} {Surname}" : Name;

    public string MoneyText => Money > 0 ? Format.Money(Money) : "-";
    public bool HasMoney => Money > 0;
    public IReadOnlyList<Coin> MoneyCoins => Coin.Full(Money);
    public IReadOnlyList<Coin> ShortCoins => Coin.Short(Money);
    public bool HasCrests => Crests.Length > 0;
    public IReadOnlyList<IconChip> CrestList { get; private set; } = [];
    public IReadOnlyList<IconChip> ProfessionList { get; private set; } = [];
    public bool HasProfessions => ProfessionList.Count > 0;

    // Fills in icons and tooltips: crests and items from Wowhead, professions from the built-in list.
    public async Task LoadIconsAsync(Flavor flavor)
    {
        CrestList = CrestChips(Crests);
        ProfessionList = ProfessionChips(Professions);
        TalentPickList = TalentChips(TalentPicks);
        Raise(nameof(TalentPickList));
        Raise(nameof(HasTalentPicks));
        Raise(nameof(CrestList));
        Raise(nameof(ProfessionList));
        Raise(nameof(HasProfessions));

        foreach (var chip in CrestList.Where(c => c.Tag > 0))
            if (await Wowhead.GetAsync("currency", chip.Tag, flavor) is { } info)
            {
                chip.IconUrl = Wowhead.IconUrl(info.Icon, "small");
                chip.Tooltip = info.Tooltip.Length > 0 ? info.Tooltip : info.Name;
            }
        foreach (var chip in TalentPickList)
            if (await Wowhead.GetAsync("spell", chip.Tag, flavor) is { } info)
            {
                chip.IconUrl = Wowhead.IconUrl(info.Icon, "small");
                chip.Text = $"{info.Name} {chip.Text}";
                chip.Tooltip = info.Tooltip.Length > 0 ? info.Tooltip : info.Name;
            }
        foreach (var h in Highlights.Where(h => h.ItemId > 0))
            if (await Wowhead.GetAsync("item", h.ItemId, flavor) is { } info)
            {
                h.IconUrl = Wowhead.IconUrl(info.Icon, "small");
                h.Tooltip = info.Tooltip;
                if (HighlightRow.QualityBrush(info.Quality) is { } q) h.Brush = q;
            }
    }
    public required IReadOnlyList<LevelRow> Levels { get; init; }
    public required IReadOnlyList<HighlightRow> Highlights { get; init; }

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
    public string Description => string.Join(" ", new[] { Race, Spec, ClassName }.Where(s => s.Length > 0));
    public bool HasLevels => Levels.Count > 0;
    public bool HasHighlights => Highlights.Count > 0;
    public bool IsTest => TestCharacters.IsTest(Guid);
    public string? Tag => IsTest ? "TEST" : Dead ? "DEAD" : Hardcore ? "HARDCORE" : null;

    public SolidColorBrush ClassBrush => ClassColors.Brush(ClassColors.For(ClassFile));
    public SolidColorBrush ClassSoftBrush => ClassColors.Brush(ClassColors.For(ClassFile), 0x26);

    // Segoe Fluent Icons glyphs: shield, heart, lightning bolt.
    public string RoleGlyph => Role switch { "TANK" => "", "HEALER" => "", "DAMAGER" => "", _ => "" };
    public SolidColorBrush RoleBrush => Role switch
    {
        "TANK" => ClassColors.Hex(0x94A3B8),
        "HEALER" => ClassColors.Hex(0x4ADE80),
        _ => ClassColors.Hex(0xF87171),
    };

    public string? PictureUrl { get => pictureUrl; set => Set(ref pictureUrl, value); }

    public async Task LoadPictureAsync(Flavor flavor)
    {
        PictureUrl = CharacterArt.ClassIconUrl(ClassFile);
        if (await CharacterArt.PictureUrlAsync(flavor, Region, Realm, Name, RaceFile, Sex, ClassFile) is { } url) PictureUrl = url;
    }

    // One row per character: the latest snapshot, filled in from events when there isn't one yet.
    public static List<CharacterRow> Build(TrackerData data)
    {
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

        var rows = new List<CharacterRow>();
        foreach (var guid in snaps.Keys.Union(events.Keys))
        {
            snaps.TryGetValue(guid, out var s);
            var all = events.GetValueOrDefault(guid) ?? [];
            var levels = all.Where(e => e.Kind == "level").ToList();
            var last = all.LastOrDefault();
            if (s is null && last is null) continue;

            rows.Add(new CharacterRow
            {
                Guid = guid,
                Name = s?.Char is { Length: > 0 } n ? n : last?.Char ?? "?",
                Realm = s?.Realm ?? last!.Realm,
                Level = Math.Max(s?.Level ?? 0, all.Count > 0 ? all.Max(e => e.Level) : 0),
                ClassName = Pick(s?.Class, last?.Class),
                ClassFile = Pick(s?.ClassFile, last?.ClassFile),
                Race = Pick(s?.Race, last?.Race),
                RaceFile = Pick(s?.RaceFile, last?.RaceFile),
                Sex = Pick(s?.Sex, last?.Sex) is { Length: > 0 } sex ? sex : "male",
                Spec = Pick(s?.Spec, last?.Spec) is var spec && spec != Pick(s?.Class, last?.Class) ? spec : "", // old Forever data had the class as spec
                Role = s?.Role ?? "",
                ItemLevel = s?.ItemLevel is > 0 ? s.ItemLevel : last?.ItemLevel ?? 0,
                Played = Math.Max(s?.Played ?? 0, all.Count > 0 ? all.Max(e => e.Played) : 0),
                Zone = Pick(s?.Zone, last?.Zone),
                Professions = Pick(s?.Professions, last?.Professions),
                Talents = Pick(s?.Talents, last?.Talents),
                LastSeen = Math.Max(s?.LastSeen ?? 0, last?.At ?? 0),
                Hardcore = (s?.Hardcore ?? false) || all.Any(e => e.Hardcore),
                Ruleset = Pick(s?.Ruleset, last?.Ruleset) is { Length: > 0 } rs ? rs
                    : (s?.Hardcore ?? false) || all.Any(e => e.Hardcore) ? "Hardcore" : "Normal",
                Dead = all.Any(e => e.Kind == "death"),
                // The newest of the snapshot and the last event says what the character has now.
                Money = last is not null && last.At > (s?.LastSeen ?? 0) && last.Money > 0 ? last.Money : s?.Money ?? last?.Money ?? 0,
                Crests = Pick(s?.Crests, last?.Crests),
                Guild = guilds.GetValueOrDefault(guid) ?? all.LastOrDefault(e => e.Guild is not null)?.Guild ?? "",
                Surname = Pick(s?.Surname, last?.Surname),
                TalentPicks = Newest(guid, c => c.TalentPicks),
                TalentExport = Newest(guid, c => c.TalentExport),
                TalentCalc = Newest(guid, c => c.TalentCalc),
                Region = Newest(guid, c => c.Region),
                Stats = Newest(guid, c => c.Stats),
                Levels = levels
                    .OrderByDescending(e => e.Level)
                    .Select(e => new LevelRow($"{e.Level - 1} to {e.Level}", Format.Duration(e.Seconds), e.Zone, When(e.At)))
                    .ToList(),
                Highlights = all
                    .Where(e => e.Kind is "boss" or "loot" or "mount" or "death" or "duel" or "makgora" or "mplus" or "pvp")
                    .OrderByDescending(e => e.At)
                    .Select(Highlight)
                    .ToList(),
            });
        }

        return rows.OrderByDescending(r => r.LastSeen).ToList();
    }

    static HighlightRow Highlight(Entry e)
    {
        var row = MakeHighlight(e);
        row.Source = e;
        return row;
    }

    static HighlightRow MakeHighlight(Entry e) => e.Kind switch
    {
        "boss" => new("", ClassColors.Hex(0xFBBF24), "Killed ", e.Boss + (e.Instance.Length > 0 ? $" ({e.Instance})" : ""), When(e.At)),
        "loot" => new("", HighlightRow.QualityBrush(e.Quality) ?? ClassColors.Hex(0x0070DD), "Looted ", e.Item, When(e.At), e.ItemId),
        "duel" => new("", ClassColors.Hex(0x93C5FD), "Won a duel against ", e.Opponent, When(e.At)),
        "makgora" => new("", ClassColors.Hex(0xF87171), "Won a Mak'gora against ", e.Opponent, When(e.At)),
        "mplus" => new("", ClassColors.Hex(0xC084FC), "Reached ", $"{e.Step} Mythic+ rating ({e.Rating})", When(e.At)),
        "pvp" => new("", ClassColors.Hex(0xF87171), "Reached ", $"{e.Step} in {e.Bracket} ({e.Rating})", When(e.At)),
        "mount" => new("", ClassColors.Hex(0x4ADE80), e.Bought ? "Bought " : "Got ", e.Mount, When(e.At), e.ItemId),
        _ => new("", ClassColors.Hex(0xF87171), "", $"Died at level {e.Level}" + (e.Killer.Length > 0 ? $", killed by {e.Killer}" : ""), When(e.At)),
    };

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
