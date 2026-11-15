using System.Text;

namespace WoWCharacterTracker.Infrastructure;

// File chores that may fail without it mattering (the game or another program has the file open),
// and writing files so they're never left half written.
static class Files
{
    // Deletes the file if it's there; a file in use or not ours to delete is left alone.
    public static void TryDelete(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // Bytes in a file or a whole folder; 0 when it's not there or can't be read.
    public static long Size(string path)
    {
        try
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
            return Directory.Exists(path) ? new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    // Writes next to the file first and then moves it over the old one, so a crash or power cut
    // mid-write leaves the old file instead of a broken one (a broken config.json lost every webhook,
    // a broken posted-*.json stopped every sync). Throws like File.WriteAllBytes.
    public static void WriteAtomic(string path, byte[] bytes)
    {
        var temp = path + ".new";
        try
        {
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes);
                fs.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    // UTF-8 without a BOM, like File.WriteAllText.
    public static void WriteAtomic(string path, string text) => WriteAtomic(path, Encoding.UTF8.GetBytes(text));
}
