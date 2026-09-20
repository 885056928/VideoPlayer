using System.Diagnostics;
using VideoPlayer.Enums;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;
using VideoPlayer.Models.Events;
using VideoPlayer.ViewModels;
using Xunit;

namespace VideoPlayer.Tests.Unit.ViewModels;

public sealed class PlayerViewModelTests
{
    [Fact]
    public async Task OpenAsync_ForwardsRequestAndRequestsAutoPlay()
    {
        var fake = new FakePlayerService();
        await using var viewModel = CreateViewModel(fake);

        await viewModel.OpenAsync(@"C:\视频\movie.mp4");

        Assert.Equal(@"C:\视频\movie.mp4", fake.LastRequest!.FilePath);
        Assert.True(fake.LastRequest.AutoPlay);
    }

    [Fact]
    public async Task OpenAsync_AppliesInitialVolumeAndRateAfterSuccess()
    {
        var fake = new FakePlayerService();
        await using var viewModel = CreateViewModel(fake);
        viewModel.Volume = 65;
        viewModel.Rate = 1.5;
        await WaitUntilAsync(() => fake.LastVolume == 65 && fake.LastRate == 1.5);
        fake.ResetMutationTracking();

        await viewModel.OpenAsync(@"C:\视频\movie.mp4");

        Assert.Equal(65, fake.LastVolume);
        Assert.Equal(1.5, fake.LastRate);
        Assert.True(fake.SetVolumeCallCount >= 1);
        Assert.True(fake.SetRateCallCount >= 1);
    }

    [Fact]
    public async Task SeekAsync_ForwardsPendingSliderPosition()
    {
        var fake = new FakePlayerService();
        await using var viewModel = CreateViewModel(fake);
        viewModel.Duration = TimeSpan.FromMinutes(2);
        viewModel.BeginSeek();
        viewModel.PendingSeekPosition = 42.5;

        await viewModel.CommitSeekAsync();

        Assert.Equal(TimeSpan.FromSeconds(42.5), fake.LastSeekPosition);
    }

    [Theory]
    [InlineData(PlaybackState.Playing)]
    [InlineData(PlaybackState.Paused)]
    public async Task ClickSeek_CommitsClickedPositionAndPreservesPlaybackState(
        PlaybackState playbackState)
    {
        var fake = new FakePlayerService();
        await using var viewModel = CreateViewModel(fake);
        viewModel.Duration = TimeSpan.FromMinutes(2);
        viewModel.PlaybackState = playbackState;
        viewModel.BeginSeek(TimeSpan.FromSeconds(75.5));

        await viewModel.CommitSeekAsync();

        Assert.Equal(75.5, fake.LastSeekPosition!.Value.TotalSeconds);
        Assert.Equal(playbackState, viewModel.PlaybackState);
        Assert.Equal(0, fake.PlayCallCount);
        Assert.Equal(0, fake.PauseCallCount);
    }

    [Fact]
    public async Task PlayAsync_WhenServiceFails_SetsLastError()
    {
        var fake = new FakePlayerService
        {
            PlayResult = ServiceResult.Failure(
                ErrorCode.InvalidState,
                "没有视频。",
                "No media")
        };
        await using var viewModel = CreateViewModel(fake);

        await viewModel.PlayAsync();

        Assert.Equal("没有视频。", viewModel.LastError);
    }

    [Fact]
    public async Task VolumeChange_ForwardsToService()
    {
        var fake = new FakePlayerService();
        await using var viewModel = CreateViewModel(fake);

        viewModel.Volume = 73;
        await WaitUntilAsync(() => fake.LastVolume == 73);

        Assert.Equal(73, fake.LastVolume);
    }

    [Fact]
    public async Task RateChange_ForwardsToService()
    {
        var fake = new FakePlayerService();
        await using var viewModel = CreateViewModel(fake);

        viewModel.Rate = 1.75;
        await WaitUntilAsync(() => fake.LastRate == 1.75);

        Assert.Equal(1.75, fake.LastRate);
    }

    [Fact]
    public async Task VolumeFailure_SetsLastError()
    {
        var fake = new FakePlayerService
        {
            VolumeResult = ServiceResult.Failure(
                ErrorCode.InvalidState,
                "音量设置失败。",
                "Volume failed")
        };
        await using var viewModel = CreateViewModel(fake);

        viewModel.Volume = 55;
        await WaitUntilAsync(() => viewModel.LastError == "音量设置失败。");

        Assert.Equal("音量设置失败。", viewModel.LastError);
    }

    [Fact]
    public async Task RateFailure_SetsLastError()
    {
        var fake = new FakePlayerService
        {
            RateResult = ServiceResult.Failure(
                ErrorCode.InvalidState,
                "速率设置失败。",
                "Rate failed")
        };
        await using var viewModel = CreateViewModel(fake);

        viewModel.Rate = 1.5;
        await WaitUntilAsync(() => viewModel.LastError == "速率设置失败。");

        Assert.Equal("速率设置失败。", viewModel.LastError);
    }

