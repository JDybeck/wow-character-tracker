using System.Text.RegularExpressions;

namespace WoWCharacterTracker.Services;

// How a realm is read and shown, the same everywhere: pages, picture cards, Discord, the overlay.
// The type rule is the addon's ns.realmType (Shared.lua), FriendMapLuaTests checks they agree.
// Lookups (Raider.IO, armory export, "Name-Realm") keep the realm's real name.
static partial class Realms
{
    static readonly (string Plain, string Name)[] Types =
        [("RPPVP", "RP-PvP"), ("PVP", "PvP"), ("PVE", "PvE"), ("NORMAL", "Normal"), ("RP", "RP")];

    // Forever's realms end in a home realm number and their type ("CAMWOW EU5 Realm 6 PvP" -> "PvP"),
    // written with or without spaces; null for other names.
    public static string? Type(string realm)
    {
        var plain = PlainChars().Replace(realm, "").ToUpperInvariant();
        if (!ForeverName().IsMatch(plain)) return null;
        return Types.FirstOrDefault(t => plain.EndsWith(t.Plain, StringComparison.Ordinal)).Name;
    }

    // What players see: "EU · Tarren Mill", "EU · PvP" (only the type means anything on Forever),
    // region first when known so same-named realms stand apart. "" without a realm.
    public static string Label(string region, string realm)
    {
        var name = Type(realm) ?? realm;
        return region.Length > 0 && name.Length > 0 ? $"{region.ToUpperInvariant()} · {name}" : name;
    }

    // Battle.net's way of writing a realm: no spaces, hyphens or apostrophes.
    [GeneratedRegex(@"[\s\-']")]
    private static partial Regex PlainChars();

    [GeneratedRegex(@"REALM\d+[A-Z]+$")]
    private static partial Regex ForeverName();
}
