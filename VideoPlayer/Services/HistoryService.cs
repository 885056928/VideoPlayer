using System.Collections.Concurrent;
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
    private static readonly TimeSpan DatabaseTimeout = TimeSpan.FromSeconds(2);
    private readonly IDbContextFactory<VideoPlayerDbContext> _contextFactory;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<string, long> _latestSessionByPath =
        new(StringComparer.Ordinal);

    public HistoryService(IDbContextFactory<VideoPlayerDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    private volatile bool _isAvailable = true;

    public bool IsAvailable => _isAvailable;

    public void SetAvailable(bool isAvailable)
    {
        _isAvailable = isAvailable;
    }

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
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(DatabaseTimeout);
            var token = timeoutCts.Token;
            await using var context = await _contextFactory.CreateDbContextAsync(token);
            var row = await context.PlaybackHistory.AsNoTracking()
                .SingleOrDefaultAsync(x => x.PathKey == key, token);
            return ServiceResult<ResumeInfo?>.Success(row is null ? null : new ResumeInfo(
                row.FilePath,
                TimeSpan.FromTicks(row.DurationTicks),
                TimeSpan.FromTicks(row.LastPositionTicks),
                row.IsCompleted,
                row.LastPlayedAtUtc,
                row.PlayCount));
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            _isAvailable = false;
            return ServiceResult<ResumeInfo?>.Failure(
                ErrorCode.DatabaseUnavailable,
                "播放历史查询超时。",
                filePath,
                exception);
        }
        catch (OperationCanceledException exception)
        {
            return ServiceResult<ResumeInfo?>.Failure(
                ErrorCode.Cancelled, "播放历史查询已取消。", filePath, exception);
        }
        catch (Exception exception)
        {
            _isAvailable = false;
            return ServiceResult<ResumeInfo?>.Failure(
                ErrorCode.DatabaseUnavailable, "播放历史不可用。", filePath, exception);
        }
    }

    public Task<ServiceResult> StartPlaybackAsync(
        string filePath,
        string fileName,
        TimeSpan duration,
        long mediaSessionId = 0,
        CancellationToken cancellationToken = default)
        => UpsertAsync(filePath, fileName, mediaSessionId, row =>
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

        var completed = snapshot.Duration > TimeSpan.Zero
            && snapshot.Position >= snapshot.Duration * 0.95;
        return UpsertAsync(snapshot.MediaPath, null, snapshot.MediaSessionId, row =>
        {
            row.DurationTicks = snapshot.Duration.Ticks;
            row.LastPositionTicks = completed ? 0 : snapshot.Position.Ticks;
            row.LastPlayedAtUtc = DateTimeOffset.UtcNow;
            row.IsCompleted = completed;
        }, cancellationToken);
    }

    public Task<ServiceResult> MarkCompletedAsync(
        string filePath,
        TimeSpan duration,
        long mediaSessionId = 0,
        CancellationToken cancellationToken = default)
        => UpsertAsync(filePath, null, mediaSessionId, row =>
        {
            row.DurationTicks = duration.Ticks;
            row.LastPositionTicks = 0;
            row.IsCompleted = true;
            row.LastPlayedAtUtc = DateTimeOffset.UtcNow;
        }, cancellationToken);

    public Task<ServiceResult> ClearCompletedAsync(
        string filePath,
        long mediaSessionId = 0,
        CancellationToken cancellationToken = default)
        => UpsertAsync(filePath, null, mediaSessionId, row =>
        {
            row.LastPositionTicks = 0;
            row.IsCompleted = false;
        }, cancellationToken);

    public Task<ServiceResult> FlushAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ServiceResult.Success());

    private async Task<ServiceResult> UpsertAsync(
        string filePath,
        string? fileName,
        long mediaSessionId,
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
            if (!IsLatestSession(key, mediaSessionId))
            {
                return ServiceResult.Success();
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(DatabaseTimeout);
            var token = timeoutCts.Token;
            await using var context = await _contextFactory.CreateDbContextAsync(token);
            var row = await context.PlaybackHistory
                .SingleOrDefaultAsync(x => x.PathKey == key, token);
            if (row is null)
            {
                row = new PlaybackHistory
                {
                    FilePath = normalized,
                    PathKey = key,
                    FileName = string.IsNullOrWhiteSpace(fileName)
                        ? Path.GetFileName(normalized)
                        : fileName
                };
                context.PlaybackHistory.Add(row);
            }

            update(row);
            await context.SaveChangesAsync(token);
            return ServiceResult.Success();
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            _isAvailable = false;
            return ServiceResult.Failure(
                ErrorCode.DatabaseUnavailable,
                "播放历史保存超时。",
                filePath,
                exception);
        }
        catch (OperationCanceledException exception)
        {
            return ServiceResult.Failure(
                ErrorCode.Cancelled, "播放历史保存已取消。", filePath, exception);
        }
        catch (Exception exception)
        {
            _isAvailable = false;
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

    private bool IsLatestSession(string pathKey, long mediaSessionId)
    {
        if (mediaSessionId <= 0)
        {
            return true;
        }

        while (true)
        {
            if (_latestSessionByPath.TryGetValue(pathKey, out var latestSessionId))
            {
                if (mediaSessionId < latestSessionId)
                {
                    return false;
                }

                if (mediaSessionId == latestSessionId ||
                    _latestSessionByPath.TryUpdate(
                        pathKey,
                        mediaSessionId,
                        latestSessionId))
                {
                    return true;
                }
            }
            else if (_latestSessionByPath.TryAdd(pathKey, mediaSessionId))
            {
                return true;
            }
        }
    }
}