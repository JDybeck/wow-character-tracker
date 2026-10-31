using System.Text;
using System.Text.Json;

namespace WoWCharacterTracker.Services;

// A thing to share as one line of text: a prefix, then the thing as base64 JSON. Decoding finds the
// code anywhere in the pasted text, so a chat line around it doesn't matter.
static class ShareCode
{
    public static string Encode<T>(string prefix, T value) =>
        prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)));

    public static bool Has(string prefix, string text) => text.Contains(prefix, StringComparison.Ordinal);

    // Null when it's damaged.
    public static T? Decode<T>(string prefix, string text) where T : class
    {
        var start = text.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0) return null;
        var b64 = new string(text[(start + prefix.Length)..].TakeWhile(c => char.IsLetterOrDigit(c) || c is '+' or '/' or '=').ToArray());
        try { return JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(Convert.FromBase64String(b64))); }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException) { return null; }
    }
}
