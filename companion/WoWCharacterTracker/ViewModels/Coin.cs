using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WoWCharacterTracker.ViewModels;

// One part of an amount of money: "73,101" with a gold coin, "77" with a silver one...
sealed record Coin(string Amount, SolidColorBrush Brush)
{
    static readonly SolidColorBrush Gold = ClassColors.Hex(0xF2C14E), Silver = ClassColors.Hex(0xC7CDD3), Copper = ClassColors.Hex(0xC27C4B);

    // Everything: gold, silver and copper (leading zero parts left out).
    public static IReadOnlyList<Coin> Full(long copper)
    {
        long g = copper / 10000, s = copper / 100 % 100, c = copper % 100;
        var parts = new List<Coin>();
        if (g > 0) parts.Add(new(g.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), Gold));
        if (g > 0 || s > 0) parts.Add(new(s.ToString(), Silver));
        parts.Add(new(c.ToString(), Copper));
        return parts;
    }

    // For the list: just gold from 100g up, gold and silver below that, silver and copper under 1g.
    public static IReadOnlyList<Coin> Short(long copper)
    {
        long g = copper / 10000, s = copper / 100 % 100, c = copper % 100;
        if (g >= 100) return [new(g.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), Gold)];
        if (g > 0) return [new(g.ToString(), Gold), new(s.ToString(), Silver)];
        return s > 0 ? [new(s.ToString(), Silver), new(c.ToString(), Copper)] : [new(c.ToString(), Copper)];
    }
}
