using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VideoPlayer.Configuration;
using VideoPlayer.Database;
using VideoPlayer.Enums;
using VideoPlayer.Models;
using Xunit;

namespace VideoPlayer.Tests.Integration.Database;

public sealed class DatabaseInitializerTests
{
    [Fact]
    public async Task InitializeAsync_CreatesDatabaseAndMigrates()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new AppPaths(root);
            var result = await new DatabaseInitializer(paths).InitializeAsync();

            Assert.True(result.IsSuccess);
            Assert.True(File.Exists(paths.DatabasePath));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task UniquePathKey_PreventsDuplicateRecords()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new AppPaths(root);
            await new DatabaseInitializer(paths).InitializeAsync();
            var options = new DbContextOptionsBuilder<VideoPlayerDbContext>()
                .UseSqlite("Data Source=" + paths.DatabasePath)
                .Options;
            await using var context = new VideoPlayerDbContext(options);
            context.PlaybackHistory.Add(new PlaybackHistory
            {
                FilePath = "C:\\Videos\\Movie.mp4",
                PathKey = "C:\\VIDEOS\\MOVIE.MP4",
                FileName = "Movie.mp4",
                LastPlayedAtUtc = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
            context.PlaybackHistory.Add(new PlaybackHistory
            {
                FilePath = "c:\\videos\\movie.mp4",
                PathKey = "C:\\VIDEOS\\MOVIE.MP4",
                FileName = "movie.mp4",
                LastPlayedAtUtc = DateTimeOffset.UtcNow
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task InitializeAsync_CoreDirectoryFailure_ReturnsDatabaseUnavailable()
    {
        var rootFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(rootFile, "not a directory");
        try
        {
            var paths = new AppPaths(rootFile);

            var result = await new DatabaseInitializer(paths).InitializeAsync();

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCode.DatabaseUnavailable, result.ErrorCode);
        }
        finally
        {
            File.Delete(rootFile);
        }
    }

    [Fact]
    public async Task InitializeAsync_RecoversCorruptDatabase()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new AppPaths(root);
            await File.WriteAllTextAsync(paths.DatabasePath, "not a sqlite database");

            var result = await new DatabaseInitializer(paths).InitializeAsync();

            Assert.True(result.IsSuccess, result.TechnicalMessage);
            Assert.True(File.Exists(paths.DatabasePath));
            var backup = Assert.Single(Directory.GetFiles(root, "VideoPlayer.corrupt.*.db"));
            var timestamp = Path.GetFileNameWithoutExtension(backup)
                .Split('.')
                .Last();
            Assert.Matches("^\\d{17}$", timestamp);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }
}
