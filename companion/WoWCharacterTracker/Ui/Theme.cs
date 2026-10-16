using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WoWCharacterTracker.Ui;

// Each game tab has its own look: Retail is Midnight (void violet), Forever is classic gold.
// The palette brushes in App.xaml are recolored in place, so everything using them follows.
static class Theme
{
    // Background, card, card header, line, then the accent (buttons, checks), its hover,
    // its light and lightest text, then the second accent (chips, webhook list) from strong to lightest.
    sealed record Palette(string Bg, string Card, string CardHeader, string Line,
        string Accent, string AccentHover, string AccentText, string AccentTextBright,
        string Second, string SecondMid, string SecondLight, string SecondText, string SecondTextBright, string SecondDeep);

    static readonly Palette Midnight = new("#07040F", "#0E0A1E", "#150F2A", "#241A3D",
        "#7C3AED", "#8B5CF6", "#C4B5FD", "#EDE9FE",
        "#A21CAF", "#C026D3", "#E879F9", "#F0ABFC", "#FAE8FF", "#581C87");

    static readonly Palette Gold = new("#080604", "#120E08", "#19130B", "#2B2216",
        "#B45309", "#D97706", "#FCD34D", "#FEF3C7",
        "#A16207", "#CA8A04", "#FACC15", "#FDE68A", "#FEF9C3", "#713F12");

    public static void Apply(Flavor flavor, bool animate = true)
    {
        var p = flavor == Flavors.Retail ? Midnight : Gold;
        Set("Bg", p.Bg); Set("Card", p.Card); Set("CardHeader", p.CardHeader);
        Set("Line", p.Line); Set("LineSoft", p.Line, 0x80); Set("Hover", p.Line, 0x4D); Set("Selected", p.Line, 0x99);
        Set("Blue", p.Accent); Set("BlueHover", p.AccentHover); Set("BlueSoft", p.Accent, 0x33); Set("BlueLine", p.AccentHover, 0x80);
        Set("BlueText", p.AccentText); Set("BlueTextBright", p.AccentTextBright);
        Set("IndigoStrong", p.Second); Set("IndigoMid", p.SecondMid); Set("Indigo", p.SecondLight);
        Set("IndigoChip", p.SecondMid, 0x33); Set("IndigoLine", p.SecondMid, 0x80); Set("IndigoLineSoft", p.SecondMid, 0x4D);
        Set("IndigoText", p.SecondText); Set("IndigoTextBright", p.SecondTextBright);
        Set("IndigoSoft", p.SecondDeep, 0x33); Set("IndigoPanel", p.SecondDeep, 0x33);

        // Styles freeze the brushes they use, so each key gets a new brush (the XAML uses
        // DynamicResource for these), fading from the old color.
        void Set(string key, string hex, byte alpha = 0xFF)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            c.A = alpha;
            var brush = new SolidColorBrush(c);
            if (animate && Application.Current.Resources[key] is SolidColorBrush old && old.Color != c)
            {
                brush.Color = old.Color;
                brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(c, TimeSpan.FromMilliseconds(250)));
            }
            Application.Current.Resources[key] = brush;
        }
    }
}
