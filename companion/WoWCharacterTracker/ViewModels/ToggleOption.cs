namespace WoWCharacterTracker.ViewModels;

// A clickable on/off chip, used for a webhook's games and rulesets.
sealed class ToggleOption(string name, bool on, Action<bool> changed) : Observable
{
    bool isOn = on;
    public string Name { get; } = name;
    public bool IsOn
    {
        get => isOn;
        set
        {
            if (!Set(ref isOn, value)) return;
            changed(value);
        }
    }
}
