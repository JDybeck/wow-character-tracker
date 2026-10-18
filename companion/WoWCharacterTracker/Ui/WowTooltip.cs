using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WoWCharacterTracker.Ui;

// An item tooltip the way the game and Wowhead show it: dark box, quality colors, yellow item level,
// green effects, class colors, two-column rows ("Two-Hand ... Mace") and colored coins. The lines
// come from Services/TooltipHtml; this only draws them, and only once the tooltip is shown, so a
// page of icons costs nothing until one is hovered. Without HTML it shows the plain Text.
sealed class WowTooltip : Border
{
    public static readonly DependencyProperty HtmlProperty = DependencyProperty.Register(nameof(Html), typeof(string), typeof(WowTooltip),
        new PropertyMetadata(null, Rebuild));
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(WowTooltip),
        new PropertyMetadata(null, Rebuild));

    public string? Html { get => (string?)GetValue(HtmlProperty); set => SetValue(HtmlProperty, value); }
    public string? Text { get => (string?)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    // Wowhead's tooltip colors, by its class names.
    static readonly Brush White = Frozen(0xFFFFFF);
    static readonly Dictionary<string, Brush> Colors = new()
    {
        ["q0"] = Frozen(0x9D9D9D), ["q1"] = White, ["q2"] = Frozen(0x1EFF00), ["q3"] = Frozen(0x0070DD), ["q4"] = Frozen(0xA335EE),
        ["q5"] = Frozen(0xFF8000), ["q6"] = Frozen(0xE6CC80), ["q7"] = Frozen(0x00CCFF), ["q"] = Frozen(0xFFD100), ["toycolor"] = Frozen(0x68CCEF),
        ["c1"] = Frozen(0xC69B6D), ["c2"] = Frozen(0xF48CBA), ["c3"] = Frozen(0xAAD372), ["c4"] = Frozen(0xFFF468), ["c5"] = Frozen(0xFFFFFF),
        ["c6"] = Frozen(0xC41E3A), ["c7"] = Frozen(0x0070DD), ["c8"] = Frozen(0x3FC7EB), ["c9"] = Frozen(0x8788EE), ["c10"] = Frozen(0x00FF98),
        ["c11"] = Frozen(0xFF7C0A), ["c12"] = Frozen(0xA330C9), ["c13"] = Frozen(0x33937F),
        ["moneygold"] = Frozen(0xFFD700), ["moneysilver"] = Frozen(0xC7C7CF), ["moneycopper"] = Frozen(0xC87533),
    };

    public WowTooltip()
    {
        Background = Frozen(0x070C1A, 0xF2);
        BorderBrush = Frozen(0x5C6474);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(4);
        Padding = new Thickness(9, 7, 9, 8);
        MaxWidth = 340;
        TextElement.SetFontSize(this, 13);
        TextElement.SetFontFamily(this, new FontFamily("Verdana, Segoe UI"));
        Loaded += (_, _) => Build();
    }

    static void Rebuild(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WowTooltip { IsLoaded: true } tip) tip.Build();
    }

    void Build()
    {
        var lines = Html is { Length: > 0 } html
            ? TooltipHtml.Parse(html)
            : (Text ?? "").Split('\n').Where(l => l.Length > 0).Select(l => new TooltipHtml.Line([new(l, "")], [])).ToList();
        var panel = new StackPanel();
        for (var i = 0; i < lines.Count; i++)
        {
            var left = Draw(lines[i].Left, title: i == 0);
            // A new paragraph (an empty line in Wowhead's text) starts a little further down.
            var gap = lines[i].Gap ? new Thickness(0, 8, 0, 0) : new Thickness(0);
            if (lines[i].Right.Count == 0)
            {
                if (lines[i].Gap) left.Margin = gap;
                panel.Children.Add(left);
                continue;
            }
            var row = new Grid { Margin = gap };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var right = Draw(lines[i].Right, title: false);
            right.Margin = new Thickness(16, 0, 0, 0);
            Grid.SetColumn(right, 1);
            row.Children.Add(left);
            row.Children.Add(right);
            panel.Children.Add(row);
        }
        Child = panel;
    }

    static TextBlock Draw(IReadOnlyList<TooltipHtml.Part> parts, bool title)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = White, LineHeight = 18 };
        if (title)
        {
            text.FontSize = 14.5;
            text.Margin = new Thickness(0, 0, 0, 1);
        }
        foreach (var p in parts) text.Inlines.Add(new Run(p.Text) { Foreground = Colors.GetValueOrDefault(p.Color, White) });
        return text;
    }

    static SolidColorBrush Frozen(int rgb, byte alpha = 0xFF)
    {
        var b = new SolidColorBrush(Color.FromArgb(alpha, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        b.Freeze();
        return b;
    }
}
