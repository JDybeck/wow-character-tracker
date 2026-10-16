using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WoWCharacterTracker.Views;

public partial class CharactersPage : UserControl
{
    public CharactersPage() => InitializeComponent();

    // Clicking the open character's row header closes its details again.
    void CharacterHeader_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CharacterRow row } && DataContext is MainViewModel { SelectedFlavor: { } tab }
            && tab.SelectedCharacter == row)
        {
            tab.SelectedCharacter = null;
            e.Handled = true;
        }
    }
}
