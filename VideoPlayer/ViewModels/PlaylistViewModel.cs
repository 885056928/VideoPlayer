using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.ViewModels;

public sealed partial class PlaylistViewModel : ObservableObject
{
    private readonly IPlaylistService _playlistService;
    private readonly object _refreshLock = new();

    public PlaylistViewModel(IPlaylistService playlistService)
    {
        ArgumentNullException.ThrowIfNull(playlistService);

        _playlistService = playlistService;
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
        Refresh();
    }

    public async Task RemoveAsync(PlaylistItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var result = await _playlistService.RemoveAsync(item.FilePath);
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
        Refresh();
    }

    [RelayCommand]
    public async Task ClearAsync()
    {
        var result = await _playlistService.ClearAsync();
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
        Refresh();
    }

    private void OnPlaylistChanged(object? sender, EventArgs e)
    {
        Refresh();
    }

    private void Refresh()
    {
        lock (_refreshLock)
        {
            Items.Clear();
            foreach (var item in _playlistService.Items)
            {
                Items.Add(item);
            }
        }
    }
}
