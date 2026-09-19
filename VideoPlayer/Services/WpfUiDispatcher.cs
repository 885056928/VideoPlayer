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

    public void Post(Action action) => _dispatcher.BeginInvoke(action);

    public Task InvokeAsync(Action action) => _dispatcher.InvokeAsync(action).Task;

    public Task<T> InvokeAsync<T>(Func<T> action) => _dispatcher.InvokeAsync(action).Task;
}
