using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace WoWCharacterTracker.Services;

// Which Wowhead page an enchant on a worn item is. The game gives the enchant's id (in the item link)
// and a line of text, which on Retail is the name ("Enchant Ring - Nature's Fury") but on Classic
// clients like Forever only the stats ("Stamina +2"), and Wowhead has no tooltips by enchant id. So:
//   1. the id in the game's enchanting spells (Flavor.EnchantSpells, made by tools/enchant-spells.ps1:
//      Forever's own data, Classic Era's for Retail, which kept those ids), else
//   2. the text as a name on Wowhead (Wowhead.FindAsync), which finds Retail's named enchants.
static class Enchants
{
    static readonly ConcurrentDictionary<string, Dictionary<int, int>> Maps = new();

    public static async Task<(string Type, int Id)?> FindAsync(int enchantId, string text, Flavor flavor)
    {
        if (SpellFor(enchantId, flavor) is { } spell) return ("spell", spell);
        return await Wowhead.FindAsync(text, flavor);
    }

    // The enchanting spell that puts this enchant on in this game, or null when it has none.
    public static int? SpellFor(int enchantId, Flavor flavor) =>
        enchantId > 0 && Maps.GetOrAdd(flavor.EnchantSpells, Load).TryGetValue(enchantId, out var spell) ? spell : null;

    static Dictionary<int, int> Load(string file)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Data/" + file);
        if (s is null) return [];
        var byText = JsonSerializer.Deserialize<Dictionary<string, int>>(s) ?? [];
        return byText.Where(p => int.TryParse(p.Key, out _)).ToDictionary(p => int.Parse(p.Key), p => p.Value);
    }
}
