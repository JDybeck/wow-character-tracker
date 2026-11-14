namespace WoWCharacterTracker.Services;

// The Friends page's order (BattleTags, first is top left) is one list for every game and the addon,
// but each place only shows some friends (this game's, the ones who share something). A drag there
// puts the shown friends, in their new order, in the places the shown ones had; everyone else keeps
// theirs, and new ones take places at the end. Friends.lua's ns.mergeFriendOrder does the same.
static class FriendOrder
{
    public static List<string> Merge(IReadOnlyList<string> saved, IReadOnlyList<string> visible)
    {
        var shown = visible.ToHashSet();
        var known = saved.ToHashSet();
        var merged = new List<string>();
        var slots = new List<int>();
        foreach (var tag in saved)
        {
            if (shown.Contains(tag)) slots.Add(merged.Count);
            merged.Add(tag);
        }
        foreach (var tag in visible.Where(t => !known.Contains(t)))
        {
            slots.Add(merged.Count);
            merged.Add(tag);
        }
        for (var i = 0; i < slots.Count; i++) merged[slots[i]] = visible[i];
        return merged;
    }
}
