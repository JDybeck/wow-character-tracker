using System.Collections.ObjectModel;
using System.Windows.Input;

namespace WoWCharacterTracker.ViewModels;

// The Data page (Settings, Manage data...), for the selected game: remove characters completely, old
// history, what friends shared, and the app's caches. Each removal asks first, right on the page.
// The saved file is the game's, so the addon finishes the job the next time it loads (DataCleanup).
sealed class DataViewModel : Observable
{
    readonly MainViewModel main;

    public DataViewModel(MainViewModel main)
    {
        this.main = main;
        main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedFlavor) && main.Page == "data") Refresh();
        };
        AskCommand = new RelayCommand(p => Confirming = p as string ?? "");
        CancelAskCommand = new RelayCommand(_ => Confirming = "");
        RemoveCharactersCommand = new RelayCommand(_ => Clean(new CleanupOrder { At = NextAt(), Chars = [.. Characters.Where(c => c.IsOn).Select(c => c.Guid)] }),
            _ => Characters.Any(c => c.IsOn));
        RemoveHistoryCommand = new RelayCommand(_ => Clean(HistoryOrder()), _ => HistoryKinds.Any(k => k.IsOn));
        RemoveFriendsCommand = new RelayCommand(_ => Clean(FriendsOrder()), _ => HasFriendsToRemove);
        ClearCachesCommand = new RelayCommand(_ => ClearCaches());
        PickHistoryAgeCommand = new RelayCommand(p => { if (int.TryParse(p as string, out var d)) { HistoryAge = d; Counted(); } });
        PickFriendsAgeCommand = new RelayCommand(p => { if (int.TryParse(p as string, out var d)) { FriendsAge = d; Counted(); } });
        BackCommand = new RelayCommand(_ => main.Page = "settings");
    }

    public ICommand AskCommand { get; }
    public ICommand CancelAskCommand { get; }
    public ICommand RemoveCharactersCommand { get; }
    public ICommand RemoveHistoryCommand { get; }
    public ICommand RemoveFriendsCommand { get; }
    public ICommand ClearCachesCommand { get; }
    public ICommand PickHistoryAgeCommand { get; }
    public ICommand PickFriendsAgeCommand { get; }
    public ICommand BackCommand { get; }

    FlavorViewModel? Tab => main.SelectedFlavor;
    public string Title => $"Your data in {Tab?.Name}";

    // Where the removal happens, said once at the top.
    public string HowText => Tab?.KnownFolder is { } dir && Wow.IsRunning(dir)
        ? $"{Tab.Name} is running: /reload in game to finish. The app hides what you remove right away."
        : "The addon finishes the job the next time the game loads it (log in or /reload). The app hides what you remove right away.";

    // Which section asks "Are you sure?" now: "chars", "history", "friends" or "".
    string confirming = "";
    public string Confirming { get => confirming; set => Set(ref confirming, value); }

    public ObservableCollection<DataCharacter> Characters { get; } = [];
    public bool HasPickedCharacters => Characters.Any(c => c.IsOn);
    public string CharactersText => Characters.Count(c => c.IsOn) is var n and > 0
        ? $"Removes {Format.Count(n, "character")} and everything saved about them: level history, gear, bags and bank, highlights, notes and day stats. Playing one again starts it fresh."
        : "Pick the characters to remove.";

    // History: older than HistoryAge days, the picked kinds (plus day stats).
    public ObservableCollection<ToggleOption> HistoryKinds { get; } = [];
    int historyAge = 90;
    public int HistoryAge { get => historyAge; private set => Set(ref historyAge, value); }
    public string HistoryText { get; private set; } = "";

    // Friends: what they shared and you haven't heard again in FriendsAge days (0 = all of it).
    int friendsAge = 30;
    public int FriendsAge { get => friendsAge; private set => Set(ref friendsAge, value); }
    public string FriendsText { get; private set; } = "";
    public bool HasFriendsToRemove { get; private set; }

    public string CachesText { get; private set; } = "";

    const string DaysKey = "days";

    public void Refresh()
    {
        Confirming = "";
        Characters.Clear();
        HistoryKinds.Clear();
        if (Tab is { } tab)
        {
            foreach (var c in tab.ShownData.Characters.GroupBy(c => c.Guid).Select(g => g.MaxBy(c => c.LastSeen)!)
                         .OrderByDescending(c => c.LastSeen))
            {
                var note = tab.IsIgnored(c.Guid) ? ", not tracked" : tab.IsRemovedFromList(c) ? ", removed from the list" : "";
                Characters.Add(new DataCharacter(c.Guid, c.Char, $"Level {c.Level} {c.Class}, {Realms.Label(c.Region, c.Realm)}, played {Format.Ago(c.LastSeen)}{note}", Counted));
            }
            // Level-ups are left off to start with: level times and the Leveling tab are built from them.
            var kinds = EventKinds.All.Where(k => EventKinds.OfferedIn(k.Key, tab.Flavor.HasRatings))
                .Select(k => (k.Key, k.Name, k.Key == "level" ? "Level times and the Leveling tab come from these" : $"{k.Name} older than that"))
                .Append((DaysKey, "Day stats", "Quests, gathering, gold and item level per day (the Stats page)"));
            foreach (var (key, name, tip) in kinds)
                HistoryKinds.Add(new ToggleOption(name, key != "level", _ => Counted(), tip) { Key = key });
        }
        Raise(nameof(Title));
        Raise(nameof(HowText));
        Counted();
    }

    // The texts saying how much each removal takes, from what the app has now.
    void Counted()
    {
        CachesText = $"{Format.Size(AppCaches.Bytes)}: Wowhead item and spell info, Raider.IO renders, icons and tab logos. They come back as they're needed.";
        if (Tab is { } tab)
        {
            var (_, events, days, _) = DataCleanup.Count(tab.ShownData, tab.KeptDays, HistoryOrder());
            string[] parts = [.. new[] { (events, "event"), (days, "day") }.Where(p => p.Item1 > 0).Select(p => Format.Count(p.Item1, p.Item2))];
            HistoryText = parts.Length == 0 ? "Nothing that old." : $"Removes {string.Join(" and ", parts)}.";
            var friendOrder = FriendsOrder();
            var friends = tab.ShownData.Friends.Where(f => f.Characters.Any(c => DataCleanup.RemovesFriend(friendOrder, c))).ToList();
            var chars = friends.Sum(f => f.Characters.Count(c => DataCleanup.RemovesFriend(friendOrder, c)));
            HasFriendsToRemove = chars > 0;
            FriendsText = chars == 0 ? "Nothing to remove." : $"Removes {Format.Count(chars, "character")} from {Format.Count(friends.Count, "friend")}. Friends who still share send theirs again.";
        }
        Raise(nameof(CharactersText));
        Raise(nameof(HasPickedCharacters));
        Raise(nameof(HistoryText));
        Raise(nameof(FriendsText));
        Raise(nameof(HasFriendsToRemove));
        Raise(nameof(CachesText));
        CommandManager.InvalidateRequerySuggested();
    }

    CleanupOrder HistoryOrder()
    {
        var picked = HistoryKinds.Where(k => k.IsOn).Select(k => k.Key!).ToList();
        var now = DateTimeOffset.Now;
        return new CleanupOrder
        {
            At = NextAt(),
            Before = picked.Any(k => k != DaysKey) ? now.AddDays(-HistoryAge).ToUnixTimeSeconds() : 0,
            Kinds = [.. picked.Where(k => k != DaysKey)],
            BeforeDay = picked.Contains(DaysKey) ? now.AddDays(-HistoryAge).ToString("yyyy-MM-dd") : "",
        };
    }

    CleanupOrder FriendsOrder()
    {
        var at = NextAt();
        return new CleanupOrder { At = at, FriendsBefore = FriendsAge == 0 ? at + 1 : DateTimeOffset.UtcNow.AddDays(-FriendsAge).ToUnixTimeSeconds() };
    }

    // Now, but always after the tab's last order (the addon does each order newer than its last one).
    long NextAt() => Math.Max(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), (Tab?.LastCleanupAt ?? 0) + 1);

    void Clean(CleanupOrder order)
    {
        if (Tab is not { } tab) return;
        tab.Clean(order);
        Log.Success($"[{tab.Name}] {Describe(order)} The addon finishes on the next login or /reload.");
        Refresh();
    }

    static string Describe(CleanupOrder o) =>
        o.Chars.Count > 0 ? $"Removed {Format.Count(o.Chars.Count, "character")}."
        : o.FriendsBefore > 0 ? "Removed what friends shared."
        : "Removed old history.";

    void ClearCaches()
    {
        AppCaches.Clear();
        Log.Success("Cleared the app's caches.");
        Counted();
    }
}

// One character on the Data page, ticked to be removed.
sealed class DataCharacter(string guid, string name, string detail, Action changed) : Observable
{
    public string Guid { get; } = guid;
    public string Name { get; } = name;
    public string Detail { get; } = detail;

    bool isOn;
    public bool IsOn { get => isOn; set { if (Set(ref isOn, value)) changed(); } }
}
