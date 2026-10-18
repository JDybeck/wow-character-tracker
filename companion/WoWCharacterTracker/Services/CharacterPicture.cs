namespace WoWCharacterTracker.Services;

// Who a character is, as far as its picture goes.
sealed record PictureSubject(string Guid, string Region, string Realm, string Name, string RaceFile, string Sex, string ClassFile);

// The one rule for a character's picture, used everywhere it shows (the character list and details,
// Discord posts, character cards), so they always agree:
// - Forever: the in-game portrait from the character's last check-in, unless the user picked the
//   standard picture for it (Config.StandardPortraits).
// - Otherwise the standard picture: on Retail the character's render from Raider.IO (needs the
//   region), else the race portrait, else the class icon (CharacterArt).
// Discord needs a web address for the sender's avatar, so posts use the standard picture there and
// upload the in-game portrait as the thumbnail.
static class CharacterPicture
{
    // The web picture: render, race portrait or class icon.
    public static async Task<string?> StandardAsync(Flavor flavor, PictureSubject who)
    {
        if (flavor == Flavors.Retail && await RaiderIo.AvatarUrlAsync(who.Region, who.Realm, who.Name) is { } render) return render;
        return await CharacterArt.PictureUrlAsync(who.RaceFile, who.Sex, who.ClassFile);
    }

    // The in-game portrait file when that's the one to show, else null.
    public static string? GamePortrait(Flavor flavor, string guid, bool standardChosen) =>
        flavor == Flavors.Forever && !standardChosen ? CharacterArt.LocalPortrait(guid) : null;

    // What the app shows: the in-game portrait file or the standard picture's address.
    public static async Task<string?> ShownAsync(Flavor flavor, PictureSubject who, bool standardChosen) =>
        GamePortrait(flavor, who.Guid, standardChosen) ?? await StandardAsync(flavor, who);
}
