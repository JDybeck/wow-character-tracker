using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;

namespace WoWCharacterTracker.ViewModels;

// The Friends page of one game tab: what Battle.net friends shared, as the same rows as your own
// characters (CharacterRow.Build), with a chip per friend, hiding, and the sharing settings (the
// addon's `share`, edited here with a newer time; changed() saves and writes Settings.lua).
// Friends' rows live only here, never in the tab's own lists, totals, posts or the stream.
sealed class FriendsViewModel : Observable
{
    readonly Flavor flavor;
    readonly GameLookup lookup;
    readonly Config config;
    readonly Func<FlavorSettings> settings;
    readonly Action changed;
    readonly Action save;
    TrackerData data = TrackerData.Empty;
    List<CharacterRow> allRows = [];

    // changed: a sharing setting or nickname changed (saves and tells the addon); save: an app-only change (hiding).
    public FriendsViewModel(Flavor flavor, GameLookup lookup, Config config, Func<FlavorSettings> settings, Action changed, Action save)
    {
        (this.flavor, this.lookup, this.config, this.settings, this.changed, this.save) = (flavor, lookup, config, settings, changed, save);
        Contacts = new ChipFilter("Friend", ShowRows);
        HideCommand = new RelayCommand(p => SetHidden(p, true));
        UnhideCommand = new RelayCommand(p => SetHidden(p, false));
        MainCommand = new RelayCommand(p => SetMain(p, true));
        NotMainCommand = new RelayCommand(p => SetMain(p, false));
        UnblockCommand = new RelayCommand(p => { if (p is string tag) Edit(s => s with { Blocked = [.. s.Blocked.Where(t => t != tag)] }); });
    }

    // --- friends' characters ---

    public ChipFilter Contacts { get; }
    public ObservableCollection<CharacterRow> Characters { get; } = [];
    CharacterRow? selectedCharacter;
    public CharacterRow? SelectedCharacter
    {
        get => selectedCharacter;
        set { if (Set(ref selectedCharacter, value)) _ = value?.LoadIconsAsync(flavor); }
    }
    public bool HasFriends => allRows.Count > 0;
    public int HiddenCount => allRows.Count(r => config.HiddenFriendCharacters.Contains(r.Guid));
    public bool HasHidden => HiddenCount > 0;
    public string HiddenText => $"{Format.Count(HiddenCount, "hidden character")}, {(ShowHidden ? "hide them" : "show them")}";

    bool showHidden;
    public bool ShowHidden { get => showHidden; set { if (Set(ref showHidden, value)) ShowRows(); } }
    public ICommand ToggleHiddenCommand => toggleHidden ??= new RelayCommand(_ => ShowHidden = !ShowHidden);
    ICommand? toggleHidden;
    public ICommand HideCommand { get; }
    public ICommand UnhideCommand { get; }
    public ICommand MainCommand { get; }
    public ICommand NotMainCommand { get; }

    string NameOf(string tag) => config.FriendNicknames.TryGetValue(tag, out var n) && n.Length > 0 ? n : tag;

    // Every friend character you keep and haven't hidden (Home lists the ones seen lately).
    public IEnumerable<CharacterRow> Shown => allRows.Where(r => !config.HiddenFriendCharacters.Contains(r.Guid));

    // After every sync: friends' rows rebuilt, the picked chip and open character kept.
    public void Show(TrackerData data)
    {
        this.data = data;
        var keep = SelectedCharacter?.Guid;
        var keepOpen = SelectedCharacter; // the list refreshes on every sync
        var rows = new List<CharacterRow>();
        // Friends don't share their region, and Battle.net friends who share play in yours.
        var region = data.Characters.Where(c => c.Region.Length > 0).MaxBy(c => c.LastSeen)?.Region ?? "";
        // Only friends whose characters you keep: unticking "Keep theirs" or blocking them hides what came before.
        foreach (var contact in data.Friends.Where(c => ContactFor(c.Tag).Receive && !Share.Blocked.Contains(c.Tag)))
            foreach (var row in CharacterRow.Build(new TrackerData(contact.Events, contact.Characters) { Inventories = contact.Inventories }, flavor, lookup))
            {
                row.IsFriend = true;
                row.FriendTag = contact.Tag;
                row.FriendSynced = contact.Seen;
                row.FriendSharesText = $"Shares with you: {ShareCategories.Summary(contact.Shares)}";
                if (row.Region.Length == 0) row.Region = region;
                var guid = row.Guid;
                row.SetPortraitChoice(config.StandardPortraits.Contains(guid), standard => { config.SetStandardPortrait(guid, standard); save(); });
                row.SetArmoryChoice(config.ArmorySd, config.ArmoryModels.TryGetValue(guid, out var sd) ? sd : null, choice => { config.SetArmoryModel(guid, choice); save(); });
                rows.Add(row);
            }
        AddRaces(rows, data);
        allRows = rows;
        ShowRows();
        SelectedCharacter = Characters.FirstOrDefault(c => c.Guid == keep);
        SelectedCharacter?.KeepTabFrom(keepOpen);
        BuildContactRows();
        BuildOwnCharacters();
        RaiseSharing();
    }

