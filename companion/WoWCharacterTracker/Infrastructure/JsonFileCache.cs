using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace WoWCharacterTracker.Infrastructure;

// Answers from the web kept in a JSON file in the data folder (Wowhead tooltips, Raider.IO renders),
// so each thing is only asked for once. The file is read the first time it's needed and written
// (Files.WriteAtomic) once changes go quiet for a couple of seconds, not once per entry: a bank full
// of new items is hundreds of entries. Safe to use from any thread.
// onLoad: tidies each entry read from the file (Wowhead drops text it makes again from the HTML).
sealed class JsonFileCache<T>(string fileName, JsonSerializerOptions? json = null, Func<T, T>? onLoad = null)
{
    static readonly TimeSpan Quiet = TimeSpan.FromSeconds(2);
    readonly object gate = new();
    Dictionary<string, T>? entries;
    int version;

    string FilePath => Path.Combine(Config.Dir, fileName);

    public bool TryGet(string key, [MaybeNullWhen(false)] out T value)
    {
        lock (gate) return Entries().TryGetValue(key, out value);
    }

    public void Set(string key, T value)
    {
        lock (gate) Entries()[key] = value;
        SaveSoon();
    }

    // The file's text for these entries, and back (kept public for the tests of each cache's format).
    public string Serialize(Dictionary<string, T> cache) => JsonSerializer.Serialize(cache, json);

    public Dictionary<string, T> Deserialize(string text)
    {
        var read = JsonSerializer.Deserialize<Dictionary<string, T>>(text, json) ?? [];
        return onLoad is null ? read : read.ToDictionary(kv => kv.Key, kv => onLoad(kv.Value));
    }

    // Called under the lock.
    Dictionary<string, T> Entries()
    {
        if (entries is not null) return entries;
        try { entries = File.Exists(FilePath) ? Deserialize(File.ReadAllText(FilePath)) : []; }
        catch (Exception) { entries = []; } // a broken file just means asking again
        return entries;
    }

    async void SaveSoon()
    {
        var mine = Interlocked.Increment(ref version);
        await Task.Delay(Quiet);
        if (mine != Volatile.Read(ref version)) return;
        lock (gate)
        {
            try { Files.WriteAtomic(FilePath, Serialize(entries!)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
