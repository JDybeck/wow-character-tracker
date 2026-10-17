using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WoWCharacterTracker.Ui;

// Pictures from the web (Wowhead icons, Raider.IO renders, class icons) and portrait files, each
// decoded once and shared by every Image showing it. Binding a URL straight to Image.Source makes
// WPF download and decode it again for every Image, which is hundreds of icons each time a page
// is built (a tab switch, opening a character's gear).
static class ImageCache
{
    static readonly Dictionary<string, ImageSource> Cache = [];

    public static ImageSource? Get(string? source)
    {
        if (string.IsNullOrEmpty(source)) return null;
        // A portrait file is rewritten at each check-in, so its write time is part of the key.
        var isFile = !source.StartsWith("http", StringComparison.OrdinalIgnoreCase);
        if (isFile && !File.Exists(source)) return null;
        var key = isFile ? $"{source}|{File.GetLastWriteTimeUtc(source).Ticks}" : source;
        if (Cache.TryGetValue(key, out var cached)) return cached;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri(source, UriKind.Absolute);
            img.CacheOption = BitmapCacheOption.OnLoad; // files aren't kept open
            img.DownloadFailed += (_, _) => Cache.Remove(key); // try again next time it's shown
            img.EndInit();
            if (!img.IsDownloading && img.CanFreeze) img.Freeze();
            Cache[key] = img;
            return img;
        }
        catch (Exception ex) when (ex is UriFormatException or IOException or NotSupportedException)
        {
            return null;
        }
    }
}

// Image.Source="{Binding SomeUrl, Converter={StaticResource WebImage}}"
sealed class WebImage : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) => ImageCache.Get(value as string);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
