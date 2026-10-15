using System.Globalization;
using System.Text;

namespace WoWCharacterTracker;

// Reads the addon's live events from the game's chat log (Logs\WoWChatLog.txt).
// The addon whispers each event to the player as "#CT1#<msgid>#<part>/<total>#<data>";
// the chat log gets both the sent and the received copy, so parts are deduplicated.
sealed class ChatLog(string flavorDir) : IDisposable
{
    const string Prefix = "#CT1#";

    readonly string file = Wow.ChatLogFile(flavorDir);
    readonly Dictionary<string, string?[]> partial = [];
    readonly HashSet<string> done = [];
    long offset = -1;
    Timer? timer;

    public event Action<Entry>? EntryReceived;

    // The first time the file grows while the app runs. Tells the user the game is writing it live.
    public event Action? FirstWrite;
    bool sawWrite;

    public void Start() => timer = new Timer(_ => Poll(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

    readonly object pollLock = new();

    void Poll()
    {
        if (!Monitor.TryEnter(pollLock)) return; // previous poll still running
        try
        {
            if (!File.Exists(file)) { offset = 0; return; }
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            // Start at the end so old lines from earlier sessions aren't replayed.
            if (offset < 0) { offset = fs.Length; return; }
            if (fs.Length < offset) offset = 0; // the file was cleared or replaced
            if (fs.Length == offset) return;
            if (!sawWrite)
            {
                sawWrite = true;
                FirstWrite?.Invoke();
            }

            fs.Seek(offset, SeekOrigin.Begin);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            var text = sr.ReadToEnd();

            // Only consume whole lines; a half-written last line is read again next time.
            var lastNewline = text.LastIndexOf('\n');
            if (lastNewline < 0) return;
            offset += Encoding.UTF8.GetByteCount(text.AsSpan(0, lastNewline + 1));

            foreach (var line in text[..lastNewline].Split('\n'))
                Handle(line.TrimEnd('\r'));
        }
        catch (IOException) { } // locked for a moment, try again next second
        catch (Exception ex) { Log.Warn("Couldn't read the chat log: " + ex.Message); }
        finally { Monitor.Exit(pollLock); }
    }

    void Handle(string line)
    {
        var start = line.IndexOf(Prefix, StringComparison.Ordinal);
        if (start < 0) return;

        // <msgid>#<part>/<total>#<data>
        var parts = line[(start + Prefix.Length)..].Split('#', 3);
        if (parts.Length < 3) return;
        var msgid = parts[0];
        var nums = parts[1].Split('/');
        if (nums.Length != 2 || !int.TryParse(nums[0], out var part) || !int.TryParse(nums[1], out var total)) return;
        if (total is < 1 or > 20 || part < 1 || part > total || done.Contains(msgid)) return;

        if (!partial.TryGetValue(msgid, out var chunks)) partial[msgid] = chunks = new string?[total];
        if (chunks.Length != total) return;
        chunks[part - 1] = parts[2];
        if (chunks.Any(c => c is null)) return;

        partial.Remove(msgid);
        done.Add(msgid);
        var entry = Decode(string.Concat(chunks));
        if (entry is not null && entry.Id.Length > 0) EntryReceived?.Invoke(entry);
    }

    // "key=value;key=value" with %XX escapes, the format the addon's encode() writes.
    static Entry? Decode(string data)
    {
        var values = new Dictionary<string, string>();
        foreach (var pair in data.Split(';'))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            values[pair[..eq]] = Unescape(pair[(eq + 1)..]);
        }
        if (values.Count == 0) return null;

        return Entry.From(
            k => values.GetValueOrDefault(k, ""),
            k => double.TryParse(values.GetValueOrDefault(k), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0);
    }

    // Only ASCII separators are escaped, so each %XX is a single character.
    static string Unescape(string s) =>
        s.Contains('%')
            ? System.Text.RegularExpressions.Regex.Replace(s, "%([0-9A-Fa-f]{2})",
                m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString())
            : s;

    public void Dispose() => timer?.Dispose();
}
