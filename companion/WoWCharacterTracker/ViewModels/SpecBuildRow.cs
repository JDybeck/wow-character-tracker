namespace WoWCharacterTracker.ViewModels;

// The character's other dual spec build (Forever), shown in place of the active one with its own
// Wowhead link and Discord post. Owner: the character, for the class, name and webhooks.
sealed class SpecBuildRow(CharacterRow owner, SpecBuild build) : Observable
{
    public CharacterRow Owner { get; } = owner;
    public SpecBuild Build { get; } = build;
    public string Talents => Build.Talents;
    public string TalentCalc => Build.TalentCalc;
    public bool HasTalentLink => TalentCalc.Length > 0;
    public string Description => string.Join(" ", new[] { Owner.Race, Build.Spec, Owner.ClassName }.Where(s => s.Length > 0));

    public IReadOnlyList<IconChip> TalentPickList { get; private set; } = [];
    public bool HasTalentPicks => TalentPickList.Count > 0;

    // Chips and icons the same way as the active build's (CharacterRow.LoadIcons).
    public async Task LoadIconsAsync(Flavor flavor)
    {
        TalentPickList = CharacterRow.TalentChips(Build.TalentPicks);
        Raise(nameof(TalentPickList));
        Raise(nameof(HasTalentPicks));
        await CharacterRow.LoadTalentIconsAsync(TalentPickList, Owner.Find, flavor);
    }
}
