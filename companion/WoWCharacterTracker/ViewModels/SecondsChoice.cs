namespace WoWCharacterTracker.ViewModels;

// One of a few second counts shown as radio chips (how long the in-game post box stays). isOn reads
// the setting, pick sets it; Refresh after the setting changed elsewhere (another chip, the game).
sealed class SecondsChoice(int seconds, string group, Func<bool> isOn, Action pick) : Observable
{
    public int Seconds { get; } = seconds;
    public string Label { get; } = $"{seconds} s";
    public string Group { get; } = group;
    public bool IsOn { get => isOn(); set { if (value && !isOn()) pick(); } }
    public void Refresh() => Raise(nameof(IsOn));
}
