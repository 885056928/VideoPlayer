using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.ViewModels;

public sealed partial class PlaylistViewModel : ObservableObject
{
    #region 属性
    private const int BulkReplaceThreshold = 128;
    private readonly IPlaylistService _playlistService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly BulkObservableCollection<PlaylistItem> _items = [];

    public ObservableCollection<PlaylistItem> Items => _items;

    public event EventHandler<PlaylistItem?>? PlayRequested;

    [ObservableProperty]
    private PlaylistItem? _selectedItem;

    [ObservableProperty]
    private string _lastError = string.Empty;
    #endregion

    #region 命令
    [RelayCommand]
    public async Task RemoveAsync(PlaylistItem? item)
    {
        if (item is null)
        {
            return;
        }

        var result = await _playlistService.RemoveAsync(item.FilePath);
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
    }

    [RelayCommand]
    public async Task ClearAsync()
    {
        var result = await _playlistService.ClearAsync();
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
    }
    #endregion

    #region 方法
    public PlaylistViewModel(IPlaylistService playlistService, IUiDispatcher uiDispatcher)
    {
        ArgumentNullException.ThrowIfNull(playlistService);
        ArgumentNullException.ThrowIfNull(uiDispatcher);

        _playlistService = playlistService;
        _uiDispatcher = uiDispatcher;
        _playlistService.PlaylistChanged += OnPlaylistChanged;
        _playlistService.CurrentChanged += OnPlaylistChanged;
    }

    public async Task<ServiceResult<IReadOnlyList<PlaylistItem>>> AddPathsAsync(
        IEnumerable<string> paths,
        bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var result = await _playlistService.AddFilesAsync(
            paths,
            recursive,
            cancellationToken);
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
        return result;
    }

    public async Task<ServiceResult> AddFolderAsync(
        string folderPath,
        bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        var result = await _playlistService.AddFolderAsync(
            folderPath,
            recursive,
            cancellationToken);
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
        return result;
    }

    public void RequestPlay(PlaylistItem? item)
    {
        if (item is not null)
        {
            PlayRequested?.Invoke(this, item);
        }
    }

    private void OnPlaylistChanged(object? sender, EventArgs e)
    {
        _uiDispatcher.Post(ApplyPlaylistChanges);
    }

    private void ApplyPlaylistChanges()
    {
        var targetItems = _playlistService.Items;
        if (targetItems.Count == 0)
        {
            if (_items.Count > 0)
            {
                _items.ReplaceAll([]);
            }

            SelectedItem = null;
            return;
        }

        var sharedCount = Math.Min(Items.Count, targetItems.Count);
        var isSharedPrefix = true;
        for (var index = 0; index < sharedCount; index++)
        {
            if (!string.Equals(
                    Items[index].FilePath,
                    targetItems[index].FilePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                isSharedPrefix = false;
                break;
            }
        }

        if (!isSharedPrefix || targetItems.Count - Items.Count >= BulkReplaceThreshold)
        {
            _items.ReplaceAll(targetItems);
        }
        else if (targetItems.Count >= Items.Count)
        {
            for (var index = Items.Count; index < targetItems.Count; index++)
            {
                Items.Add(targetItems[index]);
            }
        }
        else
        {
            while (Items.Count > targetItems.Count)
            {
                Items.RemoveAt(Items.Count - 1);
            }
        }

        if (SelectedItem is not null
            && !targetItems.Any(
                item => string.Equals(
                    item.FilePath,
                    SelectedItem.FilePath,
                    StringComparison.OrdinalIgnoreCase)))
        {
            SelectedItem = null;
        }
    }
    #endregion
}