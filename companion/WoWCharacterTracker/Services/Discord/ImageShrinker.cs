using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WoWCharacterTracker.Services.Discord;

// Makes a screenshot small enough for Discord's free upload limit: a JPEG, scaled down step by
// step until it fits. Used only when Discord turned the original down for its size.
static class ImageShrinker
{
    public static byte[]? ToJpegUnder(byte[] image, long limit)
    {
        try
        {
            var source = BitmapFrame.Create(new MemoryStream(image), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            foreach (var (scale, quality) in new[] { (1.0, 90), (1.0, 80), (0.75, 85), (0.5, 85), (0.35, 80) })
            {
                BitmapSource frame = scale < 1 ? new TransformedBitmap(source, new ScaleTransform(scale, scale)) : source;
                var jpeg = new JpegBitmapEncoder { QualityLevel = quality };
                jpeg.Frames.Add(BitmapFrame.Create(frame));
                using var ms = new MemoryStream();
                jpeg.Save(ms);
                if (ms.Length <= limit) return ms.ToArray();
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or ArgumentException or InvalidOperationException)
        {
        }
        return null;
    }
}
