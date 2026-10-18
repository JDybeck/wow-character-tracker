namespace WoWCharacterTracker.Infrastructure;

// The app's HTTP clients: each service keeps its own (its own timeout), all named the same way.
// Sites like GitHub's API and Wowhead's search refuse requests without a User-Agent.
static class Web
{
    public static HttpClient Client(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("WoWCharacterTracker");
        return http;
    }
}
