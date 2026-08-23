using System.Windows.Input;

namespace Kanstraction.ViewModels.Commands;

public sealed class AsyncCommand : ICommand
{
    private readonly Func<object?, CancellationToken, Task> _execute;
    private readonly Predicate<object?>? _canExecute;
    private CancellationTokenSource? _execution;
    private bool _isExecuting;

    public AsyncCommand(
        Func<object?, CancellationToken, Task> execute,
        Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        !_isExecuting && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _execution?.Cancel();
        _execution?.Dispose();
        _execution = new CancellationTokenSource();
        _isExecuting = true;
        NotifyCanExecuteChanged();

        try
        {
            await _execute(parameter, _execution.Token);
        }
        catch (OperationCanceledException) when (_execution.IsCancellationRequested)
        {
        }
        finally
        {
            _isExecuting = false;
            NotifyCanExecuteChanged();
        }
    }

    public void Cancel() => _execution?.Cancel();

    public void NotifyCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