    // Each friend's character against the one of yours it shares the most levels with.
    static void AddRaces(List<CharacterRow> rows, TrackerData data)
    {
        var mine = data.Events.Where(e => e.Kind == "level").GroupBy(e => e.Guid)
            .Select(g => (Guid: g.Key, Timed: (IReadOnlyList<LevelStats.Level>)LevelStats.Timed(g), Last: g.MaxBy(e => e.At)!))
            .Where(c => c.Timed.Count > 0)
            .ToDictionary(c => c.Guid);
        if (mine.Count == 0) return;
        var candidates = mine.Values.Select(c => (c.Guid, c.Timed, LastSeen: c.Last.At)).ToList();
        foreach (var row in rows)
        {
            var theirs = LevelStats.Timed(row.Events);
            if (theirs.Count == 0 || LevelRace.PickRival(theirs, candidates) is not { } guid) continue;
            var rival = mine[guid];
            var snapshot = data.Characters.Where(c => c.Guid == guid).MaxBy(c => c.LastSeen);
            var name = snapshot?.Char is { Length: > 0 } n ? n : rival.Last.Char;
            var level = Math.Max(snapshot?.Level ?? 0, rival.Last.Level);
            row.Rival = LevelRaceRow.From(LevelRace.Compare(rival.Timed, theirs), row.Name, name, level);
        }
    }

    void ShowRows()
    {
        foreach (var r in allRows)
        {
            r.FriendName = NameOf(r.FriendTag);
            r.IsHiddenFriend = config.HiddenFriendCharacters.Contains(r.Guid);
            r.IsFriendMain = config.FriendMains.TryGetValue(r.FriendTag, out var main) && main == r.Guid;
        }
        Contacts.Fill(allRows, r => r.FriendTag, NameOf);
        BuildCards();
        Characters.Clear();
        foreach (var r in Ordered(allRows.Where(r => Contacts.Matches(r.FriendTag) && (ShowHidden || !config.HiddenFriendCharacters.Contains(r.Guid))), RankOf))
        {
            Characters.Add(r);
            _ = r.LoadPictureAsync(flavor);
        }
        Professions.Show(Characters, r => r.FriendName);
        Listed = FriendTotals.Of(Characters.Where(r => !r.IsHiddenFriend));
        foreach (var name in new[] { nameof(Listed), nameof(ShowsTotals), nameof(TotalGoldText), nameof(TotalCoins), nameof(HasTotalGold), nameof(TotalPlayedText), nameof(NotSharedText) })
            Raise(name);
        Raise(nameof(HasFriends));
        Raise(nameof(HiddenCount));
        Raise(nameof(HasHidden));
        Raise(nameof(HiddenText));
    }

