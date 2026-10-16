using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WoWCharacterTracker.ViewModels;

// One of the three setup checks at the top of the window.
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
}
