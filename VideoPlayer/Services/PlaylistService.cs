using System.IO;

using VideoPlayer.Enums;
using VideoPlayer.Helpers;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.Services;

public sealed class PlaylistService : IPlaylistService
{
    private readonly IFileService _fileService;
    private readonly object _sync = new();
    private readonly List<PlaylistItem> _items = [];
    private readonly HashSet<string> _pathKeys = new(StringComparer.Ordinal);

    public PlaylistService(IFileService fileService)
    {
        ArgumentNullException.ThrowIfNull(fileService);
        _fileService = fileService;
    }

    public IReadOnlyList<PlaylistItem> Items
    {
        get
        {
            lock (_sync)
            {
                return _items.ToArray();
            }
        }
    }

    public int CurrentIndex
    {
        get
        {
            lock (_sync)
            {
                return _currentIndex;
            }
        }
    }

    public PlaylistItem? Current
    {
        get
        {
            lock (_sync)
            {
                return _currentIndex >= 0 && _currentIndex < _items.Count
                    ? _items[_currentIndex]
                    : null;
            }
        }
    }

    private int _currentIndex = -1;

    public event EventHandler? PlaylistChanged;
    public event EventHandler? CurrentChanged;

    public async Task<ServiceResult<IReadOnlyList<PlaylistItem>>> AddFilesAsync(
        IEnumerable<string> paths,
        bool recursive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var eventContext = SynchronizationContext.Current;
        return await Task.Run(
            () => AddFilesCoreAsync(paths, recursive, cancellationToken, eventContext),
            CancellationToken.None);
    }

