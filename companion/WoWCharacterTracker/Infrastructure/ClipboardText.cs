using System.Runtime.InteropServices;
using System.Windows;

namespace WoWCharacterTracker.Infrastructure;

// The clipboard as text. Another program (a clipboard manager, Remote Desktop) can hold it open for a
// moment, and then Windows throws instead of waiting; these say so instead.
static class ClipboardText
{
    // False when the clipboard was busy.
    public static bool TrySet(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (ExternalException) { return false; }
    }

    // The text on the clipboard ("" when it holds none), or null when it was busy.
    public static string? TryGet()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : ""; }
        catch (ExternalException) { return null; }
    }
}
