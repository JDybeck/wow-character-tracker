using System.Windows;
using System.Windows.Threading;

namespace WoWCharacterTracker.Ui;

// local:FocusWhenVisible.Enabled="True" focuses a TextBox and selects its text when it appears,
// so renaming a webhook can start typing right away.
static class FocusWhenVisible
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(FocusWhenVisible), new PropertyMetadata(false, OnChanged));

    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject d, bool value) => d.SetValue(EnabledProperty, value);

    static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not System.Windows.Controls.TextBox box || e.NewValue is not true) return;
        box.IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is not true) return;
            box.Dispatcher.BeginInvoke(() => { box.Focus(); box.SelectAll(); }, DispatcherPriority.Input);
        };
    }
}
