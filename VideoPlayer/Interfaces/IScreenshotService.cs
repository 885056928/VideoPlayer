using VideoPlayer.Models;

namespace VideoPlayer.Interfaces;

public interface IScreenshotService
{
    Task<ServiceResult<string>> CaptureAsync(
        PlaybackSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
