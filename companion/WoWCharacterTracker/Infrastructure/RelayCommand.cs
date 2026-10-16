using System.Windows.Input;

namespace WoWCharacterTracker.Infrastructure;

sealed class RelayCommand(Func<object?, Task> run, Func<object?, bool>? canRun = null) : ICommand
{
    bool busy;

    public RelayCommand(Action<object?> run, Func<object?, bool>? canRun = null)
        : this(p => { run(p); return Task.CompletedTask; }, canRun) { }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !busy && (canRun?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        busy = true;
        CommandManager.InvalidateRequerySuggested();
        try { await run(parameter); }
        catch (Exception ex) { Log.Error(ex.Message); }
        finally
        {
            busy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
