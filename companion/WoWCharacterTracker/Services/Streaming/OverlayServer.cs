using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace WoWCharacterTracker.Services.Streaming;

// The stream overlay as a web page for an OBS / Streamlabs Browser source: http://localhost:<port>/
// serves the page (Assets/overlay.html, inside the exe) and /state the current StreamSnapshot as
// JSON, which the page asks for every couple of seconds. Only localhost can reach it.
sealed class OverlayServer : IDisposable
{
    readonly HttpListener listener = new();
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    static readonly Lazy<byte[]> Page = new(() =>
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Stream/overlay.html")!;
        using var m = new MemoryStream();
        s.CopyTo(m);
        return m.ToArray();
    });

    public StreamSnapshot Snapshot { get; set; } = StreamSnapshot.Empty;
    public string Url { get; }

    public OverlayServer(int port)
    {
        Url = $"http://localhost:{port}/";
        listener.Prefixes.Add(Url);
    }

    // False (with the reason logged) when the port is taken or Windows says no.
    public bool Start()
    {
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            Log.Warn($"Couldn't start the stream overlay on {Url} ({ex.Message}). Another program may use that port.");
            return false;
        }
        _ = Task.Run(ServeAsync);
        return true;
    }

    async Task ServeAsync()
    {
        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException) { return; }
            try { Answer(ctx); }
            catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException) { }
        }
    }

    void Answer(HttpListenerContext ctx)
    {
        var res = ctx.Response;
        res.Headers["Cache-Control"] = "no-store";
        var (body, type, status) = ctx.Request.Url?.AbsolutePath switch
        {
            "/" or "/index.html" => (Page.Value, "text/html; charset=utf-8", 200),
            "/state" => (JsonSerializer.SerializeToUtf8Bytes(Snapshot with { Now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() }, Json), "application/json", 200),
            _ => (Encoding.UTF8.GetBytes("Not found"), "text/plain", 404),
        };
        res.StatusCode = status;
        res.ContentType = type;
        res.ContentLength64 = body.Length;
        res.OutputStream.Write(body);
        res.Close();
    }

    public void Dispose()
    {
        try { listener.Stop(); listener.Close(); } catch (ObjectDisposedException) { }
    }
}
