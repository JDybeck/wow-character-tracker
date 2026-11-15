using System.Globalization;

namespace WoWCharacterTracker.Services;

// The Data page's cleanups (CleanupOrder). The app forgets its own copies right away (Forget), the
// addon removes the rest from the saved file (Cleanup.lua, the same rules as Removes here), and until
// every account has done that the app hides what the orders cover (Hide).
static class DataCleanup
{
    // An event the order removes: a removed character's from before the order, or one of its kinds
    // older than Before (an ask event counts as the kind it asked about).
    public static bool Removes(CleanupOrder o, Entry e) =>
        (o.Chars.Contains(e.Guid) && e.At <= o.At)
        || (o.Before > 0 && e.At < o.Before && o.Kinds.Contains(e.Moment.Kind));

    // A day the order removes: a removed character's up to the order's day, or any older than BeforeDay.
    public static bool Removes(CleanupOrder o, DayTotals d) =>
        (o.Chars.Contains(d.Guid) && string.CompareOrdinal(d.Day, CharsDay(o)) <= 0) || TooOld(o, d);

    static bool TooOld(CleanupOrder o, DayTotals d) => o.BeforeDay.Length > 0 && string.CompareOrdinal(d.Day, o.BeforeDay) < 0;

    // The local date the order was made ("2026-11-15"): a removed character played since keeps only later days.
    public static string CharsDay(CleanupOrder o) =>
        DateTimeOffset.FromUnixTimeSeconds(o.At).ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // A friend's character the order removes.
    public static bool RemovesFriend(CleanupOrder o, Snapshot c) => o.FriendsBefore > 0 && c.LastSeen < o.FriendsBefore;

    // The data without what pending orders cover. A removed character played since the order shows
    // again as it is now, without its events and days up to the order (Cleanup.lua removeOrdered).
    public static TrackerData Hide(TrackerData data, IReadOnlyList<CleanupOrder> orders)
    {
        if (orders.Count == 0) return data;
        var played = data.Characters.GroupBy(c => c.Guid).ToDictionary(g => g.Key, g => g.Max(c => c.LastSeen));
        var gone = orders.SelectMany(o => o.Chars.Where(guid => played.GetValueOrDefault(guid) <= o.At)).ToHashSet();
        return data with
        {
            Events = [.. data.Events.Where(e => !orders.Any(o => Removes(o, e)))],
            Characters = [.. data.Characters.Where(c => !gone.Contains(c.Guid))],
            Inventories = [.. data.Inventories.Where(i => !gone.Contains(i.Guid))],
            Notes = [.. data.Notes.Where(n => !gone.Contains(n.Guid))],
            Today = [.. data.Today.Where(d => !gone.Contains(d.Guid) && !orders.Any(o => Removes(o, d)))],
            Days = [.. data.Days.Where(d => !gone.Contains(d.Guid) && !orders.Any(o => Removes(o, d)))],
            Friends = [.. data.Friends.Select(f => HideFriend(f, orders))],
        };
    }

    static FriendContact HideFriend(FriendContact f, IReadOnlyList<CleanupOrder> orders)
    {
        var gone = f.Characters.Where(c => orders.Any(o => RemovesFriend(o, c))).Select(c => c.Guid).ToHashSet();
        if (gone.Count == 0) return f;
        return f with
        {
            Characters = [.. f.Characters.Where(c => !gone.Contains(c.Guid))],
            Inventories = [.. f.Inventories.Where(i => !gone.Contains(i.Guid))],
            Events = [.. f.Events.Where(e => !gone.Contains(e.Guid))],
            Days = [.. f.Days.Where(d => !gone.Contains(d.Guid))],
        };
    }

    // The orders some account hasn't done yet. cleanedAt: the oldest cleanedAt among the accounts'
    // saved files (0 while one hasn't loaded the addon since).
    public static List<CleanupOrder> StillPending(IReadOnlyList<CleanupOrder> orders, long cleanedAt) =>
        [.. orders.Where(o => o.At > cleanedAt)];

    // What the app itself kept about these characters: tags, notes, portrait choice and file. They're
    // hidden (Removed) until played again, which also stops the addon sharing them with friends.
    public static void Forget(Config config, FlavorSettings settings, IEnumerable<string> guids, long at, Action<string> deletePortrait)
    {
        foreach (var guid in guids)
        {
            config.CharacterTags.Remove(guid);
            config.CharacterNotes.Remove(guid);
            config.CharacterNotesAt.Remove(guid);
            config.StandardPortraits.Remove(guid);
            settings.Removed[guid] = at;
            deletePortrait(guid);
        }
    }

    // How much an order removes from what the app has now: characters, events, days, friends' characters.
    public static (int Characters, int Events, int Days, int FriendCharacters) Count(TrackerData data, IReadOnlyList<DayTotals> history, CleanupOrder o) =>
        (data.Characters.Select(c => c.Guid).Distinct().Count(o.Chars.Contains),
         data.Events.Count(e => Removes(o, e)),
         history.Count(d => Removes(o, d)),
         data.Friends.Sum(f => f.Characters.Count(c => RemovesFriend(o, c))));
}
