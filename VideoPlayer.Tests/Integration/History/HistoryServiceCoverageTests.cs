using System.IO;
using Microsoft.EntityFrameworkCore;
using VideoPlayer.Database;
using VideoPlayer.Enums;
using VideoPlayer.Models;
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Integration.History;

public sealed class HistoryServiceCoverageTests
{
    [Fact]
    public async Task FactoryFailure_MarksHistoryUnavailableAndRejectsWrites()
    {
        var service = new HistoryService(new ThrowingDbContextFactory());
        var filePath = Path.Combine(Path.GetTempPath(), "Movie.mp4");

        var read = await service.GetResumeInfoAsync(filePath);
        var write = await service.StartPlaybackAsync(
            filePath,
            "Movie.mp4",
            TimeSpan.FromMinutes(2));

        Assert.False(read.IsSuccess);
        Assert.Equal(ErrorCode.DatabaseUnavailable, read.ErrorCode);
        Assert.False(service.IsAvailable);
        Assert.False(write.IsSuccess);
        Assert.Equal(ErrorCode.DatabaseUnavailable, write.ErrorCode);
    }

    [Fact]
    public async Task NullSnapshotPathAndFlush_ReturnSuccessWithoutDatabaseAccess()
    {
        var service = new HistoryService(new ThrowingDbContextFactory());
        var snapshot = new PlaybackSnapshot(
            1,
            PlaybackState.None,
            null,
            TimeSpan.Zero,
            TimeSpan.Zero,
            80,
            false,
            1.0,
            false,
            false,
            false,
            DateTimeOffset.UtcNow);

        var saved = await service.SaveProgressAsync(snapshot);
        var flushed = await service.FlushAsync();

        Assert.True(saved.IsSuccess);
        Assert.True(flushed.IsSuccess);
    }

    [Fact]
    public async Task DatabaseOperationTimeout_MarksHistoryUnavailableWithinShortBudget()
    {
        var service = new HistoryService(new DelayingDbContextFactory());
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var result = await service.GetResumeInfoAsync("Movie.mp4");

        stopwatch.Stop();
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.DatabaseUnavailable, result.ErrorCode);
        Assert.False(service.IsAvailable);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(4),
            $"Database timeout returned after {stopwatch.Elapsed.TotalSeconds:F2} seconds.");
    }

    private sealed class DelayingDbContextFactory : IDbContextFactory<VideoPlayerDbContext>
    {
        public VideoPlayerDbContext CreateDbContext()
        {
            throw new InvalidOperationException("Use async factory.");
        }

        public async Task<VideoPlayerDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            throw new InvalidOperationException("Delay should have been canceled.");
        }
    }

    private sealed class ThrowingDbContextFactory : IDbContextFactory<VideoPlayerDbContext>
    {
        public VideoPlayerDbContext CreateDbContext()
        {
            throw new InvalidOperationException("database unavailable");
        }

        public Task<VideoPlayerDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<VideoPlayerDbContext>(
                new InvalidOperationException("database unavailable"));
        }
    }
}