using VideoPlayer.Models;

namespace VideoPlayer.Interfaces;

public interface ISubtitleService
{
    Task<ServiceResult<string>> FindAutoSubtitleAsync(
        string mediaPath,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> ValidateSubtitleAsync(
        string subtitlePath,
        CancellationToken cancellationToken = default);
}
