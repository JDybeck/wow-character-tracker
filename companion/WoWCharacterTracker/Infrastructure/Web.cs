namespace WoWCharacterTracker.Infrastructure;

// The app's one HTTP client, so every request shares one connection pool (Wowhead and its icon CDN,
// Discord, GitHub, Raider.IO, Warcraft Wiki) instead of one per service. Each call says how long it
// may take; a request that runs out of time throws TaskCanceledException, as HttpClient's own timeout did.
// Sites like GitHub's API and Wowhead's search refuse requests without a User-Agent.
static class Web
{
    // No timeout of its own: each call brings one. Updater streams the download with its own token.
    public static readonly HttpClient Http = Create();

    static HttpClient Create()
    {
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("WoWCharacterTracker");
        return http;
    }

    public static async Task<string> GetStringAsync(string url, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        return await Http.GetStringAsync(url, cts.Token);
    }

    public static async Task<byte[]> GetBytesAsync(string url, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        return await Http.GetByteArrayAsync(url, cts.Token);
    }

    // The whole answer is read before it returns, so the caller can look at it after the time is up.
    public static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        return await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token);
    }
}
