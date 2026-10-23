using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WoWCharacterTracker.Ui;

// Draws an item's tooltip as a PNG for loot and mount posts, the way the game and Wowhead show it:
// the icon on the left, then the same tooltip the app shows on hover (WowTooltip, from Wowhead's
// tooltip HTML through Services/TooltipHtml).
static class ItemTooltipImage
{
    const double Scale = 2; // drawn at 2x so it stays sharp when Discord scales it

    // Null when Wowhead doesn't know the item (or can't be reached) or drawing fails; the post then
    // goes out without the picture. Safe to call from any thread. type "spell" draws a spell's tooltip
    // the same way (mounts learned without an item).
    public static async Task<byte[]?> RenderAsync(int id, Flavor flavor, string type = "item")
    {
        if (id <= 0 || Application.Current is not { } app) return null;
        var info = await Wowhead.GetAsync(type, id, flavor);
        if (info?.Html is not { Length: > 0 } html) return null;
        var icon = info.Icon.Length > 0 ? await ImageCache.LoadAsync(Wowhead.IconUrl(info.Icon, "large")) : null;
        return await app.Dispatcher.InvokeAsync(() => Draw(html, icon));
    }

    static byte[]? Draw(string html, BitmapSource? icon)
    {
        try
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
            if (icon is not null)
            {
                row.Children.Add(new Border
                {
                    Width = 52, Height = 52, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Top, BorderThickness = new Thickness(2),
                    BorderBrush = ClassColors.Hex(0x3A3A3A),
                    Background = new ImageBrush(icon) { Stretch = Stretch.UniformToFill },
                });
            }
            var tip = new WowTooltip { Html = html, VerticalAlignment = VerticalAlignment.Top };
            tip.Build();
            row.Children.Add(tip);
            return ImageRender.ToPng(row, new Size(double.PositiveInfinity, double.PositiveInfinity), Scale);
        }
        catch (Exception ex)
        {
            Log.Warn("Couldn't draw the item picture, posting without it: " + ex.Message);
            return null;
        }
    }
}
