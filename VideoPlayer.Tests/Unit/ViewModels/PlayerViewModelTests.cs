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
        var viewModel = new PlayerViewModel(fake);

        await viewModel.OpenAsync(@"C:\视频\movie.mp4");

        Assert.Equal(@"C:\视频\movie.mp4", fake.LastRequest!.FilePath);
        Assert.True(fake.LastRequest.AutoPlay);
    }

    [Fact]
    public async Task PlayAsync_WhenServiceFails_SetsLastError()
    {
        var fake = new FakePlayerService
        {
            PlayResult = ServiceResult.Failure(
                Enums.ErrorCode.InvalidState,
                "没有视频。",
                "No media")
        };
        var viewModel = new PlayerViewModel(fake);

        await viewModel.PlayAsync();

        Assert.Equal("没有视频。", viewModel.LastError);
    }

    private sealed class FakePlayerService : IPlayerService
    {
        public OpenMediaRequest? LastRequest { get; private set; }
        public ServiceResult PlayResult { get; set; } = ServiceResult.Success();
        public LibVLCSharp.Shared.MediaPlayer? MediaPlayer => null;
        public bool IsInitialized => true;
        public bool HasMedia => true;
        public PlaybackSnapshot Snapshot => new(
            1, Enums.PlaybackState.Playing, "movie.mp4",
            TimeSpan.Zero, TimeSpan.FromMinutes(1), 80, false, 1.0,
            true, true, true, DateTimeOffset.UtcNow);
        public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;
        public event EventHandler<MediaOpenedEventArgs>? MediaOpened;
        public event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;

        public Task<ServiceResult<MediaSessionInfo>> OpenAsync(
            OpenMediaRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(ServiceResult<MediaSessionInfo>.Success(
                new MediaSessionInfo(1, request.FilePath, true, TimeSpan.Zero, DateTimeOffset.UtcNow)));
        }

        public Task<ServiceResult> PlayAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(PlayResult);

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

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
