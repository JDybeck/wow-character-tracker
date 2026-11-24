using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace WoWCharacterTracker.Services;

// The skill a recipe needs ("Requires Blacksmithing (125)"), for games that have the list
// (Flavor.HasRecipeSkills). The game doesn't say it for learned recipes, so the addon carries it:
// RecipeSkills.lua, made by tools/recipe-skills.ps1 from Wowhead. The app reads the same file from the
// addon it ships, so there is one list.
static partial class RecipeSkills
{
    static readonly Lazy<IReadOnlyDictionary<int, (string Profession, int Skill)>> Bundled = new(() =>
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Addon/RecipeSkills.lua");
        if (s is null) return new Dictionary<int, (string, int)>();
        using var r = new StreamReader(s);
        return Parse(r.ReadToEnd());
    });

    // The skill and profession, or null when the recipe isn't in the list (or the game has none).
    public static (string Profession, int Skill)? For(Flavor flavor, int recipeId) =>
        flavor.HasRecipeSkills && Bundled.Value.TryGetValue(recipeId, out var found) ? found : null;

    // RecipeSkills.lua's table: ["Blacksmithing"] = { [9993]=185, ... }. Bad entries are skipped.
    public static IReadOnlyDictionary<int, (string Profession, int Skill)> Parse(string lua)
    {
        var skills = new Dictionary<int, (string, int)>();
        foreach (Match p in Profession().Matches(lua))
        {
            foreach (Match e in Entry().Matches(p.Groups["body"].Value))
            {
                if (int.TryParse(e.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                    && int.TryParse(e.Groups["skill"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var skill) && skill > 0)
                    skills.TryAdd(id, (p.Groups["name"].Value, skill));
            }
        }
        return skills;
    }

    [GeneratedRegex(@"\[""(?<name>[^""]+)""\]\s*=\s*\{(?<body>[^}]*)\}")]
    private static partial Regex Profession();

    [GeneratedRegex(@"\[(?<id>\d+)\]\s*=\s*(?<skill>\d+)")]
    private static partial Regex Entry();
}
