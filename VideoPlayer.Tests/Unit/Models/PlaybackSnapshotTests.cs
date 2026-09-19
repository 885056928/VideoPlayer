using VideoPlayer.Enums;
using VideoPlayer.Models;
using Xunit;

namespace VideoPlayer.Tests.Unit.Models;

public sealed class PlaybackSnapshotTests
{
    [Fact]
    public void Constructor_PreservesAllPlaybackFields()
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var snapshot = new PlaybackSnapshot(
            7,
            PlaybackState.Playing,
            @"C:\视频\测试.mp4",
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(2),
            80,
            false,
            1.25,
            true,
            true,
            true,
            capturedAt);

        Assert.Equal(7, snapshot.MediaSessionId);
        Assert.Equal(PlaybackState.Playing, snapshot.State);
        Assert.Equal(1.25, snapshot.Rate);
        Assert.Equal(capturedAt, snapshot.CapturedAtUtc);
    }
}
