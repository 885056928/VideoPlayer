using System.Collections.Concurrent;
using VideoPlayer.Enums;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;
using VideoPlayer.Models.Events;
using VideoPlayer.Tests.Fakes;
using VideoPlayer.ViewModels;
using Xunit;

namespace VideoPlayer.Tests.Unit.ViewModels;

public sealed class PlayerViewModelFinalReviewTests
{
    [Fact]
    public async Task PlaybackFailed_SetsErrorFlushesHistoryAndShowsDialog()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 61);
        await using var viewModel = fixture.ViewModel;
        await viewModel.OpenAsync("movie.mp4");

        fixture.Player.RaisePlaybackFailed(ErrorCode.UnsupportedMedia, "无法播放。");
        await WaitUntilAsync(() => fixture.Dialog.Errors.Count > 0);

        Assert.Equal(PlaybackState.Error, viewModel.PlaybackState);
        Assert.Equal("无法播放。", viewModel.LastError);
        Assert.True(fixture.History.FlushCallCount > 0);
        Assert.NotEmpty(fixture.Dialog.Errors);
    }

    [Fact]
    public async Task StaleOpenRequest_ReturnsSupersededWithoutShowingError()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 62);
        await using var viewModel = fixture.ViewModel;
        var staleStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStale = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Player.OpenHandler = async (request, cancellationToken) =>
        {
            if (request.FilePath == "slow.mp4")
            {
                staleStarted.TrySetResult();
                await releaseStale.Task.WaitAsync(cancellationToken);
            }

            return ServiceResult<MediaSessionInfo>.Success(
                new MediaSessionInfo(
                    62,
                    request.FilePath,
                    request.AutoPlay,
                    request.StartPosition,
                    DateTimeOffset.UtcNow));
        };

        var stale = viewModel.OpenAsync("slow.mp4");
        await staleStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var current = viewModel.OpenAsync("current.mp4");
        releaseStale.TrySetResult();
        await current;
        var staleResult = await stale;

        Assert.Equal(ErrorCode.Superseded, staleResult.ErrorCode);
        Assert.Equal(string.Empty, viewModel.LastError);
    }

    [Fact]
    public async Task NoOpNext_DoesNotCancelValidInFlightOpen()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 63);
        await using var viewModel = fixture.ViewModel;
        fixture.Playlist.SetCurrentIndexForTest(1);
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Player.OpenHandler = async (request, cancellationToken) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return ServiceResult<MediaSessionInfo>.Success(
                new MediaSessionInfo(
                    63,
                    request.FilePath,
                    request.AutoPlay,
                    request.StartPosition,
                    DateTimeOffset.UtcNow));
        };

        var open = viewModel.OpenAsync("slow.mp4");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var navigation = await viewModel.NextAsync();
        release.TrySetResult();
        var openResult = await open;

        Assert.False(navigation.IsSuccess);
        Assert.True(openResult.IsSuccess);
        Assert.Equal(1, fixture.Player.OpenCount);
    }

    [Fact]
    public async Task SelectAudioTrack_RefreshesTrackCollections()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 64);
        await using var viewModel = fixture.ViewModel;
        var audio = new PlayerTrackInfo(PlayerTrackType.Audio, 1, "Audio", "und", true, false);
        var subtitle = new PlayerTrackInfo(PlayerTrackType.Subtitle, 2, "Subtitle", "und", true, false);
        fixture.Player.TracksResult =
            ServiceResult<IReadOnlyList<PlayerTrackInfo>>.Success([audio, subtitle]);

        await viewModel.SelectAudioTrackAsync(audio);

        Assert.Equal([audio], viewModel.AudioTracks);
        Assert.Equal([subtitle], viewModel.SubtitleTracks);
    }

    [Fact]
    public async Task MediaParsed_UpdatesPlaylistDuration()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 65);
        await using var viewModel = fixture.ViewModel;
        await viewModel.OpenAsync("movie.mp4");

        fixture.Player.RaiseMediaParsed(TimeSpan.FromMinutes(2));

        Assert.Equal(TimeSpan.FromMinutes(2), fixture.Playlist.Current!.Duration);
    }

    [Fact]
    public async Task SubtitleAutoLoadDisabled_SkipsSubtitleDiscovery()
    {
        var player = new FakePlayerService(66);
        var playlist = new FakePlaylistService();
        var settings = new FakeSettingsService();
        settings.Current.Subtitle.AutoLoad = false;
        var subtitle = new FakeSubtitleService();
        await using var viewModel = new PlayerViewModel(
            player,
            new ImmediateUiDispatcher(),
            playlist,
            settings,
            new FakeDialogService(),
            subtitle,
            new FakeHistoryService(),
            new FakeScreenshotService());

        await viewModel.OpenAsync("movie.mp4");

        Assert.Equal(0, subtitle.FindCallCount);
    }

    [Fact]
    public async Task InitialVolumeAndRate_SurviveOpenSnapshotRefresh()
    {
        var player = new FakePlayerService(68);
        var settings = new FakeSettingsService();
        settings.Current.Player.DefaultVolume = 37;
        settings.Current.Player.DefaultPlaybackRate = 1.25;
        await using var viewModel = new PlayerViewModel(
            player,
            new ImmediateUiDispatcher(),
            new FakePlaylistService(),
            settings,
            new FakeDialogService(),
            new FakeSubtitleService(),
            new FakeHistoryService(),
            new FakeScreenshotService());

        await viewModel.OpenAsync("movie.mp4");

        Assert.Equal(37, viewModel.Volume);
        Assert.Equal(1.25, viewModel.Rate);
    }

    [Fact]
    public async Task OpenOrchestration_RunsOnDispatcherThread()
    {
        using var dispatcher = new BackgroundUiDispatcher();
        var player = new FakePlayerService(67);
        var dispatcherThreadId = -1;
        player.OpenHandler = (request, _) =>
        {
            dispatcherThreadId = Environment.CurrentManagedThreadId;
            return Task.FromResult(ServiceResult<MediaSessionInfo>.Success(
                new MediaSessionInfo(
                    67,
                    request.FilePath,
                    request.AutoPlay,
                    request.StartPosition,
                    DateTimeOffset.UtcNow)));
        };
        await using var viewModel = new PlayerViewModel(
            player,
            dispatcher,
            new FakePlaylistService(),
            new FakeSettingsService(),
            new FakeDialogService(),
            new FakeSubtitleService(),
            new FakeHistoryService(),
            new FakeScreenshotService());

        await viewModel.OpenAsync("movie.mp4");

        Assert.Equal(dispatcher.UiThreadId, dispatcherThreadId);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new Xunit.Sdk.XunitException("Timed out waiting for condition.");
    }

    private sealed class ImmediateUiDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;
        public void Post(Action action) => action();
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private sealed class BackgroundUiDispatcher : IUiDispatcher, IDisposable
    {
        private readonly BlockingCollection<Action> _queue = [];
        private readonly Thread _thread;

        public BackgroundUiDispatcher()
        {
            _thread = new Thread(
                () =>
                {
                    foreach (var action in _queue.GetConsumingEnumerable())
                    {
                        action();
                    }
                })
            {
                IsBackground = true,
                Name = "PlayerViewModel test UI"
            };
            _thread.Start();
            UiThreadId = _thread.ManagedThreadId;
        }

        public int UiThreadId { get; }

        public bool CheckAccess() => Environment.CurrentManagedThreadId == UiThreadId;

        public void Post(Action action)
        {
            if (CheckAccess())
            {
                action();
                return;
            }

            _queue.Add(action);
        }

        public Task InvokeAsync(Action action)
        {
            if (CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }

            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add(() =>
            {
                try
                {
                    action();
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            });
            return completion.Task;
        }

        public Task<T> InvokeAsync<T>(Func<T> action)
        {
            if (CheckAccess())
            {
                return Task.FromResult(action());
            }

            var completion = new TaskCompletionSource<T>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add(() =>
            {
                try
                {
                    completion.TrySetResult(action());
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            });
            return completion.Task;
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            _thread.Join(TimeSpan.FromSeconds(2));
            _queue.Dispose();
        }
    }
}
