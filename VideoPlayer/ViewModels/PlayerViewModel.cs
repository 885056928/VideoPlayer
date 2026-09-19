using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.ViewModels;

public sealed partial class PlayerViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IPlayerService _playerService;

    public PlayerViewModel(IPlayerService playerService)
    {
        _playerService = playerService;
    }

    public MediaPlayer? MediaPlayer => _playerService.MediaPlayer;

    [ObservableProperty]
    private string _lastError = string.Empty;

    [RelayCommand]
    public async Task OpenAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var request = new OpenMediaRequest(
            filePath,
            TimeSpan.Zero,
            AutoPlay: true,
            ExternalSubtitlePath: null,
            PreferredAudioLanguage: null);
        var result = await _playerService.OpenAsync(request, cancellationToken);
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
    }

    [RelayCommand]
    public async Task PlayAsync(CancellationToken cancellationToken = default)
    {
        var result = await _playerService.PlayAsync(cancellationToken);
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
    }

    [RelayCommand]
    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        var result = await _playerService.PauseAsync(cancellationToken);
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
    }

    [RelayCommand]
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var result = await _playerService.StopAsync(cancellationToken);
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
