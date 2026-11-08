namespace WoWCharacterTracker.ViewModels;

// One row of the Sharing table: a moment and a box for each place it can go. The boxes edit the
// existing settings and kind lists (Moments). posted: a Discord or screenshot box changed; inGame: a
// chat or ask box (the addon can change those too, so they carry the In game time); stream: an alert box.
sealed class SharingRow(Moment moment, FlavorSettings settings, Func<List<string>> streamAlerts,
    Action posted, Action inGame, Action stream) : Observable
{
    public Moment Moment { get; } = moment;
    public string Name => Moment.Name;
    public string Tip => Moment.Tip;
    public string? Rule => Moments.Rule(Moment, settings);
    public bool HasRule => Rule is { Length: > 0 };

    public bool Post
    {
        get => Moment.GetPost(settings);
        set { Moment.SetPost(settings, value); posted(); Raise(nameof(Post)); }
    }

    // One box for the screenshot: taken in game and attached to the post.
    public bool HasShot => Moment.Shot is not null;
    public bool Shot
    {
        get => Moment.Shot is { } k && settings.PostsShot(k);
        set
        {
            if (Moment.Shot is not { } k) return;
            settings.Screenshot[k] = value;
            settings.PostScreenshot[k] = value;
            posted();
            Raise(nameof(Shot));
        }
    }

    public bool HasAnnounce => Moment.Announce is not null;
    public string? AnnounceSample => Moment.AnnounceSample;
    public bool Guild
    {
        get => Moment.Announce is { } k && settings.AnnounceKinds.Contains(k);
        set
        {
            settings.AnnouncePartyKinds ??= [.. settings.AnnounceKinds]; // group chat keeps its own list
            Edit(settings.AnnounceKinds, Moment.Announce, value, inGame);
        }
    }
    public bool Group
    {
        get => Moment.Announce is { } k && (settings.AnnouncePartyKinds ?? settings.AnnounceKinds).Contains(k);
        set => Edit(settings.AnnouncePartyKinds ??= [.. settings.AnnounceKinds], Moment.Announce, value, inGame);
    }

    public bool HasAsk => Moment.Ask is not null;
    public bool Ask
    {
        get => Moment.Ask is { } k && (settings.AskKinds ?? []).Contains(k);
        set => Edit(settings.AskKinds ??= [], Moment.Ask, value, inGame);
    }

    public bool HasAlert => Moment.Alert is not null;
    public bool Alert
    {
        get => Moment.Alert is { } k && streamAlerts().Contains(k);
        set => Edit(streamAlerts(), Moment.Alert, value, stream);
    }

    void Edit(List<string> kinds, string? kind, bool on, Action changed, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (kind is null) return;
        kinds.Remove(kind);
        if (on) kinds.Add(kind);
        changed();
        Raise(name!);
    }

    public void Refresh()
    {
        foreach (var name in new[] { nameof(Post), nameof(Shot), nameof(Guild), nameof(Group), nameof(Ask), nameof(Alert), nameof(Rule), nameof(HasRule) })
            Raise(name);
    }
}
