using System.Windows;
using System.Windows.Controls;

namespace WoWCharacterTracker.Views;

public partial class Header : UserControl
{
    public Header() => InitializeComponent();

    // Switching game tabs plays the orb animation from the old tab to the new one (not at startup,
    // when there's no old tab).
    void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!Motion.Enabled || e.RemovedItems.Count == 0 || e.AddedItems is not [FlavorViewModel next]) return;
        if (Tabs.ItemContainerGenerator.ContainerFromItem(e.RemovedItems[0]) is not FrameworkElement from
            || Tabs.ItemContainerGenerator.ContainerFromItem(next) is not FrameworkElement to) return;
        TabOrb.Play(OrbLayer, from, to, next.Flavor == Flavors.Forever ? TabOrb.Kind.Wisp : TabOrb.Kind.Void);
    }
}
