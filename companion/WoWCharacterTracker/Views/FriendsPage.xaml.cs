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

    // A header per friend over their characters (the list's GroupStyle), by BattleTag so two friends
    // with the same nickname stay apart; the rows come in friend order.
    static void GroupByFriend(object list)
    {
        var view = CollectionViewSource.GetDefaultView(list);
        if (view.GroupDescriptions.Count == 0) view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(CharacterRow.FriendTag)));
    }

    // Dragging a friend's card: past a few pixels it starts a drag carrying their BattleTag, the card
    // moves live onto the ones it passes, and letting go on the grid keeps the order (shared with the
    // addon). Esc, or letting go off the grid, puts the cards back.
    Point dragStart;
    string? pressed;

    void Card_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        dragStart = e.GetPosition(this);
        pressed = ((sender as FrameworkElement)?.DataContext as FriendCard)?.Tag;
    }

    void Card_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => pressed = null;

    void Card_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (pressed is null || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || friends is null) return;
        var moved = e.GetPosition(this) - dragStart;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var tag = pressed;
        pressed = null;
        var before = friends.CardOrder;
        var result = DragDrop.DoDragDrop((DependencyObject)sender, tag, DragDropEffects.Move);
        friends.EndDrag(before, result == DragDropEffects.Move);
    }

    void Card_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        if (friends is null || e.Data.GetData(typeof(string)) is not string dragged) return;
        if ((sender as FrameworkElement)?.DataContext is FriendCard over) friends.MoveCard(dragged, over.Tag);
    }

    // Anywhere on the grid (a card or the gaps between them) counts as letting go there.
    void Grid_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    void Card_Drop(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
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
