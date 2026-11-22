using System.Windows.Media;

namespace WoWCharacterTracker.ViewModels;

// One faction on a character's overview: its name, standing (renown, a friendship's rank) in the
// game's standing colors and a bar for how far through it the character is.
sealed class ReputationRow(Reputation r)
{
    public string Name { get; } = r.Name;
    public string StandingText { get; } = r.StandingText;
    public double Fraction { get; } = r.Fraction;
    public string Tooltip { get; } = r.ProgressText.Length > 0 ? $"{r.StandingText}, {r.ProgressText}" : r.StandingText;
    public SolidColorBrush Brush { get; } = ClassColors.Hex(Color(r));

    // As the game's FACTION_BAR_COLORS: red for hated and hostile, orange unfriendly, yellow neutral,
    // green from friendly up. Renown and friendships in blue.
    internal static int Color(Reputation r) => r.Renown is not null || r.Label is not null ? 0x38BDF8 : r.Standing switch
    {
        <= 2 => 0xCC2222,
        3 => 0xBF4500,
        4 => 0xE6B300,
        _ => 0x22C55E,
    };
}
