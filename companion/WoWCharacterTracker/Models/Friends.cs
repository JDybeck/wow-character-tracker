namespace WoWCharacterTracker.Models;

// What one Battle.net friend shared (the addon's WoWCharacterTrackerDB.friends[battleTag]): their
// characters as snapshots, gear and highlights (events), so the app shows them with the same rows as
// yours. Protocol is set when their addon speaks another one (one of you needs to update).
record FriendContact(string Tag, long Seen, string Version, int Protocol, bool Refused, List<Snapshot> Characters, List<Inventory> Inventories,
    List<Entry> Events);

// What all friends get, unless set for one of them. Gold and map position stay off until turned on.
// OtherFaction isn't data: on lets a friend playing the other faction see your map position.
record GeneralCategories(bool Gear = true, bool Talents = true, bool Location = true, bool Gold = false, bool Professions = true,
    bool Map = false, bool OtherFaction = false, bool Highlights = true);

// One contact: whether we share with them and keep what they send, and their own categories (null = the general choice).
record ShareContact(bool Share, bool Receive, bool? Gear = null, bool? Talents = null, bool? Location = null, bool? Gold = null, bool? Professions = null,
    bool? Map = null, bool? OtherFaction = null, bool? Highlights = null);

// Sharing with friends, the same shape as the addon's `share` (Friends.lua). Changed in the app or in
// game, the newest At wins. MapRate: seconds between two map position readings, one of MapRates.
record ShareSettings(bool Enabled, bool NewChars, GeneralCategories Defaults, Dictionary<string, bool> Chars,
    Dictionary<string, ShareContact> Contacts, HashSet<string> Blocked, long At, int MapRate = 5)
{
    public static readonly int[] MapRates = [5, 60, 300]; // live, every minute, every 5 minutes
    public static ShareSettings Empty => new(true, true, new(), [], [], [], 0); // on, as in the addon: only allowing a request shares
}
