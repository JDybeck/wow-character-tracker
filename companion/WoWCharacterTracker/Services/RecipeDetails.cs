using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace WoWCharacterTracker.Services;

// One reagent of a recipe: the item and how many it takes.
sealed record RecipeReagent(int ItemId, string Name, int Count);

// What a recipe needs and makes, read from Wowhead's tooltip for the recipe's spell: its tools
// ("Blacksmith Hammer"), reagents with counts, and the item it makes (0 for one that makes none, like
// an enchant). The rest of the tooltip (cast time, the item's stats) is drawn by Ui/WowTooltip.
sealed partial record RecipeDetails(IReadOnlyList<string> Tools, IReadOnlyList<RecipeReagent> Reagents, int MadeItemId)
{
    public static RecipeDetails Empty { get; } = new([], [], 0);

    public static RecipeDetails Parse(string? html)
    {
        if (string.IsNullOrEmpty(html)) return Empty;
        var sections = Section().Matches(html);
        var tools = sections.FirstOrDefault(m => m.Groups["head"].Value == "Tools");
        var reagents = sections.FirstOrDefault(m => m.Groups["head"].Value == "Reagents");
        List<string> toolNames = tools is null ? [] : [.. Link().Matches(tools.Groups["list"].Value).Select(m => Text(m.Groups["name"].Value))];
        List<RecipeReagent> reagentList = reagents is null ? [] : [.. Link().Matches(reagents.Groups["list"].Value)
            .Where(m => m.Groups["type"].Value == "item")
            .Select(m => new RecipeReagent(Number(m.Groups["id"].Value), Text(m.Groups["name"].Value),
                m.Groups["count"].Success ? Math.Max(1, Number(m.Groups["count"].Value)) : 1))];
        // The made item is the first item link after the lists (Wowhead puts its tooltip there).
        var after = Math.Max(tools is null ? 0 : tools.Index + tools.Length, reagents is null ? 0 : reagents.Index + reagents.Length);
        var made = after > 0 ? Link().Matches(html[after..]).FirstOrDefault(m => m.Groups["type"].Value == "item") : null;
        return new(toolNames, reagentList, made is null ? 0 : Number(made.Groups["id"].Value));
    }

    static int Number(string s) => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
    static string Text(string s) => WebUtility.HtmlDecode(s).Trim();

    // "Reagents:<br /><div class="indent q1">...</div>", the same for Tools.
    [GeneratedRegex(@"(?<head>Tools|Reagents):<br\s*/?>\s*<div[^>]*>(?<list>.*?)</div>", RegexOptions.Singleline)]
    private static partial Regex Section();

    // A link to an item or spell, and a count after it: <a href="/forever/item=2841/bronze-bar">Bronze Bar</a>&nbsp;(5)
    [GeneratedRegex(@"<a href=""[^""]*?(?<type>item|spell)=(?<id>\d+)[^""]*""[^>]*>(?<name>[^<]*)</a>(?:(?:&nbsp;|\s)*\((?<count>\d+)\))?")]
    private static partial Regex Link();
}
