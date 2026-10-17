using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace WoWCharacterTracker.Ui;

// The game tab switch animation: an orb flies from the old tab to the new one with a short trail and
// bursts on arrival. A blue wisp (the Forever trailer's) going to Forever, a void orb going to Retail.
// A handful of shapes with radial gradients (no blur effects) for about 0.7 s, then they're removed,
// so it costs nothing outside the switch.
static class TabOrb
{
    public enum Kind { Wisp, Void }

    const double FlightMs = 520;

    public static void Play(Canvas layer, FrameworkElement from, FrameworkElement to, Kind kind)
    {
        if (!from.IsVisible || !to.IsVisible) return;
        var a = from.TranslatePoint(new Point(from.ActualWidth / 2, from.ActualHeight / 2), layer);
        var b = to.TranslatePoint(new Point(to.ActualWidth / 2, to.ActualHeight / 2), layer);

        // The orb and its trail: smaller, fainter copies a little behind.
        (double Size, double Opacity, double DelayMs)[] trail = [(24, 1, 0), (17, 0.6, 45), (12, 0.4, 90), (8, 0.25, 135)];
        foreach (var (size, opacity, delay) in trail)
        {
            var orb = new Ellipse { Width = size, Height = size, Fill = Orb(kind), Opacity = 0, IsHitTestVisible = false };
            layer.Children.Add(orb);
            var start = TimeSpan.FromMilliseconds(delay);
            var flight = TimeSpan.FromMilliseconds(FlightMs);
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

            orb.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(a.X - size / 2, b.X - size / 2, flight) { BeginTime = start, EasingFunction = ease });
            // A slight arc up and back down on the way.
            var top = new DoubleAnimationUsingKeyFrames { BeginTime = start };
            top.KeyFrames.Add(new LinearDoubleKeyFrame(a.Y - size / 2, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            top.KeyFrames.Add(new EasingDoubleKeyFrame(Math.Min(a.Y, b.Y) - size / 2 - 9, KeyTime.FromTimeSpan(flight / 2), new SineEase { EasingMode = EasingMode.EaseOut }));
            top.KeyFrames.Add(new EasingDoubleKeyFrame(b.Y - size / 2, KeyTime.FromTimeSpan(flight), new SineEase { EasingMode = EasingMode.EaseIn }));
            orb.BeginAnimation(Canvas.TopProperty, top);

            var fade = new DoubleAnimationUsingKeyFrames { BeginTime = start };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(opacity, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(opacity, KeyTime.FromTimeSpan(flight)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(flight + TimeSpan.FromMilliseconds(160))));
            fade.Completed += (_, _) => layer.Children.Remove(orb);
            orb.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        // The burst where it lands: a ring that grows and fades.
        const double ring = 34;
        var burst = new Ellipse
        {
            Width = ring, Height = ring, StrokeThickness = 2, Stroke = Edge(kind), Opacity = 0, IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(0.3, 0.3),
        };
        Canvas.SetLeft(burst, b.X - ring / 2);
        Canvas.SetTop(burst, b.Y - ring / 2);
        layer.Children.Add(burst);
        var at = TimeSpan.FromMilliseconds(FlightMs - 40);
        var grow = new DoubleAnimation(0.3, 1.9, TimeSpan.FromMilliseconds(380)) { BeginTime = at, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        burst.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        burst.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        var ringFade = new DoubleAnimationUsingKeyFrames { BeginTime = at };
        ringFade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(60))));
        ringFade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(380))));
        ringFade.Completed += (_, _) => layer.Children.Remove(burst);
        burst.BeginAnimation(UIElement.OpacityProperty, ringFade);
    }

    // Wisp: a bright white-blue core in a sky blue glow. Void: a dark core in a purple corona.
    static readonly Brush WispFill = Radial((0, "#FFFFFFFF"), (0.35, "#FFBAE6FD"), (0.6, "#B338BDF8"), (1, "#000EA5E9"));
    static readonly Brush VoidFill = Radial((0, "#FF0B0014"), (0.4, "#FF1E0B3A"), (0.62, "#E67C3AED"), (0.8, "#80A855F7"), (1, "#00A855F7"));
    static readonly Brush WispEdge = Solid("#CC7DD3FC");
    static readonly Brush VoidEdge = Solid("#CCA78BFA");

    static Brush Orb(Kind kind) => kind == Kind.Wisp ? WispFill : VoidFill;
    static Brush Edge(Kind kind) => kind == Kind.Wisp ? WispEdge : VoidEdge;

    static Brush Radial(params (double Offset, string Color)[] stops)
    {
        var brush = new RadialGradientBrush();
        foreach (var (offset, color) in stops) brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(color), offset));
        brush.Freeze();
        return brush;
    }

    static Brush Solid(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
