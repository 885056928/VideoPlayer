using Microsoft.Extensions.DependencyInjection;

namespace VideoPlayer.Services;

internal static class ServiceProviderShutdown
{
    public static async Task<bool> DisposeAsync(
        ServiceProvider services,
        TimeSpan timeout,
        Action<Exception>? onError = null)
    {
        Task disposeTask;
        try
        {
            disposeTask = services.DisposeAsync().AsTask();
        }
        catch (Exception exception)
        {
            onError?.Invoke(exception);
            return true;
        }

        var completed = await Task.WhenAny(
            disposeTask,
            Task.Delay(timeout)).ConfigureAwait(false);
        if (completed != disposeTask)
        {
            _ = ObserveAsync(disposeTask, onError);
            return false;
        }

        try
        {
            await disposeTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            onError?.Invoke(exception);
        }

        return true;
    }

    private static async Task ObserveAsync(Task disposeTask, Action<Exception>? onError)
    {
        try
        {
            await disposeTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            onError?.Invoke(exception);
        }
    }
}
