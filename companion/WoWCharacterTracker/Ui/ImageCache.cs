using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WoWCharacterTracker.Ui;

// Every picture the app shows or draws: Wowhead icons, Raider.IO renders, class icons, tab logos and
// portrait files, each decoded once and shared. Binding a URL straight to Image.Source makes WPF
// download and decode it again for every Image, which is hundreds of icons each time a page is built
// (a tab switch, opening a character's gear).
// Icons from Wowhead's CDN and the tab logos never change at their address, so they're also kept on
// disk (<data dir>\images) and the next start doesn't download them again. Raider.IO renders do
// change (same address, new gear), so those stay in memory only.
static class ImageCache
{
    static readonly Dictionary<string, ImageSource> Shown = []; // Get's, UI thread only
    static readonly ConcurrentDictionary<string, Task<BitmapSource?>> Loads = new(); // LoadAsync's
    static readonly string[] KeptHosts = ["wow.zamimg.com", "warcraft.wiki.gg"];
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    // For bindings (WebImage): the picture now, or one that fills in when its download finishes.
    public static ImageSource? Get(string? source)
    {
        if (string.IsNullOrEmpty(source)) return null;
        // A portrait file is rewritten at each check-in, so its write time is part of the key.
        var isFile = !source.StartsWith("http", StringComparison.OrdinalIgnoreCase);
        if (isFile && !File.Exists(source)) return null;
        var key = isFile ? $"{source}|{File.GetLastWriteTimeUtc(source).Ticks}" : source;
        if (Shown.TryGetValue(key, out var cached)) return cached;
        if (!isFile && Loads.TryGetValue(source, out var loaded) && loaded.IsCompletedSuccessfully && loaded.Result is { } done)
            return Shown[key] = done;
        // A replaced portrait: the older picture of the same file goes.
        if (isFile) foreach (var old in Shown.Keys.Where(k => k.StartsWith(source + "|", StringComparison.OrdinalIgnoreCase)).ToList()) Shown.Remove(old);
        try
        {
            // Usually already loaded off the UI thread by LoadAsync (ItemInfo does that before it hands
            // out its icon); this is for the few pictures bound straight from a URL.
            var kept = isFile ? null : KeptFile(source);
            if (kept is not null && File.Exists(kept))
            {
                try { return Shown[key] = Decode(File.ReadAllBytes(kept)); }
                catch (Exception ex) when (IsBadPicture(ex)) { Files.TryDelete(kept); } // fetched again below
            }

            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri(source, UriKind.Absolute);
            img.CacheOption = BitmapCacheOption.OnLoad; // files aren't kept open
            img.DownloadFailed += (_, _) => Shown.Remove(key); // try again next time it's shown
            if (kept is not null) img.DownloadCompleted += (_, _) => Keep(kept, img);
            img.EndInit();
            if (!img.IsDownloading && img.CanFreeze) img.Freeze();
            Shown[key] = img;
            return img;
        }
        catch (Exception ex) when (ex is UriFormatException or IOException or UnauthorizedAccessException || IsBadPicture(ex))
        {
            return null;
        }
    }

    // Data that isn't a picture WPF can read (an error page, a cut-off file): NotSupportedException for
    // an unknown format, FileFormatException (a FormatException) for a broken one.
    static bool IsBadPicture(Exception ex) => ex is NotSupportedException or FormatException or ArgumentException;

    // For drawing (the character card, the item picture) and the tab logos: decoded and frozen, so
    // any thread can use it. Null when it can't be had (offline, gone); a later call tries again.
    public static async Task<BitmapSource?> LoadAsync(string source)
    {
        if (File.Exists(source)) return await Fetch(source).ConfigureAwait(false); // portraits change, so files aren't kept
        var img = await Loads.GetOrAdd(source, Fetch).ConfigureAwait(false);
        if (img is null) Loads.TryRemove(source, out _);
        return img;
    }

    // Reading, downloading and decoding all happen off the UI thread (ConfigureAwait). Only a picture
    // that decodes is kept on disk, and a kept one that doesn't (damaged) is dropped and fetched again.
    static async Task<BitmapSource?> Fetch(string source)
    {
        try
        {
            if (File.Exists(source)) return Decode(await File.ReadAllBytesAsync(source).ConfigureAwait(false));
            var kept = KeptFile(source);
            if (kept is not null && File.Exists(kept))
            {
                try { return Decode(await File.ReadAllBytesAsync(kept).ConfigureAwait(false)); }
                catch (Exception ex) when (IsBadPicture(ex)) { Files.TryDelete(kept); }
            }
            var bytes = await Web.GetBytesAsync(source, Timeout).ConfigureAwait(false);
            var img = Decode(bytes);
            if (kept is not null) Save(kept, bytes);
            return img;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                       or UnauthorizedAccessException or UriFormatException || IsBadPicture(ex))
        {
            return null;
        }
    }

    // Where a picture that never changes at its address is kept, or null for the others.
    static string? KeptFile(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !KeptHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)) return null;
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..24].ToLowerInvariant();
        return Path.Combine(Config.Dir, "images", name);
    }

    // A picture WPF downloaded for a binding, kept as PNG (WPF doesn't hand out the downloaded bytes).
    static void Keep(string file, BitmapSource img)
    {
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(img));
        using var ms = new MemoryStream();
        png.Save(ms);
        Save(file, ms.ToArray());
    }

    // Never makes the data folder itself: after Uninstall removed it, a download that finishes in the
    // moment before the app quits mustn't bring it back.
    static void Save(string file, byte[] bytes)
    {
        if (!Directory.Exists(Config.Dir)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            Files.WriteAtomic(file, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // only a cache
    }

    static BitmapSource Decode(byte[] bytes)
    {
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = new MemoryStream(bytes);
        img.EndInit();
        img.Freeze();
        return img;
    }
}

// Image.Source="{Binding SomeUrl, Converter={StaticResource WebImage}}"
sealed class WebImage : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) => ImageCache.Get(value as string);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
