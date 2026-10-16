using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace WoWCharacterTracker;

// Draws the character card as a PNG for Discord, since embeds can't show icons next to text.
// Same look as the details panel, in the current tab's theme. Runs on the UI thread.
static class CardImage
{
    const double Width = 560;
    const double Scale = 2; // drawn at 2x so it stays sharp when Discord scales it

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    static readonly FontFamily Ui = new("Segoe UI Variable Text, Segoe UI");
    static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    public static async Task<byte[]?> RenderAsync(CharacterRow c, Flavor flavor, IReadOnlyCollection<string> hidden, string nameStyle = "full")
    {
        bool Shows(string part) => !hidden.Contains(part);
        var highlights = Shows("highlights") ? c.Highlights.Take(5).ToList() : [];

        // Icons first: the visual is drawn in one go, so the pictures have to be loaded already.
        var urls = new List<string?> { c.PictureUrl };
        if (Shows("crests")) urls.AddRange(c.CrestList.Select(x => x.IconUrl));
        if (Shows("professions")) urls.AddRange(c.ProfessionList.Select(x => x.IconUrl));
        urls.AddRange(highlights.Select(h => h.IconUrl));
        var images = new Dictionary<string, BitmapSource>();
        await Task.WhenAll(urls.OfType<string>().Distinct().Select(async url =>
        {
            if (await LoadAsync(url) is { } img) lock (images) images[url] = img;
        }));
        BitmapSource? Img(string? url) => url is not null && images.TryGetValue(url, out var i) ? i : null;

        try
        {
            var card = Build(c, flavor, Format.Name(c.Name, c.Surname, nameStyle), Shows, highlights, Img);
            card.Measure(new Size(Width, double.PositiveInfinity));
            card.Arrange(new Rect(card.DesiredSize));
            card.UpdateLayout();

            var bmp = new RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth * Scale), (int)Math.Ceiling(card.ActualHeight * Scale),
                96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
            bmp.Render(card);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            png.Save(ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            Log.Warn("Couldn't draw the character card, posting it as text instead: " + ex.Message);
            return null;
        }
    }

    static async Task<BitmapSource?> LoadAsync(string url)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = new MemoryStream(bytes);
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    static Brush Res(string key) => Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;

