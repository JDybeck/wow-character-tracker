namespace WoWCharacterTracker.ViewModels;

// Friends' gold and played time added up: per friend over the characters they share, and for all of
// them. Only what they share counts: gold comes with the Gold category and played with Level times,
// so a friend who doesn't share one has none of it and isn't counted as sharing it. (The addon's
// ns.friendTotals does the same.)
sealed record FriendTotal(string Tag, int Characters, long Money, long Played)
{
    public bool SharesGold => Money > 0;
    public bool SharesPlayed => Played > 0;

    // "12,345g · 6d 4h 12m played" on the friend's card.
    public string Text()
    {
        var parts = new List<string>();
        if (SharesGold) parts.Add(Format.Gold(Money));
        if (SharesPlayed) parts.Add($"{Format.Played(Played)} played");
        return string.Join(" · ", parts);
    }
}

sealed record FriendTotals(IReadOnlyDictionary<string, FriendTotal> ByFriend, long Money, long Played, int Characters)
{
    public static readonly FriendTotals Empty = new(new Dictionary<string, FriendTotal>(), 0, 0, 0);

    public int Friends => ByFriend.Count;
    public int WithGold => ByFriend.Values.Count(f => f.SharesGold);
    public int WithPlayed => ByFriend.Values.Count(f => f.SharesPlayed);

    public FriendTotal? For(string tag) => ByFriend.GetValueOrDefault(tag);

    // One friend's line.
    public string Text(string tag) => For(tag)?.Text() ?? "";

    public static FriendTotals Of(IEnumerable<CharacterRow> rows) => FromChars(rows.Select(r => (r.FriendTag, r.Money, r.Played)));

    // chars: one per friend's character, its friend's BattleTag, copper and seconds played (0: not shared).
    internal static FriendTotals FromChars(IEnumerable<(string Tag, long Money, long Played)> chars)
    {
        var groups = chars.GroupBy(c => c.Tag)
            .Select(g => (Tag: g.Key, Count: g.Count(), Money: g.Sum(c => Math.Max(0, c.Money)), Played: g.Sum(c => Math.Max(0, c.Played))))
            .ToList();
        var byFriend = groups.ToDictionary(g => g.Tag, g => new FriendTotal(g.Tag, g.Count, g.Money, g.Played));
        return new(byFriend, groups.Sum(g => g.Money), groups.Sum(g => g.Played), groups.Sum(g => g.Count));
    }
}
