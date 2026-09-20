using System.Diagnostics;
using VideoPlayer.Enums;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;
using VideoPlayer.Models.Events;
using VideoPlayer.ViewModels;
using Xunit;

namespace VideoPlayer.Tests.Unit.ViewModels;

public sealed class PlayerViewModelHistoryTests
{
    [Fact]
    public async Task OpenAsync_WhenUserContinues_StartsAtSavedPosition()
    {
        var history = new FakeHistoryService
        {
            ResumeInfoResult = ServiceResult<ResumeInfo?>.Success(CreateResumeInfo(50))
        };
        var dialog = new FakeDialogService { Choice = ResumeChoice.Continue };
        var player = new FakePlayerService();
        await using var viewModel = CreateViewModel(player, history, dialog);

        await viewModel.OpenAsync(@"C:\Videos\movie.mp4");

        Assert.Equal(TimeSpan.FromSeconds(50), player.LastRequest!.StartPosition);
        Assert.Equal(1, dialog.CallCount);
    }

    [Fact]
    public async Task OpenAsync_WhenUserRestarts_ClearsHistoryAndStartsAtZero()
    {
        var history = new FakeHistoryService
        {
            ResumeInfoResult = ServiceResult<ResumeInfo?>.Success(CreateResumeInfo(50))
        };
        var dialog = new FakeDialogService { Choice = ResumeChoice.Restart };
        var player = new FakePlayerService();
        await using var viewModel = CreateViewModel(player, history, dialog);

        await viewModel.OpenAsync(@"C:\Videos\movie.mp4");

        Assert.Equal(TimeSpan.Zero, player.LastRequest!.StartPosition);
        Assert.Equal(1, history.ClearCompletedCallCount);
    }

    [Fact]
    public async Task OpenAsync_WhenUserCancels_DoesNotOpenMedia()
    {
        var history = new FakeHistoryService
        {
            ResumeInfoResult = ServiceResult<ResumeInfo?>.Success(CreateResumeInfo(50))
        };
        var dialog = new FakeDialogService { Choice = ResumeChoice.Cancel };
        var player = new FakePlayerService();
        await using var viewModel = CreateViewModel(player, history, dialog);

        await viewModel.OpenAsync(@"C:\Videos\movie.mp4");

        Assert.Equal(0, player.OpenCount);
    }

    [Fact]
    public async Task OnStateChanged_WhenSameSessionPlaysAgain_CountsStartOnce()
    {
        var history = new FakeHistoryService();
        var dialog = new FakeDialogService();
        var player = new FakePlayerService();
        await using var viewModel = CreateViewModel(player, history, dialog);

        await viewModel.OpenAsync(@"C:\Videos\movie.mp4");
        await WaitUntilAsync(() => history.StartPlaybackCallCount == 1);

        player.RaiseStateChanged(PlaybackState.Playing);

        await Task.Delay(50);
        Assert.Equal(1, history.StartPlaybackCallCount);
    }

    private static PlayerViewModel CreateViewModel(
        FakePlayerService player,
        FakeHistoryService history,
        FakeDialogService dialog)
    {
        return new PlayerViewModel(
            player,
            new ImmediateUiDispatcher(),
            playlistService: null,
            new FakeSettingsService(),
            dialog,
            subtitleService: null,
            historyService: history);
    }

    private static ResumeInfo CreateResumeInfo(double percent)
    {
        var duration = TimeSpan.FromSeconds(100);
        return new ResumeInfo(
            @"C:\Videos\movie.mp4",
            duration,
            TimeSpan.FromSeconds(duration.TotalSeconds * percent / 100),
            false,
            DateTimeOffset.UtcNow,
            1);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(2))
            {
                throw new Xunit.Sdk.XunitException("Timed out waiting for history state.");
            }

