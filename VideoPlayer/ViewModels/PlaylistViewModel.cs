using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.ViewModels;

public sealed partial class PlaylistViewModel : ObservableObject
{
    private readonly IPlaylistService _playlistService;
    private readonly IUiDispatcher _uiDispatcher;

    public PlaylistViewModel(IPlaylistService playlistService, IUiDispatcher uiDispatcher)
    {
        ArgumentNullException.ThrowIfNull(playlistService);
        ArgumentNullException.ThrowIfNull(uiDispatcher);

        _playlistService = playlistService;
        _uiDispatcher = uiDispatcher;
        _playlistService.PlaylistChanged += OnPlaylistChanged;
    }

    public ObservableCollection<PlaylistItem> Items { get; } = [];

    [ObservableProperty]
    private string _lastError = string.Empty;

    public async Task AddPathsAsync(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var result = await _playlistService.AddFilesAsync(paths, recursive: false);
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
    }

    public async Task AddFolderAsync(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        var result = await _playlistService.AddFolderAsync(folderPath, recursive: false);
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
    }

    public async Task RemoveAsync(PlaylistItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var result = await _playlistService.RemoveAsync(item.FilePath);
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
    }

    [RelayCommand]
    public async Task ClearAsync()
    {
        var result = await _playlistService.ClearAsync();
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
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
            Items.Clear();
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

        if (isSharedPrefix && targetItems.Count >= Items.Count)
        {
            for (var index = Items.Count; index < targetItems.Count; index++)
            {
                Items.Add(targetItems[index]);
            }

            return;
        }

        if (isSharedPrefix)
        {
            while (Items.Count > targetItems.Count)
            {
                Items.RemoveAt(Items.Count - 1);
            }

            return;
        }

        Items.Clear();
        foreach (var item in targetItems)
        {
            Items.Add(item);
        }
    }
}
