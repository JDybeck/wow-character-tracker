using System.Collections.ObjectModel;
using System.Windows.Input;

namespace WoWCharacterTracker.ViewModels;

// The Uninstall page (settings gear, Uninstall...): everything the app and the addon created, each with
// its paths and size and a checkbox, presets for the usual choices, and afterwards what was removed.
sealed class UninstallViewModel : Observable
{
    readonly MainViewModel main;

    public UninstallViewModel(MainViewModel main)
    {
        this.main = main;
        EverythingCommand = new RelayCommand(_ => Preset(_ => true));
        KeepDataCommand = new RelayCommand(_ => Preset(k => k is Uninstaller.Kind.App or Uninstaller.Kind.Addon));
        OnlyAppCommand = new RelayCommand(_ => Preset(k => k == Uninstaller.Kind.App));
        UninstallCommand = new RelayCommand(_ => Uninstall(), _ => !Done && Rows.Any(r => r.IsOn) && Blocked.Length == 0);
        CancelCommand = new RelayCommand(_ => main.Page = "characters");
        CloseCommand = new RelayCommand(_ => QuitRequested?.Invoke());
    }

    // The app should close now (after an uninstall), removing its exe if that was picked.
    public event Action? QuitRequested;

    public ObservableCollection<UninstallRow> Rows { get; } = [];
    public ObservableCollection<UninstallRow> Results { get; } = [];
    public ICommand EverythingCommand { get; }
    public ICommand KeepDataCommand { get; }
    public ICommand OnlyAppCommand { get; }
    public ICommand UninstallCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand CloseCommand { get; }

    bool done;
    public bool Done { get => done; private set { Set(ref done, value); Raise(nameof(NotDone)); } }
    public bool NotDone => !done;
    public bool RemovesApp { get; private set; }

    // Why it can't go ahead right now ("" when it can): the game rewrites its saved file when it exits,
    // so a running game would put back what was just removed.
    public string Blocked
    {
        get
        {
            var running = Rows.Where(r => r.IsOn && r.Part.GameDir is { } dir && Wow.IsRunning(dir))
                .Select(r => r.Part.Game!.Name).Distinct().ToList();
            return running.Count == 0 ? "" : $"Close {string.Join(" and ", running)} first: the game writes the addon's files again when it exits.";
        }
    }

    public string TotalText
    {
        get
        {
            var picked = Rows.Where(r => r.IsOn).ToList();
            return picked.Count == 0 ? "Nothing picked." : $"Removes {picked.Count} of {Rows.Count} ({Format.Size(picked.Sum(r => r.Part.Bytes))}).";
        }
    }

    // Lists what's there now; "Keep my data" is picked to start with.
    public void Refresh()
    {
        Done = false;
        Results.Clear();
        Rows.Clear();
        foreach (var part in Uninstaller.Find(main.FlavorTabs.Select(t => (t.Flavor, t.KnownFolder))))
            Rows.Add(new UninstallRow(part, Changed));
        Preset(k => k is Uninstaller.Kind.App or Uninstaller.Kind.Addon);
    }

    void Preset(Func<Uninstaller.Kind, bool> on)
    {
        foreach (var r in Rows) r.SetOn(on(r.Part.Kind));
        Changed();
    }

    void Changed()
    {
        Raise(nameof(TotalText));
        Raise(nameof(Blocked));
        CommandManager.InvalidateRequerySuggested();
    }

    void Uninstall()
    {
        Changed(); // the game may have started since the last pick
        if (Blocked.Length > 0) return;
        var picked = Rows.Where(r => r.IsOn).Select(r => r.Part).ToList();
        main.Shutdown(); // no more syncs writing state or caches while files go
        Log.Info("Uninstalling: " + string.Join(", ", picked.Select(p => p.Title)) + ".");
        foreach (var (part, problem) in Uninstaller.Remove(picked))
        {
            Results.Add(new UninstallRow(part, null) { Problem = problem });
            if (problem is null) Log.Success($"Removed: {part.Title}.");
            else Log.Error($"Couldn't remove {part.Title}: {problem}");
        }
        RemovesApp = picked.Any(p => p.Kind == Uninstaller.Kind.App);
        Raise(nameof(RemovesApp));
        Done = true;
    }
}

// One part on the Uninstall page: what it is, where (every path), how big, and whether it's picked.
sealed class UninstallRow(Uninstaller.Part part, Action? changed) : Observable
{
    public Uninstaller.Part Part { get; } = part;
    public string Title => Part.Title;
    public string Detail => Part.Detail;
    public string PathsText => string.Join("\n", Part.Paths);
    public string SizeText => Format.Size(Part.Bytes);

    bool isOn;
    public bool IsOn { get => isOn; set { if (Set(ref isOn, value)) changed?.Invoke(); } }
    public void SetOn(bool on) { isOn = on; Raise(nameof(IsOn)); }

    // After removing: null when it went, else why it couldn't.
    public string? Problem { get; init; }
    public bool Failed => Problem is not null;
}
