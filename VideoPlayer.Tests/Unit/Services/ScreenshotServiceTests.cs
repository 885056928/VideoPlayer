using System.IO;
using VideoPlayer.Configuration;
using VideoPlayer.Models;
using VideoPlayer.Services;
using VideoPlayer.Tests.Fakes;
using Xunit;

namespace VideoPlayer.Tests.Unit.Services;

public sealed class ScreenshotServiceTests
{
    [Fact]
    public async Task CaptureAsync_RemovesInvalidFileNameCharacters()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new AppPaths(root);
            var player = new PlayerViewModelFixture(1).Player;
            var settings = new SettingsService(paths);
            settings.Current.Screenshot.Directory = Path.Combine(root, "Screenshots");
            var service = new ScreenshotService(player, settings, paths);
            var snapshot = new PlaybackSnapshot(
                1, Enums.PlaybackState.Playing, "Movie:Part?.mp4",
                TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1),
                80, false, 1.0, true, true, true, DateTimeOffset.UtcNow);

            var result = await service.CaptureAsync(snapshot);

            Assert.True(result.IsSuccess);
            Assert.DoesNotContain(":", Path.GetFileName(result.Value));
            Assert.DoesNotContain("?", Path.GetFileName(result.Value));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task CaptureAsync_WithoutVideo_ReturnsFailureWithoutCallingPlayer()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new AppPaths(root);
            var player = new PlayerViewModelFixture(1).Player;
            var settings = new SettingsService(paths);
            settings.Current.Screenshot.Directory = Path.Combine(root, "Screenshots");
            var service = new ScreenshotService(player, settings, paths);
            var snapshot = new PlaybackSnapshot(
                1, Enums.PlaybackState.Paused, "Movie.mp4",
                TimeSpan.Zero, TimeSpan.FromMinutes(1),
                80, false, 1.0, true, false, true, DateTimeOffset.UtcNow);

            var result = await service.CaptureAsync(snapshot);

            Assert.False(result.IsSuccess);
            Assert.Equal(Enums.ErrorCode.SnapshotFailed, result.ErrorCode);
            Assert.Equal(0, player.TakeSnapshotCallCount);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task CaptureAsync_WhenTimestampCollides_AppendsSuffix()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new AppPaths(root);
            var player = new PlayerViewModelFixture(1).Player;
            var settings = new SettingsService(paths);
            settings.Current.Screenshot.Directory = Path.Combine(root, "Screenshots");
            var now = new DateTime(2026, 9, 20, 0, 12, 35);
            var service = new ScreenshotService(player, settings, paths, () => now);
            var snapshot = new PlaybackSnapshot(
                1, Enums.PlaybackState.Playing, "Movie.mp4",
                TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1),
                80, false, 1.0, true, true, true, DateTimeOffset.UtcNow);

            var first = await service.CaptureAsync(snapshot);
            var second = await service.CaptureAsync(snapshot);

            Assert.True(first.IsSuccess);
            Assert.True(second.IsSuccess);
            Assert.NotEqual(first.Value, second.Value);
            Assert.Contains("Movie_00-12-35_01.png", Path.GetFileName(second.Value));
            Assert.True(File.Exists(first.Value));
            Assert.True(File.Exists(second.Value));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
