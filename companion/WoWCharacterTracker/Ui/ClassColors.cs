using System.Collections.Concurrent;
using System.Windows.Media;

namespace WoWCharacterTracker.Ui;

// Brushes for the app's colors (Models/GameColors and the app's own): frozen and made once per
// color, so hundreds of rows and tooltip lines share them instead of each getting its own.
static class ClassColors
{
    static readonly Color Neutral = Color.FromRgb(0x94, 0xA3, 0xB8);
    static readonly ConcurrentDictionary<uint, SolidColorBrush> Brushes = new();

    public static Color For(string classFile) =>
        GameColors.Class(classFile) is int c
            ? Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c)
            : Neutral;

    public static SolidColorBrush Brush(Color c, byte alpha = 255) =>
        Brushes.GetOrAdd((uint)alpha << 24 | (uint)c.R << 16 | (uint)c.G << 8 | c.B, _ =>
        {
            var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            b.Freeze();
            return b;
        });

    public static SolidColorBrush Hex(int rgb, byte alpha = 255) => Brush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb), alpha);
}
