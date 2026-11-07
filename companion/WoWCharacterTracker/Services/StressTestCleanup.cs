namespace WoWCharacterTracker.Services;

// A pre-launch stress test (Flavor.StressTestBefore) left characters behind. Once per game tab the app
// forgets what it kept about them (notes, tags, portrait choice, the in-game portrait file) and hides
// them; the addon removes them from the saved file itself (Cleanup.lua), since the game owns that file.
static class StressTestCleanup
{
    // Characters whose snapshot and newest event are both from before `before`.
    public static List<string> OldCharacters(TrackerData data, long before)
    {
        var newest = data.Events.Where(e => e.Guid.Length > 0).GroupBy(e => e.Guid).ToDictionary(g => g.Key, g => g.Max(e => e.At));
        var seen = data.Characters.GroupBy(c => c.Guid).ToDictionary(g => g.Key, g => g.Max(c => c.LastSeen));
        return [.. seen.Keys.Union(newest.Keys)
            .Where(guid => seen.GetValueOrDefault(guid) < before && newest.GetValueOrDefault(guid) < before)];
    }

    public static void Apply(Config config, FlavorSettings settings, IEnumerable<string> guids, long before, Action<string> deletePortrait)
    {
        foreach (var guid in guids)
        {
            config.CharacterTags.Remove(guid);
            config.CharacterNotes.Remove(guid);
            config.CharacterNotesAt.Remove(guid);
            config.StandardPortraits.Remove(guid);
            settings.Removed[guid] = before; // hidden until the addon has cleaned it out of the saved file
            deletePortrait(guid);
        }
        settings.StressTestCleaned = true;
    }
}
