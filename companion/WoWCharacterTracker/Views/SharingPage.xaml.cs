using System.Windows;
using System.Windows.Controls;

namespace WoWCharacterTracker.Views;

public partial class SharingPage : UserControl
{
    public SharingPage() => InitializeComponent();

    // A row's rule ("every 5 to 30, then every level") clicked: scroll to where it's set under Advanced,
    // with the cursor in the box when there is one.
    void Rule_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SharingRow row) return;
        FrameworkElement? target = row.Moment.Key switch
        {
            "level" => LevelSplitBox,
            "profession" => ProfessionStepsBox,
            "keyrun" => KeyRunMinBox,
            _ => FiltersTitle, // rares Bind on Equip only, end bosses first kill only
        };
        target.BringIntoView(new Rect(0, -40, target.ActualWidth, target.ActualHeight + 160));
        if (target is TextBox box)
        {
            box.Focus();
            box.SelectAll();
        }
    }
}
