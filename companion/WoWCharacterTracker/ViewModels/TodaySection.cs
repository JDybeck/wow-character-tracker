using System.Windows.Input;

namespace WoWCharacterTracker.ViewModels;

// One of Home's TODAY rows: the first TodayStats.Shown numbers always, the rest after "more"
// (kept open for the session).
sealed class TodaySection(string title) : Observable
{
    public string Title { get; } = title;

    IReadOnlyList<TodayCount> all = [];
    bool open;

    public IReadOnlyList<TodayCount> Items => open ? all : [.. all.Take(TodayStats.Shown)];
    public bool HasMore => all.Count > TodayStats.Shown;
    public string ToggleText => open ? "Less" : $"+{all.Count - TodayStats.Shown} more";

    public ICommand ToggleCommand => new RelayCommand(_ =>
    {
        open = !open;
        Refresh();
    });

    public void Show(IReadOnlyList<TodayCount> counts)
    {
        all = counts;
        Refresh();
    }

    void Refresh()
    {
        Raise(nameof(Items));
        Raise(nameof(HasMore));
        Raise(nameof(ToggleText));
    }
}
