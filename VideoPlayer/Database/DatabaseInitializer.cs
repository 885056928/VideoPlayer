using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VideoPlayer.Configuration;
using VideoPlayer.Enums;
using VideoPlayer.Models;

namespace VideoPlayer.Database;

public sealed class DatabaseInitializer
{
    private const int SqliteCorruptErrorCode = 11;
    private const int SqliteNotDatabaseErrorCode = 26;
    private readonly AppPaths _paths;

    public DatabaseInitializer(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<ServiceResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _paths.EnsureCoreDirectories();
            await MigrateAsync(cancellationToken);
            return ServiceResult.Success();
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            return CreateCancelledResult(exception);
        }
        catch (Exception firstFailure)
        {
            // 锁定、超时和迁移冲突都不表示数据损坏，只有 SQLite 明确报告损坏时才允许重建。
            if (firstFailure is not SqliteException
                {
                    SqliteErrorCode: SqliteCorruptErrorCode or SqliteNotDatabaseErrorCode
                })
            {
                return ServiceResult.Failure(
                    ErrorCode.DatabaseUnavailable,
                    "播放历史暂时不可用。",
                    firstFailure.Message,
                    firstFailure);
            }

            try
            {
                await RecoverAsync(cancellationToken);
                await MigrateAsync(cancellationToken);
                return ServiceResult.Success();
            }
            catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
            {
                return CreateCancelledResult(exception);
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

    private static ServiceResult CreateCancelledResult(OperationCanceledException exception)
        => ServiceResult.Failure(
            ErrorCode.Cancelled,
            "数据库初始化已取消。",
            exception.Message,
            exception);

    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));
        var options = new DbContextOptionsBuilder<VideoPlayerDbContext>()
            .UseSqlite(GetConnectionString())
            .Options;
        await using var context = new VideoPlayerDbContext(options);
        await context.Database.MigrateAsync(timeoutCts.Token);
    }

    private string GetConnectionString()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _paths.DatabasePath,
            DefaultTimeout = 2,
            Pooling = false
        };
        return builder.ToString();
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        // 先响应取消，再执行有文件副作用的恢复操作，避免把健康数据库移走。
        cancellationToken.ThrowIfCancellationRequested();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_paths.DatabasePath))
        {
            var backup = Path.Combine(
                _paths.RootDirectory,
                "VideoPlayer.corrupt." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".db");
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(_paths.DatabasePath, backup, overwrite: false);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
