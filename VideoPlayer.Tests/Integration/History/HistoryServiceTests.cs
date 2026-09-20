using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VideoPlayer.Configuration;
using VideoPlayer.Database;
using VideoPlayer.Enums;
using VideoPlayer.Models;
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Integration.History;

public sealed class HistoryServiceTests
{
    [Fact]
    public async Task StartPlaybackAsync_Twice_UpsertsCaseInsensitivePathAndCountsStarts()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var service = new HistoryService(fixture.Factory);
        var filePath = Path.Combine(fixture.Root, "Videos", "Movie.mp4");

        var first = await service.StartPlaybackAsync(
            filePath, "Movie.mp4", TimeSpan.FromMinutes(2));
        var second = await service.StartPlaybackAsync(
            filePath.ToUpperInvariant(), "Movie.mp4", TimeSpan.FromMinutes(2));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        await using var context = await fixture.Factory.CreateDbContextAsync();
        var row = Assert.Single(await context.PlaybackHistory.AsNoTracking().ToListAsync());
        Assert.Equal(2, row.PlayCount);
    }

    [Fact]
    public async Task SaveProgressAsync_StoresSuppliedPositionAndDuration()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var service = new HistoryService(fixture.Factory);
        var filePath = Path.Combine(fixture.Root, "Videos", "Movie.mp4");
        var position = TimeSpan.FromSeconds(42);
        var duration = TimeSpan.FromMinutes(3);
        await service.StartPlaybackAsync(filePath, "Movie.mp4", duration);
        var snapshot = new PlaybackSnapshot(
            1,
            PlaybackState.Playing,
            filePath,
            position,
            duration,
            80,
            false,
            1.0,
            true,
            true,
            true,
            DateTimeOffset.UtcNow);

        var result = await service.SaveProgressAsync(snapshot);

        Assert.True(result.IsSuccess);
        await using var context = await fixture.Factory.CreateDbContextAsync();
        var row = Assert.Single(await context.PlaybackHistory.AsNoTracking().ToListAsync());
        Assert.Equal(position.Ticks, row.LastPositionTicks);
        Assert.Equal(duration.Ticks, row.DurationTicks);
        Assert.Equal(1, row.PlayCount);
    }

    [Fact]
    public async Task MarkCompletedAsync_SetsCompletedAndClearsPosition()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var service = new HistoryService(fixture.Factory);
        var filePath = Path.Combine(fixture.Root, "Videos", "Movie.mp4");
        var duration = TimeSpan.FromMinutes(4);
        var snapshot = new PlaybackSnapshot(
            1,
            PlaybackState.Playing,
            filePath,
            TimeSpan.FromSeconds(90),
            duration,
            80,
            false,
            1.0,
            true,
            true,
            true,
            DateTimeOffset.UtcNow);
        await service.SaveProgressAsync(snapshot);

        var result = await service.MarkCompletedAsync(filePath, duration);

        Assert.True(result.IsSuccess);
        await using var context = await fixture.Factory.CreateDbContextAsync();
        var row = Assert.Single(await context.PlaybackHistory.AsNoTracking().ToListAsync());
        Assert.True(row.IsCompleted);
        Assert.Equal(0, row.LastPositionTicks);
    }

    [Fact]
    public async Task ClearCompletedAsync_ResetsCompletedWithoutDeletingRow()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var service = new HistoryService(fixture.Factory);
        var filePath = Path.Combine(fixture.Root, "Videos", "Movie.mp4");
        await service.MarkCompletedAsync(filePath, TimeSpan.FromMinutes(4));

        var result = await service.ClearCompletedAsync(filePath);

        Assert.True(result.IsSuccess);
        await using var context = await fixture.Factory.CreateDbContextAsync();
        var row = Assert.Single(await context.PlaybackHistory.AsNoTracking().ToListAsync());
        Assert.False(row.IsCompleted);
        Assert.Equal(0, row.LastPositionTicks);
    }

    [Fact]
    public async Task GetResumeInfoAsync_ReturnsStoredResumeMetadata()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var service = new HistoryService(fixture.Factory);
        var filePath = Path.Combine(fixture.Root, "Videos", "Movie.mp4");
        var position = TimeSpan.FromSeconds(75);
        var duration = TimeSpan.FromMinutes(10);
        await service.StartPlaybackAsync(filePath, "Movie.mp4", duration);
        var snapshot = new PlaybackSnapshot(
            1,
            PlaybackState.Paused,
            filePath,
            position,
            duration,
            80,
            false,
            1.0,
            true,
            true,
            true,
            DateTimeOffset.UtcNow);
        await service.SaveProgressAsync(snapshot);

        var result = await service.GetResumeInfoAsync(filePath.ToUpperInvariant());

        Assert.True(result.IsSuccess);
        var info = Assert.IsType<ResumeInfo>(result.Value);
        Assert.Equal(position, info.LastPosition);
        Assert.Equal(duration, info.Duration);
        Assert.False(info.IsCompleted);
        Assert.Equal(1, info.PlayCount);
    }

    [Fact]
    public async Task DatabaseFailure_SetsUnavailableAndReturnsFailureWithoutThrowing()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var options = new DbContextOptionsBuilder<VideoPlayerDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(root, "missing", "VideoPlayer.db"))
                .Options;
            var service = new HistoryService(new TestDbContextFactory(options));

            var result = await service.StartPlaybackAsync(
                Path.Combine(root, "Movie.mp4"),
                "Movie.mp4",
                TimeSpan.FromMinutes(2));

            Assert.False(service.IsAvailable);
            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCode.DatabaseUnavailable, result.ErrorCode);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task GetResumeInfoAsync_Cancellation_ReturnsCancelledAndKeepsAvailable()
    {
        var options = new DbContextOptionsBuilder<VideoPlayerDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var service = new HistoryService(new TestDbContextFactory(options));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await service.GetResumeInfoAsync(
            Path.Combine(Path.GetTempPath(), "Movie.mp4"),
            cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.Cancelled, result.ErrorCode);
        Assert.True(service.IsAvailable);
    }

    [Fact]
    public async Task StartPlaybackAsync_Cancellation_ReturnsCancelledAndKeepsAvailable()
    {
        var options = new DbContextOptionsBuilder<VideoPlayerDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var service = new HistoryService(new TestDbContextFactory(options));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await service.StartPlaybackAsync(
            Path.Combine(Path.GetTempPath(), "Movie.mp4"),
            "Movie.mp4",
            TimeSpan.FromMinutes(2),
            cancellationToken: cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.Cancelled, result.ErrorCode);
        Assert.True(service.IsAvailable);
    }
    [Fact]
    public async Task SaveProgressAsync_OlderSession_DoesNotOverwriteNewerProgress()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var service = new HistoryService(fixture.Factory);
        var filePath = Path.Combine(fixture.Root, "Videos", "Movie.mp4");
        var duration = TimeSpan.FromMinutes(10);
        await service.SaveProgressAsync(CreateSnapshot(filePath, duration, 100, 2));

        var stale = await service.SaveProgressAsync(CreateSnapshot(filePath, duration, 20, 1));

        Assert.True(stale.IsSuccess);
        await using var context = await fixture.Factory.CreateDbContextAsync();
        var row = Assert.Single(await context.PlaybackHistory.AsNoTracking().ToListAsync());
        Assert.Equal(TimeSpan.FromSeconds(100).Ticks, row.LastPositionTicks);
    }

    [Fact]
    public async Task SaveProgressAsync_AtNinetyFivePercent_MarksCompleted()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var service = new HistoryService(fixture.Factory);
        var filePath = Path.Combine(fixture.Root, "Videos", "Movie.mp4");
        var duration = TimeSpan.FromMinutes(10);

        var result = await service.SaveProgressAsync(CreateSnapshot(filePath, duration, 570, 3));

        Assert.True(result.IsSuccess);
        await using var context = await fixture.Factory.CreateDbContextAsync();
        var row = Assert.Single(await context.PlaybackHistory.AsNoTracking().ToListAsync());
        Assert.True(row.IsCompleted);
        Assert.Equal(0, row.LastPositionTicks);
    }

    [Fact]
    public async Task StartPlaybackAsync_UsesSuppliedFileName()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var service = new HistoryService(fixture.Factory);
        var filePath = Path.Combine(fixture.Root, "Videos", "Physical.mp4");

        var result = await service.StartPlaybackAsync(
            filePath,
            "DisplayName.mp4",
            TimeSpan.FromMinutes(2));

        Assert.True(result.IsSuccess);
        await using var context = await fixture.Factory.CreateDbContextAsync();
        var row = Assert.Single(await context.PlaybackHistory.AsNoTracking().ToListAsync());
        Assert.Equal("DisplayName.mp4", row.FileName);
    }

    [Fact]
    public async Task SetAvailableFalse_SkipsDatabaseQueries()
    {
        var factory = new ThrowingDbContextFactory();
        var service = new HistoryService(factory);
        service.SetAvailable(false);

        var resume = await service.GetResumeInfoAsync("Movie.mp4");
        var save = await service.SaveProgressAsync(CreateSnapshot("Movie.mp4", TimeSpan.FromMinutes(1), 1, 1));

        Assert.True(resume.IsSuccess);
        Assert.Null(resume.Value);
        Assert.False(save.IsSuccess);
        Assert.Equal(ErrorCode.DatabaseUnavailable, save.ErrorCode);
        Assert.Equal(0, factory.CreateCount);
    }

    private static PlaybackSnapshot CreateSnapshot(
        string filePath,
        TimeSpan duration,
        double positionSeconds,
        long sessionId)
        => new(
            sessionId,
            PlaybackState.Playing,
            filePath,
            TimeSpan.FromSeconds(positionSeconds),
            duration,
            80,
            false,
            1.0,
            true,
            true,
            true,
            DateTimeOffset.UtcNow);

    private sealed class DatabaseFixture : IAsyncDisposable
    {
        private DatabaseFixture(
            string root,
            IDbContextFactory<VideoPlayerDbContext> factory)
        {
            Root = root;
            Factory = factory;
        }

        public string Root { get; }

        public IDbContextFactory<VideoPlayerDbContext> Factory { get; }

        public static async Task<DatabaseFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var paths = new AppPaths(root);
            var initialization = await new DatabaseInitializer(paths).InitializeAsync();
            if (!initialization.IsSuccess)
            {
                throw new InvalidOperationException(initialization.TechnicalMessage);
            }

            var options = new DbContextOptionsBuilder<VideoPlayerDbContext>()
                .UseSqlite("Data Source=" + paths.DatabasePath)
                .Options;
            return new DatabaseFixture(root, new TestDbContextFactory(options));
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(Root, true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingDbContextFactory : IDbContextFactory<VideoPlayerDbContext>
    {
        public int CreateCount { get; private set; }

        public VideoPlayerDbContext CreateDbContext()
        {
            CreateCount++;
            throw new InvalidOperationException("Factory must not be called.");
        }

        public Task<VideoPlayerDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }

    private sealed class TestDbContextFactory(
        DbContextOptions<VideoPlayerDbContext> options)
        : IDbContextFactory<VideoPlayerDbContext>
    {
        public VideoPlayerDbContext CreateDbContext()
        {
            return new VideoPlayerDbContext(options);
        }

        public Task<VideoPlayerDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }
}
