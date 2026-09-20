using System.IO;
using Microsoft.EntityFrameworkCore;
using VideoPlayer.Database;
using VideoPlayer.Enums;
using VideoPlayer.Helpers;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.Services;

public sealed class HistoryService : IHistoryService
{
    private readonly IDbContextFactory<VideoPlayerDbContext> _contextFactory;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public HistoryService(IDbContextFactory<VideoPlayerDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public bool IsAvailable { get; private set; } = true;

    public async Task<ServiceResult<ResumeInfo?>> GetResumeInfoAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return ServiceResult<ResumeInfo?>.Success(null);
        }

        try
        {
            var key = PathHelper.CreatePathKey(filePath);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var row = await context.PlaybackHistory.AsNoTracking()
                .SingleOrDefaultAsync(x => x.PathKey == key, cancellationToken);
            return ServiceResult<ResumeInfo?>.Success(row is null ? null : new ResumeInfo(
                row.FilePath,
                TimeSpan.FromTicks(row.DurationTicks),
                TimeSpan.FromTicks(row.LastPositionTicks),
                row.IsCompleted,
                row.LastPlayedAtUtc,
                row.PlayCount));
        }
        catch (OperationCanceledException exception)
        {
            return ServiceResult<ResumeInfo?>.Failure(
                ErrorCode.Cancelled, "播放历史查询已取消。", filePath, exception);
        }
        catch (Exception exception)
        {
            IsAvailable = false;
            return ServiceResult<ResumeInfo?>.Failure(
                ErrorCode.DatabaseUnavailable, "播放历史不可用。", filePath, exception);
        }
    }

    public Task<ServiceResult> StartPlaybackAsync(
        string filePath,
        string fileName,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
        => UpsertAsync(filePath, row =>
        {
            row.DurationTicks = duration.Ticks;
            row.LastPlayedAtUtc = DateTimeOffset.UtcNow;
            row.PlayCount += 1;
            row.IsCompleted = false;
        }, cancellationToken);

    public Task<ServiceResult> SaveProgressAsync(
        PlaybackSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (snapshot.MediaPath is null)
        {
            return Task.FromResult(ServiceResult.Success());
        }

        return UpsertAsync(snapshot.MediaPath, row =>
        {
            row.DurationTicks = snapshot.Duration.Ticks;
            row.LastPositionTicks = snapshot.Position.Ticks;
            row.LastPlayedAtUtc = DateTimeOffset.UtcNow;
        }, cancellationToken);
    }

    public Task<ServiceResult> MarkCompletedAsync(
        string filePath,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
        => UpsertAsync(filePath, row =>
        {
            row.DurationTicks = duration.Ticks;
            row.LastPositionTicks = 0;
            row.IsCompleted = true;
            row.LastPlayedAtUtc = DateTimeOffset.UtcNow;
        }, cancellationToken);

    public Task<ServiceResult> ClearCompletedAsync(
        string filePath,
        CancellationToken cancellationToken = default)
        => UpsertAsync(filePath, row =>
        {
            row.LastPositionTicks = 0;
            row.IsCompleted = false;
        }, cancellationToken);

    public Task<ServiceResult> FlushAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ServiceResult.Success());

    private async Task<ServiceResult> UpsertAsync(
        string filePath,
        Action<PlaybackHistory> update,
        CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return ServiceResult.Failure(
                ErrorCode.DatabaseUnavailable, "播放历史不可用。", filePath);
        }

        var gateAcquired = false;
        try
        {
            await _writeGate.WaitAsync(cancellationToken);
            gateAcquired = true;
            var normalized = PathHelper.NormalizePath(filePath);
            var key = PathHelper.CreatePathKey(normalized);
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var row = await context.PlaybackHistory
                .SingleOrDefaultAsync(x => x.PathKey == key, cancellationToken);
            if (row is null)
            {
                row = new PlaybackHistory
                {
                    FilePath = normalized,
                    PathKey = key,
                    FileName = Path.GetFileName(normalized)
                };
                context.PlaybackHistory.Add(row);
            }

            update(row);
            await context.SaveChangesAsync(cancellationToken);
            return ServiceResult.Success();
        }
        catch (OperationCanceledException exception)
        {
            return ServiceResult.Failure(
                ErrorCode.Cancelled, "播放历史保存已取消。", filePath, exception);
        }
        catch (Exception exception)
        {
            IsAvailable = false;
            return ServiceResult.Failure(
                ErrorCode.DatabaseUnavailable, "播放历史保存失败。", filePath, exception);
        }
        finally
        {
            if (gateAcquired)
            {
                _writeGate.Release();
            }
        }
    }
}
