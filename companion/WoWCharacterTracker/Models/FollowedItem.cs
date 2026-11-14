namespace WoWCharacterTracker.Models;

// An item on a game's followed list (FlavorSettings.FollowedItems). The name is what Wowhead called
// it when followed, so the list reads right before a lookup answers or without a connection.
sealed record FollowedItem(int Id, string Name);
