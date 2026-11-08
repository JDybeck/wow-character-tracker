using System.Windows.Input;

namespace WoWCharacterTracker.ViewModels;

// One step of the setup helper: its title, one line, and the key the view shows its part by.
sealed record IntroStep(string Key, string Title, string Text);

// "Help me set it up": game folders, the addon, Discord and how much gets shared, one step at a time,
// each one doing the setup right there (the same checks and buttons as the rest of the app) and each
// one skippable. Opens by itself for a new install (Config.WantsIntro) and from the ? menu.
sealed class IntroViewModel(MainViewModel main) : Observable
{
    public static readonly IReadOnlyList<IntroStep> Steps =
    [
        new("games", "Your games",
            "Welcome! First, where your games are. The app found what it could; change a folder, or turn off a game you don't play."),
        new("addon", "The addon",
            "The addon notices your dings, kills, loot and mounts in game and hands them to the app. Nothing is written to the game folder until you click Install."),
        new("discord", "Where should your moments go?",
            "Paste a Discord webhook (channel settings, Integrations, Webhooks, Copy URL). Send test shows what a post looks like. No Discord? Skip this."),
        new("sharing", "How much gets shared?",
            "Pick how chatty it is. You can change it any time, or choose per moment, on Sharing."),
        new("done", "You're all set",
            "That's it. Play, and your moments show up here. Where things live:"),
    ];

    bool isOpen;
    public bool IsOpen { get => isOpen; private set => Set(ref isOpen, value); }

    int index;
    public int Index
    {
        get => index;
        set
        {
            if (!Set(ref index, Math.Clamp(value, 0, Steps.Count - 1))) return;
            foreach (var name in new[] { nameof(Step), nameof(Counter), nameof(IsFirst), nameof(IsLast), nameof(NextText), nameof(Progress) }) Raise(name);
        }
    }

    public IntroStep Step => Steps[index];
    public string Counter => $"Step {index + 1} of {Steps.Count}";
    public bool IsFirst => index == 0;
    public bool IsLast => index == Steps.Count - 1;
    public string NextText => IsLast ? "Let's go" : "Next";
    // One bar per step: done, the open one, or still to come.
    public IReadOnlyList<string> Progress => [.. Steps.Select((_, i) => i < index ? "done" : i == index ? "open" : "")];

    public ICommand OpenCommand => new RelayCommand(_ => OpenAt(Steps[0].Key));
    public ICommand NextCommand => new RelayCommand(_ => { if (IsLast) Close(); else Index++; });
    public ICommand BackCommand => new RelayCommand(_ => Index--);
    public ICommand CloseCommand => new RelayCommand(_ => Close());

    public void OpenAt(string key)
    {
        main.ShowHelp = false;
        Index = Math.Max(0, Steps.ToList().FindIndex(s => s.Key == key));
        IsOpen = true;
    }

    // Closing counts as done: it doesn't open by itself again, the ? menu brings it back.
    void Close()
    {
        var finished = IsLast;
        IsOpen = false;
        main.Config.IntroDone = true;
        main.Config.Save();
        if (finished) main.Page = "home";
    }
}
