using VideoPlayer.Enums;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;
using VideoPlayer.Models.Events;
using VideoPlayer.Tests.Fakes;
using VideoPlayer.ViewModels;
using Xunit;

namespace VideoPlayer.Tests.Unit.ViewModels;

public sealed class PlayerViewModelEndReachedConcurrencyTests
{
    [Fact]
    public async Task HandleEndReachedAsync_WhenManualOpenPrecedesQueuedNavigation_DoesNotAdvancePlaylist()
    {
        var dispatcher = new HoldingUiDispatcher();
        var player = new FakePlayerService(1);
        var playlist = new FakePlaylistService();
        await using var viewModel = new PlayerViewModel(
            player, dispatcher, playlist, new FakeSettingsService(), new FakeDialogService(),
            historyService: new FakeHistoryService());
        await viewModel.OpenAsync("a.mp4");
        Action? queuedNavigation = null;

        try
        {
            // 让自动导航留在 UI 队列中，先执行用户的打开请求，再恢复旧回调。
            dispatcher.HasAccess = false;
            var ended = viewModel.HandleEndReachedAsync(new EndReachedEventArgs
            {
                MediaSessionId = 1,
                FilePath = "a.mp4",
                OccurredAtUtc = DateTimeOffset.UtcNow
            });
            queuedNavigation = await dispatcher.QueuedInvocation.Task.WaitAsync(TimeSpan.FromSeconds(2));

            dispatcher.HasAccess = true;
            var manualOpen = await viewModel.OpenAsync("a.mp4");
            queuedNavigation();
            queuedNavigation = null;
            var endedResult = await ended.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.True(manualOpen.IsSuccess, manualOpen.TechnicalMessage);
            Assert.Equal(ErrorCode.Superseded, endedResult.ErrorCode);
            Assert.Equal(2, player.OpenCount);
            Assert.Equal("a.mp4", player.LastRequest!.FilePath);
            Assert.Equal("a.mp4", playlist.Current!.FilePath);
        }
        finally
        {
            dispatcher.HasAccess = true;
            queuedNavigation?.Invoke();
        }
    }

