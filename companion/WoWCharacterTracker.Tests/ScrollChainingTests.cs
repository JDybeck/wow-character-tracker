using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WoWCharacterTracker.Tests;

// The wheel over a short list inside a page (the character's highlights) scrolls the page.
public class ScrollChainingTests
{
    [Fact]
    public void The_wheel_over_a_list_that_cannot_scroll_scrolls_the_page()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Run(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    static void Run()
    {
        WoWCharacterTracker.Ui.ScrollChaining.Register();
        // A long page, and in it a list with its own scroll box that fits without scrolling.
        var row = new Border { Height = 20 };
        var list = new ScrollViewer { MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new StackPanel { Children = { row } } };
        var page = new ScrollViewer { Height = 300, Content = new StackPanel { Children = { list, new Border { Height = 2000 } } } };
        page.Measure(new Size(400, 300));
        page.Arrange(new Rect(0, 0, 400, 300));
        page.UpdateLayout();

        Wheel(row, -120);
        page.UpdateLayout();
        Assert.True(page.VerticalOffset > 0);
    }

    // What the input system does for a wheel turn: the preview down to the element, then, unless
    // something took it, the bubbling event back up.
    static void Wheel(UIElement over, int delta)
    {
        var preview = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
        over.RaiseEvent(preview);
        if (!preview.Handled) over.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta) { RoutedEvent = UIElement.MouseWheelEvent });
    }
}
