using VideoPlayer.Models;

namespace VideoPlayer.Interfaces;

public interface IFileService
{
    bool IsSupportedVideo(string filePath);

    string NormalizePath(string filePath);

    Task<ServiceResult<IReadOnlyList<string>>> EnumerateVideosAsync(
        string folderPath,
        bool recursive,
        CancellationToken cancellationToken = default);
}
