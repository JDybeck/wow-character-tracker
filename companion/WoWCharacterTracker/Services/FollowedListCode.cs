namespace WoWCharacterTracker.Services;

// Your own followed items as a code to share (the ready-made lists have their own switches). It says
// which game it's for: ids and items differ between games, so it only goes in on the same one.
static class FollowedListCode
{
    const string Prefix = "WCTL1:";
    const int MaxItems = 1000, MaxName = 200;

    sealed record Code(string Game, List<FollowedItem> Items);

    public static string Export(IEnumerable<FollowedItem> items, Flavor flavor) => ShareCode.Encode(Prefix, new Code(flavor.Key, [.. items]));

    // The code's items, cleaned up, or why there are none.
    public static (IReadOnlyList<FollowedItem> Items, string? Problem) Import(string text, Flavor flavor)
    {
        if (!ShareCode.Has(Prefix, text)) return ([], $"That isn't a list code. Codes start with {Prefix}");
        if (ShareCode.Decode<Code>(Prefix, text) is not { Items: not null } code) return ([], "The list code is damaged. Copy it again, the whole line.");
        if (code.Game != flavor.Key)
            return ([], $"That list is for {Flavors.All.FirstOrDefault(f => f.Key == code.Game)?.Name ?? "another game"}, and items differ between games.");
        return ([.. code.Items
            .Where(i => i is { Id: > 0 })
            .DistinctBy(i => i.Id)
            .Take(MaxItems)
            .Select(i => i with { Name = (i.Name ?? "").Length > MaxName ? i.Name![..MaxName] : i.Name ?? "" })], null);
    }

    // What's new in the code goes on the list, what's there stays. Returns how many were added.
    public static int Merge(FlavorSettings s, IReadOnlyList<FollowedItem> items)
    {
        var added = items.Where(i => s.FollowedItems.All(o => o.Id != i.Id)).ToList();
        s.FollowedItems = [.. added, .. s.FollowedItems];
        return added.Count;
    }
}
