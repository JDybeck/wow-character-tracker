using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WoWCharacterTracker;

// Reads the addon's live events from the game's Screenshots folder. For each event the addon
// draws a black and white grid in the top-left corner and takes a screenshot (see the addon's
// "Live channel"). This finds those pictures, reads the grid, and deletes them.
sealed class ScreenshotChannel(string flavorDir) : IDisposable
{
    readonly string dir = Path.Combine(flavorDir, "Screenshots");
    readonly ConcurrentDictionary<string, bool> seen = new(StringComparer.OrdinalIgnoreCase);
    readonly object pollLock = new();
    Timer? timer;

    public event Action<Entry>? EntryReceived;
    public event Action<string>? Unreadable; // looked like a data grid but didn't check out

    public void Start()
    {
        // Anything already there is old.
        if (Directory.Exists(dir))
            foreach (var f in Directory.EnumerateFiles(dir)) seen[f] = true;
        timer = new Timer(_ => Poll(), null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
    }

    void Poll()
    {
        if (!Monitor.TryEnter(pollLock)) return;
        try
        {
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.EnumerateFiles(dir, "*.jp*g").Concat(Directory.EnumerateFiles(dir, "*.png")))
            {
                if (seen.ContainsKey(file)) continue;
                // Wait until the game has finished writing it.
                if (!TryRead(file, out var bytes)) continue;
                seen[file] = true;

                switch (GridDecoder.Decode(bytes, out var payload))
                {
                    case GridResult.Ok:
                        TryDelete(file);
                        if (Payload.Parse(payload!) is { } entry) EntryReceived?.Invoke(entry);
                        break;
                    case GridResult.Damaged:
                        Unreadable?.Invoke(file);
                        break;
                }
            }
        }
        catch (Exception ex) { Log.Warn("Couldn't check the Screenshots folder: " + ex.Message); }
        finally { Monitor.Exit(pollLock); }
    }

    static bool TryRead(string file, out byte[] bytes)
    {
        bytes = [];
        try
        {
            var info = new FileInfo(file);
            if (DateTime.Now - info.LastWriteTime < TimeSpan.FromMilliseconds(300)) return false; // still being written
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            bytes = new byte[fs.Length];
            fs.ReadExactly(bytes);
            return bytes.Length > 0;
        }
        catch (IOException) { return false; }
    }

    static void TryDelete(string file)
    {
        try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public void Dispose() => timer?.Dispose();
}

enum GridResult { NotAGrid, Damaged, Ok }

static class GridDecoder
{
    // 0xC8: 160 columns of 2 px, short keys (current). 0xC7: 128 columns of 5 px, long keys (older addon).
    const int ColsSmall = 160, ColsOld = 128;
    const int MagicSmall = 0xC8, MagicOld = 0xC7;

    // Finds the grid in the top-left corner of the picture and reads its bytes.
    public static GridResult Decode(byte[] image, out string? payload)
    {
        payload = null;
        BitmapSource frame;
        try
        {
            using var ms = new MemoryStream(image);
            frame = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        }
        catch (Exception) { return GridResult.NotAGrid; }

        var w = Math.Min(frame.PixelWidth, 2000);
        var h = Math.Min(frame.PixelHeight, 1200);
        if (w < 64 || h < 16) return GridResult.NotAGrid;
        var bgra = new FormatConvertedBitmap(new CroppedBitmap(frame, new System.Windows.Int32Rect(0, 0, w, h)), PixelFormats.Bgra32, null, 0);
        var px = new byte[w * h * 4];
        bgra.CopyPixels(px, w * 4, 0);

        int Lum(int x, int y)
        {
            var i = (y * w + x) * 4;
            return (px[i + 2] * 299 + px[i + 1] * 587 + px[i] * 114) / 1000;
        }

        // Cell size from the clock row (white, black, white...) and the clock column.
        // The grid is wide but only a few cells tall, so the column clock is short; cells are square,
        // so the row's size stands in when the column has too few cells to measure.
        var cellW = ClockPeriod(i => Lum(i, 1), w, minRuns: 16);
        if (cellW is null) return GridResult.NotAGrid;
        var cellH = ClockPeriod(i => Lum(1, i), h, minRuns: 5) ?? cellW;

        // A cell is black when its middle is dark: the middle 3x3 pixels for big cells (JPEG smudges
        // edges), the center pixel for small ones (PNG, sharp).
        var small = cellW.Value < 4 || cellH.Value < 4;
        int Bit(int r, int c)
        {
            var cx = (int)((c + 0.5) * cellW.Value);
            var cy = (int)((r + 0.5) * cellH.Value);
            if (cx < 1 || cy < 1 || cx >= w - 1 || cy >= h - 1) return -1;
            if (small) return Lum(cx, cy) < 128 ? 1 : 0;
            var sum = 0;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++) sum += Lum(cx + dx, cy + dy);
            return sum / 9 < 128 ? 1 : 0;
        }

