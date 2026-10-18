namespace WoWCharacterTracker.Services;

static class Format
{
    // The name posts use, from the tab's NameStyle: "full", "first" or "last" (surname, if there is one).
    public static string Name(string first, string surname, string style) => style switch
    {
        _ when surname.Length == 0 => first,
        "first" => first,
        "last" => surname,
        _ => $"{first} {surname}",
    };

    // Discord can't show item icons in text, so professions get a plain emoji each.
    static readonly Dictionary<string, string> ProfessionEmoji = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Alchemy"] = "⚗️", ["Blacksmithing"] = "🔨", ["Enchanting"] = "✨", ["Engineering"] = "⚙️",
        ["Herbalism"] = "🌿", ["Leatherworking"] = "🧥", ["Mining"] = "⛏️", ["Skinning"] = "🔪",
        ["Tailoring"] = "🧵", ["Inscription"] = "📜", ["Jewelcrafting"] = "💎", ["Cooking"] = "🍳",
        ["Fishing"] = "🎣", ["First Aid"] = "🩹", ["Archaeology"] = "🏺",
    };

    // "Mining 150, Herbalism 75" -> "⛏️ Mining 150, 🌿 Herbalism 75"
    public static string ProfessionsWithEmoji(string professions)
    {
        if (professions is "" or "None") return "";
        return string.Join(", ", professions.Split(", ").Select(p =>
        {
            var name = p.TrimEnd("0123456789 ".ToCharArray());
            return ProfessionEmoji.TryGetValue(name, out var emoji) ? $"{emoji} {p}" : p;
        }));
    }

    // Crests as text: "id~qty~name,..." (or the older "Name 45, Name 120") -> "Name 45, Name 120"
    public static string Crests(string crests)
    {
        if (!crests.Contains('~')) return crests;
        return string.Join(", ", crests.Split(',').Select(c => c.Split('~')).Where(p => p.Length >= 3).Select(p => $"{p[2]} {p[1]}"));
    }

    // The Retail keystone: "12~503~Ara-Kara, City of Echoes" -> "+12 Ara-Kara, City of Echoes"; "" for
    // none ("none") or not known.
    public static string Keystone(string keystone)
    {
        var p = keystone.Split('~', 3);
        if (p.Length < 3 || !int.TryParse(p[0], out var level) || level <= 0) return "";
        return $"+{level} {p[2]}".TrimEnd();
    }

    // GetPersonalRatedInfo's bracket numbers as the addon sends them in pvpRatings.
    static readonly Dictionary<int, string> PvpBrackets = new()
    {
        [1] = "2v2", [2] = "3v3", [4] = "RBG", [7] = "Solo Shuffle", [9] = "Blitz",
    };

    // The Retail ratings, Mythic+ first, then the PvP brackets in the game's order: score 2850 and
    // "2~1850,7~2100" -> (Mythic+, 2850), (3v3, 1850), (Solo Shuffle, 2100). Zeros, unknown brackets
    // and broken parts are left out.
    public static IReadOnlyList<(string Name, int Rating)> Ratings(int mplusScore, string pvpRatings)
    {
        var list = new List<(string, int)>();
        if (mplusScore > 0) list.Add(("Mythic+", mplusScore));
        var pvp = pvpRatings.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('~'))
            .Where(p => p.Length == 2 && int.TryParse(p[0], out _) && int.TryParse(p[1], out var r) && r > 0)
            .Select(p => (Bracket: int.Parse(p[0]), Rating: int.Parse(p[1])))
            .Where(p => PvpBrackets.ContainsKey(p.Bracket))
            .OrderBy(p => p.Bracket);
        foreach (var (bracket, rating) in pvp) list.Add((PvpBrackets[bracket], rating));
        return list;
    }

    // "Mythic+ 2850, 3v3 1850" (the stream card), "" without ratings.
    public static string RatingsText(int mplusScore, string pvpRatings) =>
        string.Join(", ", Ratings(mplusScore, pvpRatings).Select(r => $"{r.Name} {r.Rating}"));

    public static string Duration(long seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        if (ts.TotalMinutes >= 1) return $"{ts.Minutes}m {ts.Seconds}s";
        return $"{ts.Seconds}s";
    }

    // Longer form for total /played, e.g. "2d 3h 10m".
    public static string Played(long seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
        return Duration(seconds);
    }

    // Copper as "12,345g 34s 56c", leaving out leading zero parts.
    public static string Money(long copper)
    {
        long g = copper / 10000, s = copper / 100 % 100, c = copper % 100;
        if (g > 0) return $"{g.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}g {s}s {c}c";
        if (s > 0) return $"{s}s {c}c";
        return $"{c}c";
    }

    // "840 KB", "77.1 MB".
    public static string Size(long bytes) => bytes switch
    {
        < 1_000 => $"{bytes} bytes",
        < 1_000_000 => $"{bytes / 1e3:0} KB",
        < 1_000_000_000 => $"{bytes / 1e6:0.0} MB",
        _ => $"{bytes / 1e9:0.0} GB",
    };

    public static string Quality(int q) => q switch { 3 => "Rare", 4 => "Epic", 5 => "Legendary", 6 => "Artifact", _ => "" };

    public static string Ago(long unixSeconds)
    {
        if (unixSeconds <= 0) return "";
        var span = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays}d ago";
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime.ToString("yyyy-MM-dd");
    }
}
