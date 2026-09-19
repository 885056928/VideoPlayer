using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using VideoPlayer.Helpers;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.ViewModels;

public sealed partial class PlayerViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IPlayerService _playerService;
    private readonly IUiDispatcher _dispatcher;
    private readonly SemaphoreSlim _progressGate = new(1, 1);
    private CancellationTokenSource? _progressCts;
    private Task? _progressTask;
    private int _disposed;
    private bool _isSeeking;
    private bool _isApplyingSnapshot;

    public PlayerViewModel(IPlayerService playerService, IUiDispatcher dispatcher)
    {
        _playerService = playerService;
        _dispatcher = dispatcher;
    }

    public MediaPlayer? MediaPlayer => _playerService.MediaPlayer;

    [ObservableProperty]
    private string _lastError = string.Empty;

    [ObservableProperty]
    private TimeSpan _position;

    [ObservableProperty]
    private TimeSpan _duration;

    [ObservableProperty]
    private double _pendingSeekPosition;

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
        if (!result.IsSuccess)
        {
            LastError = result.UserMessage;
            return;
        }

        var volumeResult = await _playerService.SetVolumeAsync(Volume, cancellationToken);
        var rateResult = await _playerService.SetRateAsync(Rate, cancellationToken);
        var failedResult = !volumeResult.IsSuccess ? volumeResult : rateResult;
        LastError = failedResult.IsSuccess ? string.Empty : failedResult.UserMessage;

        await StartProgressRefreshAsync(cancellationToken);
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
    public Task SeekAsync(CancellationToken cancellationToken = default)
        => SeekToAsync(Position, cancellationToken);

    public void BeginSeek()
    {
        if (_isSeeking)
        {
            return;
        }

        _isSeeking = true;
        PendingSeekPosition = Position.TotalSeconds;
    }

    public Task CommitSeekAsync(CancellationToken cancellationToken = default)
    {
        if (!_isSeeking)
        {
            return Task.CompletedTask;
        }

        _isSeeking = false;
        var seconds = double.IsFinite(PendingSeekPosition)
            ? PendingSeekPosition
            : Position.TotalSeconds;
        var target = PlaybackMath.ClampPosition(
            TimeSpan.FromSeconds(seconds),
            Duration);
        return SeekToAsync(target, cancellationToken);
    }

    [RelayCommand]
    public async Task ToggleMuteAsync(CancellationToken cancellationToken = default)
    {
        IsMuted = !IsMuted;
        var result = await _playerService.SetMuteAsync(IsMuted, cancellationToken);
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
    }

    public async Task StartProgressRefreshAsync(CancellationToken cancellationToken = default)
    {
        await _progressGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopProgressLoopCoreAsync().ConfigureAwait(false);
            if (IsDisposed)
            {
                return;
            }

            var progressCts = new CancellationTokenSource();
            _progressCts = progressCts;
            _progressTask = Task.Run(
                () => RunProgressLoopAsync(progressCts.Token),
                CancellationToken.None);
        }
        finally
        {
            _progressGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _progressGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopProgressLoopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _progressGate.Release();
        }
    }

    partial void OnVolumeChanged(int value)
    {
        if (_isApplyingSnapshot || IsDisposed)
        {
            return;
        }

        _ = ApplyVolumeAsync(value);
    }

    partial void OnRateChanged(double value)
    {
        if (_isApplyingSnapshot || IsDisposed)
        {
            return;
        }

        _ = ApplyRateAsync(value);
    }

    private async Task SeekToAsync(TimeSpan position, CancellationToken cancellationToken)
    {
        var result = await _playerService.SeekAsync(position, cancellationToken);
        await UpdateLastErrorAsync(
            result.IsSuccess ? string.Empty : result.UserMessage).ConfigureAwait(false);
    }

    private async Task RunProgressLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var snapshot = _playerService.Snapshot;
                cancellationToken.ThrowIfCancellationRequested();
                await _dispatcher.InvokeAsync(() =>
                {
                    if (cancellationToken.IsCancellationRequested || IsDisposed)
                    {
                        return;
                    }

                    ApplySnapshot(snapshot);
                }).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            await UpdateLastErrorAsync("播放进度更新失败。").ConfigureAwait(false);
        }
    }

    private void ApplySnapshot(PlaybackSnapshot snapshot)
    {
        _isApplyingSnapshot = true;
        try
        {
            if (!_isSeeking)
            {
                Position = snapshot.Position;
                Duration = snapshot.Duration;
                PendingSeekPosition = snapshot.Position.TotalSeconds;
            }

            Volume = snapshot.Volume;
            Rate = snapshot.Rate;
            IsMuted = snapshot.IsMuted;
        }
        finally
        {
            _isApplyingSnapshot = false;
        }
    }

    private async Task ApplyVolumeAsync(int value)
    {
        try
        {
            var result = await _playerService.SetVolumeAsync(value).ConfigureAwait(false);
            await UpdateLastErrorAsync(
                result.IsSuccess ? string.Empty : result.UserMessage).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await UpdateLastErrorAsync("音量设置失败。").ConfigureAwait(false);
        }
    }

    private async Task ApplyRateAsync(double value)
    {
        try
        {
            var result = await _playerService.SetRateAsync(value).ConfigureAwait(false);
            await UpdateLastErrorAsync(
                result.IsSuccess ? string.Empty : result.UserMessage).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await UpdateLastErrorAsync("速率设置失败。").ConfigureAwait(false);
        }
    }

    private async Task UpdateLastErrorAsync(string message)
    {
        try
        {
            await _dispatcher.InvokeAsync(() =>
            {
                if (!IsDisposed)
                {
                    LastError = message;
                }
            }).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private async Task StopProgressLoopCoreAsync()
    {
        var progressCts = _progressCts;
        var progressTask = _progressTask;
        _progressCts = null;
        _progressTask = null;
        if (progressCts is null)
        {
            return;
        }

        progressCts.Cancel();
        try
        {
            if (progressTask is not null)
            {
                await progressTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }
        finally
        {
            progressCts.Dispose();
        }
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;
}
