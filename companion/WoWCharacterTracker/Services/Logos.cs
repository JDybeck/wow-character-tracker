using System.Windows.Media.Imaging;

namespace WoWCharacterTracker.Services;

// The game logos on the tabs (current retail expansion, Forever). They're Blizzard's, so they're
// not in the repo or the exe: downloaded from Warcraft Wiki the first time and kept next to the
// config. Without a connection the tab just shows its name.
static class Logos
{
    static readonly HttpClient Http = Web.Client(TimeSpan.FromSeconds(15));

    public static async Task<BitmapSource?> LoadAsync(Flavor flavor)
    {
        if (flavor.LogoUrl.Length == 0) return null;
        var dir = Path.Combine(Config.Dir, "logos");
        // The file name follows the URL, so a new logo (next expansion) is downloaded again.
        var file = Path.Combine(dir, $"{flavor.Key}-{Path.GetFileName(new Uri(flavor.LogoUrl).AbsolutePath)}");
        try
        {
            if (!File.Exists(file))
            {
                var bytes = await Http.GetByteArrayAsync(flavor.LogoUrl);
                Directory.CreateDirectory(dir);
                Files.WriteAtomic(file, bytes); // a cut-off download would otherwise stay broken (the file counts as there)
            }
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.UriSource = new Uri(file);
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
