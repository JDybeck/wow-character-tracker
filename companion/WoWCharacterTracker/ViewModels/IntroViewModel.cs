using System.Windows.Input;

namespace WoWCharacterTracker.ViewModels;

// One stop on the tour: what it says and which part of the window it points at (Views/IntroTour
// finds it by Target; null = a card in the middle).
sealed record IntroStep(string Title, string Text, string? Target);

// The first-run tour: a few cards pointing at the parts of the window. Opens by itself for a new
// install (Config.WantsIntro), and from Settings, Show the tour.
sealed class IntroViewModel(MainViewModel main) : Observable
{
    public static readonly IReadOnlyList<IntroStep> Steps =
    [
        new("Welcome to Character Tracker",
            "It watches your WoW characters and posts the good moments to Discord: dings, boss kills, epic loot, mounts. Here's a quick look around.",
            null),
        new("Your games",
            "Retail and Forever each have their own tab, with their own characters, settings and colors. Only play one? Turn the other off in Settings.",
            "tabs"),
        new("Setup checks",
            "Game folder, addon and Discord. Green means it's fine. Amber says what's missing, with a button for it. Hover a check for the details.",
            "setup"),
        new("Discord",
            "Paste a webhook link here to pick where posts go. Send test shows what a post looks like. You can add several and choose per game.",
            "discord"),
        new("Pages",
            "Characters lists everyone you've logged in with, click one for gear, leveling and notes. What gets posted picks the moments that go to Discord, In game has the addon's options.",
            "pages"),
        new("Settings",
            "Open with WoW, color themes, where the setup checks sit, and this tour again whenever you want it.",
            "settings"),
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
            foreach (var name in new[] { nameof(Step), nameof(Counter), nameof(IsFirst), nameof(IsLast), nameof(NextText) }) Raise(name);
        }
    }

    public IntroStep Step => Steps[index];
    public string Counter => $"{index + 1} of {Steps.Count}";
    public bool IsFirst => index == 0;
    public bool IsLast => index == Steps.Count - 1;
    public string NextText => IsLast ? "Done" : IsFirst ? "Show me" : "Next";

    // Ticked by default: closing the tour usually means "got it". Unticked, it comes back next start
    // (only while the app still looks new, see Config.WantsIntro).
    bool dontShowAgain = true;
    public bool DontShowAgain { get => dontShowAgain; set => Set(ref dontShowAgain, value); }

    public ICommand OpenCommand => new RelayCommand(_ => Open());
    public ICommand NextCommand => new RelayCommand(_ => { if (IsLast) Close(finished: true); else Index++; });
    public ICommand BackCommand => new RelayCommand(_ => Index--);
    public ICommand CloseCommand => new RelayCommand(_ => Close(finished: false));

    public void Open()
    {
        main.ShowSettings = false;
        Index = 0;
        IsOpen = true;
    }

    void Close(bool finished)
    {
        IsOpen = false;
        if (!finished && !DontShowAgain) return;
        main.Config.IntroDone = true;
        main.Config.Save();
    }
}