    [Fact]
    public async Task HandleEndReachedAsync_AfterManualOpenFails_StillAdvancesActiveMedia()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 1);
        await using var viewModel = fixture.ViewModel;
        await viewModel.OpenAsync("a.mp4");
        fixture.Player.OpenHandler = (request, _) => Task.FromResult(
            request.FilePath == "missing.mp4"
                ? ServiceResult<MediaSessionInfo>.Failure(
                    ErrorCode.FileNotFound, "文件不存在。", "File not found.")
                : ServiceResult<MediaSessionInfo>.Success(new MediaSessionInfo(
                    2, request.FilePath, request.AutoPlay, request.StartPosition, DateTimeOffset.UtcNow)));

        var failedOpen = await viewModel.OpenAsync("missing.mp4");
        var ended = await viewModel.HandleEndReachedAsync(new EndReachedEventArgs
        {
            MediaSessionId = 1,
            FilePath = "a.mp4",
            OccurredAtUtc = DateTimeOffset.UtcNow
        });

        Assert.False(failedOpen.IsSuccess);
        Assert.True(ended.IsSuccess, ended.TechnicalMessage);
        Assert.Equal(3, fixture.Player.OpenCount);
        Assert.Equal("b.mp4", fixture.Player.LastRequest!.FilePath);
        Assert.Equal("b.mp4", fixture.Playlist.Current!.FilePath);
    }

    [Fact]
    public async Task HandleEndReachedAsync_WhenActiveMediaOpenIsFinishing_StillAdvancesPlaylist()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 1);
        await using var viewModel = fixture.ViewModel;
        var playbackStartRecorded = NewCompletionSource();
        var releasePlaybackStart = NewCompletionSource();
        var startCount = 0;
        fixture.History.StartPlaybackHandler = async cancellationToken =>
        {
            if (Interlocked.Increment(ref startCount) == 1)
            {
                // 媒体已经成为活动会话，但打开流程尚在等待历史记录落库。
                playbackStartRecorded.TrySetResult();
                await releasePlaybackStart.Task.WaitAsync(cancellationToken);
            }

            return ServiceResult.Success();
        };

        try
        {
            var opening = viewModel.OpenAsync("a.mp4");
            await playbackStartRecorded.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var ended = viewModel.HandleEndReachedAsync(new EndReachedEventArgs
            {
                MediaSessionId = 1,
                FilePath = "a.mp4",
                OccurredAtUtc = DateTimeOffset.UtcNow
            });
            releasePlaybackStart.TrySetResult();
            var endedResult = await ended.WaitAsync(TimeSpan.FromSeconds(2));
            await opening.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.True(endedResult.IsSuccess, endedResult.TechnicalMessage);
            Assert.Equal(2, fixture.Player.OpenCount);
            Assert.Equal("b.mp4", fixture.Player.LastRequest!.FilePath);
            Assert.Equal("b.mp4", fixture.Playlist.Current!.FilePath);
        }
        finally
        {
            releasePlaybackStart.TrySetResult();
        }
    }

    [Fact]
    public async Task HandleEndReachedAsync_WhenManualOpenIsAlreadyPending_DoesNotAdvancePlaylist()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 1);
        await using var viewModel = fixture.ViewModel;
        await viewModel.OpenAsync("a.mp4");
        var originalSnapshot = fixture.Player.Snapshot;
        var manualOpenStarted = NewCompletionSource();
        var releaseManualOpen = NewCompletionSource();
        fixture.Player.OpenHandler = async (request, cancellationToken) =>
        {
            if (request.FilePath == "a.mp4")
            {
                manualOpenStarted.TrySetResult();
                await releaseManualOpen.Task.WaitAsync(cancellationToken);
            }

            fixture.Player.SnapshotOverride = originalSnapshot with
            {
                MediaSessionId = 2,
                MediaPath = request.FilePath
            };
            return ServiceResult<MediaSessionInfo>.Success(new MediaSessionInfo(
                2, request.FilePath, request.AutoPlay, request.StartPosition, DateTimeOffset.UtcNow));
        };

        try
        {
            var manualOpen = viewModel.OpenAsync("a.mp4");
            await manualOpenStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var endedResult = await viewModel.HandleEndReachedAsync(new EndReachedEventArgs
            {
                MediaSessionId = 1,
                FilePath = "a.mp4",
                OccurredAtUtc = DateTimeOffset.UtcNow
            });
            releaseManualOpen.TrySetResult();
            var manualResult = await manualOpen.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(ErrorCode.Superseded, endedResult.ErrorCode);
            Assert.True(manualResult.IsSuccess, manualResult.TechnicalMessage);
            Assert.Equal(2, fixture.Player.OpenCount);
            Assert.Equal("a.mp4", fixture.Player.LastRequest!.FilePath);
            Assert.Equal("a.mp4", fixture.Playlist.Current!.FilePath);
        }
        finally
        {
            releaseManualOpen.TrySetResult();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task HandleEndReachedAsync_WhenManualOpenArrivesDuringPersistence_DoesNotAdvancePlaylist(
        bool waitForCompleted,
        bool keepManualOpenPending)
    {
        var fixture = new PlayerViewModelFixture(sessionId: 1);
        await using var viewModel = fixture.ViewModel;
        await viewModel.OpenAsync("a.mp4");
        var originalSnapshot = fixture.Player.Snapshot;
        var persistenceStarted = NewCompletionSource();
        var releasePersistence = NewCompletionSource();
        var manualOpenStarted = NewCompletionSource();
        var releaseManualOpen = NewCompletionSource();
        var saveCount = 0;
        var sessionId = 1L;

        // 用真实完成信号控制两个数据库等待点，避免依赖延时碰撞来复现竞态。
        fixture.History.SaveProgressHandler = async (_, cancellationToken) =>
        {
            if (!waitForCompleted && Interlocked.Increment(ref saveCount) == 1)
            {
                persistenceStarted.TrySetResult();
                await releasePersistence.Task.WaitAsync(cancellationToken);
            }

            return ServiceResult.Success();
        };
        fixture.History.MarkCompletedHandler = async (_, _, _, cancellationToken) =>
        {
            if (waitForCompleted)
            {
                persistenceStarted.TrySetResult();
                await releasePersistence.Task.WaitAsync(cancellationToken);
            }

            return ServiceResult.Success();
        };
        fixture.Player.OpenHandler = async (request, cancellationToken) =>
        {
            if (request.FilePath == "a.mp4")
            {
                manualOpenStarted.TrySetResult();
                if (keepManualOpenPending)
                {
                    await releaseManualOpen.Task.WaitAsync(cancellationToken);
                }
            }

            var openedSessionId = Interlocked.Increment(ref sessionId);
            fixture.Player.SnapshotOverride = originalSnapshot with
            {
                MediaSessionId = openedSessionId,
                MediaPath = request.FilePath
            };
            return ServiceResult<MediaSessionInfo>.Success(new MediaSessionInfo(
                openedSessionId,
                request.FilePath,
                request.AutoPlay,
                request.StartPosition,
                DateTimeOffset.UtcNow));
        };

        try
        {
            var ended = viewModel.HandleEndReachedAsync(new EndReachedEventArgs
            {
                MediaSessionId = 1,
                FilePath = "a.mp4",
                OccurredAtUtc = DateTimeOffset.UtcNow
            });
            await persistenceStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var manualOpen = viewModel.OpenAsync("a.mp4");
            await manualOpenStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (!keepManualOpenPending)
            {
                await manualOpen;
            }

            releasePersistence.TrySetResult();
            var endedResult = await ended.WaitAsync(TimeSpan.FromSeconds(2));
            releaseManualOpen.TrySetResult();
            var manualResult = await manualOpen.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(ErrorCode.Superseded, endedResult.ErrorCode);
            Assert.True(manualResult.IsSuccess, manualResult.TechnicalMessage);
            Assert.Equal(2, fixture.Player.OpenCount);
            Assert.Equal("a.mp4", fixture.Player.LastRequest!.FilePath);
            Assert.Equal("a.mp4", fixture.Playlist.Current!.FilePath);
        }
        finally
        {
            releasePersistence.TrySetResult();
            releaseManualOpen.TrySetResult();
        }
    }

    private static TaskCompletionSource NewCompletionSource()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class HoldingUiDispatcher : IUiDispatcher
    {
        // 只挂起待测回调的上下文，避免已经启动的进度循环占用唯一的调度完成信号。
        private readonly AsyncLocal<bool?> _hasAccess = new();

        public bool HasAccess
        {
            get => _hasAccess.Value ?? true;
            set => _hasAccess.Value = value;
        }

        public TaskCompletionSource<Action> QueuedInvocation { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CheckAccess() => HasAccess;

        public void Post(Action action) => action();

        public Task InvokeAsync(Action action)
        {
            if (HasAccess)
            {
                action();
                return Task.CompletedTask;
            }

            var completion = NewCompletionSource();
            QueuedInvocation.TrySetResult(() =>
            {
                action();
                completion.TrySetResult();
            });
            return completion.Task;
        }

        public async Task<T> InvokeAsync<T>(Func<T> action)
        {
            T result = default!;
            await InvokeAsync(() => { result = action(); });
            return result;
        }
    }
}
