using WoWCharacterTracker.Services;

namespace WoWCharacterTracker.Tests;

// Installing the addon bundled in the exe, into a fake game folder.
public class AddonInstallerTests
{
    [Fact]
    public void Install_copies_pictures_as_bytes_and_still_sets_the_interface_in_the_toc()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wct-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Interface"));
        try
        {
            AddonInstaller.Install(dir, 99999);
            var target = AddonInstaller.TargetDir(dir);

            using var s = typeof(AddonInstaller).Assembly.GetManifestResourceStream("Addon/Icon.tga")!;
            using var bundled = new MemoryStream();
            s.CopyTo(bundled);
            Assert.Equal(bundled.ToArray(), File.ReadAllBytes(Path.Combine(target, "Icon.tga")));

            Assert.Equal("99999", AddonInstaller.InstalledInterface(dir));
            Assert.Equal(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, "Minimap.lua")).ReplaceLineEndings(),
                File.ReadAllText(Path.Combine(target, "Minimap.lua")).ReplaceLineEndings());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // An app update can change the addon without raising its version: the files tell, not the number.
    // The interface number and Settings.lua are the app's own changes and still count as the same.
    [Fact]
    public void An_installed_addon_matches_only_with_the_same_files()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wct-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Interface"));
        try
        {
            Assert.False(AddonInstaller.MatchesBundled(dir)); // not installed
            AddonInstaller.Install(dir, 16001);
            var target = AddonInstaller.TargetDir(dir);
            File.WriteAllText(Path.Combine(target, "Settings.lua"), "WoWCharacterTrackerSettings = { discord = true }");
            Assert.True(AddonInstaller.MatchesBundled(dir));

            File.AppendAllText(Path.Combine(target, "Recipes.lua"), "-- an older build's copy\n");
            Assert.False(AddonInstaller.MatchesBundled(dir));
            AddonInstaller.Install(dir, 16001);
            Assert.True(AddonInstaller.MatchesBundled(dir));

            File.Delete(Path.Combine(target, "Icon.tga"));
            Assert.False(AddonInstaller.MatchesBundled(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
