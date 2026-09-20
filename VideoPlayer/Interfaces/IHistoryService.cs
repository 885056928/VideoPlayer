using VideoPlayer.Models;

namespace VideoPlayer.Interfaces;

public interface IHistoryService
{
    bool IsAvailable { get; }

    Task<ServiceResult<ResumeInfo?>> GetResumeInfoAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> StartPlaybackAsync(
        string filePath,
        string fileName,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> SaveProgressAsync(
        PlaybackSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> MarkCompletedAsync(
        string filePath,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> ClearCompletedAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> FlushAsync(CancellationToken cancellationToken = default);
}
