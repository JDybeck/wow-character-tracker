using System.Collections.ObjectModel;

namespace WoWCharacterTracker.ViewModels;

// A clickable on/off chip, used for a webhook's games and rulesets, the stream's kinds and what the
// addon announces in game. Tip: what it does, on hover.
sealed class ToggleOption(string name, bool on, Action<bool> changed, string? tip = null) : Observable
{
    bool isOn = on;
    public string Name { get; } = name;
    public string? Tip { get; } = tip;
    public bool IsOn
    {
        get => isOn;
        set
        {
            if (!Set(ref isOn, value)) return;
            changed(value);
        }
    }

    // One chip per kind in names, on when picked has it; a click adds the kind to picked or takes it
    // out, then calls changed (to save).
    public static ObservableCollection<ToggleOption> Kinds(IEnumerable<(string Kind, string Name, string Tip)> names, List<string> picked, Action changed) =>
        new(names.Select(k => new ToggleOption(k.Name, picked.Contains(k.Kind), on =>
        {
            picked.Remove(k.Kind);
            if (on) picked.Add(k.Kind);
            changed();
        }, k.Tip)));
}
