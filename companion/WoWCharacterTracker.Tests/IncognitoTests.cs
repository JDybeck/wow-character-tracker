using MoonSharp.Interpreter;

namespace WoWCharacterTracker.Tests;

// Incognito stretches from the saved file (Shared.lua): the app never posts what happened in one.
public class IncognitoTests
{
    [Fact]
    public void Stretches_are_read_and_cover_what_happened_inside_them()
    {
        var windows = IncognitoWindow.Read(new Script().DoString("""
            return { { from = 1000, ['until'] = 2000, friends = true }, { from = 5000, ['until'] = 4000 }, 'odd', { from = 3000, ['until'] = 3600 } }
            """));
        Assert.Equal([new IncognitoWindow(1000, 2000, true), new IncognitoWindow(3000, 3600, false)], windows);
        Assert.True(IncognitoWindow.Any(windows, 1500));
        Assert.True(IncognitoWindow.Any(windows, 3600));
        Assert.False(IncognitoWindow.Any(windows, 2500));
        Assert.Empty(IncognitoWindow.Read(DynValue.Nil));
    }
}
