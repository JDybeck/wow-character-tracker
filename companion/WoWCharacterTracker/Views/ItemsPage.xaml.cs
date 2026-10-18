using System.Windows;
using System.Windows.Controls;

namespace WoWCharacterTracker.Views;

public partial class ItemsPage : UserControl
{
    public ItemsPage() => InitializeComponent();

    // An item takes 344 px (330 + its margin); the list's padding (34) and the scroll bar (~18) don't hold items.
    void ListSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ItemsViewModel items })
            items.Columns = Math.Max(1, (int)((e.NewSize.Width - 34 - 18) / 344));
    }
}
