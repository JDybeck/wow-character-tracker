using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;

namespace WoWCharacterTracker.ViewModels;

// The color theme picker in the settings menu: one row with the picked palette, which unfolds into
// small swatches of every palette (Ui/Theme), grouped. Saved per game in Config.Themes.
sealed class AppearanceViewModel : Observable
{
    readonly MainViewModel main;

    public AppearanceViewModel(MainViewModel main)
    {
        this.main = main;
        PickCommand = new RelayCommand(p => Pick((string)p!));
        ToggleCommand = new RelayCommand(_ => ShowAll = !ShowAll);
    }

    public ObservableCollection<ThemeGroup> Groups { get; } = [];
    public ICommand PickCommand { get; }
    public ICommand ToggleCommand { get; }
    public string Title => main.SelectedFlavor is { } tab ? $"Colors for {tab.Name}" : "Colors";

    ThemeChoice? current;
    public ThemeChoice? Current { get => current; private set => Set(ref current, value); }

    // The swatches are folded away until the row is clicked.
    bool showAll;
    public bool ShowAll { get => showAll; set => Set(ref showAll, value); }

    // The tab's palette, applied to the window; called when the tab changes.
    public void Apply()
    {
        if (main.SelectedFlavor is not { } tab) return;
        Theme.Apply(tab.Flavor, main.Config.Themes.GetValueOrDefault(tab.Flavor.Key));
        Refresh(tab.Flavor);
    }

    void Pick(string key)
    {
        if (main.SelectedFlavor is not { } tab) return;
        main.Config.Themes[tab.Flavor.Key] = key;
        main.Config.Save();
        Apply();
    }

    void Refresh(Flavor flavor)
    {
        var picked = Theme.Find(flavor, main.Config.Themes.GetValueOrDefault(flavor.Key)).Key;
        Groups.Clear();
        foreach (var group in Theme.GroupsFor(flavor))
            Groups.Add(new ThemeGroup(group.Title, group.Options.Select(o => new ThemeChoice(o, o.Key == picked)).ToList()));
        Current = Groups.SelectMany(g => g.Choices).FirstOrDefault(c => c.IsSelected);
        Raise(nameof(Title));
    }
}

sealed record ThemeGroup(string Title, IReadOnlyList<ThemeChoice> Choices);

// One palette in the picker: its name, a line about it, and swatches of its colors.
sealed class ThemeChoice(Theme.Option option, bool isSelected)
{
    public string Key => option.Key;
    public string Name => option.Name;
    public string Description => option.Description;
    public string Tip => $"{option.Name}: {option.Description}";
    public bool IsSelected => isSelected;
    public SolidColorBrush Bg { get; } = Brush(option.Colors.Card);
    public SolidColorBrush Accent { get; } = Brush(option.Colors.Accent);
    public SolidColorBrush AccentText { get; } = Brush(option.Colors.AccentText);
    public SolidColorBrush Second { get; } = Brush(option.Colors.SecondMid);

    static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush(Theme.ColorOf(hex));
        b.Freeze();
        return b;
    }
}
