using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WoWCharacterTracker.Ui;

// For testing: CHARACTERTRACKER_SCREENSHOT=<png> renders the window to that file and quits, so
// the layout can be checked without clicking around. CHARACTERTRACKER_SCREENSHOT_VIEW picks what
// to show, as "tab:page[:extra[:extra2]]", e.g. "forever:posting" or "forever:characters:testchars:post".
//   extra:  rename | options (a webhook) | testchars | testall (every Test button) | addonupdate
//   extra2: cardopts (card options open) | post (Post to Discord for the first character)
static class ScreenshotMode
{
    public static string? Target => Environment.GetEnvironmentVariable("CHARACTERTRACKER_SCREENSHOT") is { Length: > 0 } s ? s : null;

    public static void Attach(Window window, MainViewModel vm, string target)
    {
        window.Loaded += async (_, _) =>
        {
            await Task.Delay(4000);
            var view = (Environment.GetEnvironmentVariable("CHARACTERTRACKER_SCREENSHOT_VIEW") ?? "").Split(':');
            string Part(int i) => view.Length > i ? view[i] : "";

            if (vm.FlavorTabs.FirstOrDefault(t => t.Flavor.Key == Part(0)) is { } tab) vm.SelectedFlavor = tab;
            if (Part(1).Length > 0) vm.Page = Part(1);
            if (Part(2) == "rename" && vm.Webhooks.FirstOrDefault() is { } w) w.IsEditing = true;
            if (Part(2) == "options" && vm.Webhooks.LastOrDefault() is { } o) o.IsExpanded = true;
            if (Part(2) == "testchars") { vm.ShowTestCharacters = true; await Task.Delay(1500); }
            if (Part(2) == "testall")
                foreach (var kind in new[] { "level", "max", "rare", "epic", "legendary", "mount", "boss", "death", "duel", "makgora", "mplus", "pvp" })
                    vm.TestKindCommand.Execute(kind);
            if (vm.SelectedFlavor is { } f) f.SelectedCharacter = f.Characters.FirstOrDefault(); // show the details panel too
            if (Part(3) == "cardopts") vm.ShowCardOptions = true;
            if (Part(2) == "addonupdate") vm.SelectedFlavor?.AddonStep.ActionCommand?.Execute(null);
            if (Part(3) == "posthighlight" && vm.SelectedFlavor?.SelectedCharacter?.Highlights.FirstOrDefault() is { } h)
            {
                await Task.Delay(3000);
                vm.PostHighlightCommand.Execute(h);
                await Task.Delay(3000);
            }
            if (Part(3) == "post" && vm.SelectedFlavor?.SelectedCharacter is { } c)
            {
                await Task.Delay(3000); // icons
                vm.PostCharacterCommand.Execute(c);
                await Task.Delay(4000);
            }
            await Task.Delay(2500);
            Save(window, target);
            window.Close();
        };
    }

    static void Save(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        var w = content.ActualWidth + content.Margin.Left + content.Margin.Right;
        var h = content.ActualHeight + content.Margin.Top + content.Margin.Bottom;
        var dpi = VisualTreeHelper.GetDpi(window);
        var bmp = new RenderTargetBitmap((int)(w * dpi.DpiScaleX), (int)(h * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen())
            dc.DrawRectangle(window.Background, null, new Rect(0, 0, w, h));
        bmp.Render(bg);
        bmp.Render(content);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        png.Save(fs);
    }
}
