using System.Reflection;
using System.Text.Json;

namespace WoWCharacterTracker.Services;

// Which Wowhead page an enchant on a worn item is. The game gives the enchant's id (in the item link)
// and a line of text, which on Retail is the name ("Enchant Ring - Nature's Fury") but on Classic
// clients like Forever only the stats ("Stamina +2"), and Wowhead has no tooltips by enchant id. So:
//   1. the id in Classic's enchanting spells (Assets/enchant-spells.json, made by tools/enchant-spells.ps1
//      from Classic Era's game data; Retail kept those ids, so it helps there too), else
//   2. the text as a name on Wowhead (Wowhead.FindAsync), which finds Retail's named enchants.
static class Enchants
{
    static readonly Lazy<Dictionary<int, int>> Spells = new(LoadSpells);

    public static async Task<(string Type, int Id)?> FindAsync(int enchantId, string text, Flavor flavor)
    {
        if (SpellFor(enchantId) is { } spell) return ("spell", spell);
        return await Wowhead.FindAsync(text, flavor);
    }

    // The enchanting spell that puts this enchant on, or null when Classic has none.
    public static int? SpellFor(int enchantId) =>
        enchantId > 0 && Spells.Value.TryGetValue(enchantId, out var spell) ? spell : null;

    static Dictionary<int, int> LoadSpells()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Data/enchant-spells.json");
        if (s is null) return [];
        var byText = JsonSerializer.Deserialize<Dictionary<string, int>>(s) ?? [];
        return byText.Where(p => int.TryParse(p.Key, out _)).ToDictionary(p => int.Parse(p.Key), p => p.Value);
    }
}
