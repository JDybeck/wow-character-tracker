using System.Net;
using System.Text.RegularExpressions;

namespace WoWCharacterTracker.Services;

// Wowhead's tooltip HTML as lines to draw like the game's tooltip: each line has a left part and an
// optional right part ("One-Hand ... Sword", "30 - 57 Damage ... Speed 2.80"), made of colored
// pieces. Colors follow Wowhead's classes (q3 = rare blue, q2 = green "Equip:" lines, q = gold...).
static partial class ItemTooltip
{
    // Coin is "gold", "silver" or "copper" for the sell price's coin pieces, else null.
    public sealed record Piece(string Text, string Color, string? Coin = null);
    public sealed record Line(List<Piece> Left, List<Piece> Right);

    public const string White = "#FFFFFF";

    static readonly Dictionary<string, string> Colors = new()
    {
        ["q"] = "#FFD100", ["q0"] = "#9D9D9D", ["q1"] = "#FFFFFF", ["q2"] = "#1EFF00", ["q3"] = "#0070DD",
        ["q4"] = "#A335EE", ["q5"] = "#FF8000", ["q6"] = "#E6CC80", ["q7"] = "#00CCFF", ["q8"] = "#00CCFF",
        ["q9"] = "#71D5FF", ["q10"] = "#FF4040",
    };

    // An item quality's color ("#A335EE" for epic), null when it isn't one.
    public static string? QualityColor(int quality) => quality is >= 0 and <= 8 ? Colors[$"q{quality}"] : null;

    public static List<Line> Parse(string html)
    {
        var lines = new List<Line>();
        var left = new List<Piece>();
        var right = new List<Piece>();
        var onRight = false;
        // What each open span/b sets (a color, a coin, or nothing), so closing tags pop the right one.
        var stack = new Stack<(string? Color, string? Coin)>();

        void Flush()
        {
            Trim(left); Trim(right);
            if (left.Count > 0 || right.Count > 0) lines.Add(new Line(left, right));
            left = []; right = [];
            onRight = false;
        }

        foreach (Match m in Tokens().Matches(html))
        {
            var token = m.Value;
            if (token.StartsWith("<!--")) continue;
            if (token.StartsWith('<'))
            {
                var tag = TagName().Match(token).Groups[1].Value.ToLowerInvariant();
                var closing = token.StartsWith("</");
                switch (tag)
                {
                    case "br":
                    case "div":
                    case "table": // also opening: "Binds when picked up<table>One-Hand..." is two lines
                    case "tr" when closing:
                        Flush(); // a new line
                        break;
                    case "th": // the right-hand part of a two-column row
                        onRight = !closing;
                        break;
                    case "span" or "b" or "a":
                        if (closing) { if (stack.Count > 0) stack.Pop(); }
                        else stack.Push(Style(token));
                        break;
                }
                continue;
            }

            var text = Spaces().Replace(WebUtility.HtmlDecode(token), " ");
            var target = onRight ? right : left;
            if (target.Count == 0) text = text.TrimStart();
            if (text.Length == 0) continue;
            var color = stack.Select(s => s.Color).FirstOrDefault(c => c is not null) ?? White;
            var coin = stack.Select(s => s.Coin).FirstOrDefault(c => c is not null);
            target.Add(new Piece(text, color, coin));
        }
        Flush();
        return lines;
    }

    static (string? Color, string? Coin) Style(string tag)
    {
        var cls = ClassAttr().Match(tag);
        if (!cls.Success) return (null, null);
        string? color = null, coin = null;
        foreach (var c in cls.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Colors.TryGetValue(c, out var hex)) color = hex;
            else if (c.StartsWith("money")) coin = c[5..]; // moneygold, moneysilver, moneycopper
        }
        return (color, coin);
    }

    static void Trim(List<Piece> pieces)
    {
        if (pieces.Count == 0) return;
        var last = pieces[^1];
        pieces[^1] = last with { Text = last.Text.TrimEnd() };
        pieces.RemoveAll(p => p.Text.Length == 0);
    }

    [GeneratedRegex("<!--.*?-->|<[^>]+>|[^<]+", RegexOptions.Singleline)]
    private static partial Regex Tokens();

    [GeneratedRegex(@"^</?\s*([a-zA-Z0-9]+)")]
    private static partial Regex TagName();

    [GeneratedRegex(@"class\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex ClassAttr();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
