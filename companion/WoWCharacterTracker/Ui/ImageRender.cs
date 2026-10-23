using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WoWCharacterTracker.Ui;

// Pictures drawn for Discord (the character card, the item tooltip). The pictures they need come from ImageCache.LoadAsync.
static class ImageRender
{
    // Lays the element out at its own size and draws it as a PNG, scale times larger (2 stays sharp
    // when Discord scales it). Runs on the UI thread.
    public static byte[] ToPng(FrameworkElement element, Size available, double scale)
    {
        element.Measure(available);
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * scale), (int)Math.Ceiling(element.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(element);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        png.Save(ms);
        return ms.ToArray();
    }
}
