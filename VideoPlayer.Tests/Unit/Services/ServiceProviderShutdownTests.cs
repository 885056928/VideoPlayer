using Microsoft.Extensions.DependencyInjection;
using VideoPlayer.Enums;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;
using VideoPlayer.Models.Events;
using VideoPlayer.Services;
using VideoPlayer.ViewModels;
using Xunit;

namespace VideoPlayer.Tests.Unit.Services;

public sealed class ServiceProviderShutdownTests
{
    [Fact]
    public async Task DisposeAsync_WhenDispatcherWorkIsPending_CompletesAfterUiPump()
    {
        var playerService = new FakePlayerService();
        var dispatcher = new QueueingUiDispatcher();
        var viewModel = new PlayerViewModel(playerService, dispatcher);
        var services = new ServiceCollection();
        services.AddSingleton(_ => new ShutdownProbe(viewModel));
        await using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<ShutdownProbe>();
        await viewModel.StartProgressRefreshAsync();
        await dispatcher.InvocationQueued.WaitAsync(TimeSpan.FromSeconds(2));

        var shutdownTask = ServiceProviderShutdown.DisposeAsync(
            provider,
            TimeSpan.FromSeconds(2));
        await Task.Delay(75);

        Assert.False(shutdownTask.IsCompleted);
        dispatcher.Flush();
        Assert.True(await shutdownTask.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task DisposeAsync_WhenProviderExceedsTimeout_ReturnsFalse()
    {
        var blocker = new BlockingAsyncDisposable();
        var services = new ServiceCollection();
        services.AddSingleton(_ => blocker);
        var provider = services.BuildServiceProvider();
        Assert.Same(blocker, provider.GetRequiredService<BlockingAsyncDisposable>());

        var shutdownTask = ServiceProviderShutdown.DisposeAsync(
            provider,
            TimeSpan.FromMilliseconds(50));
        var completed = await Task.WhenAny(
            shutdownTask,
            Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(shutdownTask, completed);
        Assert.False(await shutdownTask);

        blocker.Release();
        await provider.DisposeAsync();
    }

    private sealed class ShutdownProbe : IAsyncDisposable
    {
        private readonly PlayerViewModel _viewModel;

        public ShutdownProbe(PlayerViewModel viewModel)
        {
            _viewModel = viewModel;
        }

        public ValueTask DisposeAsync() => _viewModel.DisposeAsync();
    }
    private sealed class QueueingUiDispatcher : IUiDispatcher
    {
        private readonly object _sync = new();
        private readonly Queue<Action> _pending = new();
        private readonly TaskCompletionSource<bool> _invocationQueued =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task InvocationQueued => _invocationQueued.Task;

        public bool CheckAccess() => false;

        public void Post(Action action) => Enqueue(action);

        public Task InvokeAsync(Action action)
        {
            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Enqueue(() =>
            {
                try
                {
                    action();
                    completion.TrySetResult(true);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            });
            _invocationQueued.TrySetResult(true);
            return completion.Task;
        }

        public Task<T> InvokeAsync<T>(Func<T> action)
        {
            var completion = new TaskCompletionSource<T>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Enqueue(() =>
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
            _invocationQueued.TrySetResult(true);
            return completion.Task;
        }

        public void Flush()
        {
            while (TryDequeue(out var action))
            {
                action();
            }
        }

        private void Enqueue(Action action)
        {
            lock (_sync)
            {
                _pending.Enqueue(action);
            }
        }

        private bool TryDequeue(out Action action)
        {
            lock (_sync)
            {
                if (_pending.TryDequeue(out var queuedAction))
                {
                    action = queuedAction;
                    return true;
                }

                action = null!;
                return false;
            }
        }
    }

    private sealed class BlockingAsyncDisposable : IAsyncDisposable
    {
        private readonly TaskCompletionSource<bool> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask DisposeAsync() => new(_completion.Task);

        public void Release() => _completion.TrySetResult(true);
    }

    private sealed class FakePlayerService : IPlayerService
    {
        public LibVLCSharp.Shared.MediaPlayer? MediaPlayer => null;
        public bool IsInitialized => true;
        public bool HasMedia => true;
        public PlaybackSnapshot Snapshot => new(
            1,
            PlaybackState.Playing,
            "movie.mp4",
            TimeSpan.Zero,
            TimeSpan.FromMinutes(1),
            80,
            false,
            1.0,
            true,
            true,
            true,
            DateTimeOffset.UtcNow);

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
            => throw new NotSupportedException();

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
    }
}