        // Bits run left to right, top to bottom, through the cells right of the clock column.
        // The magic byte sits in the first 8 cells either way, and says how wide the grid is.
        var dataCols = 8;
        int ReadByte(int index)
        {
            var b = 0;
            for (var k = 0; k < 8; k++)
            {
                var bit = index * 8 + k;
                var v = Bit(bit / dataCols + 1, bit % dataCols + 1);
                if (v < 0) return -1;
                b = b * 2 + v;
            }
            return b;
        }

        var magic = ReadByte(0);
        if (magic == MagicSmall) dataCols = ColsSmall - 1;
        else if (magic == MagicOld) dataCols = ColsOld - 1;
        else return GridResult.NotAGrid;
        int hi = ReadByte(1), lo = ReadByte(2);
        if (hi < 0 || lo < 0) return GridResult.Damaged;
        var length = hi * 256 + lo;
        if (length is 0 or > 4000) return GridResult.Damaged;

        var data = new byte[length];
        var sum = 0;
        for (var i = 0; i < length; i++)
        {
            var b = ReadByte(3 + i);
            if (b < 0) return GridResult.Damaged;
            data[i] = (byte)b;
            sum = (sum + b) % 65536;
        }
        int ch = ReadByte(3 + length), cl = ReadByte(4 + length);
        if (ch < 0 || cl < 0 || ch * 256 + cl != sum) return GridResult.Damaged;

        payload = Encoding.UTF8.GetString(data);
        return GridResult.Ok;
    }

    // Measures the alternating pattern along a line from the top-left corner. Returns the
    // average cell size in pixels, or null when the line doesn't look like a clock.
    static double? ClockPeriod(Func<int, int> lum, int length, int minRuns)
    {
        var runs = new List<int>();
        var dark = lum(0) < 128;
        if (dark) return null; // the clock starts with a white cell
        var run = 0;
        for (var i = 0; i < length && runs.Count < 24; i++)
        {
            var d = lum(i) < 128;
            if (d == dark) { run++; continue; }
            runs.Add(run);
            dark = d;
            run = 1;
        }
        if (runs.Count < minRuns) return null;

        // Check the runs are even (leaving out the last one, which may run into the scene).
        var counted = runs.Take(runs.Count - 1).Take(15).ToList();
        var avg = counted.Average();
        if (avg < 2 || avg > 60) return null;
        if (counted.Any(r => Math.Abs(r - avg) > avg * 0.45 + 1)) return null;
        return avg;
    }
}

// The addon's "key=value;key=value" format with %XX escapes. Newer addons use one-letter keys,
// leave the GUID off the front of the id, and write sex as f/m.
static class Payload
{
    // Same table as SHORT in the addon.
    static readonly Dictionary<string, string> ShortKeys = new()
    {
        ["i"] = "id", ["k"] = "kind", ["g"] = "guid", ["n"] = "char", ["r"] = "realm", ["l"] = "level", ["a"] = "at",
        ["z"] = "zone", ["p"] = "professions", ["t"] = "talents", ["s"] = "spec", ["o"] = "role", ["v"] = "ilvl",
        ["c"] = "class", ["C"] = "classFile", ["e"] = "race", ["E"] = "raceFile", ["x"] = "sex", ["h"] = "hardcore",
        ["u"] = "ruleset", ["d"] = "seconds", ["y"] = "played", ["b"] = "boss", ["I"] = "instance", ["m"] = "item",
        ["M"] = "itemId", ["q"] = "quality", ["G"] = "gold", ["K"] = "killer", ["w"] = "mount", ["B"] = "bought",
        ["S"] = "shot", ["P"] = "group", ["Z"] = "groupSize", ["N"] = "money", ["R"] = "crests", ["X"] = "maxLevel", ["U"] = "guild", ["L"] = "surname", ["O"] = "opponent", ["F"] = "fled", ["T"] = "rating", ["H"] = "step", ["D"] = "bracket",
    };

    public static Entry? Parse(string data)
    {
        var values = new Dictionary<string, string>();
        foreach (var pair in data.Split(';'))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            var key = pair[..eq];
            values[ShortKeys.GetValueOrDefault(key, key)] = Unescape(pair[(eq + 1)..]);
        }
        if (values.Count == 0) return null;
        if (values.TryGetValue("sex", out var sex) && sex.Length == 1) values["sex"] = sex == "f" ? "female" : "male";
        if (values.TryGetValue("id", out var id) && id.StartsWith(':') && values.TryGetValue("guid", out var guid))
            values["id"] = guid + id;

        var entry = Entry.From(
            k => values.GetValueOrDefault(k, ""),
            k => double.TryParse(values.GetValueOrDefault(k), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0);
        return entry.Id.Length > 0 ? entry : null;
    }

    static string Unescape(string s) =>
        s.Contains('%') ? Regex.Replace(s, "%([0-9A-Fa-f]{2})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString()) : s;
}
