using System.Windows;
using System.Windows.Media;

namespace WoWCharacterTracker.Ui;

// Each game tab has its own look, picked from lore palettes (Config.Themes, per game). Both games
// offer every palette; the defaults differ: Retail follows the current expansion (Midnight),
// Forever is Classic+ (gold).
// The tab's palette lives in its own merged dictionary, swapped in whole: every change to the
// app's resources makes WPF walk the whole window for DynamicResource users, so one swap is one
// walk instead of one per color. (It used to fade each color too, which redrew the window for
// every animation frame; on Retail that was most of a second of CPU per tab switch.)
static class Theme
{
    // Background, card, card header, line, then the accent (buttons, checks, frames), its hover,
    // its light and lightest text, then the second accent (chips, webhook list) from strong to lightest.
    public sealed record Palette(string Bg, string Card, string CardHeader, string Line,
        string Accent, string AccentHover, string AccentText, string AccentTextBright,
        string Second, string SecondMid, string SecondLight, string SecondText, string SecondTextBright, string SecondDeep);

    public sealed record Option(string Key, string Name, string Description, Palette Colors);

    static readonly Option[] RetailOptions =
    [
        new("midnight", "Midnight", "Void violet, the current expansion", new("#07040F", "#0E0A1E", "#150F2A", "#241A3D",
            "#7C3AED", "#8B5CF6", "#C4B5FD", "#EDE9FE",
            "#A21CAF", "#C026D3", "#E879F9", "#F0ABFC", "#FAE8FF", "#581C87")),
        new("arcane", "Void arcane", "Deep indigo, electric arcane blue, mystic purple", new("#05061A", "#0B0D28", "#111437", "#1F2452",
            "#2F5BFF", "#4D78FF", "#A9C1FF", "#E3EBFF",
            "#7E22CE", "#9333EA", "#C084FC", "#D8B4FE", "#F3E8FF", "#3B0764")),
        new("silvermoon", "Silvermoon", "Quel'Thalas crimson and gold", new("#0D0507", "#170A0D", "#200E12", "#3A1A20",
            "#B91C1C", "#DC2626", "#FCA5A5", "#FEE2E2",
            "#B45309", "#D97706", "#FBBF24", "#FCD34D", "#FEF3C7", "#78350F")),
    ];

    static readonly Option[] ForeverOptions =
    [
        new("classic", "Classic gold", "Warm bronze and gold, Classic+", new("#080604", "#120E08", "#19130B", "#2B2216",
            "#B45309", "#D97706", "#FCD34D", "#FEF3C7",
            "#A16207", "#CA8A04", "#FACC15", "#FDE68A", "#FEF9C3", "#713F12")),
        new("wisp", "Forever wisp", "The trailer's blue wisp glow", new("#03080D", "#07121B", "#0B1A26", "#15303F",
            "#0E7490", "#0891B2", "#67E8F9", "#CFFAFE",
            "#1D4ED8", "#2563EB", "#60A5FA", "#93C5FD", "#DBEAFE", "#1E3A8A")),
        new("emerald", "Emerald Dream", "Dreamy greens and teal", new("#030A06", "#08140C", "#0C1D12", "#17331F",
            "#15803D", "#16A34A", "#86EFAC", "#DCFCE7",
            "#0F766E", "#0D9488", "#5EEAD4", "#99F6E4", "#CCFBF1", "#134E4A")),
        new("ember", "Blackrock ember", "Molten orange and red", new("#0A0503", "#140A06", "#1C0F08", "#35190E",
            "#C2410C", "#EA580C", "#FDBA74", "#FFEDD5",
            "#B91C1C", "#DC2626", "#F87171", "#FCA5A5", "#FEE2E2", "#7F1D1D")),
    ];

