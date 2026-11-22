namespace WoWCharacterTracker.ViewModels;

// One weekday as a radio chip (the weekly recap's day, "Sun"). isOn reads the setting, pick sets it;
// Refresh after the setting changed elsewhere (another chip).
sealed class DayChoice(DayOfWeek day, string group, Func<bool> isOn, Action pick) : Observable
{
    public DayOfWeek Day { get; } = day;
    public string Label { get; } = day.ToString()[..3];
    public string Group { get; } = group;
    public bool IsOn { get => isOn(); set { if (value && !isOn()) pick(); } }
    public void Refresh() => Raise(nameof(IsOn));
}
