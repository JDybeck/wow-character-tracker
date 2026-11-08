using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace WoWCharacterTracker.Views;

public partial class FriendsPage : UserControl
{
    FriendsViewModel? friends;

    public FriendsPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (friends is not null) friends.Opened -= ScrollToTop;
            friends = (DataContext as FlavorViewModel)?.Friends;
            if (friends is not null) friends.Opened += ScrollToTop;
        };
    }

    // The character opened from Home at the top of the list, once the page is shown and laid out.
    void ScrollToTop(CharacterRow row) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
    {
        CharacterList.ScrollIntoView(row);
        CharacterList.UpdateLayout();
        if (CharacterList.ItemContainerGenerator.ContainerFromItem(row) is not ListBoxItem item) return;
        DependencyObject? d = item;
        while (d is not null and not ScrollViewer) d = VisualTreeHelper.GetParent(d);
        if (d is ScrollViewer scroll)
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + item.TranslatePoint(new Point(0, 0), scroll).Y);
    });
}
