using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;

namespace WoWCharacterTracker.ViewModels;

// New versions: the "Check for updates" button, the banner with the release notes, installing an
// update, and what's new after one.
sealed class UpdatesViewModel : Observable
{
    // justUpdated: started by the updater (--updated), so the banner shows what's new instead.
    public UpdatesViewModel(bool justUpdated)
    {
        this.justUpdated = justUpdated;
        CheckUpdatesCommand = new RelayCommand(_ => CheckAsync(manual: true));
        InstallUpdateCommand = new RelayCommand(_ => InstallAsync(), _ => update is not null && !IsInstalling);
        ToggleNotesCommand = new RelayCommand(_ => ShowNotes = !ShowNotes);
        CloseBannerCommand = new RelayCommand(_ => ShowBanner = false);
    }

    readonly bool justUpdated;
    Updater.Release? update;

    // The new version has started and waits for this one to exit.
    public event Action? UpdateStarted;

    public string VersionText => $"v{Updater.Current}";

    string bannerTitle = "";
    public string BannerTitle { get => bannerTitle; set => Set(ref bannerTitle, value); }
    string bannerNotes = "";
    public string BannerNotes { get => bannerNotes; set => Set(ref bannerNotes, value); }
    bool showBanner;
    public bool ShowBanner { get => showBanner; set => Set(ref showBanner, value); }
    bool showNotes;
    public bool ShowNotes { get => showNotes; set => Set(ref showNotes, value); }
    public bool HasUpdate => update is not null;

    // What the update button says: "Check for updates", "Checking...", "Up to date", "v0.11.0 ready".
    string updateButtonText = "Check for updates";
    public string UpdateButtonText { get => updateButtonText; set => Set(ref updateButtonText, value); }

    public ICommand CheckUpdatesCommand { get; }
    public ICommand InstallUpdateCommand { get; }
    public ICommand ToggleNotesCommand { get; }
    public ICommand CloseBannerCommand { get; }

    // On start: what's new after an update, or a quiet check. The app can sit in the tray for
    // days, so it looks again every 6 hours.
    public async Task StartAsync()
    {
        if (justUpdated) await ShowWhatsNewAsync();
        else await CheckAsync(manual: false);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        timer.Tick += async (_, _) => { if (update is null) await CheckAsync(manual: false); };
        timer.Start();
    }

    // Quietly on start and every few hours after; the button shows the answer too.
    async Task CheckAsync(bool manual)
    {
        if (manual) UpdateButtonText = "Checking...";
        var latest = await Updater.LatestAsync();
        if (latest is null)
        {
            if (manual) Log.Warn("Couldn't reach GitHub to check for updates.");
            UpdateButtonText = manual ? "Couldn't check" : "Check for updates";
            return;
        }
        if (latest.Version <= Updater.Current)
        {
            if (manual) Log.Info($"You have the latest version ({VersionText}).");
            UpdateButtonText = "Up to date";
            return;
        }
        UpdateButtonText = $"v{latest.Version} ready";
        update = latest;
        BannerTitle = $"Version {latest.Version} is available (you have {Updater.Current}).";
        BannerNotes = latest.Notes.Trim();
        ShowNotes = false;
        ShowBanner = true;
        Raise(nameof(HasUpdate));
        CommandManager.InvalidateRequerySuggested(); // Update now is clickable right away
        Log.Info($"Update available: {latest.Version}.");
    }

    // --- installing, step by step in the banner ---

    public ObservableCollection<UpdateStepRow> InstallSteps { get; } = [];
    bool isInstalling;
    public bool IsInstalling { get => isInstalling; set => Set(ref isInstalling, value); }
    double downloadPercent;
    public double DownloadPercent { get => downloadPercent; set => Set(ref downloadPercent, value); }
    string downloadText = "";
    public string DownloadText { get => downloadText; set => Set(ref downloadText, value); }

    async Task InstallAsync()
    {
        if (update is null || IsInstalling) return;
        Log.Info($"Updating to version {update.Version}...");
        InstallSteps.Clear();
        foreach (var s in Updater.Steps) InstallSteps.Add(new UpdateStepRow(s == "Downloading" ? $"Downloading version {update.Version}" : s));
        IsInstalling = true;
        ShowNotes = false;

        var problem = await Updater.InstallAsync(update, new Progress<Updater.Step>(ShowStep));
        if (problem is null)
        {
            UpdateStarted?.Invoke();
            return;
        }
        if (InstallSteps.FirstOrDefault(s => s.State == "active") is { } failed) failed.State = "failed";
        InstallSteps.Add(new UpdateStepRow(problem) { State = "note" });
        Log.Error(problem);
        Browser.Open(update.PageUrl);
    }

    public void ShowStep(Updater.Step step)
    {
        for (var i = 0; i < InstallSteps.Count; i++)
            InstallSteps[i].State = i < step.Index ? "done" : i == step.Index ? "active" : "waiting";
        if (step.Index == 0)
        {
            DownloadPercent = (step.Fraction ?? 0) * 100;
            DownloadText = step.Detail;
        }
        else DownloadPercent = 100;
    }

    // After an update: what's new in this version.
    async Task ShowWhatsNewAsync()
    {
        Log.Success($"Updated to {VersionText}.");
        if (await Updater.ForVersionAsync(Updater.Current) is not { } release) return;
        BannerTitle = $"Updated to {VersionText}. Here's what's new:";
        BannerNotes = release.Notes.Trim();
        ShowNotes = true;
        ShowBanner = true;
    }
}

// One step of an app update: waiting, active, done or failed (and "note" for the reason it failed).
sealed class UpdateStepRow(string text) : Observable
{
    public string Text { get; } = text;
    string state = "waiting";
    public string State { get => state; set => Set(ref state, value); }
}
