using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
            if (friends is null) return;
            friends.Opened += ScrollToTop;
            GroupByFriend(friends.Characters);
        };
    }

    // A header per friend over their characters (the list's GroupStyle); the rows come in friend order.
    static void GroupByFriend(object list)
    {
        var view = CollectionViewSource.GetDefaultView(list);
        if (view.GroupDescriptions.Count == 0) view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CharacterRow.FriendName)));
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
