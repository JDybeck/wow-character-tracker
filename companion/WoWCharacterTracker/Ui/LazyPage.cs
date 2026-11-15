using System.Windows;
using System.Windows.Controls;

namespace WoWCharacterTracker.Ui;

// A page that's built the first time it's shown, then kept. Every page of a game tab used to be built
// at startup, hidden ones too, and their bindings and lists cost about 0.7 s before the window could
// answer. Set Visibility like the page's own (PageIs) and put the page in Page.
public sealed class LazyPage : ContentControl
{
    public DataTemplate? Page { get; set; }

    public LazyPage()
    {
        Focusable = false;
        IsVisibleChanged += (_, _) => Build();
        Loaded += (_, _) => Build();
    }

    void Build()
    {
        if (Content is null && IsVisible && Page is not null) Content = Page.LoadContent();
    }
}
