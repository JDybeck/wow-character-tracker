using System.Net;
using System.Text.RegularExpressions;

namespace WoWCharacterTracker.Services;

// Wowhead's tooltip HTML as lines of colored text: what the app's tooltips (Ui/WowTooltip) draw and
// what the plain-text version (Wowhead.ToText) is made of. Colors are Wowhead's class names ("q2"
// green, "q" the yellow item level, "c2" Paladin pink...), so this stays free of WPF.
static partial class TooltipHtml
{
    // One piece of text in one color (Color "" = the default white).
    public sealed record Part(string Text, string Color);

    // A line: the left side, and the right side of two-column rows ("Two-Hand ... Mace").
    public sealed record Line(IReadOnlyList<Part> Left, IReadOnlyList<Part> Right)
    {
        public string Text => Right.Count == 0 ? Join(Left) : $"{Join(Left)}   {Join(Right)}";
        static string Join(IEnumerable<Part> parts) => string.Concat(parts.Select(p => p.Text));
    }

    // Wowhead keeps these for its own extended view (drop sources, max stack), not the tooltip.
    static readonly string[] Hidden = ["whtt-droppedby", "whtt-dropchance", "whtt-maxstack"];

    // Walks the HTML once: a stack of colors from class attributes, a new line at <br>, rows, tables
    // and divs, a row's second cell (<th>) as the right side, and g/s/c after money (Wowhead shows
    // coin icons there). The HTML's own line breaks mean nothing.
    public static List<Line> Parse(string html)
    {
        var lines = new List<Line>();
        var left = new List<Part>();
        var right = new List<Part>();
        var inRight = false;
        var stack = new Stack<(string Tag, string? Color, bool Hide, string? Coin)>();

        void NewLine()
        {
            Trim(left);
            Trim(right);
            if (left.Count > 0 || right.Count > 0) lines.Add(new Line(left, right));
            left = [];
            right = [];
            inRight = false;
        }
        bool Hiding() => stack.Any(s => s.Hide);

        html = Comments().Replace(html, "");
        foreach (Match m in Token().Matches(html))
        {
            if (m.Groups["text"].Success)
            {
                if (Hiding()) continue;
                var text = Whitespace().Replace(WebUtility.HtmlDecode(m.Value), " ");
                var coin = stack.FirstOrDefault(s => s.Coin is not null).Coin;
                var color = stack.FirstOrDefault(s => s.Color is not null).Color ?? "";
                (inRight ? right : left).Add(new Part(coin is null ? text : text.Trim() + coin, color));
                continue;
            }

            var close = m.Groups["close"].Value == "/";
            var tag = m.Groups["tag"].Value.ToLowerInvariant();
            if (tag == "br") { NewLine(); continue; }
            if (tag == "img") continue;
            if (tag is "table" or "div" or "tr" && !Hiding()) NewLine();
            if (close)
            {
                // Back to the matching open tag (Wowhead's HTML is well formed, but be forgiving).
                if (stack.Any(s => s.Tag == tag)) while (stack.Count > 0 && stack.Pop().Tag != tag) { }
                continue;
            }
            if (tag == "th") inRight = true;

            var classes = ClassAttr().Match(m.Groups["attrs"].Value).Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var colorClass = classes.LastOrDefault(c => IsColor(c));
            var coinMark = classes.FirstOrDefault(c => c.StartsWith("money")) switch
            {
                "moneygold" => "g",
                "moneysilver" => "s",
                "moneycopper" => "c",
                _ => null,
            };
            stack.Push((tag, colorClass, classes.Any(Hidden.Contains), coinMark));
        }
        NewLine();
        return lines;
    }

    // The tooltip as plain text, one line per row.
    public static string ToText(string html) => string.Join("\n", Parse(html).Select(l => l.Text));

    static bool IsColor(string c) =>
        c is "q" or "toycolor" || c.StartsWith("money")
        || (c.Length >= 2 && c[0] is 'q' or 'c' && c[1..].All(char.IsDigit));

    // Spaces at either end of a line, and empty pieces, go.
    static void Trim(List<Part> parts)
    {
        parts.RemoveAll(p => p.Text.Length == 0);
        if (parts.Count == 0) return;
        parts[0] = parts[0] with { Text = parts[0].Text.TrimStart() };
        parts[^1] = parts[^1] with { Text = parts[^1].Text.TrimEnd() };
        parts.RemoveAll(p => p.Text.Length == 0);
    }

    [GeneratedRegex(@"<(?<close>/?)(?<tag>[a-zA-Z0-9]+)(?<attrs>[^>]*)>|(?<text>[^<]+)")]
    private static partial Regex Token();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comments();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"class=""([^""]*)""")]
    private static partial Regex ClassAttr();
}
