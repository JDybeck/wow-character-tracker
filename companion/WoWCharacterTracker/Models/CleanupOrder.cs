namespace WoWCharacterTracker.Models;

// One cleanup picked on the Data page. The game owns the saved file, so the addon does it
// (Cleanup.lua applyCleanups) the next time the game loads it, once per account. Until every account
// has, the order stays in Settings.lua and the app hides what it covers (DataCleanup.Hide).
sealed record CleanupOrder
{
    public long At { get; init; }                // when it was made; also its id
    public List<string> Chars { get; init; } = []; // characters removed completely
    public long Before { get; init; }            // events of Kinds older than this go (0 = none)
    public List<string> Kinds { get; init; } = [];
    public string BeforeDay { get; init; } = ""; // day stats older than this local date (yyyy-MM-dd) go ("" = none)
    public long FriendsBefore { get; init; }     // friends' characters received before this go (0 = none)
}
