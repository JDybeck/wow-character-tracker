using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WoWCharacterTracker;

public partial class MainWindow : Window
{
    readonly MainViewModel vm;

    internal MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = this.vm = vm;
        SourceInitialized += (_, _) => UseDarkTitleBar();
        // Startup, closing to the tray and quitting are handled in App.

        // For testing: render the window to a PNG and quit, so the layout can be checked without clicking around.
        if (Environment.GetEnvironmentVariable("CHARACTERTRACKER_SCREENSHOT") is { Length: > 0 } shot)
            Loaded += async (_, _) =>
            {
                await Task.Delay(4000);
                // CHARACTERTRACKER_SCREENSHOT_VIEW="forever:posting" picks the tab and page.
                var view = (Environment.GetEnvironmentVariable("CHARACTERTRACKER_SCREENSHOT_VIEW") ?? "").Split(':');
                if (vm.FlavorTabs.FirstOrDefault(t => t.Flavor.Key == view[0]) is { } tab) vm.SelectedFlavor = tab;
                if (view.Length > 1) vm.Page = view[1];
                if (view.Length > 2 && view[2] == "rename" && vm.Webhooks.FirstOrDefault() is { } w) w.IsEditing = true;
                if (view.Length > 2 && view[2] == "options" && vm.Webhooks.LastOrDefault() is { } o) o.IsExpanded = true;
                if (view.Length > 2 && view[2] == "testchars") { vm.ShowTestCharacters = true; await Task.Delay(1500); }
                if (view.Length > 2 && view[2] == "testall")
                    foreach (var kind in new[] { "level", "max", "rare", "epic", "legendary", "mount", "boss", "death" }) vm.TestKindCommand.Execute(kind);
                if (vm.SelectedFlavor is { } f) f.SelectedCharacter = f.Characters.FirstOrDefault(); // show the details panel too
                if (view.Length > 3 && view[3] == "cardopts") vm.ShowCardOptions = true;
                if (view.Length > 2 && view[2] == "addonupdate") vm.SelectedFlavor?.AddonStep.ActionCommand?.Execute(null);
                if (view.Length > 3 && view[3] == "post" && vm.SelectedFlavor?.SelectedCharacter is { } c)
                {
                    await Task.Delay(3000); // icons
                    vm.PostCharacterCommand.Execute(c);
                    await Task.Delay(4000);
                }
                await Task.Delay(2500);
                SaveScreenshot(shot);
                Close();
            };
    }

    // Clicking the open character's row header closes its details again.
    void CharacterHeader_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CharacterRow row } && vm.SelectedFlavor is { } tab
            && tab.SelectedCharacter == row)
        {
            tab.SelectedCharacter = null;
            e.Handled = true;
        }
    }

    void SaveScreenshot(string path)
    {
        var content = (FrameworkElement)Content;
        var w = content.ActualWidth + content.Margin.Left + content.Margin.Right;
        var h = content.ActualHeight + content.Margin.Top + content.Margin.Bottom;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)(w * dpi.DpiScaleX), (int)(h * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
        var bg = new System.Windows.Media.DrawingVisual();
        using (var dc = bg.RenderOpen())
            dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));
        bmp.Render(bg);
        bmp.Render(content);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        png.Save(fs);
    }

    // Dark title bar to match the window (Windows 10 20H1 and later).
    void UseDarkTitleBar()
    {
        var on = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
