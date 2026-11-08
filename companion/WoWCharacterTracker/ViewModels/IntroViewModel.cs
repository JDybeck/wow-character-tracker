using System.Windows.Input;

namespace WoWCharacterTracker.ViewModels;

// One stop on the tour: what it says and which part of the window it points at (Views/IntroTour
// finds it by Target; null = a card in the middle).
sealed record IntroStep(string Title, string Text, string? Target);

// The first-run tour: a few cards pointing at the parts of the window. Opens by itself for a new
// install (Config.WantsIntro), and from the ? button in the header.
sealed class IntroViewModel(MainViewModel main) : Observable
{
    public static readonly IReadOnlyList<IntroStep> Steps =
    [
        new("Welcome to Character Tracker",
            "All your WoW characters in one place, and the good moments in Discord and on stream if you want them there: dings, boss kills, epic loot, mounts. Here's a quick look around.",
            null),
        new("Your games",
            "Retail and Forever each have their own tab, with their own characters, settings and colors. Only play one? Turn the other off in Settings.",
            "tabs"),
        new("Your characters",
            "Everyone you've logged in with, click one for gear, bags, leveling and notes. The switch on the right shows Items (anything they carry), Professions (who crafts what) and Followed (your wish list).",
            "characters"),
        new("Sharing",
            "Who hears about your moments: Discord, your guild or group, and your stream. Pick Quiet, Normal or Everything, or open Customize to choose per moment. Your Discord webhooks and the stream overlay are here too.",
            "sharing"),
        new("Quick switches",
            "Discord, guild chat, group chat and the stream overlay on or off in one click, each with a dot that says how it's doing. The console at the bottom says what the app is doing.",
            "rail"),
        new("Friends",
            "Characters your Battle.net friends share with you, and what you share back. They need the addon too, and nothing is shared until you allow it.",
            "friends"),
        new("Settings",
            "Open with WoW, automatic updates, games, color themes, the addon's in game options and Uninstall. Want this tour again? It's in the ? menu.",
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
        main.ShowHelp = false;
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
