using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WoWCharacterTracker.Ui;

// For testing: CHARACTERTRACKER_SCREENSHOT=<png> renders the window to that file and quits, so
// the layout can be checked without clicking around. CHARACTERTRACKER_SCREENSHOT_VIEW picks what
// to show, as "tab:page[:extra[:extra2]]", e.g. "forever:posting" or "forever:characters:testchars:post".
//   extra:  rename | options (a webhook) | removehook | addhook (the Add form open) | testchars | updating (the update banner mid-download) | updateready (a new version offered)
//           | stream (the Stream overlay panel on and open) | testall (every Test button; test characters on when Forever has none) | addonupdate
//           | nofolder (the tab as if no game folder was found, faked: no detection runs)
//           | settings (the settings menu, saved next to it as <name>-settings.png) | orb (switches tab, captured mid-animation)
// CHARACTERTRACKER_SCREENSHOT_THEME picks a color theme for the tab (Ui/Theme option key).
//   extra2: gear | gearset (the gear tab on the last Equipment Manager set) | leveling (a details tab) | closed (no character open) | search (Items page, "potion") | exclude (Items page: Bags only, then Bank left out) | cardopts (card options open) | post (Post to Discord for the first character) | posttalents (its Post talents) | remove (its remove question) | sync (Sync now)
// CHARACTERTRACKER_SCREENSHOT_SIZE sets the window size as "width" or "widthxheight", to check narrow layouts.
static class ScreenshotMode
{
    public static string? Target => Environment.GetEnvironmentVariable("CHARACTERTRACKER_SCREENSHOT") is { Length: > 0 } s ? s : null;

