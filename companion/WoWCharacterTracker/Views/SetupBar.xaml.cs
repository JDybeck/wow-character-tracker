using System.Windows;
using System.Windows.Controls;

namespace WoWCharacterTracker.Views;

public partial class SetupBar : UserControl
{
    public SetupBar() => InitializeComponent();

    // Full window width (top or bottom): the chips and the fixes on one line. Off in the right column.
    public static readonly DependencyProperty WideProperty =
        DependencyProperty.Register(nameof(Wide), typeof(bool), typeof(SetupBar));
    public bool Wide { get => (bool)GetValue(WideProperty); set => SetValue(WideProperty, value); }
}
