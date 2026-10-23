using System.Windows.Media;

namespace WoWCharacterTracker.Ui;

// Alliance blue and Horde red for the faction accents (the edge on a character row, the glow at
// the top of its details). Muted a little so they sit next to class colors without fighting them,
// and fixed like class colors, so they read the same in every theme. One frozen brush each.
static class FactionColors
{
    static readonly Color Alliance = Color.FromRgb(0x3E, 0x7B, 0xE0);
    static readonly Color Horde = Color.FromRgb(0xCC, 0x3B, 0x33);

    static readonly Dictionary<string, (SolidColorBrush Edge, LinearGradientBrush Glow)> Brushes = new()
    {
        [Factions.Alliance] = Make(Alliance),
        [Factions.Horde] = Make(Horde),
    };

    public static SolidColorBrush? Edge(string faction) => Brushes.TryGetValue(faction, out var b) ? b.Edge : null;
    public static Brush? Glow(string faction) => Brushes.TryGetValue(faction, out var b) ? b.Glow : null;

    static (SolidColorBrush, LinearGradientBrush) Make(Color c)
    {
        var glow = new LinearGradientBrush(Color.FromArgb(0x2E, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B), 90);
        glow.Freeze();
        return (ClassColors.Brush(c, 0xCC), glow);
    }
}
