using System.Windows;

using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.Services;

public sealed class WpfDialogService : IDialogService
{
    public Task<ResumeChoice> AskResumeAsync(
        string fileName,
        TimeSpan lastPosition,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<ResumeChoice>(cancellationToken);
        }

        return RunOnUiThread(() =>
        {
            var result = MessageBox.Show(
                "是否继续播放 " + fileName + "？\n\n上次播放位置：" +
                lastPosition.ToString(@"hh\:mm\:ss"),
                "继续播放",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            return result switch
            {
                MessageBoxResult.Yes => ResumeChoice.Continue,
                MessageBoxResult.No => ResumeChoice.Restart,
                _ => ResumeChoice.Cancel
            };
        }, cancellationToken);
    }

    public Task ShowErrorAsync(
        string title,
        string message,
        CancellationToken cancellationToken = default)
        => ShowMessageAsync(title, message, MessageBoxImage.Error, cancellationToken);

    public Task ShowInformationAsync(
        string title,
        string message,
        CancellationToken cancellationToken = default)
        => ShowMessageAsync(title, message, MessageBoxImage.Information, cancellationToken);

    private static Task ShowMessageAsync(
        string title,
        string message,
        MessageBoxImage image,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        return RunOnUiThread(
            () =>
            {
                MessageBox.Show(message, title, MessageBoxButton.OK, image);
                return true;
            },
            cancellationToken);
    }

    private static Task<T> RunOnUiThread<T>(
        Func<T> action,
        CancellationToken cancellationToken)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return Task.FromResult(action());
        }

        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        completion.TrySetCanceled(cancellationToken);
                        return;
                    }

                    try
                    {
                        completion.TrySetResult(action());
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
                    }
                }));
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }

        return completion.Task;
    }
}