            await Task.Delay(20);
        }
    }

    private sealed class FakeHistoryService : IHistoryService
    {
        public bool IsAvailable => true;

        public ServiceResult<ResumeInfo?> ResumeInfoResult { get; set; } =
            ServiceResult<ResumeInfo?>.Success(null);

        public int StartPlaybackCallCount { get; private set; }

        public int ClearCompletedCallCount { get; private set; }

        public Task<ServiceResult<ResumeInfo?>> GetResumeInfoAsync(
            string filePath,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ResumeInfoResult);

        public Task<ServiceResult> StartPlaybackAsync(
            string filePath,
            string fileName,
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            StartPlaybackCallCount++;
            return Task.FromResult(ServiceResult.Success());
        }

        public Task<ServiceResult> SaveProgressAsync(
            PlaybackSnapshot snapshot,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult.Success());

        public Task<ServiceResult> MarkCompletedAsync(
            string filePath,
            TimeSpan duration,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult.Success());

        public Task<ServiceResult> ClearCompletedAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            ClearCompletedCallCount++;
            return Task.FromResult(ServiceResult.Success());
        }

        public Task<ServiceResult> FlushAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult.Success());
    }

    private sealed class FakeDialogService : IDialogService
    {
        public ResumeChoice Choice { get; set; } = ResumeChoice.Continue;

        public int CallCount { get; private set; }

        public Task<ResumeChoice> AskResumeAsync(
            string fileName,
            TimeSpan lastPosition,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(Choice);
        }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; } = AppSettings.CreateDefault();

        public Task<ServiceResult> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult.Success());

        public Task<ServiceResult> SaveAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult.Success());

        public Task<ServiceResult> SaveImmediateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult.Success());
    }

    private sealed class FakePlayerService : IPlayerService
    {
        private const long SessionId = 7;

        public int OpenCount { get; private set; }

        public OpenMediaRequest? LastRequest { get; private set; }

        public LibVLCSharp.Shared.MediaPlayer? MediaPlayer => null;

        public bool IsInitialized => true;

        public bool HasMedia => true;

        public PlaybackSnapshot Snapshot => new(
            SessionId,
            PlaybackState.Playing,
            "movie.mp4",
            TimeSpan.FromSeconds(50),
            TimeSpan.FromSeconds(100),
            80,
            false,
            1.0,
            true,
            true,
            true,
            DateTimeOffset.UtcNow);

        public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

        public event EventHandler<MediaOpenedEventArgs>? MediaOpened
        {
            add { }
            remove { }
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
            OpenCount++;
            LastRequest = request;
            StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs
            {
                MediaSessionId = SessionId,
                OldState = PlaybackState.None,
                NewState = PlaybackState.Playing,
                OccurredAtUtc = DateTimeOffset.UtcNow
            });
            return Task.FromResult(ServiceResult<MediaSessionInfo>.Success(
                new MediaSessionInfo(
                    SessionId,
                    request.FilePath,
                    true,
                    request.StartPosition,
                    DateTimeOffset.UtcNow)));
        }

        public Task<ServiceResult> PlayAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult.Success());

        public Task<ServiceResult> PauseAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult.Success());

        public Task<ServiceResult> StopAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult.Success());

        public Task<ServiceResult> SeekAsync(
            TimeSpan position,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult.Success());

        public Task<ServiceResult> SetVolumeAsync(
            int volume,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult.Success());

        public Task<ServiceResult> SetMuteAsync(
            bool isMuted,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult.Success());

        public Task<ServiceResult> SetRateAsync(
            double rate,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult.Success());

        public Task<ServiceResult<IReadOnlyList<PlayerTrackInfo>>> GetTracksAsync(
            PlayerTrackType type,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult<IReadOnlyList<PlayerTrackInfo>>.Success([]));

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

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void RaiseStateChanged(PlaybackState state)
        {
            StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs
            {
                MediaSessionId = SessionId,
                OldState = PlaybackState.Playing,
                NewState = state,
                OccurredAtUtc = DateTimeOffset.UtcNow
            });
        }
    }

    private sealed class ImmediateUiDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;

        public void Post(Action action) => action();

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }
}
