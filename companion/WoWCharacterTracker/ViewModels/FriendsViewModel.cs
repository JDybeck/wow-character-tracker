using System.Collections.ObjectModel;
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

    string NameOf(string tag) => config.FriendNicknames.TryGetValue(tag, out var n) && n.Length > 0 ? n : tag;

    // After every sync: friends' rows rebuilt, the picked chip and open character kept.
    public void Show(TrackerData data)
    {
        this.data = data;
        var keep = SelectedCharacter?.Guid;
        var keepTab = SelectedCharacter?.DetailTab; // the list refreshes on every sync
        var rows = new List<CharacterRow>();
        // Only friends whose characters you keep: unticking "Keep theirs" or blocking them hides what came before.
        foreach (var contact in data.Friends.Where(c => ContactFor(c.Tag).Receive && !Share.Blocked.Contains(c.Tag)))
            foreach (var row in CharacterRow.Build(new TrackerData([], contact.Characters) { Inventories = contact.Inventories }, flavor, lookup))
            {
                row.IsFriend = true;
                row.FriendTag = contact.Tag;
                rows.Add(row);
            }
        allRows = rows;
        ShowRows();
        SelectedCharacter = Characters.FirstOrDefault(c => c.Guid == keep);
        if (SelectedCharacter is not null && keepTab is not null) SelectedCharacter.DetailTab = keepTab;
        BuildContactRows();
        BuildOwnCharacters();
        RaiseSharing();
    }

    void ShowRows()
    {
        foreach (var r in allRows)
        {
            r.FriendName = NameOf(r.FriendTag);
            r.IsHiddenFriend = config.HiddenFriendCharacters.Contains(r.Guid);
        }
        Contacts.Fill(allRows, r => r.FriendTag, NameOf);
        Characters.Clear();
        foreach (var r in allRows.Where(r => Contacts.Matches(r.FriendTag) && (ShowHidden || !config.HiddenFriendCharacters.Contains(r.Guid)))
                     .OrderBy(r => r.FriendName, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Characters.Add(r);
            _ = r.LoadPictureAsync(flavor);
        }
        Professions.Show(Characters, r => r.FriendName);
        Raise(nameof(HasFriends));
        Raise(nameof(HiddenCount));
        Raise(nameof(HasHidden));
        Raise(nameof(HiddenText));
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

    // The shown friends' professions (the same view as the Professions page), by friend first.
    public ProfessionsViewModel Professions { get; } = new() { EmptyText = "No professions from friends yet. Friends who share professions show up here." };

    // What the page shows: the characters, their professions or the sharing settings.
    bool showProfessions;
    public bool ShowProfessions { get => showProfessions; set { if (Set(ref showProfessions, value)) ViewChanged(value ? "professions" : null); } }
    public ICommand ToggleProfessionsCommand => toggleProfessions ??= new RelayCommand(_ => ShowProfessions = !ShowProfessions);
    ICommand? toggleProfessions;
    public bool ShowCharacters => !ShowSettings && !ShowProfessions;

    void ViewChanged(string? opened)
    {
        if (opened == "professions") ShowSettings = false;
        if (opened == "settings") ShowProfessions = false;
        Raise(nameof(ShowCharacters));
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
        foreach (var name in new[] { nameof(Enabled), nameof(NewChars), nameof(ShareGear), nameof(ShareTalents), nameof(ShareLocation), nameof(ShareProfessions), nameof(ShareGold), nameof(Blocked), nameof(HasBlocked) })
            Raise(name);
    }

    public bool Enabled { get => Share.Enabled; set => Edit(s => s with { Enabled = value }); }
    public bool NewChars { get => Share.NewChars; set => Edit(s => s with { NewChars = value }); }
    public bool ShareGear { get => Share.Defaults.Gear; set => Edit(s => s with { Defaults = s.Defaults with { Gear = value } }); }
    public bool ShareTalents { get => Share.Defaults.Talents; set => Edit(s => s with { Defaults = s.Defaults with { Talents = value } }); }
    public bool ShareLocation { get => Share.Defaults.Location; set => Edit(s => s with { Defaults = s.Defaults with { Location = value } }); }
    public bool ShareProfessions { get => Share.Defaults.Professions; set => Edit(s => s with { Defaults = s.Defaults with { Professions = value } }); }
    public bool ShareGold { get => Share.Defaults.Gold; set => Edit(s => s with { Defaults = s.Defaults with { Gold = value } }); }

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
        _ => Share.Defaults.Gold,
    };

    // Not a contact: nothing shared, nothing kept (as the addon sees it).
    internal ShareContact ContactFor(string tag) => Share.Contacts.TryGetValue(tag, out var c) ? c : new(false, false);
    internal void EditContact(string tag, Func<ShareContact, ShareContact> edit) =>
        Edit(s => s with { Contacts = new(s.Contacts) { [tag] = edit(s.Contacts.TryGetValue(tag, out var c) ? c : new(false, false)) } });

    internal string NicknameOf(string tag) => config.FriendNicknames.GetValueOrDefault(tag, "");
    internal void SetNickname(string tag, string name)
    {
        name = name.Trim();
        if (name.Length == 0) config.FriendNicknames.Remove(tag); else config.FriendNicknames[tag] = name;
        changed(); // saves, and the addon shows the same name
        ShowRows();
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

    ShareContact C => owner.ContactFor(Tag);
    public bool YouShare { get => C.Share; set => owner.EditContact(Tag, c => c with { Share = value }); }
    public bool Receive { get => C.Receive; set => owner.EditContact(Tag, c => c with { Receive = value }); }
    public bool? Gear { get => C.Gear; set => owner.EditContact(Tag, c => c with { Gear = value }); }
    public bool? Talents { get => C.Talents; set => owner.EditContact(Tag, c => c with { Talents = value }); }
    public bool? Location { get => C.Location; set => owner.EditContact(Tag, c => c with { Location = value }); }
    public bool? Professions { get => C.Professions; set => owner.EditContact(Tag, c => c with { Professions = value }); }
    public bool? Gold { get => C.Gold; set => owner.EditContact(Tag, c => c with { Gold = value }); }

    // A chip per category that cycles: like all friends, on, off (a checkbox's third state looks unticked).
    static readonly (string Key, string Name)[] Names = [("gear", "Gear"), ("talents", "Talents"), ("location", "Location"), ("professions", "Professions"), ("gold", "Gold")];
    bool? Own(string key) => key switch { "gear" => Gear, "talents" => Talents, "location" => Location, "professions" => Professions, _ => Gold };
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
            default: Gold = next; break;
        }
    });
    ICommand? cycle;
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
