namespace WoWCharacterTracker.ViewModels;

// One kind in the In game page's chat table: the sample line, and whether guild chat and group
// chat get it.
sealed record AnnounceRow(string Name, string Tip, ToggleOption Guild, ToggleOption Party);
