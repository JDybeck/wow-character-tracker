namespace WoWCharacterTracker.ViewModels;

sealed record LevelRow(string LevelText, string Took, string Zone, string When);

// One bar in the Leveling tab's chart: a level's time, scaled to the slowest one shown.
sealed record LevelBar(int Level, double Height, string Tooltip, bool Highlight);