    // Faction and city looks. Every theme is offered in both games: the game's own first (its first
    // is the default), then the other game's, then these.
    static readonly Option[] SharedOptions =
    [
        new("alliance", "Alliance", "Stormwind blue and gold", new("#04070F", "#0A1122", "#0F182E", "#1C2A4A",
            "#1D4ED8", "#2563EB", "#93C5FD", "#DBEAFE",
            "#B45309", "#CA8A04", "#FACC15", "#FDE68A", "#FEF9C3", "#713F12")),
        new("horde", "Horde", "Orgrimmar red and black iron", new("#090606", "#120C0B", "#1A1110", "#2E201D",
            "#B91C1C", "#DC2626", "#FCA5A5", "#FEE2E2",
            "#57534E", "#78716C", "#A8A29E", "#D6D3D1", "#F5F5F4", "#292524")),
        new("darnassus", "Darnassus", "Night elf moonlight, indigo and moonwell teal", new("#05060F", "#0B0D1C", "#111428", "#222645",
            "#4F46E5", "#6366F1", "#A5B4FC", "#E0E7FF",
            "#0F766E", "#0D9488", "#5EEAD4", "#99F6E4", "#CCFBF1", "#134E4A")),
        new("undercity", "Undercity", "Plague green and Forsaken purple", new("#060805", "#0D110B", "#131910", "#232D1D",
            "#4D7C0F", "#65A30D", "#BEF264", "#ECFCCB",
            "#6B21A8", "#7E22CE", "#C084FC", "#D8B4FE", "#F3E8FF", "#3B0764")),
        new("icecrown", "Icecrown", "The Lich King's frost and saronite silver", new("#04080C", "#0A121A", "#0F1A24", "#1E3244",
            "#0369A1", "#0284C7", "#7DD3FC", "#E0F2FE",
            "#475569", "#64748B", "#CBD5E1", "#E2E8F0", "#F8FAFC", "#1E293B")),
    ];

    static readonly Option[] RetailAll = [.. RetailOptions, .. ForeverOptions, .. SharedOptions];
    static readonly Option[] ForeverAll = [.. ForeverOptions, .. RetailOptions, .. SharedOptions];

    public static IReadOnlyList<Option> OptionsFor(Flavor flavor) => flavor == Flavors.Retail ? RetailAll : ForeverAll;

    // The picked option, or the game's default (the first) for an unknown or missing key.
    public static Option Find(Flavor flavor, string? key) =>
        OptionsFor(flavor).FirstOrDefault(o => o.Key == key) ?? OptionsFor(flavor)[0];

    public static Color ColorOf(string hex, byte alpha = 0xFF)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        c.A = alpha;
        return c;
    }

    static ResourceDictionary? current;

    public static void Apply(Flavor flavor, string? key)
    {
        var p = Find(flavor, key).Colors;
        var dict = new ResourceDictionary();
        Set("Bg", p.Bg); Set("Card", p.Card); Set("CardHeader", p.CardHeader);
        Set("Line", p.Line); Set("LineSoft", p.Line, 0x80); Set("Hover", p.Line, 0x4D); Set("Selected", p.Line, 0x99);
        Set("Blue", p.Accent); Set("BlueHover", p.AccentHover); Set("BlueSoft", p.Accent, 0x33); Set("BlueLine", p.AccentHover, 0x80);
        Set("BlueText", p.AccentText); Set("BlueTextBright", p.AccentTextBright);
        Set("IndigoStrong", p.Second); Set("IndigoMid", p.SecondMid); Set("Indigo", p.SecondLight);
        Set("IndigoChip", p.SecondMid, 0x33); Set("IndigoLine", p.SecondMid, 0x80); Set("IndigoLineSoft", p.SecondMid, 0x4D);
        Set("IndigoText", p.SecondText); Set("IndigoTextBright", p.SecondTextBright);
        Set("IndigoSoft", p.SecondDeep, 0x33); Set("IndigoPanel", p.SecondDeep, 0x33);
        // The framed character rows: an accent edge, stronger on hover and when open, over a soft
        // top-to-bottom shade of the card colors.
        Set("Frame", p.Accent, 0x8C); Set("FrameStrong", p.AccentHover, 0xE6);
        var shade = new LinearGradientBrush(ColorOf(p.CardHeader), ColorOf(p.Card), 90);
        shade.Freeze();
        dict["RowBg"] = shade;

        var merged = Application.Current.Resources.MergedDictionaries;
        if (current is not null && merged.IndexOf(current) is var i and >= 0) merged[i] = dict;
        else merged.Add(dict); // last, so it wins over Palette.xaml's defaults
        current = dict;

        // Styles freeze the brushes they use, so each key gets a new brush (the XAML uses
        // DynamicResource for these). Frozen brushes are cheaper to render.
        void Set(string name, string hex, byte alpha = 0xFF)
        {
            var brush = new SolidColorBrush(ColorOf(hex, alpha));
            brush.Freeze();
            dict[name] = brush;
        }
    }
}
