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

    // A line: the left side, and the right side of two-column rows ("Two-Hand ... Mace"). Gap: an
    // empty line came before it (a new paragraph), so it's drawn a bit further down.
    public sealed record Line(IReadOnlyList<Part> Left, IReadOnlyList<Part> Right, bool Gap = false)
    {
        public string Text => Right.Count == 0 ? Join(Left) : $"{Join(Left)}   {Join(Right)}";
        static string Join(IEnumerable<Part> parts) => string.Concat(parts.Select(p => p.Text));
    }

    // Wowhead keeps these for its own extended view (drop sources, max stack), not the tooltip.
    static readonly string[] Hidden = ["whtt-droppedby", "whtt-dropchance", "whtt-maxstack"];
    // Lines that say nothing about the character: a currency's cap is the game's, not how much is left.
    static readonly string[] HiddenLines = ["Total cap:", "Weekly cap:"];

    // Walks the HTML once: a stack of colors from class attributes, a new line at <br>, rows, tables
    // and divs, a row's second cell (<th>) as the right side, and g/s/c after money (Wowhead shows
    // coin icons there). Line breaks between tags mean nothing, but breaks inside a piece of text
    // (currency descriptions are written that way) are kept, an empty line as a gap.
    public static List<Line> Parse(string html)
    {
        var lines = new List<Line>();
        var left = new List<Part>();
        var right = new List<Part>();
        var inRight = false;
        var gap = false;
        var stack = new Stack<(string Tag, string? Color, bool Hide, string? Coin)>();

        void NewLine()
        {
            Trim(left);
            Trim(right);
            var line = new Line(left, right, gap && lines.Count > 0);
            if ((left.Count > 0 || right.Count > 0) && !HiddenLines.Any(h => line.Text.StartsWith(h)))
            {
                lines.Add(line);
                gap = false;
            }
            left = [];
            right = [];
            inRight = false;
        }
        bool Hiding() => stack.Any(s => s.Hide);
        void Add(string raw)
        {
            var text = Whitespace().Replace(raw, " ");
            var coin = stack.FirstOrDefault(s => s.Coin is not null).Coin;
            var color = stack.FirstOrDefault(s => s.Color is not null).Color ?? "";
            (inRight ? right : left).Add(new Part(coin is null ? text : text.Trim() + coin, color));
        }

        html = Comments().Replace(html, "");
        foreach (Match m in Token().Matches(html))
        {
            if (m.Groups["text"].Success)
            {
                if (Hiding()) continue;
                var raw = WebUtility.HtmlDecode(m.Value);
                var core = raw.Trim();
                if (!core.Contains('\n'))
                {
                    Add(raw);
                    continue;
                }
                var at = 0;
                foreach (Match b in LineBreak().Matches(core))
                {
                    Add(core[at..b.Index]);
                    NewLine();
                    if (b.Groups["blank"].Success) gap = true;
                    at = b.Index + b.Length;
                }
                Add(core[at..]);
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

    // The tooltip as plain text, one line per row, an empty line before a gap.
    public static string ToText(string html) => string.Join("\n", Parse(html).Select(l => l.Gap ? "\n" + l.Text : l.Text));

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

    // A line break inside text, with the spaces around it; "blank" when an empty line follows.
    [GeneratedRegex(@"[ \t\r]*\n(?<blank>[ \t\r]*\n)*[ \t\r]*")]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"class=""([^""]*)""")]
    private static partial Regex ClassAttr();
}
