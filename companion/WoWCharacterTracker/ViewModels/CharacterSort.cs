namespace WoWCharacterTracker.ViewModels;

// How the character list is sorted: a key (a column, or race, played time, rating) and a direction.
// Clicking the key in use flips it; a new key starts the way that reads best (names A to Z, numbers
// highest first). Saved as "key:asc" / "key:desc" (Config.CharacterSort). Default: last seen, newest first.
sealed class CharacterSort : Observable
{
    public const string Name = "name", Class = "class", Level = "level", ItemLevel = "ilvl", Gold = "gold",
        Seen = "seen", Race = "race", Played = "played", Rating = "rating";

    static readonly HashSet<string> TextKeys = [Name, Class, Race];

    string key = Seen;
    bool descending = true;

    public string Key => key;
    public bool Descending => descending;

    public static CharacterSort Parse(string? saved)
    {
        var sort = new CharacterSort();
        var parts = (saved ?? "").Split(':');
        if (parts.Length == 2 && parts[0].Length > 0) (sort.key, sort.descending) = (parts[0], parts[1] == "desc");
        return sort;
    }

    public override string ToString() => $"{key}:{(descending ? "desc" : "asc")}";

    public void Click(string newKey)
    {
        if (newKey == key) descending = !descending;
        else (key, descending) = (newKey, !TextKeys.Contains(newKey));
        Raise(string.Empty); // every header's arrow
    }

    // "▲"/"▼" next to the header in use, "" elsewhere. Bound as Sort[name] and so on.
    public string this[string column] => column == key ? (descending ? "▼" : "▲") : "";

    // Ties keep the newest played first.
    public IEnumerable<CharacterRow> Apply(IEnumerable<CharacterRow> rows)
    {
        Func<CharacterRow, IComparable> by = key switch
        {
            Name => r => r.FullName,
            Class => r => r.ClassName,
            Level => r => r.Level,
            ItemLevel => r => r.ItemLevel,
            Gold => r => r.Money,
            Race => r => r.Race,
            Played => r => r.Played,
            Rating => r => r.BestRating,
            _ => r => r.LastSeen,
        };
        var sorted = descending ? rows.OrderByDescending(by) : rows.OrderBy(by);
        return sorted.ThenByDescending(r => r.LastSeen);
    }
}
