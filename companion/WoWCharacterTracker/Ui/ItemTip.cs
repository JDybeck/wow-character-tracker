using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace WoWCharacterTracker.Ui;

// The one way the app shows a thing from the game (an item, a crest, a talent): Wowhead's tooltip in
// the game's look (WowTooltip) on hover, its Wowhead page on click. Put ui:ItemTip.Info="{Binding Item}"
// on any element; null (an empty bag slot) leaves the element alone. The tooltip itself is only built
// the first time it opens: a bank holds hundreds of items and most are never hovered.
static class ItemTip
{
    public static readonly DependencyProperty InfoProperty = DependencyProperty.RegisterAttached("Info", typeof(ItemInfo), typeof(ItemTip),
        new PropertyMetadata(null, Changed));

    public static ItemInfo? GetInfo(DependencyObject d) => (ItemInfo?)d.GetValue(InfoProperty);
    public static void SetInfo(DependencyObject d, ItemInfo? value) => d.SetValue(InfoProperty, value);

    // The tooltip draws its own box, so the standard tooltip frame around it goes.
    static readonly ControlTemplate Bare = MakeBare();

    static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement el) return;
        el.MouseLeftButtonUp -= Open;
        el.ToolTipOpening -= Build;
        if (e.NewValue is not ItemInfo info)
        {
            // Only take away what this put there (a chip may have its own plain tooltip).
            if (el.ToolTip is ItemInfo or ToolTip { Tag: ItemInfo })
            {
                el.ClearValue(FrameworkElement.ToolTipProperty);
                el.ClearValue(FrameworkElement.CursorProperty);
            }
            return;
        }

        el.ToolTip = info; // stands in until it first opens (Build)
        ToolTipService.SetInitialShowDelay(el, 100);
        ToolTipService.SetShowDuration(el, 60000);
        el.Cursor = Cursors.Hand;
        el.ToolTipOpening += Build;
        el.MouseLeftButtonUp += Open;
    }

    static void Build(object sender, ToolTipEventArgs e)
    {
        if (sender is not FrameworkElement el || el.ToolTip is not ItemInfo info) return;
        var tip = new WowTooltip();
        tip.SetBinding(WowTooltip.HtmlProperty, new Binding(nameof(ItemInfo.Html)) { Source = info });
        tip.SetBinding(WowTooltip.TextProperty, new Binding(nameof(ItemInfo.Tooltip)) { Source = info });
        el.ToolTip = new ToolTip { Template = Bare, Content = tip, Tag = info };
    }

    static void Open(object sender, MouseButtonEventArgs e)
    {
        if (sender is DependencyObject d && GetInfo(d) is { } info) Browser.Open(info.Url);
    }

    static ControlTemplate MakeBare()
    {
        var template = new ControlTemplate(typeof(ToolTip)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
        template.Seal();
        return template;
    }
}