    public async Task<ServiceResult> AddFolderAsync(
        string folderPath,
        bool recursive,
        CancellationToken cancellationToken = default)
    {
        var result = await AddFilesAsync([folderPath], recursive, cancellationToken);
        return result.IsSuccess
            ? ServiceResult.Success()
            : ServiceResult.Failure(
                result.ErrorCode,
                result.UserMessage,
                result.TechnicalMessage,
                result.Exception);
    }
    public Task<ServiceResult> RemoveAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(CreateCancelledResult("Remove cancelled."));
        }

        string key;
        try
        {
            key = PathHelper.CreatePathKey(filePath);
        }
        catch (ArgumentException)
        {
            return Task.FromResult(ServiceResult.Failure(
                ErrorCode.FileNotFound,
                "播放列表中没有该视频。",
                filePath));
        }

        var eventContext = SynchronizationContext.Current;
        var currentChanged = false;
        lock (_sync)
        {
            var index = _items.FindIndex(
                item => string.Equals(
                    PathHelper.CreatePathKey(item.FilePath),
                    key,
                    StringComparison.Ordinal));
            if (index < 0)
            {
                return Task.FromResult(ServiceResult.Failure(
                    ErrorCode.FileNotFound,
                    "播放列表中没有该视频。",
                    filePath));
            }

            var previousIndex = _currentIndex;
            var previousCurrent = CurrentForLocked();
            _items.RemoveAt(index);
            _pathKeys.Remove(key);
            if (_items.Count == 0)
            {
                _currentIndex = -1;
            }
            else if (index < _currentIndex)
            {
                _currentIndex--;
            }
            else if (_currentIndex >= _items.Count)
            {
                _currentIndex = _items.Count - 1;
            }

            currentChanged = previousIndex != _currentIndex
                || !ReferenceEquals(previousCurrent, CurrentForLocked());
        }

        PublishChanges(eventContext, currentChanged);
        return Task.FromResult(ServiceResult.Success());
    }

    public Task<ServiceResult> ClearAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(CreateCancelledResult("Clear cancelled."));
        }

        var eventContext = SynchronizationContext.Current;
        var hadItems = false;
        lock (_sync)
        {
            hadItems = _items.Count > 0;
            _items.Clear();
            _pathKeys.Clear();
            _currentIndex = -1;
        }

        if (hadItems)
        {
            PublishChanges(eventContext, currentChanged: true);
        }

        return Task.FromResult(ServiceResult.Success());
    }

    public ServiceResult<PlaylistItem> GetNext(bool wrap = false)
    {
        var eventContext = SynchronizationContext.Current;
        lock (_sync)
        {
            if (_items.Count == 0)
            {
                return ServiceResult<PlaylistItem>.Failure(
                    ErrorCode.InvalidState,
                    "播放列表为空。",
                    "Empty playlist");
            }

            if (_currentIndex < 0)
            {
                return SetCurrentIndexCore(0, eventContext);
            }

            var target = _currentIndex + 1;
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

            return SetCurrentIndexCore(target, eventContext);
        }
    }

    public ServiceResult<PlaylistItem> GetPrevious(bool wrap = false)
    {
        var eventContext = SynchronizationContext.Current;
        lock (_sync)
        {
            if (_items.Count == 0)
            {
                return ServiceResult<PlaylistItem>.Failure(
                    ErrorCode.InvalidState,
                    "播放列表为空。",
                    "Empty playlist");
            }

            if (_currentIndex < 0)
            {
                return wrap
                    ? SetCurrentIndexCore(_items.Count - 1, eventContext)
                    : ServiceResult<PlaylistItem>.Failure(
                        ErrorCode.InvalidState,
                        "已经是第一项。",
                        "No previous item");
            }

            var target = _currentIndex - 1;
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

            return SetCurrentIndexCore(target, eventContext);
        }
    }
    public ServiceResult SetCurrent(string filePath)
    {
        var eventContext = SynchronizationContext.Current;
        lock (_sync)
        {
            var index = FindIndexLocked(filePath);
            if (index < 0)
            {
                return ServiceResult.Failure(
                    ErrorCode.InvalidState,
                    "视频不在播放列表中。",
                    filePath);
            }

            if (_currentIndex != index)
            {
                _currentIndex = index;
                PublishCurrentChange(eventContext);
            }

            return ServiceResult.Success();
        }
    }

    public void NotifyCurrentItemChanged()
    {
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void SetCurrentForTest(IEnumerable<string> paths, int index)
    {
        ArgumentNullException.ThrowIfNull(paths);
        lock (_sync)
        {
            _items.Clear();
            _pathKeys.Clear();
            foreach (var path in paths)
            {
                var item = new PlaylistItem { FilePath = path };
                _items.Add(item);
                _pathKeys.Add(PathHelper.CreatePathKey(path));
            }

            _currentIndex = index;
        }

        RefreshCurrentFlags();
    }

    private async Task<ServiceResult<IReadOnlyList<PlaylistItem>>> AddFilesCoreAsync(
        IEnumerable<string> paths,
        bool recursive,
        CancellationToken cancellationToken,
        SynchronizationContext? eventContext)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidates = new List<string>();
            var candidateKeys = new HashSet<string>(StringComparer.Ordinal);
            ServiceResult<IReadOnlyList<string>>? folderFailure = null;

            foreach (var rawPath in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Directory.Exists(rawPath))
                {
                    var folderResult = await _fileService.EnumerateVideosAsync(
                        rawPath,
                        recursive,
                        cancellationToken);
                    if (!folderResult.IsSuccess)
                    {
                        if (folderResult.ErrorCode == ErrorCode.Cancelled)
                        {
                            return CreateCancelledResult<IReadOnlyList<PlaylistItem>>(
                                folderResult.TechnicalMessage,
                                folderResult.Exception);
                        }

                        folderFailure ??= folderResult;
                        continue;
                    }

                    if (folderResult.Value is not null)
                    {
                        AddCandidatePaths(folderResult.Value, candidates, candidateKeys);
                    }
                }
                else if (File.Exists(rawPath) && _fileService.IsSupportedVideo(rawPath))
                {
                    AddCandidatePaths([rawPath], candidates, candidateKeys);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var added = new List<PlaylistItem>();
            var currentChanged = false;
            lock (_sync)
            {
                foreach (var path in candidates)
                {
                    var normalized = PathHelper.NormalizePath(path);
                    var key = PathHelper.CreatePathKey(normalized);
                    if (!_pathKeys.Add(key))
                    {
                        continue;
                    }

                    var item = new PlaylistItem { FilePath = normalized };
                    _items.Add(item);
                    added.Add(item);
                }

                if (added.Count > 0 && _currentIndex < 0)
                {
                    _currentIndex = 0;
                    currentChanged = true;
                }
            }

            if (added.Count == 0)
            {
                if (folderFailure is not null)
                {
                    return ServiceResult<IReadOnlyList<PlaylistItem>>.Failure(
                        folderFailure.ErrorCode,
                        folderFailure.UserMessage,
                        folderFailure.TechnicalMessage,
                        folderFailure.Exception);
                }

                return ServiceResult<IReadOnlyList<PlaylistItem>>.Failure(
                    ErrorCode.FileNotFound,
                    "没有找到可加入的视频。",
                    "No supported files.");
            }

            PublishChanges(eventContext, currentChanged);
            return ServiceResult<IReadOnlyList<PlaylistItem>>.Success(added.AsReadOnly());
        }
        catch (OperationCanceledException exception)
        {
            return CreateCancelledResult<IReadOnlyList<PlaylistItem>>(
                "Add files cancelled.",
                exception);
        }
        catch (Exception exception)
        {
            return ServiceResult<IReadOnlyList<PlaylistItem>>.Failure(
                ErrorCode.Unknown,
                "扫描文件夹失败。",
                "Failed to add files.",
                exception);
        }
    }
    private static void AddCandidatePaths(
        IEnumerable<string> paths,
        List<string> candidates,
        HashSet<string> candidateKeys)
    {
        foreach (var path in paths)
        {
            var normalized = PathHelper.NormalizePath(path);
            var key = PathHelper.CreatePathKey(normalized);
            if (candidateKeys.Add(key))
            {
                candidates.Add(normalized);
            }
        }
    }

    private ServiceResult<PlaylistItem> SetCurrentIndexCore(
        int index,
        SynchronizationContext? eventContext)
    {
        if (index < 0 || index >= _items.Count)
        {
            return ServiceResult<PlaylistItem>.Failure(
                ErrorCode.InvalidState,
                "播放列表项目无效。",
                "Invalid playlist index.");
        }

        if (_currentIndex != index)
        {
            _currentIndex = index;
            PublishCurrentChange(eventContext);
        }

        return ServiceResult<PlaylistItem>.Success(_items[index]);
    }

    private int FindIndexLocked(string filePath)
    {
        string key;
        try
        {
            key = PathHelper.CreatePathKey(filePath);
        }
        catch (ArgumentException)
        {
            return -1;
        }

        return _items.FindIndex(
            item => string.Equals(
                PathHelper.CreatePathKey(item.FilePath),
                key,
                StringComparison.Ordinal));
    }

    private PlaylistItem? CurrentForLocked()
        => _currentIndex >= 0 && _currentIndex < _items.Count
            ? _items[_currentIndex]
            : null;

    private void PublishCurrentChange(SynchronizationContext? eventContext)
    {
        void Raise()
        {
            RefreshCurrentFlags();
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        }

        if (eventContext is null)
        {
            Raise();
            return;
        }

        eventContext.Post(_ => Raise(), null);
    }

    private void PublishChanges(SynchronizationContext? eventContext, bool currentChanged)
    {
        void Raise()
        {
            RefreshCurrentFlags();
            PlaylistChanged?.Invoke(this, EventArgs.Empty);
            if (currentChanged)
            {
                CurrentChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        if (eventContext is null)
        {
            Raise();
            return;
        }

        eventContext.Post(_ => Raise(), null);
    }

    private void RefreshCurrentFlags()
    {
        PlaylistItem[] items;
        int currentIndex;
        lock (_sync)
        {
            items = _items.ToArray();
            currentIndex = _currentIndex;
        }

        for (var index = 0; index < items.Length; index++)
        {
            items[index].IsCurrent = index == currentIndex;
        }
    }

    private static ServiceResult CreateCancelledResult(
        string technicalMessage,
        Exception? exception = null)
        => ServiceResult.Failure(
            ErrorCode.Cancelled,
            "操作已取消。",
            technicalMessage,
            exception);

    private static ServiceResult<T> CreateCancelledResult<T>(
        string technicalMessage,
        Exception? exception = null)
        => ServiceResult<T>.Failure(
            ErrorCode.Cancelled,
            "操作已取消。",
            technicalMessage,
            exception);
}