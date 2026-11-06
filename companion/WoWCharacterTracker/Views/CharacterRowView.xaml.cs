using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WoWCharacterTracker.Views;

public partial class CharacterRowView : UserControl
{
    public CharacterRowView() => InitializeComponent();

    // Clicking the open character's row header closes its details again (the list it's in, whichever page).
    void Row_MouseDown(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? up = this;
        while (up is not null and not ListBox) up = VisualTreeHelper.GetParent(up);
        if (up is ListBox list && DataContext is CharacterRow row && list.SelectedItem == row)
        {
            list.SelectedItem = null;
            e.Handled = true;
        }
    }
}
