namespace WoWCharacterTracker.Tests;

// The characters' pictures for the addon window: slot files in the addon's Portraits folder and
// the Portraits.lua index (AddonPortraits).
public class AddonPortraitsTests
{
    const string A = "Player-1-0000000A", B = "Player-1-0000000B", C = "Player-1-0000000C";

    // A character keeps its slot (the game may only see a changed file on /reload), new ones take
    // the lowest free slots, and there are never more than the slots.
    [Fact]
    public void Characters_keep_their_slots_and_new_ones_take_free_ones()
    {
        var first = AddonPortraits.AssignSlots([A, B], new Dictionary<string, int>(), 3);
        Assert.Equal(1, first[A]);
        Assert.Equal(2, first[B]);

        var next = AddonPortraits.AssignSlots([C, B], first, 3);
        Assert.Equal(2, next[B]);
        Assert.Equal(1, next[C]); // A's slot was free again
        Assert.False(next.ContainsKey(A));

        var full = AddonPortraits.AssignSlots([A, B, C, "Player-1-0000000D"], new Dictionary<string, int>(), 3);
        Assert.Equal(3, full.Count);
        Assert.False(full.ContainsKey("Player-1-0000000D"));
    }

    [Fact]
    public void The_index_reads_back_what_was_written()
    {
        var slots = new Dictionary<string, int> { [A] = 3, [B] = 1 };
        var lua = AddonPortraits.IndexLua(slots);
        Assert.Contains("WoWCharacterTrackerPortraits = {", lua);
        Assert.Equal(slots, AddonPortraits.ReadIndex(lua));
        Assert.Empty(AddonPortraits.ReadIndex(File.ReadAllText(Path.Combine(AddonLuaTests.AddonDir, "Portraits.lua"))));
    }

    // Plain 32-bit TGA, rows from the bottom; round, so the corners are see-through and the middle isn't.
    [Fact]
    public void A_portrait_is_a_round_tga()
    {
        const int size = 4;
        var pixels = new byte[size * size * 4];
        for (var i = 0; i < pixels.Length; i += 4) (pixels[i], pixels[i + 3]) = ((byte)(i / 16), 255); // blue = row
        AddonPortraits.RoundOff(pixels, size);
        Assert.Equal(0, pixels[3]); // top left corner
        Assert.Equal(255, pixels[(1 * size + 1) * 4 + 3]); // the middle

        var tga = AddonPortraits.Tga(pixels, size);
        Assert.Equal(18 + size * size * 4, tga.Length);
        Assert.Equal(2, tga[2]);
        Assert.Equal(size, tga[12]);
        Assert.Equal(size, tga[14]);
        Assert.Equal(32, tga[16]);
        Assert.Equal(8, tga[17]);
        Assert.Equal(3, tga[18]); // the first stored row is the bottom one
        Assert.Equal(0, tga[18 + 3 * size * 4]);
        Assert.Equal(AddonPortraits.Size * AddonPortraits.Size * 4 + 18, AddonPortraits.Blank.Length);
    }

    // Every slot is there from the first write (a running game only finds files it saw at start),
    // unused ones blank, and a reinstall of the addon keeps the index.
    [Fact]
    public void Writing_fills_every_slot_and_survives_an_addon_update()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wct-portraits-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Interface"));
        try
        {
            Assert.False(AddonPortraits.Write(dir, [(A, [1, 2, 3])])); // no addon: nothing
            AddonInstaller.Install(dir, 16001);
            var face = AddonPortraits.Tga(new byte[16 * 4], 4);
            Assert.True(AddonPortraits.Write(dir, [(A, face)]));
            var folder = AddonPortraits.Dir(dir);
            Assert.Equal(AddonPortraits.SlotCount, Directory.GetFiles(folder, "*.tga").Length);
            Assert.Equal(face, File.ReadAllBytes(Path.Combine(folder, "1.tga")));
            Assert.Equal(AddonPortraits.Blank, File.ReadAllBytes(Path.Combine(folder, "2.tga")));

            Assert.False(AddonPortraits.Write(dir, [(B, face), (A, face)])); // all there already
            var index = File.ReadAllText(Path.Combine(AddonInstaller.TargetDir(dir), AddonPortraits.IndexFile));
            Assert.Equal(new Dictionary<string, int> { [A] = 1, [B] = 2 }, AddonPortraits.ReadIndex(index));

            Assert.True(AddonInstaller.MatchesBundled(dir)); // the app's index isn't a changed addon file
            AddonInstaller.Install(dir, 16001);
            Assert.Equal(index, File.ReadAllText(Path.Combine(AddonInstaller.TargetDir(dir), AddonPortraits.IndexFile)));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
