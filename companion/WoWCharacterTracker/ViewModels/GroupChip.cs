namespace WoWCharacterTracker.ViewModels;

// A filter chip above the character list: a realm (Retail) or ruleset (Forever), or "All".
sealed record GroupChip(string Key, string Label);
