using System.Reflection;
using VideoPlayer.Enums;
using VideoPlayer.Models;
using VideoPlayer.Tests.Fakes;
using VideoPlayer.ViewModels;
using Xunit;

namespace VideoPlayer.Tests.Unit.ViewModels;

public sealed class PlayerViewModelHistoryLoopTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task HistoryLoop_AfterResume_ContinuesPeriodicSaving(bool useToggle, bool stopBeforeResuming)
    {
        var fixture = new PlayerViewModelFixture(1);
        await using var viewModel = fixture.ViewModel;
        await viewModel.OpenAsync("a.mp4");
        if (stopBeforeResuming)
        {
            await viewModel.StopAsync();
        }
        else
        {
            await viewModel.PauseAsync();
        }

        var idleState = stopBeforeResuming ? PlaybackState.Stopped : PlaybackState.Paused;
        fixture.Player.SnapshotOverride = fixture.Player.Snapshot with { State = idleState };
        fixture.Player.RaiseStateChanged(PlaybackState.Playing, idleState);
        var progressSaved = new TaskCompletionSource<CancellationToken>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.History.SaveProgressHandler = (_, cancellationToken) =>
        {
            progressSaved.TrySetResult(cancellationToken);
            return Task.FromResult(ServiceResult.Success());
        };

        if (useToggle)
        {
            await viewModel.TogglePlaybackAsync();
        }
        else
        {
            await viewModel.PlayAsync();
        }

        fixture.Player.SnapshotOverride = fixture.Player.Snapshot with
        {
            State = PlaybackState.Playing,
            Position = TimeSpan.FromSeconds(30)
        };
        var runningLoopToken = await progressSaved.Task.WaitAsync(TimeSpan.FromSeconds(8));
        await viewModel.PlayAsync();
        Assert.False(runningLoopToken.IsCancellationRequested);
        Assert.Equal(2, fixture.Player.PlayCallCount);
    }

    [Fact]
    public async Task HistoryLoop_WhenCancelledWriteCompletesLate_StillSavesNewSessionPosition()
    {
        var fixture = new PlayerViewModelFixture(1);
        await using var viewModel = fixture.ViewModel;
        var oldWriteStarted = NewCompletionSource();
        var releaseOldWrite = NewCompletionSource();
        var newProgressSaved = new TaskCompletionSource<PlaybackSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var saveCount = 0;
        fixture.History.SaveProgressHandler = async (snapshot, _) =>
        {
            if (Interlocked.Increment(ref saveCount) == 1)
            {
                oldWriteStarted.TrySetResult();
                // 模拟已开始的数据库写入未及时响应取消，随后才返回成功。
                await releaseOldWrite.Task;
            }

            if (snapshot.MediaSessionId == 2)
            {
                newProgressSaved.TrySetResult(snapshot);
            }

            return ServiceResult.Success();
        };

        Task? oldLoop = null;
        try
        {
            await viewModel.OpenAsync("a.mp4");
            fixture.Player.SnapshotOverride = fixture.Player.Snapshot with
            {
                Position = TimeSpan.FromSeconds(30)
            };
            // 只取得旧任务用于等待真实退出，断言仍针对新会话的实际保存行为。
            oldLoop = (Task)typeof(PlayerViewModel).GetField(
                "_historyTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewModel)!;
            await oldWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(8));
            fixture.Player.OpenHandler = (request, _) =>
            {
                fixture.Player.SnapshotOverride = fixture.Player.Snapshot with
                {
                    MediaSessionId = 2,
                    MediaPath = request.FilePath
                };
                return Task.FromResult(ServiceResult<MediaSessionInfo>.Success(new MediaSessionInfo(
                    2, request.FilePath, request.AutoPlay, request.StartPosition, DateTimeOffset.UtcNow)));
            };

            Assert.True((await viewModel.OpenAsync("b.mp4").WaitAsync(TimeSpan.FromSeconds(6))).IsSuccess);
            releaseOldWrite.TrySetResult();
            await oldLoop.WaitAsync(TimeSpan.FromSeconds(2));
            var saved = await newProgressSaved.Task.WaitAsync(TimeSpan.FromSeconds(8));

            Assert.Equal(2, saved.MediaSessionId);
            Assert.Equal("b.mp4", saved.MediaPath);
            Assert.Equal(TimeSpan.FromSeconds(30), saved.Position);
        }
        finally
        {
            releaseOldWrite.TrySetResult();
            if (oldLoop is not null) await oldLoop.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task HistoryLoop_WhenUiContextIsBusy_SavesProgressWithoutUiContinuation()
    {
        var fixture = new PlayerViewModelFixture(1);
        await using var viewModel = fixture.ViewModel;
        var progressSaved = NewCompletionSource();
        var uiContext = new HoldingSynchronizationContext();
        fixture.History.SaveProgressHandler = (_, _) =>
        {
            progressSaved.TrySetResult();
            return Task.FromResult(ServiceResult.Success());
        };

        try
        {
            var previousContext = SynchronizationContext.Current;
            Task<ServiceResult> opening;
            try
            {
                SynchronizationContext.SetSynchronizationContext(uiContext);
                opening = viewModel.OpenAsync("a.mp4");
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }

            Assert.True((await opening.WaitAsync(TimeSpan.FromSeconds(2))).IsSuccess);
            fixture.Player.SnapshotOverride = fixture.Player.Snapshot with
            {
                Position = TimeSpan.FromSeconds(30)
            };

            await progressSaved.Task.WaitAsync(TimeSpan.FromSeconds(8));
        }
        finally
        {
            uiContext.Release();
        }
    }

    private static TaskCompletionSource NewCompletionSource()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class HoldingSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _pending = [];
        private bool _released;

        public override void Post(SendOrPostCallback callback, object? state)
        {
            lock (_pending)
            {
                if (!_released)
                {
                    _pending.Enqueue((callback, state));
                    return;
                }
            }

            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }

        public void Release()
        {
            (SendOrPostCallback Callback, object? State)[] pending;
            lock (_pending)
            {
                _released = true;
                pending = _pending.ToArray();
                _pending.Clear();
            }

            foreach (var (callback, state) in pending)
            {
                ThreadPool.QueueUserWorkItem(_ => callback(state));
            }
        }
    }
}
