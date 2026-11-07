using System.Text.Json;

namespace WoWCharacterTracker.Tests;

// The armory proof of concept's JSON file: what goes in, and what stays private.
public class ArmoryExportTests
{
    const string Guid = "Player-1403-0A1B2C3D";

    static readonly Entry Character = new()
    {
        Kind = "snapshot", Guid = Guid, Char = "Zebdryg", Realm = "Firemaw", Level = 42, At = 1_800_000_000,
        Class = "Warrior", ClassFile = "WARRIOR", Race = "Human", RaceFile = "Human", Sex = "male", Faction = "Alliance",
        Spec = "Fury", ItemLevel = 41.26, Played = 360_000, Zone = "Stranglethorn Vale", Professions = "Mining 150/225, Herbalism",
        Talents = "Arms 5 / Fury 31", Money = 123_456, Guild = "Raket", Ruleset = "Hardcore", Hardcore = true,
    };

    static ArmoryFile Export(params Entry[] events)
    {
        var snap = Snapshot.Of(Character) with { Stats = "Strength~120,Crit~5.2%" };
        var inv = new Inventory(Guid, [new(Inventory.Equipped, 16, 19019, 1), new(Inventory.Equipped, 1, 12640, 1), new(Inventory.Equipped, 3, 0, 1)],
            [new(0, 1, 6948, 1)], new Dictionary<int, int>(), [], new Dictionary<int, int>(), 1, 0)
        {
            Enchants = [new GearEnchant(16, 19019, "Crusader", 1900)],
        };
        var data = new TrackerData([.. events], [snap]) { Inventories = [inv] };
        var row = CharacterRow.Build(data, Flavors.Forever, new GameLookup(Flavors.Forever, () => { })).Single();
        return ArmoryExport.From(row, Flavors.Forever, new Version(0, 33, 0), DateTimeOffset.FromUnixTimeSeconds(1_800_000_100));
    }

    [Fact]
    public void Carries_the_character_and_its_game()
    {
        var file = Export();
        Assert.Equal(("wct-character", 1, "forever", "0.33.0"), (file.Schema, file.SchemaVersion, file.Game, file.App));
        var c = file.Character;
        Assert.Equal(("Zebdryg", "Firemaw", 42, "WARRIOR", "Fury"), (c.Name, c.Realm, c.Level, c.ClassFile, c.Spec));
        Assert.Equal(("Hardcore", true, 41.3), (c.Ruleset, c.Hardcore, c.ItemLevel));
        Assert.Equal([new ArmoryProfession("Mining", 150, 225), new ArmoryProfession("Herbalism", null, null)], c.Professions);
        Assert.Equal([new ArmoryStat("Strength", "120"), new ArmoryStat("Crit", "5.2%")], c.Stats);
    }

    [Fact]
    public void Gear_is_equipped_items_in_slot_order_with_enchants()
    {
        Assert.Equal([new ArmoryGear(1, 12640, null, null), new ArmoryGear(16, 19019, "Crusader", 1900)], Export().Gear);
    }

    [Fact]
    public void Moments_are_level_ups_and_highlights_oldest_first()
    {
        var file = Export(
            Character with { Kind = "loot", At = 1_800_000_050, Item = "Thunderfury", ItemId = 19019, Quality = 5 },
            Character with { Kind = "level", At = 1_800_000_010, Level = 42, Seconds = 5400 },
            Character with { Kind = "test", At = 1_800_000_020 });

        Assert.Equal(["level", "loot"], file.Moments.Select(m => m.Kind));
        Assert.Equal(("Reached level 42", 5400L), (file.Moments[0].Text, file.Moments[0].Took));
        Assert.Equal(("Looted Thunderfury", 19019, 5), (file.Moments[1].Text, file.Moments[1].ItemId, file.Moments[1].Quality));
    }

    // An armory shows the character, not where it stands, what it owns or who it played with.
    [Fact]
    public void Leaves_out_what_is_private()
    {
        var json = ArmoryExport.ToJson(Export(Character with { Kind = "boss", Boss = "Onyxia", Group = "Tankman~WARRIOR~Protection", GroupSize = 5 }));
        using var doc = JsonDocument.Parse(json);
        var character = doc.RootElement.GetProperty("character");

        Assert.False(character.TryGetProperty("money", out _));
        Assert.False(character.TryGetProperty("zone", out _));
        Assert.DoesNotContain(Guid, json);
        Assert.DoesNotContain("Stranglethorn", json);
        Assert.DoesNotContain("Tankman", json);
        Assert.DoesNotContain("6948", json); // in the bags
        Assert.Contains("\"name\": \"Zebdryg\"", json);
    }

    [Fact]
    public void Missing_values_are_left_out()
    {
        var json = ArmoryExport.ToJson(Export());
        Assert.DoesNotContain("\"surname\"", json);
        Assert.DoesNotContain("\"talentExport\"", json);
        Assert.DoesNotContain("null", json);
    }

    [Fact]
    public void File_name_is_safe_for_windows()
    {
        var row = CharacterRow.Build(new TrackerData([], [Snapshot.Of(Character with { Char = "Zeb", Realm = "Living Flame?" })]),
            Flavors.Forever, new GameLookup(Flavors.Forever, () => { })).Single();
        Assert.Equal("Zeb-Living-Flame.json", ArmoryExport.FileName(row));
    }
}
