using System.IO;
using VideoPlayer.Enums;
using VideoPlayer.Models;
using VideoPlayer.Services;
using VideoPlayer.Tests.Fakes;
using Xunit;

namespace VideoPlayer.Tests.Unit.Services;

public sealed class MediaInfoServiceTests
{
    [Fact]
    public async Task GetCurrentMediaInfoAsync_WhenFieldsAreMissing_UsesUnknown()
    {
        var player = new PlayerViewModelFixture(1).Player;
        var service = new MediaInfoService(player);
        var snapshot = CreateSnapshot("movie.mp4");

        var result = await service.GetCurrentMediaInfoAsync(snapshot, []);

        Assert.True(result.IsSuccess);
        Assert.Equal("movie.mp4", result.Value!.FileName);
        Assert.Null(result.Value.Width);
        Assert.Null(result.Value.Height);
        Assert.Equal("未知", result.Value.VideoCodec);
        Assert.Null(result.Value.FrameRate);
        Assert.Equal("未知", result.Value.AudioCodec);
        Assert.Null(result.Value.SampleRate);
        Assert.Null(result.Value.Channels);
    }

    [Fact]
    public async Task GetCurrentMediaInfoAsync_WithoutMedia_ReturnsInvalidState()
    {
        var player = new PlayerViewModelFixture(1).Player;
        var service = new MediaInfoService(player);
        var snapshot = CreateSnapshot(null);

        var result = await service.GetCurrentMediaInfoAsync(snapshot, []);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InvalidState, result.ErrorCode);
    }

    private static PlaybackSnapshot CreateSnapshot(string? mediaPath)
    {
        return new PlaybackSnapshot(
            1,
            PlaybackState.Paused,
            mediaPath,
            TimeSpan.Zero,
            TimeSpan.FromMinutes(1),
            80,
            false,
            1.0,
            true,
            true,
            true,
            DateTimeOffset.UtcNow);
    }
}
