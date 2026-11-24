namespace WoWCharacterTracker.Services;

// Who a character is, as far as its picture goes.
sealed record PictureSubject(string Guid, string Region, string Realm, string Name, string RaceFile, string Sex, string ClassFile);

// The one rule for a character's picture, used everywhere it shows (the character list and details,
// Discord posts, character cards), so they always agree:
// - Forever: the in-game portrait from the character's last check-in, unless the user picked the
//   standard picture for it (Config.StandardPortraits).
// - A friend's character, where the game has Blizzard's armory: its armory picture (there are no
//   check-ins of theirs here), unless the user picked the standard picture for it.
// - Otherwise the standard picture: on Retail the character's render from Raider.IO (needs the
//   region), else the race portrait, else the class icon (CharacterArt).
// Discord needs a web address for the sender's avatar, so posts use the standard picture there and
// upload the in-game portrait as the thumbnail.
static class CharacterPicture
{
    // The web picture: render, race portrait or class icon.
    public static async Task<string?> StandardAsync(Flavor flavor, PictureSubject who)
    {
        if (flavor.HasRenders && await RaiderIo.AvatarUrlAsync(who.Region, who.Realm, who.Name) is { } render) return render;
        return await CharacterArt.PictureUrlAsync(who.RaceFile, who.Sex, who.ClassFile);
    }

    // The in-game portrait file when that's the one to show, else null.
    public static string? GamePortrait(Flavor flavor, string guid, bool standardChosen) =>
        flavor.HasInGamePortraits && !standardChosen ? CharacterArt.LocalPortrait(guid) : null;

    // The picture by the rule above once it's all looked up (no class icon placeholder), for the
    // addon's portraits (AddonPortraits).
    public static async Task<string?> ShownAsync(Flavor flavor, PictureSubject who, bool friend, bool standardChosen, bool armorySd)
    {
        if (friend) { if (!standardChosen && await ArmoryAsync(flavor, who, armorySd) is { } armory) return armory; }
        else if (GamePortrait(flavor, who.Guid, standardChosen) is { } own) return own;
        return await StandardAsync(flavor, who);
    }

    // A friend's armory picture (sd: old models, Config.ArmorySd) when the armory has one, else null.
    public static async Task<string?> ArmoryAsync(Flavor flavor, PictureSubject who, bool sd) =>
        BlizzardArmory.AvatarUrl(flavor.ArmoryGame, who.Region, who.Guid, sd) is { } url && await CharacterArt.ExistsAsync(url) ? url : null;
}
