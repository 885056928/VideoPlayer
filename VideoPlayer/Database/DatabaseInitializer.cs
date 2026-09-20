using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VideoPlayer.Configuration;
using VideoPlayer.Enums;
using VideoPlayer.Models;

namespace VideoPlayer.Database;

public sealed class DatabaseInitializer
{
    private readonly AppPaths _paths;

    public DatabaseInitializer(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<ServiceResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureCoreDirectories();
        try
        {
            await MigrateAsync(cancellationToken);
            return ServiceResult.Success();
        }
        catch (Exception firstFailure)
        {
            try
            {
                await RecoverAsync(cancellationToken);
                await MigrateAsync(cancellationToken);
                return ServiceResult.Success();
            }
            catch (Exception recoveryFailure)
            {
                return ServiceResult.Failure(
                    ErrorCode.DatabaseUnavailable,
                    "播放历史暂时不可用。",
                    "Initial: " + firstFailure.Message + "; Recovery: " + recoveryFailure.Message,
                    recoveryFailure);
            }
        }
    }

    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<VideoPlayerDbContext>()
            .UseSqlite("Data Source=" + _paths.DatabasePath)
            .Options;
        await using var context = new VideoPlayerDbContext(options);
        await context.Database.MigrateAsync(cancellationToken);
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_paths.DatabasePath))
        {
            var backup = Path.Combine(
                _paths.RootDirectory,
                "VideoPlayer.corrupt." + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".db");
            File.Move(_paths.DatabasePath, backup, overwrite: false);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
