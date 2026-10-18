namespace WoWCharacterTracker.ViewModels;

// A clickable on/off chip, used for a webhook's games and rulesets. Tip: what it does, on hover.
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
}
