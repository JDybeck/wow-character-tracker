using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WoWCharacterTracker.Ui;

// The app's animations, as attached properties, all switched off by Enabled (Settings, Animations):
//   Motion.Reveal="True"      unfolds the element when it appears (character details): its height
//                             grows from 0 while it fades in. The content inside keeps its measured
//                             size, so only the frame's height changes each frame, not the content's layout.
//   Motion.FadeIn="True"      fades and slides the element in a little when it appears (details tabs).
//   Motion.SlideGroup / SlideIndex
//                             slides the element in from the side when its Visibility turns Visible,
//                             from the right when its index is higher than the last one shown in the
//                             same group (pages: Characters, Items...; game tabs: Retail, Forever).
// Opacity and render transforms don't redo layout, so the slides and fades are cheap.
static class Motion
{
    public static bool Enabled { get; set; } = true;

    static readonly Duration Quick = TimeSpan.FromMilliseconds(220);
    static readonly Duration Unfold = TimeSpan.FromMilliseconds(280);
    static IEasingFunction Ease => new CubicEase { EasingMode = EasingMode.EaseOut };

    // --- Reveal (unfold) ---

    public static readonly DependencyProperty RevealProperty = DependencyProperty.RegisterAttached("Reveal", typeof(bool), typeof(Motion),
        new PropertyMetadata(false, (d, e) => { if (d is FrameworkElement el && (bool)e.NewValue) el.Loaded += RevealOnLoaded; }));
    public static void SetReveal(DependencyObject d, bool value) => d.SetValue(RevealProperty, value);
    public static bool GetReveal(DependencyObject d) => (bool)d.GetValue(RevealProperty);

    static void RevealOnLoaded(object sender, RoutedEventArgs e)
    {
        var el = (FrameworkElement)sender;
        if (!Enabled || el.ActualHeight <= 0) return;
        var full = el.ActualHeight;
        el.ClipToBounds = true;
        var grow = new DoubleAnimation(0, full, Unfold) { EasingFunction = Ease, FillBehavior = FillBehavior.Stop };
        // Back to its natural height afterwards, so it follows its content again (icons arriving...).
        grow.Completed += (_, _) => { el.Height = double.NaN; el.ClipToBounds = false; };
        el.Height = full;
        el.BeginAnimation(FrameworkElement.HeightProperty, grow);
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Unfold) { EasingFunction = Ease });
    }

    // --- FadeIn ---

    public static readonly DependencyProperty FadeInProperty = DependencyProperty.RegisterAttached("FadeIn", typeof(bool), typeof(Motion),
        new PropertyMetadata(false, (d, e) => { if (d is FrameworkElement el && (bool)e.NewValue) el.Loaded += FadeOnLoaded; }));
    public static void SetFadeIn(DependencyObject d, bool value) => d.SetValue(FadeInProperty, value);
    public static bool GetFadeIn(DependencyObject d) => (bool)d.GetValue(FadeInProperty);

    static void FadeOnLoaded(object sender, RoutedEventArgs e)
    {
        if (!Enabled) return;
        var el = (FrameworkElement)sender;
        var move = Translate(el);
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, 0, Quick) { EasingFunction = Ease });
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Quick));
    }

    // --- Slide (pages, game tabs) ---

    public static readonly DependencyProperty SlideGroupProperty = DependencyProperty.RegisterAttached("SlideGroup", typeof(string), typeof(Motion),
        new PropertyMetadata(null, (d, e) => Watch(d)));
    public static void SetSlideGroup(DependencyObject d, string value) => d.SetValue(SlideGroupProperty, value);
    public static string GetSlideGroup(DependencyObject d) => (string)d.GetValue(SlideGroupProperty);

    public static readonly DependencyProperty SlideIndexProperty = DependencyProperty.RegisterAttached("SlideIndex", typeof(int), typeof(Motion),
        new PropertyMetadata(0));
    public static void SetSlideIndex(DependencyObject d, int value) => d.SetValue(SlideIndexProperty, value);
    public static int GetSlideIndex(DependencyObject d) => (int)d.GetValue(SlideIndexProperty);

    // The index last shown per group, to slide the right way.
    static readonly Dictionary<string, int> Shown = [];
    static readonly DependencyPropertyDescriptor VisibilityWatch = DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(UIElement));

    static void Watch(DependencyObject d)
    {
        if (d is not FrameworkElement el) return;
        // Only this element's own Visibility counts (not a parent's), so a page doesn't also slide
        // when its whole game tab is switched.
        VisibilityWatch.AddValueChanged(el, (_, _) => { if (el.Visibility == Visibility.Visible) Slide(el); });
        if (el.Visibility == Visibility.Visible) el.Loaded += (_, _) => Shown.TryAdd(GetSlideGroup(el), GetSlideIndex(el));
    }

    static void Slide(FrameworkElement el)
    {
        var group = GetSlideGroup(el);
        var index = GetSlideIndex(el);
        var from = Shown.TryGetValue(group, out var last) ? Math.Sign(index - last) : 0;
        Shown[group] = index;
        if (!Enabled || from == 0) return;
        var move = Translate(el);
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from * 40, 0, Quick) { EasingFunction = Ease });
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.2, 1, Quick));
    }

    // The element's own TranslateTransform (a new one if it has none, or a frozen one from a style).
    static TranslateTransform Translate(UIElement el)
    {
        if (el.RenderTransform is TranslateTransform { IsFrozen: false } t) return t;
        var fresh = new TranslateTransform();
        el.RenderTransform = fresh;
        return fresh;
    }
}
