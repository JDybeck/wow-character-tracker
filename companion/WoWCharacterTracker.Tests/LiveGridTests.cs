using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WoWCharacterTracker.Services.LiveChannel;
using Xunit.Abstractions;

namespace WoWCharacterTracker.Tests;

// The live channel: grids drawn the way the addon draws them (LiveGrid.lua), in every corner, as the
// JPEG the game now writes, read back by the app; and how fast a new shot is picked up.
public class LiveGridTests(ITestOutputHelper output)
{
    const int Cols = 160, Cell = 2;

    // The addon's toBits: magic 0xC8, 2-byte length, payload, 2-byte sum; then the cells (black = 1)
    // with the clock row and column, mirrored for the right and bottom corners.
    static byte[] Shot(string payload, string corner, int quality = 90, int width = 1280, int height = 720)
    {
        var data = Encoding.UTF8.GetBytes(payload);
        var bytes = new List<int> { 0xC8, data.Length / 256, data.Length % 256 };
        var sum = 0;
        foreach (var b in data) { bytes.Add(b); sum = (sum + b) % 65536; }
        bytes.Add(sum / 256); bytes.Add(sum % 256);
        var bits = bytes.SelectMany(b => Enumerable.Range(0, 8).Select(i => (b >> (7 - i)) & 1)).ToList();
        var dataCols = Cols - 1;
        var rows = (int)Math.Ceiling(bits.Count / (double)dataCols) + 1;
        bool right = corner.Contains("RIGHT"), bottom = corner.Contains("BOTTOM");

        var rnd = new Random(3);
        var px = new byte[width * height * 4];
        for (var i = 0; i < px.Length; i += 4) { px[i] = (byte)rnd.Next(256); px[i + 1] = (byte)rnd.Next(256); px[i + 2] = (byte)rnd.Next(256); px[i + 3] = 255; }
        int gx0 = right ? width - Cols * Cell : 0, gy0 = bottom ? height - rows * Cell : 0;
        for (var r = 0; r < rows; r++)
        for (var c = 0; c < Cols; c++)
        {
            var black = r == 0 ? c % 2 : c == 0 ? r % 2 : (r - 1) * dataCols + (c - 1) < bits.Count ? bits[(r - 1) * dataCols + (c - 1)] : 0;
            var x = gx0 + (right ? Cols - 1 - c : c) * Cell;
            var y = gy0 + (bottom ? rows - 1 - r : r) * Cell;
            for (var dy = 0; dy < Cell; dy++)
            for (var dx = 0; dx < Cell; dx++)
            {
                var i = ((y + dy) * width + x + dx) * 4;
                px[i] = px[i + 1] = px[i + 2] = (byte)(black == 1 ? 0 : 255);
            }
        }
        var src = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, px, width * 4);
        var enc = new JpegBitmapEncoder { QualityLevel = quality };
        enc.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    const string Level = "a=1792101300;C=PALADIN;g=4619-00B6511D;i=:22;k=level;l=22;n=Ronnie;d=1391;z=Redridge Mountains";

    [Theory]
    [InlineData("TOPLEFT")]
    [InlineData("TOPRIGHT")]
    [InlineData("BOTTOMLEFT")]
    [InlineData("BOTTOMRIGHT")]
    public void Every_corner_reads_back_from_a_jpeg(string corner)
    {
        // The preferred corner deliberately differs: a corner changed in the app but not yet /reloaded in game.
        var result = GridDecoder.Decode(Shot(Level, corner), out var payload, out var cell, out var found, preferred: "TOPLEFT");
        Assert.Equal(GridResult.Ok, result);
        Assert.Equal(corner, found);
        Assert.Equal(Level, payload);
        Assert.Equal(2, cell, 0.5);
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)] // its noise once faked a clock column and threw the cell height off
    [InlineData(3440, 1440)]
    [InlineData(3840, 2160)]
    public void Common_resolutions_read(int width, int height)
    {
        foreach (var corner in GridDecoder.Corners)
            Assert.Equal(GridResult.Ok, GridDecoder.Decode(Shot(Level, corner, 90, width, height), out _, out _, out _, corner));
    }

    [Fact]
    public void Jpeg_quality_60_still_reads()
    {
        Assert.Equal(GridResult.Ok, GridDecoder.Decode(Shot(Level, "TOPLEFT", quality: 60), out _));
    }

    [Fact]
    public void The_guid_gets_its_Player_prefix_back()
    {
        var e = Payload.Parse(Level)!;
        Assert.Equal("Player-4619-00B6511D", e.Guid);
        Assert.Equal("Player-4619-00B6511D:22", e.Id);
        // Older addons still send the whole GUID.
        Assert.Equal("Player-1-ABC", Payload.Parse("g=Player-1-ABC;i=:2;k=level;l=2")!.Guid);
    }

    [Fact]
    public void A_normal_screenshot_isnt_a_grid()
    {
        var rnd = new Random(5);
        var px = new byte[640 * 360 * 4];
        rnd.NextBytes(px);
        var src = BitmapSource.Create(640, 360, 96, 96, PixelFormats.Bgra32, null, px, 640 * 4);
        var enc = new JpegBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream(); enc.Save(ms);
        Assert.Equal(GridResult.NotAGrid, GridDecoder.Decode(ms.ToArray(), out _));
    }

    [Fact]
    public async Task A_new_shot_is_picked_up_quickly()
    {
        var game = Path.Combine(Path.GetTempPath(), "wct-channel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(game, "Screenshots"));
        try
        {
            using var channel = new ScreenshotChannel(game, () => "TOPLEFT");
            var got = new TaskCompletionSource<Entry>();
            channel.EntryReceived += (e, _, _) => got.TrySetResult(e);
            channel.Start();
            await Task.Delay(700); // past the start-up scan

            // The "game" writes the shot in pieces over ~150 ms, holding the file the whole time.
            var shot = Shot(Level, "TOPLEFT", width: 2560, height: 1440);
            var file = Path.Combine(game, "Screenshots", "WoWScrnShot_101826_120000.jpg");
            using (var fs = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                for (var i = 0; i < shot.Length; i += shot.Length / 5 + 1)
                {
                    fs.Write(shot, i, Math.Min(shot.Length / 5 + 1, shot.Length - i));
                    fs.Flush();
                    await Task.Delay(30);
                }
            var written = Stopwatch.StartNew();
            var entry = await got.Task.WaitAsync(TimeSpan.FromSeconds(5));
            output.WriteLine($"Picked up {written.ElapsedMilliseconds} ms after the game finished writing ({shot.Length / 1024} KB).");
            Assert.Equal("level", entry.Kind);
            Assert.True(written.ElapsedMilliseconds < 600, $"took {written.ElapsedMilliseconds} ms");
            Assert.False(File.Exists(file)); // read and deleted
        }
        finally { Directory.Delete(game, recursive: true); }
    }
}
