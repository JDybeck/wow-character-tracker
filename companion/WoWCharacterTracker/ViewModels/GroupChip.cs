using System.Collections.ObjectModel;

namespace WoWCharacterTracker.ViewModels;

// A filter chip: a realm (Retail) or ruleset (Forever), or "All" (Key "").
sealed record GroupChip(string Key, string Label);

// A row of filter chips over a list: "All (n)" and one chip per key with its count, the chosen one
// kept when the list is rebuilt. Shown by Views/ChipBar; used for realms on the Characters and Items
// pages. Changed runs when the user picks another chip.
sealed class ChipFilter(string label, Action changed) : Observable
{
    public string Label { get; } = label;
    public ObservableCollection<GroupChip> Chips { get; } = [];
    public bool HasChoices => Chips.Count > 2; // "All" plus at least two keys

    GroupChip? selected;
    public GroupChip? Selected
    {
        get => selected;
        set { if (value is not null && Set(ref selected, value)) changed(); }
    }

    // The chosen key, "" for All.
    public string Key => selected?.Key ?? "";

    public bool Matches(string key) => Key.Length == 0 || key == Key;

    // Back to All, without running changed (the caller rebuilds once for everything it resets).
    public void Reset()
    {
        selected = Chips.FirstOrDefault();
        Raise(nameof(Selected));
    }

    // Rebuilds the chips from the items' keys (an empty key reads "Unknown"), keeping the chosen one if it's still there.
    public void Fill<T>(IReadOnlyCollection<T> items, Func<T, string> keyOf)
    {
        var chosen = Key;
        Chips.Clear();
        Chips.Add(new GroupChip("", $"All ({items.Count})"));
        foreach (var g in items.GroupBy(keyOf).OrderBy(g => g.Key))
            Chips.Add(new GroupChip(g.Key, $"{(g.Key.Length > 0 ? g.Key : "Unknown")} ({g.Count()})"));
        selected = Chips.FirstOrDefault(c => c.Key == chosen) ?? Chips[0];
        Raise(nameof(Selected));
        Raise(nameof(HasChoices));
    }
}