    public static void Attach(Window window, MainViewModel vm, string target)
    {
        window.Loaded += async (_, _) =>
        {
            var size = (Environment.GetEnvironmentVariable("CHARACTERTRACKER_SCREENSHOT_SIZE") ?? "").Split('x');
            if (int.TryParse(size[0], out var width)) window.Width = width;
            if (size.Length > 1 && int.TryParse(size[1], out var height)) window.Height = height;
            await Task.Delay(4000);
            var view = (Environment.GetEnvironmentVariable("CHARACTERTRACKER_SCREENSHOT_VIEW") ?? "").Split(':');
            string Part(int i) => view.Length > i ? view[i] : "";

            if (vm.FlavorTabs.FirstOrDefault(t => t.Flavor.Key == Part(0)) is { } tab) vm.SelectedFlavor = tab;
            if (Part(1) == "uninstall") vm.ShowUninstallCommand.Execute(null); // fills the list too
            else if (Part(1).Length > 0) vm.Page = Part(1);
            if (Part(2) == "rename" && vm.Discord.Webhooks.FirstOrDefault() is { } w) w.IsEditing = true;
            if (Part(2) == "options" && vm.Discord.Webhooks.LastOrDefault() is { } o) o.IsExpanded = true;
            if (Part(2) == "removehook" && vm.Discord.Webhooks.FirstOrDefault() is { } rh) rh.ConfirmRemove = true;
            if (Part(2) == "addhook") vm.Discord.ShowAddWebhook = true;
            if (Part(2) == "testchars") { vm.ShowTestCharacters = true; await Task.Delay(1500); }
            // Test buttons need a character; without a game folder, the test characters stand in.
            if (Part(2) == "testall" && vm.SelectedFlavor is { HasCharacters: false, Flavor.HasRulesets: true }) { vm.ShowTestCharacters = true; await Task.Delay(1500); }
            if (Part(2) == "testall")
                foreach (var kind in new[] { "level", "max", "rare", "epic", "legendary", "mount", "boss", "death", "duel", "makgora", "mplus", "pvp" })
                    vm.TestKindCommand.Execute(kind);
            // show the details panel too, for CHARACTERTRACKER_SCREENSHOT_CHARACTER (a name) or the first character
            var pick = Environment.GetEnvironmentVariable("CHARACTERTRACKER_SCREENSHOT_CHARACTER");
            if (vm.SelectedFlavor is { } f) f.SelectedCharacter = f.Characters.FirstOrDefault(c => c.Name == pick) ?? f.Characters.FirstOrDefault();
            if (Part(3) == "cardopts") vm.ShowCardOptions = true;
            if (Part(3) == "closed" && vm.SelectedFlavor is { } cf) cf.SelectedCharacter = null; // every row closed
            if (Part(3) is "gear" or "leveling" or "notes" && vm.SelectedFlavor?.SelectedCharacter is { } dc) dc.DetailTab = Part(3);
            if (Part(3) == "gearset" && vm.SelectedFlavor?.SelectedCharacter is { } gc) // the gear tab showing the last Equipment Manager set
            {
                gc.DetailTab = "gear";
                if (gc.Gear.Choices.Count > 0) gc.Gear.Show(gc.Gear.Choices[^1]);
            }
            if (Part(3) == "search" && vm.SelectedFlavor is { } sf) { await Task.Delay(2500); sf.Items.Search = "potion"; }
            if (Part(3) == "remove" && vm.SelectedFlavor?.SelectedCharacter is { } r) r.ConfirmRemove = true;
            if (Part(3) == "sync") vm.SyncCommand.Execute(null);
            if (Part(3) == "reload" && vm.SelectedFlavor is { } rf) rf.ReloadNeeded = true;
            if (Part(2) == "nofolder") vm.SelectedFlavor?.ShowFolderMissing();
            if (Part(2) == "addonupdate") vm.SelectedFlavor?.AddonStep.ActionCommand?.Execute(null);
            if (Part(2) == "updateready") // the update banner and header button for a new version
            {
                var next = new Version(Updater.Current.Major, Updater.Current.Minor + 1, 0);
                // CHARACTERTRACKER_SCREENSHOT_NOTES: a file with release notes to show, opened.
                var notesFile = Environment.GetEnvironmentVariable("CHARACTERTRACKER_SCREENSHOT_NOTES");
                var notes = notesFile is { Length: > 0 } && File.Exists(notesFile) ? File.ReadAllText(notesFile) : "- Something new";
                vm.Updates.Offer(new Updater.Release(next, $"v{next}", notes, "", ""));
                if (notesFile is { Length: > 0 }) vm.Updates.ShowNotes = true;
            }
            if (Part(2) == "stream") { vm.Stream.PanelOpen = true; vm.Stream.Enabled = true; }
            if (Part(3) == "exclude" && vm.SelectedFlavor is { } xf) // Items: Bags focused, then Bank left out (saved as <name>-only.png first)
            {
                await Task.Delay(2500);
                xf.Items.PlaceCommand.Execute(xf.Items.PlaceChips[2]);
                await Task.Delay(800);
                Save(window, Path.ChangeExtension(target, null) + "-only.png");
                xf.Items.PlaceCommand.Execute(xf.Items.PlaceChips[3]);
                xf.Items.PlaceCommand.Execute(xf.Items.PlaceChips[3]);
            }
            if (Part(2) == "updating") // the update banner mid-download
            {
                vm.Updates.BannerTitle = "Version 0.15.0 is available (you have 0.14.0)."; vm.Updates.ShowBanner = true;
                vm.Updates.InstallSteps.Clear();
                foreach (var s in Updater.Steps) vm.Updates.InstallSteps.Add(new UpdateStepRow(s == "Downloading" ? "Downloading version 0.15.0" : s));
                vm.Updates.IsInstalling = true;
                vm.Updates.ShowStep(new Updater.Step(0, 0.42, "29.6 of 70.4 MB"));
            }
            if (Part(3) == "posthighlight" && vm.SelectedFlavor?.SelectedCharacter?.Highlights.FirstOrDefault() is { } h)
            {
                await Task.Delay(3000);
                vm.PostHighlightCommand.Execute(h);
                await Task.Delay(3000);
            }
            if (Part(3) == "posttalents" && vm.SelectedFlavor?.SelectedCharacter is { } tc)
            {
                await Task.Delay(3000); // talent names
                vm.PostTalentsCommand.Execute(tc);
                await Task.Delay(4000);
            }
            if (Part(3) == "post" && vm.SelectedFlavor?.SelectedCharacter is { } c)
            {
                await Task.Delay(3000); // icons
                vm.PostCharacterCommand.Execute(c);
                await Task.Delay(4000);
            }
            if (Part(2) == "settings" && Part(3) == "themes") vm.Appearance.ShowAll = true; // the theme swatches unfolded
            if (Environment.GetEnvironmentVariable("CHARACTERTRACKER_SCREENSHOT_THEME") is { Length: > 0 } theme) vm.Appearance.PickCommand.Execute(theme);
            if (Part(2) == "settings") vm.ShowSettings = true;
            await Task.Delay(2500);
            if (Part(2) == "orb" && vm.FlavorTabs.FirstOrDefault(t => t != vm.SelectedFlavor) is { } other)
            {
                // The tab switch animation mid-flight (CHARACTERTRACKER_SCREENSHOT_ORBMS after the click).
                vm.SelectedFlavor = other;
                var ms = int.TryParse(Environment.GetEnvironmentVariable("CHARACTERTRACKER_SCREENSHOT_ORBMS"), out var v) ? v : 250;
                await Task.Delay(ms);
            }
            Save(window, target);
            if (Part(2) == "settings" && Find<Views.Header>(window)?.FindName("SettingsMenu") is System.Windows.Controls.Primitives.Popup { Child: FrameworkElement menu })
                Save(menu, Path.ChangeExtension(target, null) + "-settings.png");
            window.Close();
        };
    }

    static T? Find<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            if (Find<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    // A popup's content (it's its own window, so not in the window's picture).
    static void Save(FrameworkElement element, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(element);
        var bmp = new RenderTargetBitmap((int)(element.ActualWidth * dpi.DpiScaleX), (int)(element.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bmp.Render(element);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        png.Save(fs);
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
