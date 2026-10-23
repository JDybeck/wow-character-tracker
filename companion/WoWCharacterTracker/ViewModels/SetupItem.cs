using System.Windows.Input;

namespace WoWCharacterTracker.ViewModels;

// One of the three setup checks (Views/SetupBar).
sealed class SetupItem(string title) : Observable
{
    public string Title { get; } = title;

    bool ok;
    public bool Ok { get => ok; set => Set(ref ok, value); }

    string detail = "";
    public string Detail { get => detail; set => Set(ref detail, value); }

    string? actionText;
    public string? ActionText { get => actionText; set => Set(ref actionText, value); }

    public ICommand? ActionCommand { get; init; }
    public string? ActionTooltip { get; init; } // what the button does, e.g. which folder to pick

    // For the checks the app can't work without (folder, addon): the big card over the pages
    // (Views/SetupCard) says what to do and why, with this icon.
    public string? Headline { get; init; }
    public string? Why { get; init; }
    public string Icon { get; init; } = "";

    public void Set(bool ok, string detail, string? action = null) => (Ok, Detail, ActionText) = (ok, detail, action);
}