    // Grouped by friend on the page (FriendsPage groups by FriendTag): friends in the dragged order
    // (rank), the rest by name, each friend's main first (when you picked one), then the one played
    // last, so the one they're on leads.
    internal static IEnumerable<CharacterRow> Ordered(IEnumerable<CharacterRow> rows, Func<string, int> rank) =>
        rows.OrderBy(r => rank(r.FriendTag))
            .ThenBy(r => r.FriendName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.FriendTag, StringComparer.Ordinal) // two friends with one nickname stay apart
            .ThenByDescending(r => r.IsFriendMain)
            .ThenByDescending(r => r.LastSeen)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase);

    // --- gold and played, added up ---

    // The listed friends' characters (the picked friend's, or everyone's), hidden ones left out: the
    // footer under the list, like the Characters page's.
    public FriendTotals Listed { get; private set; } = FriendTotals.Empty;
    public bool ShowsTotals => Listed.Money > 0 || Listed.Played > 0;
    public bool HasTotalGold => Listed.Money > 0;

    // "Total gold of 3 friends (12 characters):" followed by the coins; "Total gold of Zebbe (2 characters):" for one.
    public string TotalGoldText
    {
        get
        {
            var whose = Listed.Friends == 1 ? NameOf(Listed.ByFriend.Keys.First()) : Format.Count(Listed.WithGold, "friend");
            var count = Listed.ByFriend.Values.Where(f => f.SharesGold).Sum(f => f.Characters);
            return $"Total gold of {whose} ({Format.Count(count, "character")}):";
        }
    }
    public IReadOnlyList<Coin> TotalCoins => Coin.Full(Listed.Money);
    public string TotalPlayedText => Listed.Played > 0 ? $"Total played: {Format.Played(Listed.Played)}" : "";

    // Friends listed who keep their gold or level times to themselves, so they're not in the totals.
    public string NotSharedText
    {
        get
        {
            int noGold = Listed.Friends - Listed.WithGold, noPlayed = Listed.Friends - Listed.WithPlayed;
            var parts = new List<string>();
            if (noGold > 0) parts.Add($"{Format.Count(noGold, "friend")} {(noGold == 1 ? "doesn't" : "don't")} share gold");
            if (noPlayed > 0) parts.Add($"{Format.Count(noPlayed, "friend")} {(noPlayed == 1 ? "doesn't" : "don't")} share played time");
            return string.Join(", ", parts);
        }
    }

    // --- the grid of friends over the list ---

    // One card per friend in the order you drag them into (Config.FriendOrder, shared with the addon's
    // Friends tab), the others after by name. A click shows only that friend, a second click everyone.
    public ObservableCollection<FriendCard> Cards { get; } = [];
    public event Action<IReadOnlyList<string>>? OrderChanged;

    int RankOf(string tag) => config.FriendOrder.IndexOf(tag) is var i and >= 0 ? i : int.MaxValue;

    void BuildCards()
    {
        Cards.Clear();
        // Each friend's gold and played and their share of all friends' (hidden characters left out).
        var totals = FriendTotals.Of(allRows.Where(r => !config.HiddenFriendCharacters.Contains(r.Guid)));
        foreach (var g in allRows.GroupBy(r => r.FriendTag).OrderBy(g => RankOf(g.Key)).ThenBy(g => NameOf(g.Key), StringComparer.CurrentCultureIgnoreCase))
        {
            // Their main when you picked one, else the one played last; hidden ones only when all are.
            var shown = g.Where(r => !config.HiddenFriendCharacters.Contains(r.Guid)).ToList();
            var current = g.FirstOrDefault(r => r.IsFriendMain) ?? (shown.Count > 0 ? shown : [.. g]).MaxBy(r => r.LastSeen)!;
            Cards.Add(new FriendCard(g.Key, NameOf(g.Key), current, g.Count(), g.Key == Contacts.Key)
            {
                TotalsText = totals.Text(g.Key),
                LastSeen = g.Max(r => r.LastSeen),
            });
            _ = current.LoadPictureAsync(flavor);
        }
    }

    public ICommand SelectFriendCommand => selectFriend ??= new RelayCommand(p =>
    {
        if (p is not FriendCard card) return;
        var key = Contacts.Key == card.Tag ? "" : card.Tag;
        Contacts.Selected = Contacts.Chips.FirstOrDefault(c => c.Key == key);
    });
    ICommand? selectFriend;

    // Dragging (FriendsPage): the dragged friend's card moves live onto the one it's over, by
    // BattleTag (a sync can rebuild the cards mid-drag). Letting go keeps the order when it changed;
    // a cancelled drag puts the cards back.
    public IReadOnlyList<string> CardOrder => [.. Cards.Select(c => c.Tag)];

    public void MoveCard(string tag, string overTag)
    {
        var from = Cards.ToList().FindIndex(c => c.Tag == tag);
        var to = Cards.ToList().FindIndex(c => c.Tag == overTag);
        if (from < 0 || to < 0 || from == to) return;
        Cards.Move(from, to);
    }

    public void EndDrag(IReadOnlyList<string> before, bool dropped)
    {
        var now = CardOrder;
        if (!dropped) BuildCards();
        else if (!now.SequenceEqual(before)) OrderChanged?.Invoke(now);
    }

    // One main per friend: picking another replaces it.
    void SetMain(object? p, bool main)
    {
        if (p is not CharacterRow row || row.FriendTag.Length == 0) return;
        if (main) config.FriendMains[row.FriendTag] = row.Guid;
        else if (config.FriendMains.TryGetValue(row.FriendTag, out var guid) && guid == row.Guid) config.FriendMains.Remove(row.FriendTag);
        save();
        ShowRows();
    }

    void SetHidden(object? p, bool hidden)
    {
        if (p is not CharacterRow row) return;
        if (hidden) config.HiddenFriendCharacters.Add(row.Guid); else config.HiddenFriendCharacters.Remove(row.Guid);
        save();
        ShowRows();
    }

    // Clicking the page's tab again: all friends, nothing open.
    public void Reset()
    {
        SelectedCharacter = null;
        Contacts.Reset();
        ShowRows();
    }

    // A friend's character from Home: the list back on screen with that character open (FriendsPage scrolls to it).
    public event Action<CharacterRow>? Opened;
    public void Open(CharacterRow row)
    {
        ShowSettings = false;
        ShowProfessions = false;
        if (!Contacts.Matches(row.FriendTag))
        {
            Contacts.Reset();
            ShowRows();
        }
        if (Characters.FirstOrDefault(c => c.Guid == row.Guid) is not { } shown) return;
        SelectedCharacter = shown;
        Opened?.Invoke(shown);
    }

    // The shown friends' professions (the same view as the Professions page), by friend first.
    public ProfessionsViewModel Professions { get; } = new() { EmptyText = "No professions from friends yet. Friends who share professions show up here, with their recipes." };

    // What the page shows: the characters, their professions or the sharing settings.
    bool showProfessions;
    public bool ShowProfessions { get => showProfessions; set { if (Set(ref showProfessions, value)) ViewChanged(value ? "professions" : null); } }
    public bool ShowCharacters => !ShowSettings && !ShowProfessions;

    // Friends' armory pictures with the new (HD) or old (SD) models, for every friend's character
    // that doesn't have its own choice (MainWindow's switch beside the sub-tabs).
    public bool HasArmory => flavor.ArmoryGame.Length > 0;
    public string ArmoryModel => config.ArmorySd ? "sd" : "hd";
    public ICommand ShowArmoryModelCommand => showArmoryModel ??= new RelayCommand(p =>
    {
        var sd = p as string == "sd";
        if (config.ArmorySd == sd) return;
        config.ArmorySd = sd;
        foreach (var guid in config.ArmoryModels.Where(m => m.Value == sd).Select(m => m.Key).ToList()) config.ArmoryModels.Remove(guid);
        foreach (var r in allRows) r.SetArmoryDefault(sd);
        save();
        Raise(nameof(ArmoryModel));
    });
    ICommand? showArmoryModel;

    // The page's sub-tabs (MainWindow's switch beside the page tabs): "list" or "professions".
    public string View => ShowProfessions ? "professions" : "list";
    public ICommand ShowViewCommand => showView ??= new RelayCommand(p =>
    {
        ShowSettings = false;
        ShowProfessions = p as string == "professions";
    });
    ICommand? showView;

    void ViewChanged(string? opened)
    {
        if (opened == "professions") ShowSettings = false;
        if (opened == "settings") ShowProfessions = false;
        Raise(nameof(ShowCharacters));
        Raise(nameof(View));
    }

    // --- sharing settings ---

    // The sharing settings panel open instead of the list.
    bool showSettings;
    public bool ShowSettings { get => showSettings; set { if (Set(ref showSettings, value)) ViewChanged(value ? "settings" : null); } }
    public ICommand ToggleSettingsCommand => toggleSettings ??= new RelayCommand(_ => ShowSettings = !ShowSettings);
    ICommand? toggleSettings;

    ShareSettings Share => settings().Share ?? ShareSettings.Empty;

    void Edit(Func<ShareSettings, ShareSettings> edit)
    {
        settings().EditShare(edit);
        changed();
        BuildContactRows();
        BuildOwnCharacters();
        RaiseSharing();
    }

    void RaiseSharing()
    {
        foreach (var name in new[] { nameof(Enabled), nameof(NewChars), nameof(ShareGear), nameof(ShareTalents), nameof(ShareLocation), nameof(ShareProfessions), nameof(ShareReputations), nameof(ShareHighlights), nameof(ShareLeveling), nameof(ShareGold), nameof(ShareMap), nameof(ShareOtherFaction),
            nameof(IsMapLive), nameof(IsMapMinute), nameof(IsMapFiveMinutes), nameof(Blocked), nameof(HasBlocked) })
            Raise(name);
    }

    public bool Enabled { get => Share.Enabled; set => Edit(s => s with { Enabled = value }); }
    public bool NewChars { get => Share.NewChars; set => Edit(s => s with { NewChars = value }); }
    public bool ShareGear { get => Share.Defaults.Gear; set => Edit(s => s with { Defaults = s.Defaults with { Gear = value } }); }
    public bool ShareTalents { get => Share.Defaults.Talents; set => Edit(s => s with { Defaults = s.Defaults with { Talents = value } }); }
    public bool ShareLocation { get => Share.Defaults.Location; set => Edit(s => s with { Defaults = s.Defaults with { Location = value } }); }
    public bool ShareProfessions { get => Share.Defaults.Professions; set => Edit(s => s with { Defaults = s.Defaults with { Professions = value } }); }
    public bool ShareReputations { get => Share.Defaults.Reputations; set => Edit(s => s with { Defaults = s.Defaults with { Reputations = value } }); }
    public bool ShareHighlights { get => Share.Defaults.Highlights; set => Edit(s => s with { Defaults = s.Defaults with { Highlights = value } }); }
    public bool ShareLeveling { get => Share.Defaults.Leveling; set => Edit(s => s with { Defaults = s.Defaults with { Leveling = value } }); }
    public bool ShareGold { get => Share.Defaults.Gold; set => Edit(s => s with { Defaults = s.Defaults with { Gold = value } }); }
    public bool ShareMap { get => Share.Defaults.Map; set => Edit(s => s with { Defaults = s.Defaults with { Map = value } }); }
    public bool ShareOtherFaction { get => Share.Defaults.OtherFaction; set => Edit(s => s with { Defaults = s.Defaults with { OtherFaction = value } }); }

    // How often friends' maps get your position: one rate for all friends.
    public bool IsMapLive => Share.MapRate == 5;
    public bool IsMapMinute => Share.MapRate == 60;
    public bool IsMapFiveMinutes => Share.MapRate == 300;
    public ICommand MapRateCommand => mapRate ??= new RelayCommand(p =>
    {
        if (p is string text && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rate) && ShareSettings.MapRates.Contains(rate))
            Edit(s => s with { MapRate = rate });
    });
    ICommand? mapRate;

    public IReadOnlyList<string> Blocked => [.. Share.Blocked.Order(StringComparer.CurrentCultureIgnoreCase)];
    public bool HasBlocked => Share.Blocked.Count > 0;
    public ICommand UnblockCommand { get; }

    // Everyone in the sharing settings or who shared something, one row each.
    public ObservableCollection<FriendContactRow> ContactRows { get; } = [];

    // The same friends as before keep their rows (a sync mustn't replace the nickname box you're typing in); only their values refresh.
    void BuildContactRows()
    {
        var tags = Share.Contacts.Keys.Union(data.Friends.Select(f => f.Tag)).Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        FriendContact? Got(string tag) => data.Friends.FirstOrDefault(f => f.Tag == tag);
        if (tags.SequenceEqual(ContactRows.Select(r => r.Tag)))
        {
            foreach (var row in ContactRows) row.Refresh(Got(row.Tag));
            return;
        }
        ContactRows.Clear();
        foreach (var tag in tags) ContactRows.Add(new FriendContactRow(tag, Got(tag), this));
    }

    internal bool General(string category) => category switch
    {
        "gear" => Share.Defaults.Gear,
        "talents" => Share.Defaults.Talents,
        "location" => Share.Defaults.Location,
        "professions" => Share.Defaults.Professions,
        "reputations" => Share.Defaults.Reputations,
        "highlights" => Share.Defaults.Highlights,
        "leveling" => Share.Defaults.Leveling,
        "map" => Share.Defaults.Map,
        "otherFaction" => Share.Defaults.OtherFaction,
        _ => Share.Defaults.Gold,
    };

    // Not a contact: nothing shared, nothing kept (as the addon sees it).
    internal ShareContact ContactFor(string tag) => Share.Contacts.TryGetValue(tag, out var c) ? c : new(false, false);
    internal void EditContact(string tag, Func<ShareContact, ShareContact> edit) =>
        Edit(s => s with { Contacts = new(s.Contacts) { [tag] = edit(s.Contacts.TryGetValue(tag, out var c) ? c : new(false, false)) } });

    internal string NicknameOf(string tag) => config.FriendNicknames.GetValueOrDefault(tag, "");
    internal void SetNickname(string tag, string name)
    {
        // A second newer than the last one, so it wins over the game's even within the same second.
        config.SetNickname(tag, name, Math.Max(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), config.FriendNicknamesAt.GetValueOrDefault(tag) + 1));
        changed(); // saves, and the addon shows the same name
        ShowRows();
    }

    // Nicknames taken from the game: the rows, chips and the sharing settings' name boxes show them.
    public void NamesChanged()
    {
        ShowRows();
        foreach (var row in ContactRows) row.Refresh(data.Friends.FirstOrDefault(f => f.Tag == row.Tag));
    }

    // Your own characters on this tab, each shared or kept from friends.
    public IReadOnlyList<OwnCharacterShare> OwnCharacters { get; private set; } = [];

    void BuildOwnCharacters()
    {
        var share = Share;
        var removed = settings().Removed; // removed on the Characters page, until played again
        var ignored = settings().Ignored; // never shared (Ignore tracking)
        // With the surname (Forever), so two Zebs can be told apart; the newest snapshot that has one.
        OwnCharacters = [.. data.Characters.GroupBy(c => c.Guid)
            .Select(g => (Last: g.MaxBy(c => c.LastSeen)!, Surname: g.OrderByDescending(c => c.LastSeen).FirstOrDefault(c => c.Surname.Length > 0)?.Surname ?? ""))
            .Where(x => !(removed.TryGetValue(x.Last.Guid, out var at) && x.Last.LastSeen <= at) && !ignored.Contains(x.Last.Guid))
            .Select(x => (x.Last, Name: Format.Name(x.Last.Char, x.Surname, "full")))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => new OwnCharacterShare(x.Last.Guid, x.Name, share.Chars.TryGetValue(x.Last.Guid, out var on) ? on : share.NewChars, this))];
        Raise(nameof(OwnCharacters));
    }

    internal void SetCharShared(string guid, bool shared) => Edit(s => s with { Chars = new(s.Chars) { [guid] = shared } });
}

