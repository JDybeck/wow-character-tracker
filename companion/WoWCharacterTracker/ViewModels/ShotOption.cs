using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WoWCharacterTracker.ViewModels;

// The two screenshot checkboxes for one kind of event.
sealed class ShotOption(FlavorSettings settings, string kind, Action saved) : Observable
{
    public bool Take
    {
        get => settings.Shoots(kind);
        set
        {
            settings.Screenshot[kind] = value;
            if (value && !settings.PostScreenshot.ContainsKey(kind)) settings.PostScreenshot[kind] = true;
            saved();
            Raise(nameof(Take));
            Raise(nameof(Post));
        }
    }

    public bool Post
    {
        get => settings.PostsShot(kind);
        set
        {
            settings.PostScreenshot[kind] = value;
            saved();
            Raise(nameof(Post));
        }
    }

    public void Refresh()
    {
        Raise(nameof(Take));
        Raise(nameof(Post));
    }
}
