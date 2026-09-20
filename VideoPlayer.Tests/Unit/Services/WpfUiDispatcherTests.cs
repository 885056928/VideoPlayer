using System.Windows.Threading;
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Unit.Services;

public sealed class WpfUiDispatcherTests
{
    [StaFact]
    public async Task ShutdownDispatcher_DropsPostAndCompletesInvokeTasksSafely()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var subject = new WpfUiDispatcher(dispatcher);
        var postExecuted = false;
        var actionExecuted = false;

        dispatcher.InvokeShutdown();

        var exception = Record.Exception(
            () => subject.Post(() => postExecuted = true));
        Assert.Null(exception);

        await subject.InvokeAsync(() => actionExecuted = true);
        var result = await subject.InvokeAsync(() => 42);

        Assert.False(postExecuted);
        Assert.False(actionExecuted);
        Assert.Equal(default, result);
    }

    [StaFact]
    public void ActiveDispatcher_ExecutesPostAndInvokeOperations()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var subject = new WpfUiDispatcher(dispatcher);
        var postExecuted = false;
        var actionExecuted = false;
        var frame = new DispatcherFrame();

        subject.Post(() => postExecuted = true);
        var invokeTask = subject.InvokeAsync(() => actionExecuted = true);
        var resultTask = subject.InvokeAsync(() => 42);
        _ = dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);

        Assert.True(subject.CheckAccess());
        Assert.True(postExecuted);
        Assert.True(actionExecuted);
        Assert.True(invokeTask.IsCompletedSuccessfully);
        Assert.Equal(42, resultTask.GetAwaiter().GetResult());
    }
}