using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WoWCharacterTracker.Services.LiveChannel;

enum GridResult { NotAGrid, Damaged, Ok }

static class GridDecoder
{
    // 0xC8: 160 columns of 2 px, short keys (current). 0xC7: 128 columns of 5 px, long keys (older addon).
    const int ColsSmall = 160, ColsOld = 128;
    const int MagicSmall = 0xC8, MagicOld = 0xC7;

    // Finds the grid in the top-left corner of the picture and reads its bytes.
    public static GridResult Decode(byte[] image, out string? payload) => Decode(image, out payload, out _);

    // cellSize: one grid cell in the picture's pixels (2 on screen, less if the picture is scaled).
    public static GridResult Decode(byte[] image, out string? payload, out double cellSize)
    {
        payload = null;
        cellSize = 0;
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
        cellSize = cellW.Value;
        return GridResult.Ok;
    }

    // The addon's check-in shot also shows the character's portrait at a fixed spot right of the grid
    // and its label (PORTRAIT_X in LiveGrid.lua, in screen pixels; a cell is 2 of them). Returns it as
    // a PNG, or null when the spot is empty (an older addon, or the portrait didn't draw).
    const int PortraitX = 160 * 2 + 4 + 230 + 4, PortraitSize = 64;
    public static byte[]? CropPortrait(byte[] image, double cellSize)
    {
        try
        {
            using var ms = new MemoryStream(image);
            var frame = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var scale = cellSize / 2;
            var rect = new System.Windows.Int32Rect((int)Math.Round(PortraitX * scale), 0, (int)Math.Round(PortraitSize * scale), (int)Math.Round(PortraitSize * scale));
            if (rect.Width < 16 || rect.X + rect.Width > frame.PixelWidth || rect.Height > frame.PixelHeight) return null;
            var crop = new FormatConvertedBitmap(new CroppedBitmap(frame, rect), PixelFormats.Bgra32, null, 0);

            // Something drawn there has some variety; a blank spot is one flat color.
            var px = new byte[rect.Width * rect.Height * 4];
            crop.CopyPixels(px, rect.Width * 4, 0);
            var lums = Enumerable.Range(0, rect.Width * rect.Height).Select(i => (px[i * 4 + 2] * 299 + px[i * 4 + 1] * 587 + px[i * 4] * 114) / 1000).ToList();
            var mean = lums.Average();
            if (lums.Average(l => Math.Abs(l - mean)) < 6) return null;

            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(crop));
            using var outMs = new MemoryStream();
            png.Save(outMs);
            return outMs.ToArray();
        }
        catch (Exception) { return null; }
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
