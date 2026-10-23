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

    [Fact]
    public void GameProcesses_sees_running_processes_by_name()
    {
        using var me = System.Diagnostics.Process.GetCurrentProcess();
        Assert.True(GameProcesses.IsRunning(me.ProcessName));
        Assert.True(GameProcesses.IsRunning(me.ProcessName.ToUpperInvariant()));
        Assert.False(GameProcesses.IsRunning("NoSuchProcess-" + Guid.NewGuid()));
    }
}
