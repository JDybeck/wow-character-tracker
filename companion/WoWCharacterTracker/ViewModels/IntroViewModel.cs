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
        new("Setup checks",
            "Game folder, addon and Discord. Green means it's fine. Amber says what's missing, with a button for it. Hover a check for the details.",
            "setup"),
        new("Your characters",
            "Characters lists everyone you've logged in with, click one for gear, bags, leveling and notes. Items finds anything they carry, Professions shows who can craft what.",
            "characters"),
        new("Sharing",
            "Who hears about your moments: Discord, your guild or group, and your stream. Pick Quiet, Normal or Everything, or open Customize to choose per moment.",
            "sharing"),
        new("Followed items",
            "Your wish list. Follow any item, and when one of your characters gets it, it can be posted, shown on stream and told to your guild.",
            "followed"),
        new("Friends",
            "Characters your Battle.net friends share with you, and what you share back. They need the addon too, and nothing is shared until you allow it.",
            "friends"),
        new("Discord and stream",
            "Also on Sharing: paste a Discord webhook link to pick where posts go (Send test shows a sample), and turn on the stream overlay for OBS or Streamlabs.",
            "sharing"),
        new("Settings",
            "Open with WoW, automatic updates, color themes, where the setup checks sit and Uninstall. Want this tour again? The ? button next to the info icon starts it.",
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