// One friend in the sharing settings: their nickname, whether you share with them and keep what they
// send, and their own categories (null = what all friends get).
sealed class FriendContactRow(string tag, FriendContact? got, FriendsViewModel owner) : Observable
{
    public string Tag { get; } = tag;

    // New data for the same friend: every value is read again.
    public void Refresh(FriendContact? fresh)
    {
        got = fresh;
        Raise("");
    }
    public string Nickname { get => owner.NicknameOf(Tag); set { owner.SetNickname(Tag, value); Raise(nameof(Nickname)); } }
    public bool NeedsUpdate => got is { Protocol: > 0 } && got.Protocol != 1;
    public string VersionText => NeedsUpdate ? "One of you needs to update the addon" : got?.Version is { Length: > 0 } v ? $"Addon {v}" : "";
    public string SeenText => got is { Seen: > 0 } ? $"Heard from {Format.Ago(got.Seen)}" : "Nothing from them yet";
    public int CharacterCount => got?.Characters.Count ?? 0;
    public string TheirsText => CharacterCount > 0 ? $"They share with you: {ShareCategories.Summary(got!.Shares)}" : "";

    ShareContact C => owner.ContactFor(Tag);
    public bool YouShare { get => C.Share; set => owner.EditContact(Tag, c => c with { Share = value }); }
    public bool Receive { get => C.Receive; set => owner.EditContact(Tag, c => c with { Receive = value }); }
    public bool? Gear { get => C.Gear; set => owner.EditContact(Tag, c => c with { Gear = value }); }
    public bool? Talents { get => C.Talents; set => owner.EditContact(Tag, c => c with { Talents = value }); }
    public bool? Location { get => C.Location; set => owner.EditContact(Tag, c => c with { Location = value }); }
    public bool? Professions { get => C.Professions; set => owner.EditContact(Tag, c => c with { Professions = value }); }
    public bool? Reputations { get => C.Reputations; set => owner.EditContact(Tag, c => c with { Reputations = value }); }
    public bool? Highlights { get => C.Highlights; set => owner.EditContact(Tag, c => c with { Highlights = value }); }
    public bool? Leveling { get => C.Leveling; set => owner.EditContact(Tag, c => c with { Leveling = value }); }
    public bool? Gold { get => C.Gold; set => owner.EditContact(Tag, c => c with { Gold = value }); }
    public bool? Map { get => C.Map; set => owner.EditContact(Tag, c => c with { Map = value }); }
    public bool? OtherFaction { get => C.OtherFaction; set => owner.EditContact(Tag, c => c with { OtherFaction = value }); }

