using System.Windows.Controls;
using System.Windows.Input;

namespace WoWCharacterTracker.Views;

public partial class SetupHelper : UserControl
{
    public SetupHelper()
    {
        InitializeComponent();
        Focusable = true;
        IsVisibleChanged += (_, _) => { if (IsVisible) Focus(); };
        // Esc closes it (only Esc: the webhook boxes need Enter and the arrow keys).
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is IntroViewModel vm) vm.CloseCommand.Execute(null);
        };
    }
}
