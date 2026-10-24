using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WoWCharacterTracker.Ui;

// The mouse wheel over a scroll box inside another one (the highlights list in a page) scrolls the
// inner one while it can, then the page. WPF's ScrollViewer takes the wheel even when it has nothing
// left to scroll (or is short enough to need no scrolling at all), so the page under it froze.
// Registered once for every ScrollViewer in the app (App.OnStartup).
static class ScrollChaining
{
    public static void Register() =>
        EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnPreviewMouseWheel));

    static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Preview tunnels from the page down, so act only in the innermost box under the mouse.
        var box = (ScrollViewer)sender;
        if (e.Handled || Innermost(e.OriginalSource as DependencyObject) != box) return;
        var canScroll = e.Delta > 0 ? box.VerticalOffset > 0 : box.VerticalOffset < box.ScrollableHeight;
        if (canScroll || VisualTreeHelper.GetParent(box) is not UIElement parent) return;

        e.Handled = true;
        parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = box });
    }

    static ScrollViewer? Innermost(DependencyObject? d)
    {
        while (d is not null and not ScrollViewer)
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as ScrollViewer;
    }
}
