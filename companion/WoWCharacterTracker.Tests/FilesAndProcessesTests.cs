using WoWCharacterTracker.Infrastructure;

namespace WoWCharacterTracker.Tests;

// Files.WriteAtomic and GameProcesses, the shared helpers for saving files and finding the game.
public class FilesAndProcessesTests
{
    [Fact]
    public void WriteAtomic_replaces_the_file_and_leaves_no_temp_file()
    {
        var dir = Directory.CreateTempSubdirectory("wct-files-");
        try
        {
            var path = Path.Combine(dir.FullName, "config.json");
            File.WriteAllText(path, "old and longer than the new text");
            Files.WriteAtomic(path, "new");
            Assert.Equal("new", File.ReadAllText(path));
            Assert.False(File.Exists(path + ".new"));
            Assert.Equal([(byte)'n', (byte)'e', (byte)'w'], File.ReadAllBytes(path)); // no BOM, like File.WriteAllText
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void WriteAtomic_keeps_the_old_file_when_it_cannot_write()
    {
        var dir = Directory.CreateTempSubdirectory("wct-files-");
        try
        {
            var path = Path.Combine(dir.FullName, "posted.json");
            File.WriteAllText(path, "old");
            using (new FileStream(path + ".new", FileMode.Create, FileAccess.Write, FileShare.None)) // the temp file is busy
                Assert.ThrowsAny<IOException>(() => Files.WriteAtomic(path, "new"));
            Assert.Equal("old", File.ReadAllText(path));
        }
        finally { dir.Delete(recursive: true); }
    }

    // A config.json that can't be read is kept as config.json.broken (it has the webhooks in it), not
    // overwritten by the defaults the app starts with.
    [Fact]
    public void An_unreadable_config_is_kept_aside()
    {
        var file = Path.Combine(Config.Dir, "config.json");
        Directory.CreateDirectory(Config.Dir);
        var had = File.Exists(file) ? File.ReadAllText(file) : null;
        try
        {
            File.WriteAllText(file, """{ "Webhooks": [ { "Name": "Guild", } """); // a hand edit gone wrong
            var config = Config.Load();
            Assert.Empty(config.Webhooks);
            Assert.Contains("\"Guild\"", File.ReadAllText(file + ".broken"));
        }
        finally
        {
            Files.TryDelete(file + ".broken");
            if (had is null) Files.TryDelete(file); else File.WriteAllText(file, had);
        }
    }

    // Data that isn't a whole picture (an error page, a cut-off download) gives no picture, never an error.
    [Fact]
    public void ImageCache_gives_null_for_broken_pictures()
    {
        var dir = Directory.CreateTempSubdirectory("wct-images-");
        try
        {
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
            png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(
                System.Windows.Media.Imaging.BitmapSource.Create(8, 8, 96, 96, System.Windows.Media.PixelFormats.Gray8, null, new byte[64], 8)));
            using var ms = new MemoryStream();
            png.Save(ms);
            var whole = Path.Combine(dir.FullName, "whole.png");
            var cut = Path.Combine(dir.FullName, "cut.png");
            var html = Path.Combine(dir.FullName, "page.png");
            File.WriteAllBytes(whole, ms.ToArray());
            File.WriteAllBytes(cut, ms.ToArray()[..(int)(ms.Length / 2)]);
            File.WriteAllText(html, "<html>Just a moment...</html>");

            Assert.NotNull(WoWCharacterTracker.Ui.ImageCache.LoadAsync(whole).GetAwaiter().GetResult());
            Assert.Null(WoWCharacterTracker.Ui.ImageCache.LoadAsync(cut).GetAwaiter().GetResult());
            Assert.Null(WoWCharacterTracker.Ui.ImageCache.LoadAsync(html).GetAwaiter().GetResult());
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void GameProcesses_sees_running_processes_by_name()
    {
        using var me = System.Diagnostics.Process.GetCurrentProcess();
        Assert.True(GameProcesses.IsRunning(me.ProcessName));
        Assert.True(GameProcesses.IsRunning(me.ProcessName.ToUpperInvariant()));
        Assert.False(GameProcesses.IsRunning("NoSuchProcess-" + Guid.NewGuid()));
    }
}