    static FrameworkElement Build(CharacterRow c, Flavor flavor, string name, Func<string, bool> shows, List<HighlightRow> highlights, Func<string?, BitmapSource?> img)
    {
        var text = Res("Text");
        var soft = Res("TextSoft");
        var muted = Res("Muted");
        var dim = Res("Dim");

        TextBlock Txt(string s, double size, Brush brush, FontWeight? weight = null, FontFamily? font = null) => new()
        {
            Text = s, FontSize = size, Foreground = brush, FontWeight = weight ?? FontWeights.Normal,
            FontFamily = font ?? Ui, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        };
        TextBlock Label(string s) => new()
        {
            Text = s, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = muted, FontFamily = Ui, Margin = new Thickness(0, 14, 0, 5),
        };
        Image? Icon(string? url, double size) => img(url) is { } src ? new Image { Source = src, Width = size, Height = size, Margin = new Thickness(0, 0, 6, 0) } : null;

        var body = new StackPanel();

        // Header: portrait in a class colored ring, name, description, guild/realm.
        var header = new DockPanel { LastChildFill = true };
        var portrait = new Grid { Width = 64, Height = 64, Margin = new Thickness(0, 0, 16, 0) };
        portrait.Children.Add(new Ellipse { Fill = c.ClassSoftBrush });
        if (img(c.PictureUrl) is { } pic)
            portrait.Children.Add(new Image { Source = pic, Stretch = Stretch.UniformToFill, Clip = new EllipseGeometry(new Point(32, 32), 32, 32) });
        else
            portrait.Children.Add(new TextBlock { Text = c.Name[..1], FontSize = 26, FontWeight = FontWeights.Bold, Foreground = c.ClassBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        portrait.Children.Add(new Ellipse { Stroke = c.ClassBrush, StrokeThickness = 2 });
        DockPanel.SetDock(portrait, Dock.Left);
        header.Children.Add(portrait);

        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(Txt(name, 24, c.ClassBrush, FontWeights.SemiBold));
        if (c.Dead) nameRow.Children.Add(Txt("  DEAD", 12, Res("Red"), FontWeights.Bold));
        who.Children.Add(nameRow);
        who.Children.Add(Txt($"Level {c.Level} {c.Description}".Trim(), 14, soft));
        var place = new List<string>();
        if (shows("guild") && c.HasGuild) place.Add($"<{c.Guild}>");
        place.Add(c.Realm);
        if (flavor == Flavors.Forever && c.Ruleset.Length > 0) place.Add(c.Ruleset);
        who.Children.Add(Txt(string.Join("  ·  ", place), 12, dim));
        header.Children.Add(who);
        body.Children.Add(header);

        // Stats row.
        var stats = new UniformGrid { Rows = 1, Margin = new Thickness(0, 18, 0, 0) };
        void Stat(string label, UIElement value)
        {
            var s = new StackPanel();
            s.Children.Add(new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = muted, FontFamily = Ui, Margin = new Thickness(0, 0, 0, 4) });
            s.Children.Add(value);
            stats.Children.Add(s);
        }
        Stat("LEVEL", Txt(c.Level.ToString(), 17, text, FontWeights.SemiBold, Mono));
        if (shows("ilvl") && c.ItemLevel > 0) Stat("ITEM LEVEL", Txt(c.ItemLevelText, 17, Res("BlueText"), FontWeights.SemiBold, Mono));
        if (shows("played") && c.Played > 0) Stat("TOTAL PLAYED", Txt(c.PlayedText, 15, text, FontWeights.SemiBold));
        if (shows("gold") && c.HasMoney)
        {
            var coins = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var coin in c.ShortCoins)
            {
                coins.Children.Add(Txt(coin.Amount, 15, text, FontWeights.SemiBold, Mono));
                coins.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = coin.Brush, Margin = new Thickness(4, 1, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            }
            Stat("GOLD", coins);
        }
        body.Children.Add(stats);

        // Icon chips: crests and professions.
        void Chips(string label, IEnumerable<IconChip> chips)
        {
            var list = chips.ToList();
            if (list.Count == 0) return;
            body.Children.Add(Label(label));
            var wrap = new WrapPanel();
            foreach (var chip in list)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                if (Icon(chip.IconUrl, 20) is { } i) row.Children.Add(i);
                row.Children.Add(Txt(chip.Text, 13, soft));
                wrap.Children.Add(new Border
                {
                    Child = row, Background = Res("Hover"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6), Padding = new Thickness(4, 3, 10, 3), Margin = new Thickness(0, 0, 6, 6),
                });
            }
            body.Children.Add(wrap);
        }
        if (shows("crests")) Chips("CRESTS", c.CrestList);
        if (shows("professions")) Chips("PROFESSIONS", c.ProfessionList);

        if (shows("talents") && c.Talents.Length > 0)
        {
            body.Children.Add(Label("TALENTS"));
            body.Children.Add(Txt(c.Talents, 14, text));
        }
        if (shows("zone") && c.Zone.Length > 0)
        {
            body.Children.Add(Label("LAST SEEN"));
            body.Children.Add(Txt($"{c.Zone}, {c.LastSeenText}", 14, text));
        }

        if (shows("levels") && c.Levels.Count > 0)
        {
            body.Children.Add(Label("RECENT LEVELS"));
            foreach (var l in c.Levels.Take(3))
            {
                var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var a = Txt(l.LevelText, 13, soft, null, Mono);
                var b = Txt(l.Took, 13, Res("Green"), FontWeights.SemiBold, Mono);
                var z = Txt(l.Zone, 13, muted);
                Grid.SetColumn(b, 1); Grid.SetColumn(z, 2);
                g.Children.Add(a); g.Children.Add(b); g.Children.Add(z);
                body.Children.Add(g);
            }
        }

        if (highlights.Count > 0)
        {
            body.Children.Add(Label("HIGHLIGHTS"));
            foreach (var h in highlights)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                if (Icon(h.IconUrl, 20) is { } i) row.Children.Add(i);
                else row.Children.Add(new TextBlock { Text = h.Glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14, Foreground = h.Brush, Width = 26, VerticalAlignment = VerticalAlignment.Center });
                var t = new TextBlock { FontSize = 14, FontFamily = Ui, VerticalAlignment = VerticalAlignment.Center };
                t.Inlines.Add(new Run(h.Verb) { Foreground = text });
                t.Inlines.Add(new Run(h.Subject) { Foreground = h.Brush });
                row.Children.Add(t);
                body.Children.Add(row);
            }
        }

        var footer = Txt("WoW Character Tracker", 10, Res("Faint"));
        footer.HorizontalAlignment = HorizontalAlignment.Right;
        footer.Margin = new Thickness(0, 16, 0, 0);
        body.Children.Add(footer);

        return new Border
        {
            Width = Width, Child = body, Background = Res("Card"), BorderBrush = Res("Line"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14), Padding = new Thickness(22, 20, 22, 14),
            UseLayoutRounding = true, SnapsToDevicePixels = true,
        };
    }
}
