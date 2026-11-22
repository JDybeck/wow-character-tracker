namespace WoWCharacterTracker.ViewModels;

// One of the standings a reputation milestone can start at (Friendly to Exalted), shown as radio chips
// on the Sharing page. isOn reads the setting, pick sets it; Refresh after it changed elsewhere.
sealed class StandingChoice(int standing, string group, Func<bool> isOn, Action pick) : Observable
{
    public int Standing { get; } = standing;
    public string Label { get; } = Reputations.StandingName(standing);
    public string Group { get; } = group;
    public bool IsOn { get => isOn(); set { if (value && !isOn()) pick(); } }
    public void Refresh() => Raise(nameof(IsOn));
}
