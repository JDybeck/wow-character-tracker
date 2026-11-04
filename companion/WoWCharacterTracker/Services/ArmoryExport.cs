using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WoWCharacterTracker.Services;

// A character as a JSON file to share on an armory site (a separate project, proof of concept).
// Only what an armory would show: no GUID, gold, last zone, notes, tags, bags, bank, or the names
// of the people in a boss kill's group. SchemaVersion goes up when a field changes meaning or goes
// away; added fields don't need it. Missing values are left out, not written as "" or 0.
static class ArmoryExport
{
    public const string Schema = "wct-character";
    public const int SchemaVersion = 1;

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keeps å, ä, ö and ' readable
    };

    public static ArmoryFile From(CharacterRow c, Flavor flavor, Version app, DateTimeOffset now) => new(
        Schema, SchemaVersion, flavor.Key, app.ToString(3), now.ToUniversalTime(),
        new ArmoryCharacter(
            c.Name, Text(c.Surname), c.Realm, Text(c.Region), c.Level,
            c.ClassName, c.ClassFile, c.Race, c.RaceFile, c.Sex, Text(c.Faction),
            Text(c.Spec), Text(c.Role), Text(c.Guild), c.Ruleset, c.Hardcore, c.Dead,
            c.ItemLevel > 0 ? Math.Round(c.ItemLevel, 1) : null, c.Played > 0 ? c.Played : null,
            Professions(c.Professions), Text(c.Talents), Text(c.TalentCalc), Text(c.TalentExport),
            [.. c.StatList.Select(s => new ArmoryStat(s.Name, s.Value))],
            [.. Format.Ratings(c.MplusScore, c.PvpRatings).Select(r => new ArmoryRating(r.Name, r.Rating))]),
        Gear(c.Inventory),
        Moments(c.Events));

    public static string ToJson(ArmoryFile file) => JsonSerializer.Serialize(file, Json);

    // "Zebdryg-Firemaw.json": what the save dialog suggests, without characters Windows won't take.
    public static string FileName(CharacterRow c)
    {
        var name = $"{c.Name}-{c.Realm}".Replace(' ', '-');
        foreach (var bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad.ToString(), "");
        return name + ".json";
    }

    // "Mining 150, Herbalism 75"; a name without a number keeps no skill.
    static List<ArmoryProfession> Professions(string text)
    {
        if (text is "" or "None") return [];
        return [.. text.Split(", ", StringSplitOptions.RemoveEmptyEntries).Select(p =>
        {
            var space = p.LastIndexOf(' ');
            return space > 0 && int.TryParse(p[(space + 1)..], out var skill)
                ? new ArmoryProfession(p[..space], skill)
                : new ArmoryProfession(p, null);
        })];
    }

    // Equipped items with their enchants, in slot order.
    static List<ArmoryGear> Gear(Inventory? inv)
    {
        if (inv is null) return [];
        return [.. inv.Gear.Where(s => s.ItemId > 0).OrderBy(s => s.Slot).Select(s =>
        {
            var enchant = inv.Enchants.FirstOrDefault(e => e.Slot == s.Slot && e.ItemId == s.ItemId);
            return new ArmoryGear(s.Slot, s.ItemId, Text(enchant?.Text ?? ""), enchant is { EnchantId: > 0 } ? enchant.EnchantId : null);
        })];
    }

    // Level-ups and highlights, oldest first, with the same line the app shows.
    static List<ArmoryMoment> Moments(IEnumerable<Entry> events) => [.. events
        .Where(e => e.Kind == "level" || EventKinds.Highlights.Contains(e.Kind))
        .OrderBy(e => e.At)
        .Select(e => new ArmoryMoment(
            e.Kind, DateTimeOffset.FromUnixTimeSeconds(e.At), e.Level,
            Text(e.Kind == "level" ? $"Reached level {e.Level}" : HighlightText.Text(e)),
            e.Kind == "level" && e.Seconds > 0 ? e.Seconds : null,
            e.ItemId > 0 ? e.ItemId : null,
            e.ItemId > 0 && e.Quality > 0 ? e.Quality : null,
            e.MaxLevel ? true : null))];

    static string? Text(string s) => s.Length > 0 ? s : null;
}

record ArmoryFile(string Schema, int SchemaVersion, string Game, string App, DateTimeOffset ExportedAt,
    ArmoryCharacter Character, IReadOnlyList<ArmoryGear> Gear, IReadOnlyList<ArmoryMoment> Moments);

record ArmoryCharacter(string Name, string? Surname, string Realm, string? Region, int Level,
    string Class, string ClassFile, string Race, string RaceFile, string Sex, string? Faction,
    string? Spec, string? Role, string? Guild, string Ruleset, bool Hardcore, bool Dead,
    double? ItemLevel, long? Played, IReadOnlyList<ArmoryProfession> Professions,
    string? Talents, string? TalentCalc, string? TalentExport,
    IReadOnlyList<ArmoryStat> Stats, IReadOnlyList<ArmoryRating> Ratings);

record ArmoryProfession(string Name, int? Skill);
record ArmoryStat(string Name, string Value);
record ArmoryRating(string Name, int Rating);
record ArmoryGear(int Slot, int ItemId, string? Enchant, int? EnchantId);
// Took: seconds the level took. MaxLevel: this level-up reached the game's cap.
record ArmoryMoment(string Kind, DateTimeOffset At, int Level, string? Text, long? Took, int? ItemId, int? Quality, bool? MaxLevel);
