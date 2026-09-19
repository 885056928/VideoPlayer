using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.ViewModels;

public sealed partial class PlayerViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IPlayerService _playerService;
    private CancellationTokenSource? _progressCts;

    public PlayerViewModel(IPlayerService playerService)
    {
        _playerService = playerService;
    }

    public MediaPlayer? MediaPlayer => _playerService.MediaPlayer;

    [ObservableProperty]
    private string _lastError = string.Empty;

    [ObservableProperty]
    private TimeSpan _position;

    [ObservableProperty]
    private TimeSpan _duration;

    [ObservableProperty]
    private int _volume = 80;

    [ObservableProperty]
    private double _rate = 1.0;

    [ObservableProperty]
    private bool _isMuted;

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

        if (result.IsSuccess)
        {
            StartProgressRefresh();
        }
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

    [RelayCommand]
    public async Task SeekAsync(CancellationToken cancellationToken = default)
    {
        var result = await _playerService.SeekAsync(Position, cancellationToken);
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
    }

    [RelayCommand]
    public async Task ToggleMuteAsync(CancellationToken cancellationToken = default)
    {
        IsMuted = !IsMuted;
        var result = await _playerService.SetMuteAsync(IsMuted, cancellationToken);
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
    }

    public void StartProgressRefresh()
    {
        _progressCts?.Cancel();
        _progressCts = new CancellationTokenSource();
        _ = RunProgressLoopAsync(_progressCts.Token);
    }

    private async Task RunProgressLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var snapshot = _playerService.Snapshot;
                Position = snapshot.Position;
                Duration = snapshot.Duration;
                Volume = snapshot.Volume;
                Rate = snapshot.Rate;
                IsMuted = snapshot.IsMuted;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _progressCts?.Cancel();
        _progressCts?.Dispose();
        await ValueTask.CompletedTask;
    }

    partial void OnVolumeChanged(int value)
    {
        _ = _playerService.SetVolumeAsync(value);
    }

    partial void OnRateChanged(double value)
    {
        _ = _playerService.SetRateAsync(value);
    }
}
