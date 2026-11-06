namespace WoWCharacterTracker.Models;

// What one Battle.net friend shared (the addon's WoWCharacterTrackerDB.friends[battleTag]): their
// characters as snapshots and gear, so the app shows them with the same rows as yours. Protocol is
// set when their addon speaks another one (one of you needs to update).
record FriendContact(string Tag, long Seen, string Version, int Protocol, bool Refused, List<Snapshot> Characters, List<Inventory> Inventories);

// What all friends get, unless set for one of them. Gold stays off until turned on.
record GeneralCategories(bool Gear = true, bool Talents = true, bool Location = false, bool Gold = false);

// One contact: whether we share with them and keep what they send, and their own categories (null = the general choice).
record ShareContact(bool Share, bool Receive, bool? Gear = null, bool? Talents = null, bool? Location = null, bool? Gold = null);

// Sharing with friends, the same shape as the addon's `share` (Friends.lua). Changed in the app or in
// game, the newest At wins.
record ShareSettings(bool Enabled, bool NewChars, GeneralCategories Defaults, Dictionary<string, bool> Chars,
    Dictionary<string, ShareContact> Contacts, HashSet<string> Blocked, long At)
{
    public static ShareSettings Empty => new(false, true, new(), [], [], [], 0);
}
