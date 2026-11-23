using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WoWCharacterTracker.Services;

// The characters' pictures for the addon window (your characters and your friends'): the app can't
// send the game a picture, but an addon can show a texture file from its own folder. So the app
// keeps round 64x64 TGA files in the addon's Portraits folder and an index (Portraits.lua, by GUID)
// that the addon reads when it loads (ui.setPortrait in Ui.lua).
// The game only finds files that were there when it started: a file added while it runs stays
// blank until a restart, while a changed one loads on /reload. So the folder holds a fixed set of
// numbered slots, made once (blank when unused) and then only rewritten, and a character keeps its
// slot while it has a picture. New faces then only need a /reload.
static partial class AddonPortraits
{
    public const int Size = 64;      // a power of two, as the game wants
    public const int SlotCount = 128; // 16 KB each
    public const string IndexFile = "Portraits.lua";

    public static string Dir(string flavorDir) => Path.Combine(AddonInstaller.TargetDir(flavorDir), "Portraits");
    static string SlotPath(string flavorDir, int slot) => Path.Combine(Dir(flavorDir), $"{slot}.tga");
    static string IndexPath(string flavorDir) => Path.Combine(AddonInstaller.TargetDir(flavorDir), IndexFile);

    // Slots for the characters in order (the first ones when there are more than slots): one that had
    // a slot keeps it, the others take the free ones from the lowest.
    public static Dictionary<string, int> AssignSlots(IReadOnlyList<string> guids, IReadOnlyDictionary<string, int> previous, int slots = SlotCount)
    {
        var wanted = guids.Distinct(StringComparer.Ordinal).Take(slots).ToList();
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var guid in wanted)
            if (previous.TryGetValue(guid, out var s) && s >= 1 && s <= slots && !result.ContainsValue(s)) result[guid] = s;
        var free = Enumerable.Range(1, slots).Where(s => !result.ContainsValue(s)).GetEnumerator();
        foreach (var guid in wanted)
            if (!result.ContainsKey(guid) && free.MoveNext()) result[guid] = free.Current;
        return result;
    }

    public static string IndexLua(IReadOnlyDictionary<string, int> slots) =>
        "-- Written by the Character Tracker app: which picture in the Portraits folder is whose. Changes apply on /reload.\n"
        + "WoWCharacterTrackerPortraits = {\n"
        + string.Concat(slots.OrderBy(s => s.Value).Select(s => $"  [{AddonInstaller.LuaString(s.Key)}] = {s.Value},\n"))
        + "}\n";

    public static Dictionary<string, int> ReadIndex(string lua) =>
        IndexLine().Matches(lua).ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture), StringComparer.Ordinal);

    [GeneratedRegex(@"\[""(Player-[0-9A-Fa-f-]+)""\]\s*=\s*(\d+)")]
    private static partial Regex IndexLine();

    // Uncompressed 32-bit TGA from top-down BGRA pixels, stored bottom-up (the plainest kind, which
    // every client reads).
    public static byte[] Tga(byte[] bgra, int size)
    {
        var stride = size * 4;
        var tga = new byte[18 + bgra.Length];
        tga[2] = 2; // uncompressed true color
        tga[12] = (byte)size; tga[13] = (byte)(size >> 8);
        tga[14] = (byte)size; tga[15] = (byte)(size >> 8);
        tga[16] = 32;
        tga[17] = 8; // 8 alpha bits, rows from the bottom
        for (var y = 0; y < size; y++)
            Buffer.BlockCopy(bgra, y * stride, tga, 18 + (size - 1 - y) * stride, stride);
        return tga;
    }

    // Cuts the square into a circle like the game's portraits, with a soft one-pixel edge.
    public static void RoundOff(byte[] bgra, int size)
    {
        var r = size / 2.0;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = Math.Sqrt(Math.Pow(x + 0.5 - r, 2) + Math.Pow(y + 0.5 - r, 2));
                var cover = Math.Clamp(r - d, 0, 1);
                var i = (y * size + x) * 4 + 3;
                bgra[i] = (byte)Math.Round(bgra[i] * cover);
            }
    }

    public static byte[] Blank { get; } = Tga(new byte[Size * Size * 4], Size);

    // A picture as the addon's round TGA: the middle square, scaled. Frozen pictures (ImageCache's)
    // can be read on any thread.
    public static byte[] FromPicture(BitmapSource picture)
    {
        BitmapSource src = picture;
        var side = Math.Min(src.PixelWidth, src.PixelHeight);
        if (src.PixelWidth != src.PixelHeight)
            src = new CroppedBitmap(src, new Int32Rect((src.PixelWidth - side) / 2, (src.PixelHeight - side) / 2, side, side));
        if (side != Size)
            src = new TransformedBitmap(src, new ScaleTransform((double)Size / side, (double)Size / side));
        if (src.Format != PixelFormats.Bgra32) src = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[Size * Size * 4];
        src.CopyPixels(new Int32Rect(0, 0, Size, Size), pixels, Size * 4, 0);
        RoundOff(pixels, Size);
        return Tga(pixels, Size);
    }

    // Converted pictures by source (a file with its write time, or an address), so a sync that
    // changes nothing reads and converts nothing.
    static readonly ConcurrentDictionary<string, byte[]> Converted = new();

    public static async Task<byte[]?> ConvertAsync(string source)
    {
        var isFile = !source.StartsWith("http", StringComparison.OrdinalIgnoreCase);
        if (isFile && !File.Exists(source)) return null;
        var key = isFile ? $"{source}|{File.GetLastWriteTimeUtc(source).Ticks}" : source;
        if (Converted.TryGetValue(key, out var done)) return done;
        if (await ImageCache.LoadAsync(source).ConfigureAwait(false) is not { } picture) return null;
        try { return Converted[key] = FromPicture(picture); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException) { return null; }
    }

    // Writes the pictures (in order of who matters most) into the addon folder, if the addon is
    // installed: every slot file (blank when unused, only rewritten when it changes) and the index.
    // True when it had to make slot files, which a running game only finds after a restart.
    public static bool Write(string flavorDir, IReadOnlyList<(string Guid, byte[] Tga)> pictures)
    {
        if (!Directory.Exists(AddonInstaller.TargetDir(flavorDir))) return false;
        Directory.CreateDirectory(Dir(flavorDir));
        var indexPath = IndexPath(flavorDir);
        var oldIndex = File.Exists(indexPath) ? File.ReadAllText(indexPath) : "";
        var slots = AssignSlots([.. pictures.Select(p => p.Guid)], ReadIndex(oldIndex));
        var made = false;
        var bySlot = new Dictionary<int, byte[]>();
        foreach (var (guid, tga) in pictures)
            if (slots.TryGetValue(guid, out var s)) bySlot[s] = tga;
        for (var s = 1; s <= SlotCount; s++)
        {
            var bytes = bySlot.TryGetValue(s, out var tga) ? tga : Blank;
            var path = SlotPath(flavorDir, s);
            var exists = File.Exists(path);
            made |= !exists;
            if (!exists || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) Files.WriteAtomic(path, bytes);
        }
        var index = IndexLua(slots);
        if (oldIndex != index) Files.WriteAtomic(indexPath, index);
        return made;
    }
}
