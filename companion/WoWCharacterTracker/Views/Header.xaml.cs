using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WoWCharacterTracker.Views;

public partial class Header : UserControl
{
    public Header() => InitializeComponent();

    // The search list: Up/Down pick a result, Enter opens it, Esc clears the box.
    void Search_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel { Search: var search }) return;
        switch (e.Key)
        {
            case Key.Down or Key.Up:
                search.Move(e.Key == Key.Down ? 1 : -1);
                if (SearchResults.ItemContainerGenerator.ContainerFromItem(search.Selected) is FrameworkElement picked) picked.BringIntoView();
                break;
            case Key.Enter:
                SearchBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource(); // typed faster than the binding's delay
                search.OpenSelected();
                break;
            case Key.Escape: search.Query = ""; break;
            default: return;
        }
        e.Handled = true;
    }

    // Back in the box with a search still in it: the list again.
    void Search_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is MainViewModel { Search: { Query.Length: > 0 } search }) search.IsOpen = true;
    }

    // Switching game tabs plays the orb animation from the old tab to the new one (not at startup,
    // when there's no old tab).
    void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!Motion.Enabled || e.RemovedItems.Count == 0 || e.AddedItems is not [FlavorViewModel next]) return;
        if (Tabs.ItemContainerGenerator.ContainerFromItem(e.RemovedItems[0]) is not FrameworkElement from
            || Tabs.ItemContainerGenerator.ContainerFromItem(next) is not FrameworkElement to) return;
        TabOrb.Play(OrbLayer, from, to, next.Flavor == Flavors.Forever ? TabOrb.Kind.Wisp : TabOrb.Kind.Void);
    }
}
