using System.Text.Json;
using System.Text.Json.Serialization;

namespace WoWCharacterTracker.Services;

// Every character day a game tab has seen, yours and friends', kept in days-<game>.json in the data folder.
// The addon only keeps the newest 8 days, so the Stats page's month and all time come from here,
// counted from when the app started keeping them.
sealed class DayHistory(string filePath)
{
    static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };
    List<DayTotals>? days;

    // Adds the days just read and returns all of them, one per character and day.
    public IReadOnlyList<DayTotals> Add(IEnumerable<DayTotals> seen)
    {
        days ??= Read();
        var merged = OnePerDay([.. days, .. seen]);
        if (merged.SequenceEqual(days)) return days;
        days = merged;
        try { Files.WriteAtomic(filePath, Serialize(days)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // kept in memory, saved next time
        return days;
    }

    // One per character and day, by character and date: the busiest, and of equals the later one (a day
    // still going changes its gold and item level before its counts).
    public static List<DayTotals> OnePerDay(IEnumerable<DayTotals> days) =>
        [.. days.GroupBy(d => (d.Guid, d.Day))
            .Select(g => g.Aggregate((best, d) => Activity(d) >= Activity(best) ? d : best))
            .OrderBy(d => d.Guid, StringComparer.Ordinal).ThenBy(d => d.Day, StringComparer.Ordinal)];

    static int Activity(DayTotals d) => d.Quests + d.Herbs + d.Ore + d.Skins + d.Fish
        + d.Bosses + d.Epics + d.Mounts + d.Duels + d.Battlegrounds + d.Deaths;

    // The file's text, and back (public for the tests).
    public static string Serialize(IReadOnlyList<DayTotals> days) => JsonSerializer.Serialize(days, Json);
    public static List<DayTotals> Deserialize(string text) => JsonSerializer.Deserialize<List<DayTotals>>(text, Json) ?? [];

    List<DayTotals> Read()
    {
        try { return File.Exists(filePath) ? OnePerDay(Deserialize(File.ReadAllText(filePath))) : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }
}
