namespace WoWCharacterTracker.ViewModels;

// Friends' gold and played time added up: per friend over the characters they share, and for all of
// them, with each friend's share of the whole. Only what they share counts: gold comes with the Gold
// category and played with Level times, so a friend who doesn't share one has none of it, and is
// left out of that share instead of counting as 0%. (The addon's ns.friendTotals does the same.)
sealed record FriendTotal(string Tag, int Characters, long Money, long Played, int GoldPercent, int PlayedPercent)
{
    public bool SharesGold => Money > 0;
    public bool SharesPlayed => Played > 0;

    // "12,345g (40%) · 6d 4h 12m played (25%)" on the friend's card; each percent only when more than
    // one friend shares that (FriendTotals.Text).
    public string Text(bool goldPercent, bool playedPercent)
    {
        var parts = new List<string>();
        if (SharesGold) parts.Add(Format.Gold(Money) + (goldPercent ? $" ({GoldPercent}%)" : ""));
        if (SharesPlayed) parts.Add($"{Format.Played(Played)} played" + (playedPercent ? $" ({PlayedPercent}%)" : ""));
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

    // One friend's line, with their share of each only when there's more than one friend to compare.
    public string Text(string tag) => For(tag)?.Text(WithGold > 1, WithPlayed > 1) ?? "";

    public static FriendTotals Of(IEnumerable<CharacterRow> rows) => FromChars(rows.Select(r => (r.FriendTag, r.Money, r.Played)));

    // chars: one per friend's character, its friend's BattleTag, copper and seconds played (0: not shared).
    internal static FriendTotals FromChars(IEnumerable<(string Tag, long Money, long Played)> chars)
    {
        var groups = chars.GroupBy(c => c.Tag)
            .Select(g => (Tag: g.Key, Count: g.Count(), Money: g.Sum(c => Math.Max(0, c.Money)), Played: g.Sum(c => Math.Max(0, c.Played))))
            .ToList();
        long money = groups.Sum(g => g.Money), played = groups.Sum(g => g.Played);
        var byFriend = groups.ToDictionary(g => g.Tag, g => new FriendTotal(g.Tag, g.Count, g.Money, g.Played, Percent(g.Money, money), Percent(g.Played, played)));
        return new(byFriend, money, played, groups.Sum(g => g.Count));
    }

    static int Percent(long part, long whole) => whole > 0 ? (int)Math.Round(100.0 * part / whole, MidpointRounding.AwayFromZero) : 0;
}
