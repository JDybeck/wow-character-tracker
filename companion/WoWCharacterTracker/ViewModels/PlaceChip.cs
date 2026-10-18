namespace WoWCharacterTracker.ViewModels;

// A place on the Items page (All, Equipped, Bags, Bank). Clicking one cycles it: only that place
// (State "only"), then everything but that place ("out"), then everything again. "All" is "only"
// while nothing is picked.
sealed class PlaceChip(string key, string label) : Observable
{
    public string Key { get; } = key;
    public string Label { get; } = label;

    string state = "";
    public string State
    {
        get => state;
        set
        {
            if (!Set(ref state, value)) return;
            Raise(nameof(IsOnly));
            Raise(nameof(IsOut));
            Raise(nameof(Tip));
        }
    }
    public bool IsOnly => State == "only";
    public bool IsOut => State == "out";

    string Name => Label.ToLowerInvariant();
    public string Tip => Key.Length == 0 ? "Show items from everywhere"
        : State switch
        {
            "only" => $"Showing only {Name}. Click again to hide {Name} instead.",
            "out" => $"Hiding {Name}. Click again to show everything.",
            _ => $"Show only {Name}. Click again to hide {Name} instead.",
        };
}
