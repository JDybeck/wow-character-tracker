using WoWCharacterTracker.Services;

namespace WoWCharacterTracker.Tests;

public class RolesTests
{
    [Theory]
    [InlineData("TANK", "WARRIOR", "Protection", true, Roles.Tank)]
    [InlineData("HEALER", "PRIEST", "Holy", true, Roles.Healer)]
    [InlineData("DAMAGER", "HUNTER", "Marksmanship", true, Roles.Ranged)]
    [InlineData("DAMAGER", "HUNTER", "Survival", true, Roles.Melee)]  // melee on Retail
    [InlineData("", "HUNTER", "Survival", false, Roles.Ranged)]       // a bow on Forever
    [InlineData("DAMAGER", "PRIEST", "Shadow", true, Roles.Caster)]
    [InlineData("DAMAGER", "SHAMAN", "Enhancement", true, Roles.Melee)]
    [InlineData("DAMAGER", "SHAMAN", "Elemental", true, Roles.Caster)]
    [InlineData("DAMAGER", "EVOKER", "Augmentation", true, Roles.Caster)]
    [InlineData("DAMAGER", "DEATHKNIGHT", "Frost", true, Roles.Melee)]
    [InlineData("", "MAGE", "Frost", false, Roles.Caster)]
    [InlineData("", "PALADIN", "Protection", false, Roles.Tank)]      // Forever: from the top tree
    [InlineData("", "DRUID", "Restoration", false, Roles.Healer)]
    [InlineData("", "WARRIOR", "", false, "")]                        // no talents yet: nothing to go on
    public void Role_from_role_class_and_spec(string role, string classFile, string spec, bool retail, string expected) =>
        Assert.Equal(expected, Roles.Of(role, classFile, spec, retail));
}