    [Fact]
    public async Task ProgressRefresh_AppliesSnapshotsWithoutEchoingMutations()
    {
        var fake = new FakePlayerService
        {
            CurrentSnapshot = CreateSnapshot(
                TimeSpan.FromSeconds(3),
                TimeSpan.FromMinutes(1),
                volume: 85,
                isMuted: true,
                rate: 1.25)
        };
        var dispatcher = new ImmediateUiDispatcher();
        await using var viewModel = new PlayerViewModel(fake, dispatcher);

        await viewModel.StartProgressRefreshAsync();
        await WaitUntilAsync(() =>
            viewModel.Position == TimeSpan.FromSeconds(3)
            && viewModel.Volume == 85
            && viewModel.Rate == 1.25
            && viewModel.IsMuted);

        Assert.True(dispatcher.InvokeCount >= 1);
        Assert.Equal(0, fake.SetVolumeCallCount);
        Assert.Equal(0, fake.SetRateCallCount);
    }

    [Fact]
    public async Task ProgressRefresh_WithoutDragging_UpdatesPendingSeekPosition()
    {
        var fake = new FakePlayerService
        {
            CurrentSnapshot = CreateSnapshot(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMinutes(1))
        };
        await using var viewModel = CreateViewModel(fake);

        await viewModel.StartProgressRefreshAsync();
        await WaitUntilAsync(() => viewModel.PendingSeekPosition == 1);
        fake.CurrentSnapshot = CreateSnapshot(
            TimeSpan.FromSeconds(4),
            TimeSpan.FromMinutes(1));

        await WaitUntilAsync(() => viewModel.PendingSeekPosition == 4);

        Assert.Equal(4, viewModel.PendingSeekPosition);
    }

    [Fact]
    public async Task ProgressRefresh_DoesNotChangePendingSeekWhileSeeking()
    {
        var fake = new FakePlayerService
        {
            CurrentSnapshot = CreateSnapshot(
                TimeSpan.FromSeconds(2),
                TimeSpan.FromMinutes(1))
        };
        await using var viewModel = CreateViewModel(fake);
        await viewModel.StartProgressRefreshAsync();
        await WaitUntilAsync(() => viewModel.Position == TimeSpan.FromSeconds(2));
        viewModel.BeginSeek();
        viewModel.PendingSeekPosition = 42;
        fake.CurrentSnapshot = CreateSnapshot(
            TimeSpan.FromSeconds(8),
            TimeSpan.FromMinutes(2));

        await Task.Delay(300);

        Assert.Equal(TimeSpan.FromSeconds(2), viewModel.Position);
        Assert.Equal(42, viewModel.PendingSeekPosition);
    }

    [Fact]
    public async Task StartProgressRefreshAsync_RestartWaitsForPreviousLoop()
    {
        var fake = new FakePlayerService
        {
            SnapshotStarted = NewCompletionSource(),
            SnapshotRelease = NewCompletionSource()
        };
        await using var viewModel = CreateViewModel(fake);
        await viewModel.StartProgressRefreshAsync();
        await fake.SnapshotStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var restartTask = viewModel.StartProgressRefreshAsync();
        await Task.Delay(75);

        Assert.False(restartTask.IsCompleted);
        fake.SnapshotRelease.SetResult(true);
        await restartTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(fake.MaxConcurrentSnapshotCalls <= 1);
    }

    [Fact]
    public async Task DisposeAsync_CancelsAndAwaitsProgressLoop()
    {
        var fake = new FakePlayerService
        {
            SnapshotStarted = NewCompletionSource(),
            SnapshotRelease = NewCompletionSource()
        };
        var viewModel = CreateViewModel(fake);
        await viewModel.StartProgressRefreshAsync();
        await fake.SnapshotStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var disposeTask = viewModel.DisposeAsync().AsTask();
        await Task.Delay(75);
        Assert.False(disposeTask.IsCompleted);

        fake.SnapshotRelease.SetResult(true);
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(2));
        fake.CurrentSnapshot = CreateSnapshot(
            TimeSpan.FromSeconds(9),
            TimeSpan.FromMinutes(1));
        await Task.Delay(300);

