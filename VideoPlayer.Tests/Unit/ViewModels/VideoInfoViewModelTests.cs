using VideoPlayer.Interfaces;
using VideoPlayer.Models;
using VideoPlayer.ViewModels;
using Xunit;

namespace VideoPlayer.Tests.Unit.ViewModels;

public sealed class VideoInfoViewModelTests
{
    [Fact]
    public async Task RefreshAsync_WhenFieldsAreMissing_ProjectsUnknown()
    {
        var mediaInfo = new VideoInfo(
            string.Empty,
            string.Empty,
            0,
            TimeSpan.Zero,
            null,
            null,
            string.Empty,
            null,
            string.Empty,
            null,
            null);
        var viewModel = new VideoInfoViewModel(
            new StubMediaInfoService(ServiceResult<VideoInfo>.Success(mediaInfo)));
        var snapshot = new PlaybackSnapshot(
            1,
            Enums.PlaybackState.Paused,
            "movie.mp4",
            TimeSpan.Zero,
            TimeSpan.Zero,
            80,
            false,
            1.0,
            true,
            true,
            true,
            DateTimeOffset.UtcNow);

        await viewModel.RefreshAsync(snapshot, []);

        Assert.Equal("未知", viewModel.FileName);
        Assert.Equal("未知", viewModel.FilePath);
        Assert.Equal("未知", viewModel.Resolution);
        Assert.Equal("未知", viewModel.VideoCodec);
        Assert.Equal("未知", viewModel.AudioCodec);
        Assert.Equal("未知", viewModel.Duration);
    }

    [Fact]
    public async Task RefreshAsync_WithCompleteFields_ProjectsValues()
    {
        var mediaInfo = new VideoInfo(
            "movie.mkv",
            @"C:\media\movie.mkv",
            1024,
            TimeSpan.FromSeconds(65),
            1920,
            1080,
            "H.264",
            24,
            "AAC",
            48000,
            2);
        var viewModel = new VideoInfoViewModel(
            new StubMediaInfoService(ServiceResult<VideoInfo>.Success(mediaInfo)));
        var snapshot = new PlaybackSnapshot(
            1,
            Enums.PlaybackState.Paused,
            @"C:\media\movie.mkv",
            TimeSpan.Zero,
            TimeSpan.FromSeconds(65),
            80,
            false,
            1.0,
            true,
            true,
            true,
            DateTimeOffset.UtcNow);

        await viewModel.RefreshAsync(snapshot, []);

        Assert.Equal("movie.mkv", viewModel.FileName);
        Assert.Equal(@"C:\media\movie.mkv", viewModel.FilePath);
        Assert.Equal("1920 × 1080", viewModel.Resolution);
        Assert.Equal("H.264", viewModel.VideoCodec);
        Assert.Equal("AAC", viewModel.AudioCodec);
        Assert.Equal("00:01:05", viewModel.Duration);
    }

    private sealed class StubMediaInfoService : IMediaInfoService
    {
        private readonly ServiceResult<VideoInfo> _result;

        public StubMediaInfoService(ServiceResult<VideoInfo> result)
        {
            _result = result;
        }

        public Task<ServiceResult<VideoInfo>> GetCurrentMediaInfoAsync(
            PlaybackSnapshot snapshot,
            IReadOnlyList<PlayerTrackInfo> tracks,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }
}