    // A chip per category that cycles: like all friends, on, off (a checkbox's third state looks unticked).
    static readonly (string Key, string Name)[] Names = [("gear", "Gear"), ("talents", "Talents"), ("location", "Location"), ("professions", "Professions"),
        ("reputations", "Reputations"), ("highlights", "Highlights"), ("leveling", "Level times"), ("gold", "Gold"), ("map", "Map position"), ("otherFaction", "Map for the other faction")];
    bool? Own(string key) => key switch
    {
        "gear" => Gear, "talents" => Talents, "location" => Location, "professions" => Professions, "reputations" => Reputations, "highlights" => Highlights, "leveling" => Leveling,
        "map" => Map, "otherFaction" => OtherFaction, _ => Gold,
    };
    public IReadOnlyList<CategoryChip> Categories => [.. Names.Select(n => Own(n.Key) switch
    {
        null => new CategoryChip(n.Key, $"{n.Name}: like all friends ({(owner.General(n.Key) ? "on" : "off")})", "all"),
        true => new CategoryChip(n.Key, $"{n.Name}: on", "on"),
        false => new CategoryChip(n.Key, $"{n.Name}: off", "off"),
    })];
    public ICommand CycleCommand => cycle ??= new RelayCommand(p =>
    {
        if (p is not string key) return;
        bool? next = Own(key) switch { null => true, true => false, false => null };
        switch (key)
        {
            case "gear": Gear = next; break;
            case "talents": Talents = next; break;
            case "location": Location = next; break;
            case "professions": Professions = next; break;
            case "reputations": Reputations = next; break;
            case "highlights": Highlights = next; break;
            case "leveling": Leveling = next; break;
            case "map": Map = next; break;
            case "otherFaction": OtherFaction = next; break;
            default: Gold = next; break;
        }
    });
    ICommand? cycle;
}