        Assert.Equal(TimeSpan.Zero, viewModel.Position);
    }

    [Fact]
    public async Task TogglePlayPauseAsync_WhenPlaying_Pauses()
    {
        var fake = new FakePlayerService
        {
            CurrentSnapshot = CreateSnapshot(TimeSpan.Zero, TimeSpan.FromMinutes(1))
        };
        await using var viewModel = CreateViewModel(fake);

        await viewModel.TogglePlayPauseAsync();

        Assert.Equal(1, fake.PauseCallCount);
        Assert.Equal(0, fake.PlayCallCount);
    }

    [Fact]
    public async Task TogglePlayPauseAsync_WhenPaused_Plays()
    {
        var fake = new FakePlayerService
        {
            CurrentSnapshot = CreateSnapshot(TimeSpan.Zero, TimeSpan.FromMinutes(1))
                with { State = PlaybackState.Paused }
        };
        await using var viewModel = CreateViewModel(fake);

        await viewModel.TogglePlayPauseAsync();

        Assert.Equal(1, fake.PlayCallCount);
        Assert.Equal(0, fake.PauseCallCount);
    }

    [Fact]
    public async Task SeekCommands_AreClampedToMediaBounds()
    {
        var fake = new FakePlayerService();
        await using var viewModel = CreateViewModel(fake);
        viewModel.Position = TimeSpan.FromSeconds(3);
        viewModel.Duration = TimeSpan.FromMinutes(1);

        await viewModel.SeekBackward5Async();

        Assert.Equal(TimeSpan.Zero, fake.LastSeekPosition);

        viewModel.Position = TimeSpan.FromSeconds(50);
        await viewModel.SeekForward30Async();

        Assert.Equal(TimeSpan.FromMinutes(1), fake.LastSeekPosition);
    }

    [Fact]
    public async Task VolumeCommands_AdjustByFiveAndClamp()
    {
        var fake = new FakePlayerService();
        await using var viewModel = CreateViewModel(fake);
        viewModel.Volume = 98;

        viewModel.VolumeUp();
        await WaitUntilAsync(() => fake.LastVolume == 100);

        viewModel.VolumeDown();
        viewModel.VolumeDown();
        await WaitUntilAsync(() => fake.LastVolume == 90);

        Assert.Equal(90, viewModel.Volume);
    }

    [Fact]
    public async Task RateCommands_UsePresetStepsAndReset()
    {
        var fake = new FakePlayerService();
        await using var viewModel = CreateViewModel(fake);
        viewModel.Rate = 0.5;

        viewModel.RateUp();
        await WaitUntilAsync(() => fake.LastRate == 0.75);

        viewModel.RateUp();
        await WaitUntilAsync(() => fake.LastRate == 1.0);

        viewModel.RateDown();
        await WaitUntilAsync(() => fake.LastRate == 0.75);

        viewModel.RateReset();
        await WaitUntilAsync(() => fake.LastRate == 1.0);

        Assert.Equal(1.0, viewModel.Rate);
    }
    private static PlayerViewModel CreateViewModel(FakePlayerService fake)
        => new(fake, new ImmediateUiDispatcher());

    private static PlaybackSnapshot CreateSnapshot(
        TimeSpan position,
        TimeSpan duration,
        int volume = 80,
        bool isMuted = false,
        double rate = 1.0)
        => new(
            1,
            PlaybackState.Playing,
            "movie.mp4",
            position,
            duration,
            volume,
            isMuted,
            rate,
            true,
            true,
            true,
            DateTimeOffset.UtcNow);

    private static TaskCompletionSource<bool> NewCompletionSource()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(2))
            {
                throw new Xunit.Sdk.XunitException("Timed out waiting for the expected state.");
            }

            await Task.Delay(20);
        }
    }

    private sealed class ImmediateUiDispatcher : IUiDispatcher
    {
        private int _invokeCount;

        public int InvokeCount => Volatile.Read(ref _invokeCount);

        public bool CheckAccess() => true;

        public void Post(Action action) => action();

        public Task InvokeAsync(Action action)
        {
            Interlocked.Increment(ref _invokeCount);
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> action)
        {
            Interlocked.Increment(ref _invokeCount);
            return Task.FromResult(action());
        }
    }

    private sealed class FakePlayerService : IPlayerService
    {
        private readonly object _mutationSync = new();
        private PlaybackSnapshot _snapshot = CreateSnapshot(
            TimeSpan.Zero,
            TimeSpan.FromMinutes(1));
        private int _lastVolume = 80;
        private double _lastRate = 1.0;
        private int _setVolumeCallCount;
        private int _setRateCallCount;
        private int _activeSnapshotCalls;
        private int _maxConcurrentSnapshotCalls;

        public OpenMediaRequest? LastRequest { get; private set; }
        public ServiceResult PlayResult { get; set; } = ServiceResult.Success();
        public int PlayCallCount { get; private set; }
        public int PauseCallCount { get; private set; }
        public ServiceResult VolumeResult { get; set; } = ServiceResult.Success();
        public ServiceResult RateResult { get; set; } = ServiceResult.Success();
        public TaskCompletionSource<bool> SnapshotStarted { get; set; } = NewCompletionSource();
        public TaskCompletionSource<bool> SnapshotRelease { get; set; } = CompletedSource();
        public PlaybackSnapshot CurrentSnapshot
        {
            get => Volatile.Read(ref _snapshot);
            set => Volatile.Write(ref _snapshot, value);
        }

        public LibVLCSharp.Shared.MediaPlayer? MediaPlayer => null;
        public bool IsInitialized => true;
        public bool HasMedia => true;
        public TimeSpan? LastSeekPosition { get; private set; }
        public int LastVolume { get { lock (_mutationSync) { return _lastVolume; } } }
        public double LastRate { get { lock (_mutationSync) { return _lastRate; } } }
        public int SetVolumeCallCount => Volatile.Read(ref _setVolumeCallCount);
        public int SetRateCallCount => Volatile.Read(ref _setRateCallCount);
        public int MaxConcurrentSnapshotCalls => Volatile.Read(ref _maxConcurrentSnapshotCalls);

        public PlaybackSnapshot Snapshot
        {
            get
            {
                var active = Interlocked.Increment(ref _activeSnapshotCalls);
                UpdateMaximum(ref _maxConcurrentSnapshotCalls, active);
                SnapshotStarted.TrySetResult(true);
                SnapshotRelease.Task.GetAwaiter().GetResult();
                try
                {
                    return CurrentSnapshot;
                }
                finally
                {
                    Interlocked.Decrement(ref _activeSnapshotCalls);
                }
            }
        }

        public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<MediaOpenedEventArgs>? MediaOpened
        {
            add { }
            remove { }
        }

        public event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed
        {
            add { }
            remove { }
        }

        public event EventHandler<EndReachedEventArgs>? EndReached
        {
            add { }
            remove { }
        }

        public Task<ServiceResult<MediaSessionInfo>> OpenAsync(
            OpenMediaRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(ServiceResult<MediaSessionInfo>.Success(
                new MediaSessionInfo(1, request.FilePath, true, TimeSpan.Zero, DateTimeOffset.UtcNow)));
        }

        public Task<ServiceResult> PlayAsync(CancellationToken cancellationToken = default)
        {
            PlayCallCount++;
            return Task.FromResult(PlayResult);
        }

        public Task<ServiceResult> PauseAsync(CancellationToken cancellationToken = default)
        {
            PauseCallCount++;
            return Task.FromResult(ServiceResult.Success());
        }

        public Task<ServiceResult> StopAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult.Success());

        public Task<ServiceResult> SeekAsync(
            TimeSpan position,
            CancellationToken cancellationToken = default)
        {
            LastSeekPosition = position;
            return Task.FromResult(ServiceResult.Success());
        }

        public Task<ServiceResult> SetVolumeAsync(
            int volume,
            CancellationToken cancellationToken = default)
        {
            lock (_mutationSync)
            {
                _lastVolume = volume;
                _setVolumeCallCount++;
                _snapshot = _snapshot with { Volume = volume };
            }

            return Task.FromResult(VolumeResult);
        }

        public Task<ServiceResult> SetMuteAsync(
            bool isMuted,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult.Success());

        public Task<ServiceResult> SetRateAsync(
            double rate,
            CancellationToken cancellationToken = default)
        {
            lock (_mutationSync)
            {
                _lastRate = rate;
                _setRateCallCount++;
                _snapshot = _snapshot with { Rate = rate };
            }

            return Task.FromResult(RateResult);
        }

        public event EventHandler<MediaParsedEventArgs>? MediaParsed
        {
            add { }
            remove { }
        }

        public event EventHandler<TracksChangedEventArgs>? TracksChanged
        {
            add { }
            remove { }
        }

        public Task<ServiceResult<IReadOnlyList<PlayerTrackInfo>>> GetTracksAsync(
            PlayerTrackType type,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                ServiceResult<IReadOnlyList<PlayerTrackInfo>>.Success([]));

        public Task<ServiceResult> SelectAudioTrackAsync(
            int trackId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult.Success());

        public Task<ServiceResult> SelectSubtitleTrackAsync(
            int? trackId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult.Success());

        public Task<ServiceResult> AddExternalSubtitleAsync(
            string filePath,
            bool select,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult.Success());

        public Task<ServiceResult> TakeSnapshotAsync(
            string outputPath,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult.Success());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void ResetMutationTracking()
        {
            lock (_mutationSync)
            {
                _setVolumeCallCount = 0;
                _setRateCallCount = 0;
            }
        }

        private static TaskCompletionSource<bool> CompletedSource()
        {
            var source = NewCompletionSource();
            source.SetResult(true);
            return source;
        }

        private static void UpdateMaximum(ref int maximum, int value)
        {
            var current = Volatile.Read(ref maximum);
            while (value > current)
            {
                var observed = Interlocked.CompareExchange(ref maximum, value, current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }
}
