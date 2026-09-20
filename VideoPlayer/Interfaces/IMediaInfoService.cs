using VideoPlayer.Models;

namespace VideoPlayer.Interfaces;

public interface IMediaInfoService
{
    Task<ServiceResult<VideoInfo>> GetCurrentMediaInfoAsync(
        PlaybackSnapshot snapshot,
        IReadOnlyList<PlayerTrackInfo> tracks,
        CancellationToken cancellationToken = default);
}
