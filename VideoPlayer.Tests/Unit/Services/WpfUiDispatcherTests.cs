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
}
