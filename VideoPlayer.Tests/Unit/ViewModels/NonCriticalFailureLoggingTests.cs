using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using VideoPlayer.Configuration;
using VideoPlayer.Enums;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;
using VideoPlayer.Models.Events;
using VideoPlayer.Services;
using VideoPlayer.Tests.Fakes;
using VideoPlayer.ViewModels;
using Xunit;

namespace VideoPlayer.Tests.Unit.ViewModels;

[CollectionDefinition("Diagnostic logging", DisableParallelization = true)]
public sealed class DiagnosticLoggingCollection;

[Collection("Diagnostic logging")]
public sealed class NonCriticalFailureLoggingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RuntimeSetting_WhenPlayerThrows_ReportsErrorAndLogsCause(bool changeRate)
    {
        using var logging = new CapturingLogScope();
        var fixture = new PlayerViewModelFixture(1);
        await using var viewModel = fixture.ViewModel;
        await viewModel.OpenAsync("a.mp4");
        var exception = new InvalidOperationException("Runtime setting failed.");
        if (changeRate)
        {
            fixture.Player.SetRateHandler = (_, _) => throw exception;
            viewModel.Rate = 1.5;
        }
        else
        {
            fixture.Player.SetVolumeHandler = (_, _) => throw exception;
            viewModel.Volume = 35;
        }

        Assert.Equal(changeRate ? "速率设置失败。" : "音量设置失败。", viewModel.LastError);
        Assert.Contains(logging.Events, entry => ReferenceEquals(entry.Exception, exception));
    }

    [Fact]
    public async Task ScreenshotAsync_WhenCaptureThrows_ReportsErrorAndLogsCause()
    {
        using var logging = new CapturingLogScope();
        var fixture = new PlayerViewModelFixture(1);
        await using var viewModel = fixture.ViewModel;
        var exception = new IOException("Screenshot failed.");
        fixture.Screenshot.CaptureHandler = (_, _) => throw exception;

        await viewModel.ScreenshotAsync();

        Assert.Equal("截图失败。", viewModel.LastError);
        Assert.False(viewModel.HasScreenshotMessage);
        Assert.Contains(logging.Events, entry => ReferenceEquals(entry.Exception, exception));
    }

    [Fact]
    public async Task TracksChanged_WhenDispatcherThrows_RemainsNonFatalAndLogsCause()
    {
        using var logging = new CapturingLogScope();
        var player = new FakePlayerService(1);
        var dispatcher = new FailingPostDispatcher();
        await using var viewModel = new PlayerViewModel(player, dispatcher);
        await viewModel.OpenAsync("a.mp4");
        var exception = new InvalidOperationException("Dispatcher unavailable.");
        dispatcher.PostException = exception;

        player.RaiseTracksChanged(new PlayerTrackInfo(PlayerTrackType.Audio, 1, "Audio", "und", true, false));

        Assert.Empty(viewModel.AudioTracks);
        Assert.Equal(string.Empty, viewModel.LastError);
        Assert.Contains(logging.Events, entry => ReferenceEquals(entry.Exception, exception));
    }

    [Theory]
    [InlineData("save")]
    [InlineData("flush")]
    [InlineData("complete")]
    public async Task HistoryFailureResult_RemainsNonFatalAndLogsCause(string operation)
    {
        using var logging = new CapturingLogScope();
        var fixture = new PlayerViewModelFixture(1);
        await using var viewModel = fixture.ViewModel;
        await viewModel.OpenAsync("a.mp4");
        var exception = new IOException("History persistence failed.");
        var failed = ServiceResult.Failure(
            ErrorCode.DatabaseUnavailable, "播放历史不可用。", "History write failed.", exception);
        switch (operation)
        {
            case "save":
                fixture.History.SaveProgressHandler = (_, _) => Task.FromResult(failed);
                await viewModel.PauseAsync();
                Assert.Equal(1, fixture.Player.PauseCallCount);
                break;
            case "flush":
                fixture.History.FlushHandler = _ => Task.FromResult(failed);
                await viewModel.PauseAsync();
                Assert.Equal(1, fixture.Player.PauseCallCount);
                break;
            case "complete":
                fixture.History.MarkCompletedHandler = (_, _, _, _) => Task.FromResult(failed);
                var result = await viewModel.HandleEndReachedAsync(new EndReachedEventArgs
                {
                    MediaSessionId = 1, FilePath = "a.mp4", OccurredAtUtc = DateTimeOffset.UtcNow
                });
                Assert.True(result.IsSuccess, result.TechnicalMessage);
                Assert.Equal("b.mp4", fixture.Player.LastRequest!.FilePath);
                break;
        }

        Assert.Contains(logging.Events, entry => ReferenceEquals(entry.Exception, exception));
    }

    [Fact]
    public async Task HistoryLoop_WhenSaveReturnsCancelledDuringPause_ExitsWithoutFailureLog()
    {
        using var logging = new CapturingLogScope();
        var fixture = new PlayerViewModelFixture(1);
        await using var viewModel = fixture.ViewModel;
        var saveStarted = new TaskCompletionSource<CancellationToken>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var saveCancelled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var exception = new OperationCanceledException("History save cancelled.");
        var saveCount = 0;
        fixture.History.SaveProgressHandler = async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref saveCount) != 1)
            {
                return ServiceResult.Success();
            }

            using var registration = cancellationToken.Register(() => saveCancelled.TrySetResult());
            saveStarted.TrySetResult(cancellationToken);
            // 真实历史服务会将调用方取消转换为失败结果，循环仍应按正常退出处理。
            await saveCancelled.Task;
            return ServiceResult.Failure(
                ErrorCode.Cancelled, "播放历史保存已取消。", "History save cancelled.", exception);
        };
        await viewModel.OpenAsync("a.mp4");
        fixture.Player.SnapshotOverride = fixture.Player.Snapshot with
        {
            Position = TimeSpan.FromSeconds(30)
        };
        var loopToken = await saveStarted.Task.WaitAsync(TimeSpan.FromSeconds(8));

        await viewModel.PauseAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(loopToken.IsCancellationRequested);
        Assert.Equal(1, fixture.Player.PauseCallCount);
        Assert.DoesNotContain(logging.Events, entry => ReferenceEquals(entry.Exception, exception));
    }

    [Fact]
    public async Task LoadAsync_WhenMainConfigurationIsInvalid_RecoversBackupAndLogsFailedPath()
    {
        using var logging = new CapturingLogScope();
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureCoreDirectories();
            await File.WriteAllTextAsync(paths.ConfigPath, "{ broken");
            var backup = AppSettings.CreateDefault();
            backup.Player.DefaultVolume = 21;
            await File.WriteAllTextAsync(paths.ConfigBackupPath, JsonSerializer.Serialize(backup));
            var settings = new SettingsService(paths);

            var result = await settings.LoadAsync();

            Assert.True(result.IsSuccess, result.TechnicalMessage);
            Assert.Equal(21, settings.Current.Player.DefaultVolume);
            Assert.Contains(logging.Events, entry => entry.Exception is JsonException
                && entry.Properties.TryGetValue("Path", out var path)
                && (path as ScalarValue)?.Value as string == paths.ConfigPath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private sealed class CapturingLogScope : IDisposable
    {
        private readonly ILogger _previous = Log.Logger;
        private readonly Logger _logger;
        private readonly CapturingLogSink _sink = new();
        public ConcurrentQueue<LogEvent> Events => _sink.Events;

        public CapturingLogScope()
        {
            _logger = new LoggerConfiguration().WriteTo.Sink(_sink).CreateLogger();
            Log.Logger = _logger;
        }

        public void Dispose()
        {
            Log.Logger = _previous;
            _logger.Dispose();
        }
    }

    private sealed class CapturingLogSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }

    private sealed class FailingPostDispatcher : IUiDispatcher
    {
        public Exception? PostException { get; set; }
        public bool CheckAccess() => true;
        public void Post(Action action)
        {
            if (PostException is not null) throw PostException;
            action();
        }

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }
}
