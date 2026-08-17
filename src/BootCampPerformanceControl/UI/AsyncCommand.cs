using System.Windows.Input;

namespace BootCampPerformanceControl.UI;

public sealed class AsyncCommand : ICommand
{
    private readonly Func<CancellationToken, Task> _executeAsync;
    private readonly Func<bool>? _canExecute;
    private readonly Action<OperationCanceledException>? _onCanceled;
    private readonly Action<Exception>? _onException;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isExecuting;

    public AsyncCommand(
        Func<CancellationToken, Task> executeAsync,
        Func<bool>? canExecute = null,
        Action<OperationCanceledException>? onCanceled = null,
        Action<Exception>? onException = null)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
        _onCanceled = onCanceled;
        _onException = onException;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return !_isExecuting && (_canExecute?.Invoke() ?? true);
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isExecuting = true;
        _cancellationTokenSource = new CancellationTokenSource();
        RaiseCanExecuteChanged();

        try
        {
            await _executeAsync(_cancellationTokenSource.Token);
        }
        catch (OperationCanceledException exception)
        {
            _onCanceled?.Invoke(exception);
        }
        catch (Exception exception)
        {
            if (_onException is null)
            {
                throw;
            }

            _onException(exception);
        }
        finally
        {
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    private void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
