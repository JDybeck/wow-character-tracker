using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;

namespace WoWCharacterTracker.ViewModels;

// The color theme picker in the settings menu: the open game tab's palettes (Ui/Theme), each with
// swatches, and which one is picked (saved per game in Config.Themes).
sealed class AppearanceViewModel : Observable
{
    readonly MainViewModel main;

    public AppearanceViewModel(MainViewModel main)
    {
        this.main = main;
        PickCommand = new RelayCommand(p => Pick((string)p!));
    }

    public ObservableCollection<ThemeChoice> Choices { get; } = [];
    public ICommand PickCommand { get; }
    public string Title => main.SelectedFlavor is { } tab ? $"Colors for {tab.Name}" : "Colors";

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
        Choices.Clear();
        foreach (var option in Theme.OptionsFor(flavor)) Choices.Add(new ThemeChoice(option, option.Key == picked));
        Raise(nameof(Title));
    }
}

// One palette in the picker: its name, a line about it, and swatches of its colors.
sealed class ThemeChoice(Theme.Option option, bool isSelected)
{
    public string Key => option.Key;
    public string Name => option.Name;
    public string Description => option.Description;
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
