using System.Windows.Threading;
using VideoPlayer.Interfaces;

namespace VideoPlayer.Services;

public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfUiDispatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public bool CheckAccess() => _dispatcher.CheckAccess();

    public void Post(Action action)
    {
        if (IsShuttingDown)
        {
            return;
        }

        try
        {
            _dispatcher.BeginInvoke(action);
        }
        catch (InvalidOperationException) when (IsShuttingDown)
        {
            // The dispatcher shut down between the state check and enqueue.
        }
    }

    public async Task InvokeAsync(Action action)
    {
        try
        {
            await _dispatcher.InvokeAsync(action).Task.ConfigureAwait(false);
        }
        catch (Exception exception) when (IsShutdownException(exception))
        {
            // Dispatcher shutdown cancels or aborts pending operations.
        }
    }

    public async Task<T> InvokeAsync<T>(Func<T> action)
    {
        try
        {
            return await _dispatcher.InvokeAsync(action).Task.ConfigureAwait(false);
        }
        catch (Exception exception) when (IsShutdownException(exception))
        {
            return default!;
        }
    }

    private bool IsShuttingDown =>
        _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished;

    private bool IsShutdownException(Exception exception)
    {
        return IsShuttingDown
            && exception is OperationCanceledException or InvalidOperationException;
    }
}
