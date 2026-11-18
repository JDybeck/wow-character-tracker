namespace WoWCharacterTracker.Tests;

// Forever dual spec: the addon saves the build that isn't active as chars[guid].otherSpec.
public class DualSpecTests
{
    const string Guid1 = "Player-1-0000001";

    static TrackerData Read(string otherSpec)
    {
        var file = Path.Combine(Path.GetTempPath(), "wct-dualspec-" + Guid.NewGuid().ToString("N") + ".lua");
        File.WriteAllText(file, $$"""
            WoWCharacterTrackerDB = { chars = { ["{{Guid1}}"] = {
              char = "Zeb", realm = "R", level = 60, class = "Warrior", classFile = "WARRIOR", race = "Human",
              talents = "Arms 8 / Fury 5 / Protection 38", spec = "Protection", talentCalc = "053-05-0505", lastSeen = 100,
              otherSpec = {{otherSpec}},
            } } }
            """);
        try { return SavedVariables.Read(file); }
        finally { File.Delete(file); }
    }

    static CharacterRow Row(TrackerData data) =>
        CharacterRow.Build(data, Flavors.Forever, new GameLookup(Flavors.Forever, () => { })).Single();

    const string Arms = "{ group = 2, talents = 'Arms 32 / Fury 19 / Protection 0', spec = 'Arms', talentPicks = '12294~1~1~1', talentCalc = '0330321-055' }";

    [Fact]
    public void The_saved_other_spec_is_one_switch_away()
    {
        var row = Row(Read(Arms));

        Assert.True(row.HasOtherSpec);
        Assert.True(row.ShowsActiveTalents);
        Assert.Equal("ACTIVE TALENTS (PRIMARY)", row.TalentsLabel);
        Assert.Equal("Show secondary spec", row.SwitchSpecText);

        row.SwitchSpecCommand.Execute(null);
        Assert.False(row.ShowsActiveTalents);
        Assert.Equal("SECONDARY TALENTS (INACTIVE)", row.TalentsLabel);
        Assert.Equal("Show active spec", row.SwitchSpecText);

        Assert.Equal("Arms 32 / Fury 19 / Protection 0", row.OtherSpec!.Talents);
        Assert.Equal("Human Arms Warrior", row.OtherSpec.Description);
        Assert.Equal("https://www.wowhead.com/forever/talent-calc/warrior/v20330321-055",
            Wowhead.TalentCalcUrl(Flavors.Forever, row.ClassFile, row.OtherSpec.TalentCalc, ""));
    }

    [Fact]
    public void One_spec_reads_as_before()
    {
        var row = Row(Read("nil"));

        Assert.False(row.HasOtherSpec);
        Assert.Equal("TALENTS", row.TalentsLabel);
    }

    [Theory]
    [InlineData("{ group = 3, talents = 'x' }")]
    [InlineData("{ group = 2, talents = '' }")]
    [InlineData("'not a table'")]
    public void A_broken_other_spec_is_left_out(string otherSpec) => Assert.False(Row(Read(otherSpec)).HasOtherSpec);

    // A live check-in after a swap already has the other build as the active one: it isn't shown twice.
    [Fact]
    public void A_check_in_after_a_swap_hides_the_stale_other_spec()
    {
        var saved = Read(Arms);
        var checkIn = Snapshot.Of(new Entry { Kind = "snapshot", Guid = Guid1, Char = "Zeb", Realm = "R", Level = 60, At = 200, TalentCalc = "0330321-055" });
        var row = Row(saved with { Characters = [.. saved.Characters, checkIn] });

        Assert.False(row.HasOtherSpec);
        Assert.Equal("0330321-055", row.TalentCalc);
    }
}
