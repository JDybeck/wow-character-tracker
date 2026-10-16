using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WoWCharacterTracker.Ui;

static class ClassColors
{
    static readonly Color Neutral = Color.FromRgb(0x94, 0xA3, 0xB8);

    public static Color For(string classFile) =>
        CharacterArt.ClassColors.TryGetValue(classFile.ToUpperInvariant(), out var c)
            ? Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c)
            : Neutral;

    public static SolidColorBrush Brush(Color c, byte alpha = 255)
    {
        var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    public static SolidColorBrush Hex(int rgb) => Brush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
}
