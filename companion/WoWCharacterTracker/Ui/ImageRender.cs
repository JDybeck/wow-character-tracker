using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WoWCharacterTracker.Ui;

// Pictures drawn for Discord (the character card, the item tooltip) and the pictures they need first.
static class ImageRender
{
    static readonly HttpClient Http = Web.Client(TimeSpan.FromSeconds(10));

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

    // A saved file (a Forever portrait) or a web picture, decoded and frozen so any thread can use it.
    // Null when it can't be had.
    public static async Task<BitmapSource?> LoadAsync(string url)
    {
        try
        {
            var bytes = File.Exists(url) ? await File.ReadAllBytesAsync(url) : await Http.GetByteArrayAsync(url);
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = new MemoryStream(bytes);
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException or IOException)
        {
            return null;
        }
    }
}
