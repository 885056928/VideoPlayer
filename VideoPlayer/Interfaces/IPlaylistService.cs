using VideoPlayer.Models;

namespace VideoPlayer.Interfaces;

public interface IPlaylistService
{
    IReadOnlyList<PlaylistItem> Items { get; }
    int CurrentIndex { get; }
    PlaylistItem? Current { get; }

    event EventHandler? PlaylistChanged;
    event EventHandler? CurrentChanged;

    Task<ServiceResult<IReadOnlyList<PlaylistItem>>> AddFilesAsync(
        IEnumerable<string> paths,
        bool recursive,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> AddFolderAsync(
        string folderPath,
        bool recursive,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> RemoveAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> ClearAsync(CancellationToken cancellationToken = default);

    ServiceResult<PlaylistItem> GetNext(bool wrap = false);

    ServiceResult<PlaylistItem> GetPrevious(bool wrap = false);

    ServiceResult SetCurrent(string filePath);
}
