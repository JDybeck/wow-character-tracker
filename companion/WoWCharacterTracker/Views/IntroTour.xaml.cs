using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WoWCharacterTracker.Views;

// Places the tour's shade, ring and card around the part of the window the step is about.
public partial class IntroTour : UserControl
{
    const double Gap = 14, Edge = 8, Pad = 6;
    IntroViewModel? vm;

    // The window hands over how to find a step's target ("tabs", "setup"...); null or hidden = no target.
    internal Func<string, FrameworkElement?>? FindTarget { get; set; }

    public IntroTour()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (vm is not null) vm.PropertyChanged -= StepChanged;
            vm = e.NewValue as IntroViewModel;
            if (vm is not null) vm.PropertyChanged += StepChanged;
        };
        SizeChanged += (_, _) => Place();
        IsVisibleChanged += (_, _) => { if (IsVisible) { Focus(); Show(); } };
        KeyDown += (_, e) =>
        {
            var command = e.Key switch
            {
                Key.Escape => vm?.CloseCommand,
                Key.Right or Key.Enter => vm?.NextCommand,
                Key.Left => vm?.BackCommand,
                _ => null,
            };
            if (command is null) return;
            command.Execute(null);
            e.Handled = true;
        };
    }

    void StepChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IntroViewModel.Step) && IsVisible) Show();
    }

    // After layout, so the card's size and the target's place are known.
    void Show() => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
    {
        Place();
        Motion.Pop(Card);
    });

    void Place()
    {
        if (vm is null || ActualWidth <= 0) return;
        var area = new Rect(0, 0, ActualWidth, ActualHeight);
        Card.Measure(new Size(Card.Width, double.PositiveInfinity));
        var card = Card.DesiredSize;

        var target = vm.Step.Target is { } key ? FindTarget?.Invoke(key) : null;
        if (target is not { IsVisible: true })
        {
            Shade.Data = new RectangleGeometry(area);
            Ring.Visibility = Visibility.Collapsed;
            Put(Card, (area.Width - card.Width) / 2, (area.Height - card.Height) / 2);
            return;
        }

        var spot = target.TransformToVisual(this).TransformBounds(new Rect(target.RenderSize));
        spot.Inflate(Pad, Pad);
        Shade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(area), new RectangleGeometry(spot, 12, 12));
        Ring.Visibility = Visibility.Visible;
        Ring.Width = spot.Width;
        Ring.Height = spot.Height;
        Put(Ring, spot.X, spot.Y);

        // Under the target if it fits, else to its left, else above; always inside the window.
        double x = spot.Left, y = spot.Bottom + Gap;
        if (y + card.Height > area.Height - Edge)
        {
            if (spot.Left - Gap - card.Width >= Edge) (x, y) = (spot.Left - Gap - card.Width, spot.Top);
            else y = spot.Top - Gap - card.Height;
        }
        Put(Card, Math.Clamp(x, Edge, Math.Max(Edge, area.Width - card.Width - Edge)), Math.Clamp(y, Edge, Math.Max(Edge, area.Height - card.Height - Edge)));
    }

    static void Put(UIElement el, double x, double y)
    {
        Canvas.SetLeft(el, x);
        Canvas.SetTop(el, y);
    }
}
