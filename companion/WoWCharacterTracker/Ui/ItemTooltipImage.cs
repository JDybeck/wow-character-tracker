using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace WoWCharacterTracker.Ui;

// Draws an item's tooltip as a PNG for loot posts, the way the game and Wowhead show it: icon on
// the left, then the dark tooltip with the name in its quality color, item level, binding, slot and
// type, damage and speed, green Equip lines, required level and the sell price in coins.
// The text and colors come from Wowhead's tooltip for the item (Wowhead.Info.Html).
static class ItemTooltipImage
{
    const double Scale = 2; // drawn at 2x so it stays sharp when Discord scales it
    const double TipWidth = 300;

    static readonly FontFamily Font = new("Verdana, Segoe UI");
    static readonly Dictionary<string, Color> CoinColors = new()
    {
        ["gold"] = Color.FromRgb(0xFF, 0xD7, 0x00), ["silver"] = Color.FromRgb(0xC7, 0xC7, 0xCF), ["copper"] = Color.FromRgb(0xC8, 0x75, 0x33),
    };

    // Null when Wowhead doesn't know the item (or can't be reached) or drawing fails; the post then
    // goes out without the picture. Safe to call from any thread.
    public static async Task<byte[]?> RenderAsync(int itemId, Flavor flavor)
    {
        if (itemId <= 0 || Application.Current is not { } app) return null;
        var info = await Wowhead.GetAsync("item", itemId, flavor);
        if (info?.Html is not { Length: > 0 } html) return null;
        var lines = ItemTooltip.Parse(html);
        if (lines.Count == 0) return null;
        var icon = info.Icon.Length > 0 ? await CardImage.LoadAsync(Wowhead.IconUrl(info.Icon, "large")) : null;
        return await app.Dispatcher.InvokeAsync(() => Draw(lines, icon));
    }

    static byte[]? Draw(List<ItemTooltip.Line> lines, BitmapSource? icon)
    {
        try
        {
            var root = Build(lines, icon);
            root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            root.Arrange(new Rect(root.DesiredSize));
            root.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * Scale), (int)Math.Ceiling(root.ActualHeight * Scale),
                96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
            bmp.Render(root);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            png.Save(ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            Log.Warn("Couldn't draw the item picture, posting without it: " + ex.Message);
            return null;
        }
    }

    static FrameworkElement Build(List<ItemTooltip.Line> lines, BitmapSource? icon)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };

        if (icon is not null)
        {
            row.Children.Add(new Border
            {
                Width = 52, Height = 52, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Top, BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
                Background = new ImageBrush(icon) { Stretch = Stretch.UniformToFill },
            });
        }

        var body = new StackPanel { Width = TipWidth };
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var dock = new DockPanel { LastChildFill = true, Margin = new Thickness(0, i == 0 ? 0 : 2, 0, 0) };
            if (line.Right.Count > 0)
            {
                var right = Text(line.Right, 12.5);
                right.Margin = new Thickness(12, 0, 0, 0);
                DockPanel.SetDock(right, Dock.Right);
                dock.Children.Add(right);
            }
            var left = Text(line.Left, i == 0 ? 15 : 12.5); // the first line is the item name
            left.TextWrapping = TextWrapping.Wrap;
            dock.Children.Add(left);
            body.Children.Add(dock);
        }

        row.Children.Add(new Border
        {
            Child = body, Padding = new Thickness(10, 8, 10, 9), CornerRadius = new CornerRadius(5),
            VerticalAlignment = VerticalAlignment.Top, BorderThickness = new Thickness(1.5),
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x07, 0x0C, 0x1E)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x5C, 0x5C, 0x66)),
        });
        return row;
    }

    static TextBlock Text(List<ItemTooltip.Piece> pieces, double size)
    {
        var t = new TextBlock { FontFamily = Font, FontSize = size, LineHeight = size * 1.35 };
        foreach (var p in pieces)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(p.Color));
            t.Inlines.Add(new Run(p.Text) { Foreground = brush });
            if (p.Coin is not null && CoinColors.TryGetValue(p.Coin, out var coin))
            {
                t.Inlines.Add(new InlineUIContainer(new Ellipse
                {
                    Width = 9, Height = 9, Margin = new Thickness(3, 0, 1, 0),
                    Fill = new SolidColorBrush(coin), Stroke = new SolidColorBrush(Color.FromArgb(0x90, 0, 0, 0)), StrokeThickness = 0.8,
                }) { BaselineAlignment = BaselineAlignment.Center });
            }
        }
        return t;
    }
}
