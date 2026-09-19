using System.IO;

using System.Collections.ObjectModel;

using VideoPlayer.Enums;
using VideoPlayer.Helpers;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.Services;

public sealed class PlaylistService : IPlaylistService
{
    private readonly IFileService _fileService;
    private readonly List<PlaylistItem> _items = [];
    private readonly HashSet<string> _pathKeys = new(StringComparer.Ordinal);
    private readonly ReadOnlyCollection<PlaylistItem> _itemsView;

    public PlaylistService(IFileService fileService)
    {
        ArgumentNullException.ThrowIfNull(fileService);
        _fileService = fileService;
        _itemsView = _items.AsReadOnly();
    }

    public IReadOnlyList<PlaylistItem> Items => _itemsView;

    public int CurrentIndex { get; private set; } = -1;

    public PlaylistItem? Current => CurrentIndex >= 0 && CurrentIndex < _items.Count
        ? _items[CurrentIndex]
        : null;

    public event EventHandler? PlaylistChanged;

    public event EventHandler? CurrentChanged;

    public async Task<ServiceResult<IReadOnlyList<PlaylistItem>>> AddFilesAsync(
        IEnumerable<string> paths,
        bool recursive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var added = new List<PlaylistItem>();
        var addedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rawPath in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Directory.Exists(rawPath))
            {
                var folderResult = await _fileService.EnumerateVideosAsync(
                    rawPath,
                    recursive,
                    cancellationToken).ConfigureAwait(false);

                if (!folderResult.IsSuccess)
                {
                    if (folderResult.ErrorCode == ErrorCode.Cancelled)
                    {
                        return ServiceResult<IReadOnlyList<PlaylistItem>>.Failure(
                            folderResult.ErrorCode,
                            folderResult.UserMessage,
                            folderResult.TechnicalMessage,
                            folderResult.Exception);
                    }

                    continue;
                }

                if (folderResult.Value is not null)
                {
                    AddPaths(folderResult.Value, added, addedKeys);
                }
            }
            else if (File.Exists(rawPath) && _fileService.IsSupportedVideo(rawPath))
            {
                AddPaths([rawPath], added, addedKeys);
            }
        }

        if (added.Count == 0)
        {
            return ServiceResult<IReadOnlyList<PlaylistItem>>.Failure(
                ErrorCode.FileNotFound,
                "没有找到可加入的视频。",
                "No supported files.");
        }

        _items.AddRange(added);
        foreach (var key in addedKeys)
        {
            _pathKeys.Add(key);
        }

        var currentChanged = CurrentIndex < 0;
        if (currentChanged)
        {
            CurrentIndex = 0;
        }

        PlaylistChanged?.Invoke(this, EventArgs.Empty);
        if (currentChanged)
        {
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }

        return ServiceResult<IReadOnlyList<PlaylistItem>>.Success(added.AsReadOnly());
    }

    public Task<ServiceResult> AddFolderAsync(
        string folderPath,
        bool recursive,
        CancellationToken cancellationToken = default)
    {
        return AddFolderCoreAsync(folderPath, recursive, cancellationToken);
    }

    public Task<ServiceResult> RemoveAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var key = PathHelper.CreatePathKey(filePath);
        var index = _items.FindIndex(item => PathHelper.CreatePathKey(item.FilePath) == key);
        if (index < 0)
        {
            return Task.FromResult(ServiceResult.Failure(
                ErrorCode.FileNotFound,
                "播放列表中没有该视频。",
                filePath));
        }

        var previousIndex = CurrentIndex;
        var previousCurrent = Current;
        _items.RemoveAt(index);
        _pathKeys.Remove(key);

        if (_items.Count == 0)
        {
            CurrentIndex = -1;
        }
        else if (index < CurrentIndex)
        {
            CurrentIndex--;
        }
        else if (CurrentIndex >= _items.Count)
        {
            CurrentIndex = _items.Count - 1;
        }

        var currentChanged = previousIndex != CurrentIndex ||
            !ReferenceEquals(previousCurrent, Current);

        PlaylistChanged?.Invoke(this, EventArgs.Empty);
        if (currentChanged)
        {
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }

        return Task.FromResult(ServiceResult.Success());
    }

    public Task<ServiceResult> ClearAsync(CancellationToken cancellationToken = default)
    {
        var hadItems = _items.Count > 0;
        _items.Clear();
        _pathKeys.Clear();
        CurrentIndex = -1;

        if (hadItems)
        {
            PlaylistChanged?.Invoke(this, EventArgs.Empty);
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }

        return Task.FromResult(ServiceResult.Success());
    }

    public ServiceResult<PlaylistItem> GetNext(bool wrap = false)
    {
        if (_items.Count == 0)
        {
            return ServiceResult<PlaylistItem>.Failure(
                ErrorCode.InvalidState,
                "播放列表为空。",
                "Empty playlist");
        }

        if (CurrentIndex < 0)
        {
            return SetCurrentIndex(0);
        }

        var target = CurrentIndex + 1;
        if (target >= _items.Count)
        {
            if (!wrap)
            {
                return ServiceResult<PlaylistItem>.Failure(
                    ErrorCode.InvalidState,
                    "已经是最后一项。",
                    "No next item");
            }

            target = 0;
        }

        return SetCurrentIndex(target);
    }

    public ServiceResult<PlaylistItem> GetPrevious(bool wrap = false)
    {
        if (_items.Count == 0)
        {
            return ServiceResult<PlaylistItem>.Failure(
                ErrorCode.InvalidState,
                "播放列表为空。",
                "Empty playlist");
        }

        if (CurrentIndex < 0)
        {
            return wrap
                ? SetCurrentIndex(_items.Count - 1)
                : ServiceResult<PlaylistItem>.Failure(
                    ErrorCode.InvalidState,
                    "已经是第一项。",
                    "No previous item");
        }

        var target = CurrentIndex - 1;
        if (target < 0)
        {
            if (!wrap)
            {
                return ServiceResult<PlaylistItem>.Failure(
                    ErrorCode.InvalidState,
                    "已经是第一项。",
                    "No previous item");
            }

            target = _items.Count - 1;
        }

        return SetCurrentIndex(target);
    }

    public ServiceResult SetCurrent(string filePath)
    {
        string key;
        try
        {
            key = PathHelper.CreatePathKey(filePath);
        }
        catch (ArgumentException)
        {
            return ServiceResult.Failure(
                ErrorCode.InvalidState,
                "视频不在播放列表中。",
                filePath);
        }

        var index = _items.FindIndex(item => PathHelper.CreatePathKey(item.FilePath) == key);
        if (index < 0)
        {
            return ServiceResult.Failure(
                ErrorCode.InvalidState,
                "视频不在播放列表中。",
                filePath);
        }

        if (CurrentIndex != index)
        {
            CurrentIndex = index;
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }

        return ServiceResult.Success();
    }

    internal void SetCurrentForTest(IEnumerable<string> paths, int index)
    {
        ArgumentNullException.ThrowIfNull(paths);

        _items.Clear();
        _pathKeys.Clear();

        foreach (var path in paths)
        {
            var item = new PlaylistItem { FilePath = path };
            _items.Add(item);
            _pathKeys.Add(PathHelper.CreatePathKey(path));
        }

        CurrentIndex = index;
    }

    private async Task<ServiceResult> AddFolderCoreAsync(
        string folderPath,
        bool recursive,
        CancellationToken cancellationToken)
    {
        var result = await AddFilesAsync(
            [folderPath],
            recursive,
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? ServiceResult.Success()
            : ServiceResult.Failure(
                result.ErrorCode,
                result.UserMessage,
                result.TechnicalMessage,
                result.Exception);
    }

    private void AddPaths(
        IEnumerable<string> paths,
        List<PlaylistItem> added,
        HashSet<string> addedKeys)
    {
        foreach (var path in paths)
        {
            var normalized = PathHelper.NormalizePath(path);
            var key = PathHelper.CreatePathKey(normalized);

            if (_pathKeys.Contains(key) || !addedKeys.Add(key))
            {
                continue;
            }

            added.Add(new PlaylistItem { FilePath = normalized });
        }
    }

    private ServiceResult<PlaylistItem> SetCurrentIndex(int index)
    {
        if (CurrentIndex != index)
        {
            CurrentIndex = index;
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }

        return ServiceResult<PlaylistItem>.Success(_items[index]);
    }
}
