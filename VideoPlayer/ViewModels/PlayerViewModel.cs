using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using Serilog;
using VideoPlayer.Enums;
using VideoPlayer.Helpers;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;
using VideoPlayer.Models.Events;
using VideoPlayer.Services;

namespace VideoPlayer.ViewModels;

public sealed partial class PlayerViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IPlayerService _playerService;
    private readonly IUiDispatcher _dispatcher;
    private readonly ISubtitleService? _subtitleService;
    private readonly IPlaylistService? _playlistService;
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly IHistoryService? _historyService;
    private readonly SemaphoreSlim _progressGate = new(1, 1);
    private static readonly TimeSpan HistoryFlushTimeout = TimeSpan.FromSeconds(2);
    private const long PositionSaveThresholdTicks = TimeSpan.TicksPerSecond * 2;
    private CancellationTokenSource? _progressCts;
    private Task? _progressTask;
    private CancellationTokenSource? _historyCts;
    private Task? _historyTask;
    private long _lastSavedPositionTicks;
    private int _disposed;
    private int _navigationVersion;
    private long _activeSessionId;
    private long _startedSessionId = -1;
    private long _handledEndSessionId;
    private bool _isSeeking;
    private bool _isApplyingSnapshot;

    public PlayerViewModel(IPlayerService playerService, IUiDispatcher dispatcher)
        : this(playerService, dispatcher, null, null, null, null)
    {
    }

    public PlayerViewModel(
        IPlayerService playerService,
        IUiDispatcher dispatcher,
        ISubtitleService? subtitleService)
        : this(playerService, dispatcher, null, null, null, subtitleService)
    {
    }

    public PlayerViewModel(
        IPlayerService playerService,
        IPlaylistService playlistService,
        ISettingsService settingsService,
        IDialogService dialogService,
        ISubtitleService? subtitleService = null,
        IHistoryService? historyService = null)
        : this(
            playerService,
            InlineUiDispatcher.Instance,
            playlistService,
            settingsService,
            dialogService,
            subtitleService,
            historyService)
    {
    }

    internal PlayerViewModel(
        IPlayerService playerService,
        IUiDispatcher dispatcher,
        IPlaylistService? playlistService,
        ISettingsService? settingsService,
        IDialogService? dialogService,
        ISubtitleService? subtitleService = null,
        IHistoryService? historyService = null)
    {
        ArgumentNullException.ThrowIfNull(playerService);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _playerService = playerService;
        _dispatcher = dispatcher;
        _subtitleService = subtitleService;
        _playlistService = playlistService;
        _settingsService = settingsService;
        _dialogService = dialogService;
        _historyService = historyService;
        _playerService.StateChanged += OnStateChanged;
        _playerService.EndReached += OnEndReached;
        _playerService.TracksChanged += OnTracksChanged;
    }

    public MediaPlayer? MediaPlayer => _playerService.MediaPlayer;

    public ObservableCollection<PlayerTrackInfo> AudioTracks { get; } = [];

    public ObservableCollection<PlayerTrackInfo> SubtitleTracks { get; } = [];

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
        var navigationVersion = Interlocked.Increment(ref _navigationVersion);
        await OpenCoreAsync(
            filePath,
            TimeSpan.Zero,
            navigationVersion,
            cancellationToken);
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
        await FlushHistoryAsync().ConfigureAwait(false);
        var result = await _playerService.PauseAsync(cancellationToken);
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
    }

    [RelayCommand]
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await FlushHistoryAsync().ConfigureAwait(false);
        var result = await _playerService.StopAsync(cancellationToken);
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
    }

    [RelayCommand]
    public async Task SelectAudioTrackAsync(PlayerTrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        var result = await _playerService.SelectAudioTrackAsync(track.Id);
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
    }

    [RelayCommand]
    public async Task SelectSubtitleTrackAsync(PlayerTrackInfo? track)
    {
        var result = await _playerService.SelectSubtitleTrackAsync(track?.Id);
        LastError = result.IsSuccess ? string.Empty : result.UserMessage;
    }

    [RelayCommand]
    public Task SeekAsync(CancellationToken cancellationToken = default)
        => SeekToAsync(Position, cancellationToken);

    public async Task NextAsync()
    {
        var playlistService = _playlistService;
        if (playlistService is null)
        {
            LastError = "Navigation is unavailable.";
            return;
        }

        var navigationVersion = Interlocked.Increment(ref _navigationVersion);
        var next = playlistService.GetNext();
        if (!next.IsSuccess)
        {
            LastError = next.UserMessage;
            return;
        }

        await OpenCoreAsync(
            next.Value!.FilePath,
            TimeSpan.Zero,
            navigationVersion,
            CancellationToken.None);
    }

    public async Task PreviousAsync()
    {
        var playlistService = _playlistService;
        if (playlistService is null)
        {
            LastError = "Navigation is unavailable.";
            return;
        }

        var navigationVersion = Interlocked.Increment(ref _navigationVersion);
        var previous = playlistService.GetPrevious();
        if (!previous.IsSuccess)
        {
            LastError = previous.UserMessage;
            return;
        }

        await OpenCoreAsync(
            previous.Value!.FilePath,
            TimeSpan.Zero,
            navigationVersion,
            CancellationToken.None);
    }

    public async Task HandleEndReachedAsync(EndReachedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.MediaSessionId != Interlocked.Read(ref _activeSessionId)
            || Interlocked.Exchange(ref _handledEndSessionId, args.MediaSessionId)
                == args.MediaSessionId)
        {
            return;
        }

        await FlushHistoryAsync().ConfigureAwait(false);

        if (_settingsService?.Current.Player.AutoPlayNext != true)
        {
            return;
        }

        var playlistService = _playlistService;
        if (playlistService is null)
        {
            return;
        }

        var navigationVersion = Interlocked.Increment(ref _navigationVersion);
        var next = playlistService.GetNext();
        if (!next.IsSuccess)
        {
            return;
        }

        await OpenCoreAsync(
            next.Value!.FilePath,
            TimeSpan.Zero,
            navigationVersion,
            CancellationToken.None,
            flushHistory: false);
    }

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

    public Task StartProgressRefreshAsync(CancellationToken cancellationToken = default)
        => StartProgressRefreshCoreAsync(null, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _playerService.StateChanged -= OnStateChanged;
        _playerService.EndReached -= OnEndReached;
        _playerService.TracksChanged -= OnTracksChanged;

        await _progressGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopProgressLoopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _progressGate.Release();
        }

        await FlushHistoryAsync().ConfigureAwait(false);
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

    private async Task OpenCoreAsync(
        string filePath,
        TimeSpan startPosition,
        int navigationVersion,
        CancellationToken cancellationToken,
        bool flushHistory = true)
    {
        if (IsNavigationSuperseded(navigationVersion))
        {
            return;
        }

        var resolvedStartPosition = startPosition;
        if (_historyService is not null
            && _settingsService?.Current.Player.RememberPosition == true)
        {
            try
            {
                var resume = await _historyService.GetResumeInfoAsync(
                    filePath,
                    cancellationToken).ConfigureAwait(false);
                var info = resume.Value;
                if (resume.IsSuccess
                    && info is not null
                    && ResumePolicy.ShouldAsk(info)
                    && _dialogService is not null)
                {
                    var choice = await _dialogService.AskResumeAsync(
                        Path.GetFileName(filePath),
                        info.LastPosition,
                        cancellationToken).ConfigureAwait(false);
                    if (choice == ResumeChoice.Cancel)
                    {
                        return;
                    }

                    if (choice == ResumeChoice.Restart)
                    {
                        try
                        {
                            await _historyService.ClearCompletedAsync(
                                filePath,
                                cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            LogHistoryFailure(exception, "Failed to clear playback history.");
                        }

                        resolvedStartPosition = TimeSpan.Zero;
                    }
                    else
                    {
                        resolvedStartPosition = info.LastPosition;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogHistoryFailure(exception, "Failed to query playback history.");
            }
        }

        if (IsNavigationSuperseded(navigationVersion))
        {
            return;
        }

        string? externalSubtitlePath = null;
        if (_subtitleService is not null)
        {
            try
            {
                var subtitleResult = await _subtitleService.FindAutoSubtitleAsync(
                    filePath,
                    cancellationToken).ConfigureAwait(false);
                if (subtitleResult.IsSuccess)
                {
                    externalSubtitlePath = subtitleResult.Value;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogHistoryFailure(exception, "Failed to find an automatic subtitle.");
            }
        }

        if (IsNavigationSuperseded(navigationVersion))
        {
            return;
        }

        if (flushHistory && Interlocked.Read(ref _activeSessionId) > 0)
        {
            await FlushHistoryAsync().ConfigureAwait(false);
            if (IsNavigationSuperseded(navigationVersion))
            {
                return;
            }
        }

        var request = new OpenMediaRequest(
            filePath,
            resolvedStartPosition,
            AutoPlay: true,
            ExternalSubtitlePath: externalSubtitlePath,
            PreferredAudioLanguage: null);
        var result = await _playerService.OpenAsync(request, cancellationToken);
        if (IsNavigationSuperseded(navigationVersion))
        {
            return;
        }

        if (!result.IsSuccess)
        {
            RestartHistoryFlushForActiveMedia();
            LastError = result.UserMessage;
            return;
        }

        var sessionInfo = result.Value;
        if (sessionInfo is null)
        {
            RestartHistoryFlushForActiveMedia();
            LastError = "Unable to open the media.";
            return;
        }

        AdvanceActiveSession(sessionInfo.MediaSessionId);
        StartHistoryFlush();

        var volumeResult = await _playerService.SetVolumeAsync(Volume, cancellationToken);
        if (IsNavigationSuperseded(navigationVersion))
        {
            return;
        }

        var rateResult = await _playerService.SetRateAsync(Rate, cancellationToken);
        if (IsNavigationSuperseded(navigationVersion))
        {
            return;
        }

        var failedResult = !volumeResult.IsSuccess ? volumeResult : rateResult;
        LastError = failedResult.IsSuccess ? string.Empty : failedResult.UserMessage;

        await StartProgressRefreshCoreAsync(navigationVersion, cancellationToken);
    }

    private async Task StartProgressRefreshCoreAsync(
        int? navigationVersion,
        CancellationToken cancellationToken)
    {
        await _progressGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsSuperseded(navigationVersion))
            {
                return;
            }

            await StopProgressLoopCoreAsync().ConfigureAwait(false);
            if (IsSuperseded(navigationVersion) || IsDisposed)
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

    private void RestartHistoryFlushForActiveMedia()
    {
        if (IsDisposed || _historyService is null || _activeSessionId <= 0)
        {
            return;
        }

        try
        {
            var snapshot = _playerService.Snapshot;
            if (snapshot.MediaPath is not null
                && snapshot.State is PlaybackState.Playing or PlaybackState.Paused)
            {
                StartHistoryFlush();
            }
        }
        catch (Exception exception)
        {
            LogHistoryFailure(exception, "Failed to restart playback history persistence.");
        }
    }

    private void StartHistoryFlush()
    {
        if (_historyService is null)
        {
            return;
        }

        var previousCts = Interlocked.Exchange(ref _historyCts, null);
        previousCts?.Cancel();
        previousCts?.Dispose();

        var historyCts = new CancellationTokenSource();
        Interlocked.Exchange(ref _historyCts, historyCts);
        Interlocked.Exchange(
            ref _historyTask,
            RunHistoryFlushAsync(historyCts.Token));
        Interlocked.Exchange(ref _lastSavedPositionTicks, 0);
    }

    private async Task RunHistoryFlushAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (IsDisposed
                    || _settingsService is null
                    || !_settingsService.Current.Player.RememberPosition)
                {
                    continue;
                }

                var snapshot = _playerService.Snapshot;
                cancellationToken.ThrowIfCancellationRequested();
                var lastSavedTicks = Interlocked.Read(ref _lastSavedPositionTicks);
                var positionTicks = snapshot.Position.Ticks;
                if (snapshot.MediaPath is null
                    || Math.Abs(positionTicks - lastSavedTicks) < PositionSaveThresholdTicks)
                {
                    continue;
                }

                var result = await _historyService!
                    .SaveProgressAsync(snapshot, cancellationToken)
                    .ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    Interlocked.Exchange(ref _lastSavedPositionTicks, positionTicks);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LogHistoryFailure(exception, "Failed to persist playback progress.");
        }
    }

    private async Task FlushHistoryAsync()
    {
        var historyCts = Interlocked.Exchange(ref _historyCts, null);
        historyCts?.Cancel();
        var historyTask = Interlocked.Exchange(ref _historyTask, null);
        if (historyTask is not null)
        {
            try
            {
                await historyTask.WaitAsync(HistoryFlushTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
            }
            catch (Exception exception)
            {
                LogHistoryFailure(exception, "Failed to stop the history flush loop.");
            }
        }

        historyCts?.Dispose();

        if (_historyService is null)
        {
            return;
        }

        using var flushCts = new CancellationTokenSource(HistoryFlushTimeout);
        await SaveFinalProgressAsync(flushCts.Token).ConfigureAwait(false);

        try
        {
            await _historyService.FlushAsync(flushCts.Token)
                .WaitAsync(HistoryFlushTimeout)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }
        catch (Exception exception)
        {
            LogHistoryFailure(exception, "Failed to flush playback history.");
        }
    }

    private async Task SaveFinalProgressAsync(CancellationToken cancellationToken)
    {
        var historyService = _historyService;
        if (historyService is null)
        {
            return;
        }

        PlaybackSnapshot snapshot;
        try
        {
            if (_settingsService?.Current.Player.RememberPosition != true)
            {
                return;
            }

            snapshot = _playerService.Snapshot;
        }
        catch (Exception exception)
        {
            LogHistoryFailure(exception, "Failed to read the final playback snapshot.");
            return;
        }

        if (snapshot.MediaPath is null)
        {
            return;
        }

        try
        {
            await historyService.SaveProgressAsync(snapshot, cancellationToken)
                .WaitAsync(HistoryFlushTimeout)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }
        catch (Exception exception)
        {
            LogHistoryFailure(exception, "Failed to save final playback progress.");
        }
    }

    private static void LogHistoryFailure(Exception exception, string message)
    {
        try
        {
            Log.Debug(exception, message);
        }
        catch
        {
            // History failures must never affect playback.
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

    private void OnTracksChanged(object? sender, TracksChangedEventArgs e)
    {
        if (IsDisposed || e.MediaSessionId <= 0)
        {
            return;
        }

        AdvanceActiveSession(e.MediaSessionId);
        if (e.MediaSessionId != Interlocked.Read(ref _activeSessionId))
        {
            return;
        }

        var tracks = e.Tracks.ToArray();
        try
        {
            _dispatcher.Post(() =>
            {
                if (IsDisposed
                    || e.MediaSessionId != Interlocked.Read(ref _activeSessionId))
                {
                    return;
                }

                ApplyTracks(tracks);
            });
        }
        catch (Exception)
        {
            // Track updates are non-critical and must not affect playback.
        }
    }

    private void ApplyTracks(IReadOnlyList<PlayerTrackInfo> tracks)
    {
        AudioTracks.Clear();
        SubtitleTracks.Clear();
        foreach (var track in tracks)
        {
            if (track.Type == Enums.PlayerTrackType.Audio)
            {
                AudioTracks.Add(track);
            }
            else
            {
                SubtitleTracks.Add(track);
            }
        }
    }

    private async void OnStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        try
        {
            if (IsDisposed || e.MediaSessionId <= 0)
            {
                return;
            }

            AdvanceActiveSession(e.MediaSessionId);
            if (e.NewState != PlaybackState.Playing
                || e.MediaSessionId != Interlocked.Read(ref _activeSessionId)
                || _historyService is null
                || Interlocked.Exchange(ref _startedSessionId, e.MediaSessionId)
                    == e.MediaSessionId)
            {
                return;
            }

            var snapshot = _playerService.Snapshot;
            if (snapshot.MediaSessionId != e.MediaSessionId
                || snapshot.MediaPath is not { } path)
            {
                return;
            }

            await _historyService.StartPlaybackAsync(
                path,
                Path.GetFileName(path),
                snapshot.Duration).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogHistoryFailure(exception, "Failed to record playback start.");
        }
    }

    private async void OnEndReached(object? sender, EndReachedEventArgs e)
    {
        await HandleEndReachedAsync(e);
    }

    private void AdvanceActiveSession(long sessionId)
    {
        if (sessionId <= 0)
        {
            return;
        }

        long activeSessionId;
        do
        {
            activeSessionId = Interlocked.Read(ref _activeSessionId);
            if (sessionId <= activeSessionId)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(
            ref _activeSessionId,
            sessionId,
            activeSessionId) != activeSessionId);
    }

    private bool IsNavigationSuperseded(int navigationVersion)
        => IsDisposed || Volatile.Read(ref _navigationVersion) != navigationVersion;

    private bool IsSuperseded(int? navigationVersion)
        => navigationVersion is int value && IsNavigationSuperseded(value);

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public static InlineUiDispatcher Instance { get; } = new();

        public bool CheckAccess() => true;

        public void Post(Action action) => action();

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> action)
            => Task.FromResult(action());
    }
}
