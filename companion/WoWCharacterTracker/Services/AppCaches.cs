namespace WoWCharacterTracker.Services;

// What the app downloaded and kept in its data folder so it doesn't ask again: Wowhead's item and
// spell info, Raider.IO renders, icons and tab logos. All of it comes back on its own when needed.
// Posted state, settings and day history aren't caches and stay.
static class AppCaches
{
    static IEnumerable<string> Paths => [Wowhead.CacheFile, RaiderIo.CacheFile, ImageCache.KeptDir];

    public static long Bytes => Paths.Sum(Files.Size);

    public static void Clear()
    {
        Wowhead.ClearCache();
        RaiderIo.ClearCache();
        try { if (Directory.Exists(ImageCache.KeptDir)) Directory.Delete(ImageCache.KeptDir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // a file in use stays
    }
}