// A friend's card in the Friends page's grid: their name, the character they played last, how many
// they share, when it was seen and when their data last arrived. IsSelected: only theirs are listed.
sealed record FriendCard(string Tag, string Name, CharacterRow Current, int Count, bool IsSelected)
{
    public string CountText => Format.Count(Count, "character");
    public string SeenText => $"seen {Current.LastSeenText} · {Current.FriendSyncedText}";
    // When they last played any of their characters (the main can be an older one): "seen 18h ago" on the card.
    public long LastSeen { get; init; }
    public string LastSeenText => Format.Ago(LastSeen) is { Length: > 0 } s ? $"seen {s}" : "";
    // Their gold and played added up, with their share of all friends': "12,345g (40%) · 6d 4h 12m played (25%)". Empty when they share neither.
    public string TotalsText { get; init; } = "";
}

// One category chip of a friend: State is "all" (follows what all friends get), "on" or "off".
sealed record CategoryChip(string Key, string Text, string State);

// One of your characters in the sharing settings.
sealed class OwnCharacterShare(string guid, string name, bool shared, FriendsViewModel owner) : Observable
{
    public string Guid { get; } = guid;
    public string Name { get; } = name;
    public bool IsShared { get => shared; set { shared = value; owner.SetCharShared(Guid, value); } }
}
