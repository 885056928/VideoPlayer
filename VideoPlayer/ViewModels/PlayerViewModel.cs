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
    private readonly IScreenshotService? _screenshotService;
    private readonly SemaphoreSlim _progressGate = new(1, 1);
    private readonly SemaphoreSlim _orchestrationGate = new(1, 1);
    private static readonly TimeSpan HistoryFlushTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ScreenshotSuccessMessageLifetime = TimeSpan.FromSeconds(2);
    private const long PositionSaveThresholdTicks = TimeSpan.TicksPerSecond * 2;
    private static readonly double[] PlaybackRates = [0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0];
    private CancellationTokenSource? _progressCts;
    private CancellationTokenSource? _framePreviewCts;
    private long _lastPreviewTick = -1;
    private Task? _progressTask;
    private CancellationTokenSource? _historyCts;
    private Task? _historyTask;
    private CancellationTokenSource? _requestCts;
    private long _lastSavedPositionTicks;
    private int _disposed;
    private int _navigationVersion;
    private long _activeSessionId;
    private long _startedSessionId = -1;
    private long _handledEndSessionId = -1;
    private long _handledFailureSessionId = -1;
    private string? _activeMediaPath;
    private TimeSpan _activeDuration;
    private bool _isSeeking;
    private bool _isApplyingSnapshot;
    private bool _isUpdatingTracks;
    private int _screenshotMessageVersion;

    public PlayerViewModel(IPlayerService playerService, IUiDispatcher dispatcher)
        : this(playerService, dispatcher, null, null, null, null, null)
    {
    }

    public PlayerViewModel(
        IPlayerService playerService,
        IUiDispatcher dispatcher,
        ISubtitleService? subtitleService)
        : this(playerService, dispatcher, null, null, null, subtitleService, null)
    {
    }

    public PlayerViewModel(
        IPlayerService playerService,
        IPlaylistService playlistService,
        ISettingsService settingsService,
        IDialogService dialogService,
        ISubtitleService? subtitleService = null,
        IHistoryService? historyService = null,
        IScreenshotService? screenshotService = null)
        : this(
            playerService,
            InlineUiDispatcher.Instance,
            playlistService,
            settingsService,
            dialogService,
            subtitleService,
            historyService,
            screenshotService)
    {
    }

    internal PlayerViewModel(
        IPlayerService playerService,
        IUiDispatcher dispatcher,
        IPlaylistService? playlistService,
        ISettingsService? settingsService,
        IDialogService? dialogService,
        ISubtitleService? subtitleService = null,
        IHistoryService? historyService = null,
        IScreenshotService? screenshotService = null)
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
        _screenshotService = screenshotService;
        _playerService.StateChanged += OnStateChanged;
        _playerService.EndReached += OnEndReached;
        _playerService.TracksChanged += OnTracksChanged;
        _playerService.MediaParsed += OnMediaParsed;
        _playerService.PlaybackFailed += OnPlaybackFailed;

        if (_settingsService is not null)
        {
            _isApplyingSnapshot = true;
            try
            {
                Volume = _settingsService.Current.Player.DefaultVolume;
                Rate = _settingsService.Current.Player.DefaultPlaybackRate;
            }
            finally
            {
                _isApplyingSnapshot = false;
            }
        }
    }

    public MediaPlayer? MediaPlayer => _playerService.MediaPlayer;

    public PlaybackSnapshot Snapshot => _playerService.Snapshot;

    public IReadOnlyList<PlayerTrackInfo> Tracks => [.. AudioTracks, .. SubtitleTracks];

    public ObservableCollection<PlayerTrackInfo> AudioTracks { get; } = [];

    public ObservableCollection<PlayerTrackInfo> SubtitleTracks { get; } = [];

    public event EventHandler<MediaParsedEventArgs>? MediaParsed;

    [ObservableProperty]
    private PlayerTrackInfo? _selectedAudioTrack;

    [ObservableProperty]
    private PlayerTrackInfo? _selectedSubtitleTrack;

    public bool HasActiveSubtitle => SelectedSubtitleTrack is not null;

    [ObservableProperty]
    private string _mediaTitle = "本地视频播放器";

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

    [ObservableProperty]
    private string? _hoverPreviewPath;

    [ObservableProperty]
    private string _hoverTimeText = "00:00:00";

    [ObservableProperty]
    private PlaybackState _playbackState;

    [ObservableProperty]
    private string _screenshotMessage = string.Empty;

    public bool IsOpening => PlaybackState == VideoPlayer.Enums.PlaybackState.Opening;

    public bool HasError => !string.IsNullOrWhiteSpace(LastError);

    public bool HasScreenshotMessage => !string.IsNullOrWhiteSpace(ScreenshotMessage);

    [RelayCommand]
    public Task<ServiceResult> OpenAsync(
        string filePath,
        CancellationToken cancellationToken = default)
        => RunOnUiAsync(
            () => QueueOpenRequestAsync(
                filePath,
                TimeSpan.Zero,
                flushHistory: true,
                cancellationToken),
            cancellationToken);

    [RelayCommand]
    public Task PlayAsync(CancellationToken cancellationToken = default)
        => RunOnUiAsync(async () =>
        {
            var result = await _playerService.PlayAsync(cancellationToken);
            LastError = result.IsSuccess ? string.Empty : result.UserMessage;
            if (!result.IsSuccess)
            {
                PlaybackState = VideoPlayer.Enums.PlaybackState.Error;
                await ShowErrorAsync("播放失败", result.UserMessage);
            }
        }, cancellationToken);

    [RelayCommand]
    public Task PauseAsync(CancellationToken cancellationToken = default)
        => RunOnUiAsync(async () =>
        {
            await FlushHistoryAsync();
            var result = await _playerService.PauseAsync(cancellationToken);
            LastError = result.IsSuccess ? string.Empty : result.UserMessage;
        }, cancellationToken);

    [RelayCommand]
    public Task TogglePlaybackAsync(CancellationToken cancellationToken = default)
        => RunOnUiAsync(
            async () =>
            {
                var result = PlaybackState == VideoPlayer.Enums.PlaybackState.Playing
                    ? await _playerService.PauseAsync(cancellationToken)
                    : await _playerService.PlayAsync(cancellationToken);
                LastError = result.IsSuccess ? string.Empty : result.UserMessage;
            },
            cancellationToken);
    [RelayCommand]
    public Task StopAsync(CancellationToken cancellationToken = default)
        => RunOnUiAsync(async () =>
        {
            await FlushHistoryAsync();
            var result = await _playerService.StopAsync(cancellationToken);
            LastError = result.IsSuccess ? string.Empty : result.UserMessage;
        }, cancellationToken);

    [RelayCommand]
    public Task ScreenshotAsync(CancellationToken cancellationToken = default)
        => RunOnUiAsync(async () =>
        {
            var screenshotService = _screenshotService;
            if (screenshotService is null)
            {
                LastError = "截图服务不可用。";
                ClearScreenshotMessage();
                return;
            }

            ClearScreenshotMessage();

            try
            {
                var result = await screenshotService.CaptureAsync(
                    _playerService.Snapshot,
                    cancellationToken);
                if (result.IsSuccess)
                {
                    LastError = string.Empty;
                    ShowScreenshotSuccessMessage();
                }
                else
                {
                    LastError = result.UserMessage;
                    ClearScreenshotMessage();
                }
            }
            catch (OperationCanceledException)
            {
                LastError = "截图已取消。";
                ClearScreenshotMessage();
            }
            catch
            {
                LastError = "截图失败。";
                ClearScreenshotMessage();
            }
        }, cancellationToken);

    [RelayCommand]
    public Task SelectAudioTrackAsync(PlayerTrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return RunOnUiAsync(async () =>
        {
            var result = await _playerService.SelectAudioTrackAsync(track.Id);
            LastError = result.IsSuccess ? string.Empty : result.UserMessage;
            if (result.IsSuccess)
            {
                await RefreshTracksAsync();
            }
        }, CancellationToken.None);
    }

    [RelayCommand]
    public Task SelectSubtitleTrackAsync(PlayerTrackInfo? track)
        => RunOnUiAsync(async () =>
        {
            var result = await _playerService.SelectSubtitleTrackAsync(track?.Id);
            LastError = result.IsSuccess ? string.Empty : result.UserMessage;
            if (result.IsSuccess)
            {
                await RefreshTracksAsync();
            }
        }, CancellationToken.None);

    [RelayCommand]
    public Task CloseSubtitleAsync() => SelectSubtitleTrackAsync(null);

    [RelayCommand]
    public Task AddSubtitleAsync(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Task.CompletedTask;
        }

        return RunOnUiAsync(async () =>
        {
            if (_subtitleService is not null)
            {
                var validation = await _subtitleService.ValidateSubtitleAsync(filePath);
                if (!validation.IsSuccess)
                {
                    LastError = validation.UserMessage;
                    return;
                }
            }

            var result = await _playerService.AddExternalSubtitleAsync(
                filePath,
                select: true);
            LastError = result.IsSuccess ? string.Empty : result.UserMessage;
            if (result.IsSuccess)
            {
                await RefreshTracksAsync();
            }
        }, CancellationToken.None);
    }

    public Task RefreshTracksAsync(CancellationToken cancellationToken = default)
        => RunOnUiAsync(async () =>
        {
            var audio = await _playerService.GetTracksAsync(
                PlayerTrackType.Audio,
                cancellationToken);
            var subtitles = await _playerService.GetTracksAsync(
                PlayerTrackType.Subtitle,
                cancellationToken);
            if (!audio.IsSuccess || !subtitles.IsSuccess)
            {
                var failure = !audio.IsSuccess ? audio : subtitles;
                LastError = failure.UserMessage;
                return;
            }

            var tracks = new List<PlayerTrackInfo>();
            if (audio.Value is not null)
            {
                tracks.AddRange(audio.Value);
            }

            if (subtitles.Value is not null)
            {
                tracks.AddRange(subtitles.Value);
            }

            ApplyTracks(tracks);
        }, cancellationToken);

    public async Task RequestFramePreviewAsync(TimeSpan position)
    {
        HoverTimeText = TimeTextHelper.Format(position);
        var mediaPath = _activeMediaPath;
        var sessionId = Interlocked.Read(ref _activeSessionId);
        if (mediaPath is null || Duration <= TimeSpan.Zero)
        {
            HoverPreviewPath = null;
            return;
        }

        var clamped = PlaybackMath.ClampPosition(position, Duration);
        var previewPosition = SeekHoverMath.QuantizeForPreview(clamped, Duration);
        var previewTick = (long)Math.Round(
            previewPosition.TotalSeconds * 5,
            MidpointRounding.AwayFromZero);
        if (previewTick == Interlocked.Read(ref _lastPreviewTick)
            && !string.IsNullOrWhiteSpace(HoverPreviewPath))
        {
            return;
        }

        Interlocked.Exchange(ref _lastPreviewTick, previewTick);
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _framePreviewCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        try
        {
            await Task.Delay(100, cts.Token).ConfigureAwait(false);
            var previewDirectory = Path.Combine(
                Path.GetTempPath(),
                "VideoPlayer",
                "Previews");
            var fileName = Math.Abs(mediaPath.GetHashCode()).ToString("X8")
                + "_"
                + previewTick.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
                + ".png";
            var outputPath = Path.Combine(previewDirectory, fileName);
            var result = await _playerService.CapturePreviewFrameAsync(
                mediaPath,
                previewPosition,
                outputPath,
                cts.Token).ConfigureAwait(false);

            if (result.IsSuccess
                && !cts.IsCancellationRequested
                && sessionId == Interlocked.Read(ref _activeSessionId)
                && string.Equals(mediaPath, _activeMediaPath, StringComparison.OrdinalIgnoreCase))
            {
                await ApplyUiAsync(() => HoverPreviewPath = result.Value).ConfigureAwait(false);
            }
            else
            {
                Interlocked.Exchange(ref _lastPreviewTick, -1);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            LogHistoryFailure(exception, "Failed to capture frame preview.");
        }
    }

    public void CancelFramePreview()
    {
        _framePreviewCts?.Cancel();
    }
    [RelayCommand]
    public Task SeekAsync(CancellationToken cancellationToken = default)
        => SeekToAsync(Position, cancellationToken);

    [RelayCommand]
    public Task TogglePlayPauseAsync(CancellationToken cancellationToken = default)
        => RunOnUiAsync(async () =>
        {
            if (_playerService.Snapshot.State == PlaybackState.Playing)
            {
                await PauseAsync(cancellationToken);
            }
            else
            {
                await PlayAsync(cancellationToken);
            }
        }, cancellationToken);

    [RelayCommand]
    public Task SeekBackward5Async(CancellationToken cancellationToken = default)
        => SeekRelativeAsync(TimeSpan.FromSeconds(-5), cancellationToken);

    [RelayCommand]
    public Task SeekForward5Async(CancellationToken cancellationToken = default)
        => SeekRelativeAsync(TimeSpan.FromSeconds(5), cancellationToken);

    [RelayCommand]
    public Task SeekBackward30Async(CancellationToken cancellationToken = default)
        => SeekRelativeAsync(TimeSpan.FromSeconds(-30), cancellationToken);

    [RelayCommand]
    public Task SeekForward30Async(CancellationToken cancellationToken = default)
        => SeekRelativeAsync(TimeSpan.FromSeconds(30), cancellationToken);

    [RelayCommand]
    public void VolumeUp()
        => Volume = PlaybackMath.ClampVolume(Volume + 5);

    [RelayCommand]
    public void VolumeDown()
        => Volume = PlaybackMath.ClampVolume(Volume - 5);

    [RelayCommand]
    public void RateDown()
        => ChangeRate(-1);

    [RelayCommand]
    public void RateUp()
        => ChangeRate(1);

    [RelayCommand]
    public void RateReset()
        => Rate = 1.0;

    [RelayCommand]
    public Task<ServiceResult> NextAsync()
    {
        var playlistService = _playlistService;
        if (playlistService is null)
        {
            return Task.FromResult(ServiceResult.Failure(
                ErrorCode.InvalidState,
                "导航不可用。",
                "Playlist service is unavailable."));
        }

        var next = playlistService.GetNext();
        if (!next.IsSuccess)
        {
            LastError = next.UserMessage;
            return Task.FromResult(ServiceResult.Failure(
                next.ErrorCode,
                next.UserMessage,
                next.TechnicalMessage,
                next.Exception));
        }

        return RunOnUiAsync(
            () => QueueOpenRequestAsync(
                next.Value!.FilePath,
                TimeSpan.Zero,
                flushHistory: true,
                CancellationToken.None),
            CancellationToken.None);
    }

    [RelayCommand]
    public Task<ServiceResult> PreviousAsync()
    {
        var playlistService = _playlistService;
        if (playlistService is null)
        {
            return Task.FromResult(ServiceResult.Failure(
                ErrorCode.InvalidState,
                "导航不可用。",
                "Playlist service is unavailable."));
        }

        var previous = playlistService.GetPrevious();
        if (!previous.IsSuccess)
        {
            LastError = previous.UserMessage;
            return Task.FromResult(ServiceResult.Failure(
                previous.ErrorCode,
                previous.UserMessage,
                previous.TechnicalMessage,
                previous.Exception));
        }

        return RunOnUiAsync(
            () => QueueOpenRequestAsync(
                previous.Value!.FilePath,
                TimeSpan.Zero,
                flushHistory: true,
                CancellationToken.None),
            CancellationToken.None);
    }

    public async Task<ServiceResult> HandleEndReachedAsync(EndReachedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var activeSessionId = Interlocked.Read(ref _activeSessionId);
        if (args.MediaSessionId != activeSessionId
            || Interlocked.Exchange(ref _handledEndSessionId, args.MediaSessionId)
                == args.MediaSessionId)
        {
            return ServiceResult.Failure(
                ErrorCode.Superseded,
                string.Empty,
                "Stale or duplicate end event.");
        }

        await FlushHistoryAsync();

        var snapshot = _playerService.Snapshot;
        if (_historyService?.IsAvailable == true
            && snapshot.MediaSessionId == args.MediaSessionId
            && snapshot.MediaPath is not null)
        {
            try
            {
                await _historyService.MarkCompletedAsync(
                    snapshot.MediaPath,
                    snapshot.Duration,
                    args.MediaSessionId,
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                LogHistoryFailure(exception, "Failed to mark playback completed.");
            }
        }

        if (_settingsService?.Current.Player.AutoPlayNext != true)
        {
            return ServiceResult.Success();
        }

        return await NextAsync();
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
    public Task ToggleMuteAsync(CancellationToken cancellationToken = default)
        => RunOnUiAsync(async () =>
        {
            IsMuted = !IsMuted;
            var result = await _playerService.SetMuteAsync(IsMuted, cancellationToken);
            LastError = result.IsSuccess ? string.Empty : result.UserMessage;
        }, cancellationToken);

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
        _playerService.MediaParsed -= OnMediaParsed;
        _playerService.PlaybackFailed -= OnPlaybackFailed;
        Interlocked.Increment(ref _screenshotMessageVersion);
        var previewCts = Interlocked.Exchange(ref _framePreviewCts, null);
        previewCts?.Cancel();
        previewCts?.Dispose();
        var requestCts = Interlocked.Exchange(ref _requestCts, null);
        requestCts?.Cancel();
        requestCts?.Dispose();

        await _progressGate.WaitAsync();
        try
        {
            await StopProgressLoopCoreAsync();
        }
        finally
        {
            _progressGate.Release();
        }

        await FlushHistoryAsync();
    }

    partial void OnPlaybackStateChanged(VideoPlayer.Enums.PlaybackState value)
    {
        OnPropertyChanged(nameof(IsOpening));
    }

    partial void OnLastErrorChanged(string value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnScreenshotMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasScreenshotMessage));
    }

    partial void OnSelectedAudioTrackChanged(PlayerTrackInfo? value)
    {
        if (_isUpdatingTracks || value is null || value.IsSelected)
        {
            return;
        }

        _ = SelectAudioTrackAsync(value);
    }

    partial void OnSelectedSubtitleTrackChanged(PlayerTrackInfo? value)
    {
        OnPropertyChanged(nameof(HasActiveSubtitle));
        if (_isUpdatingTracks)
        {
            return;
        }

        _ = SelectSubtitleTrackAsync(value);
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

    private async Task<ServiceResult> OpenCoreAsync(
        string filePath,
        TimeSpan startPosition,
        int navigationVersion,
        CancellationToken cancellationToken,
        bool flushHistory)
    {
        if (IsNavigationSuperseded(navigationVersion))
        {
            return CreateSupersededResult();
        }

        var resolvedStartPosition = startPosition;
        if (File.Exists(filePath)
            && _historyService?.IsAvailable == true
            && _settingsService?.Current.Player.RememberPosition == true)
        {
            try
            {
                var resume = await _historyService.GetResumeInfoAsync(
                    filePath,
                    cancellationToken);
                var info = resume.Value;
                if (resume.IsSuccess && info is not null)
                {
                    resolvedStartPosition = info.IsCompleted
                        ? TimeSpan.Zero
                        : PlaybackMath.ClampPosition(info.LastPosition, info.Duration);
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
            return CreateSupersededResult();
        }

        string? externalSubtitlePath = null;
        if (_subtitleService is not null
            && _settingsService?.Current.Subtitle.AutoLoad != false)
        {
            try
            {
                var subtitleResult = await _subtitleService.FindAutoSubtitleAsync(
                    filePath,
                    cancellationToken);
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
            return CreateSupersededResult();
        }

        if (flushHistory && Interlocked.Read(ref _activeSessionId) > 0)
        {
            await FlushHistoryAsync();
            if (IsNavigationSuperseded(navigationVersion))
            {
                return CreateSupersededResult();
            }
        }

        var request = new OpenMediaRequest(
            filePath,
            resolvedStartPosition,
            AutoPlay: true,
            ExternalSubtitlePath: externalSubtitlePath,
            PreferredAudioLanguage: null);
        LastError = string.Empty;
        PlaybackState = VideoPlayer.Enums.PlaybackState.Opening;
        var result = await _playerService.OpenAsync(request, cancellationToken);
        if (IsNavigationSuperseded(navigationVersion))
        {
            return CreateSupersededResult();
        }

        if (!result.IsSuccess || result.Value is null)
        {
            RestartHistoryFlushForActiveMedia();
            PlaybackState = VideoPlayer.Enums.PlaybackState.Error;
            var message = result.IsSuccess
                ? "无法打开视频。"
                : result.UserMessage;
            LastError = message;
            await ShowErrorAsync("播放失败", message);
            return result.IsSuccess
                ? ServiceResult.Failure(ErrorCode.Unknown, message, "Open result had no session.")
                : ServiceResult.Failure(
                    result.ErrorCode,
                    result.UserMessage,
                    result.TechnicalMessage,
                    result.Exception);
        }

        var sessionInfo = result.Value;
        var mediaTitle = Path.GetFileName(sessionInfo.FilePath);
        MediaTitle = string.IsNullOrWhiteSpace(mediaTitle)
            ? "本地视频播放器"
            : mediaTitle;
        await EnsureCurrentPlaylistItemAsync(filePath);
        AdvanceActiveSession(sessionInfo.MediaSessionId);
        Interlocked.Exchange(ref _lastPreviewTick, -1);
        HoverPreviewPath = null;
        Interlocked.Exchange(ref _startedSessionId, -1);
        Interlocked.Exchange(ref _handledEndSessionId, -1);
        Interlocked.Exchange(ref _handledFailureSessionId, -1);
        var targetVolume = Volume;
        var targetRate = Rate;
        var activeSnapshot = _playerService.Snapshot;
        if (activeSnapshot.MediaSessionId == sessionInfo.MediaSessionId)
        {
            ApplySnapshot(activeSnapshot);
            Volume = targetVolume;
            Rate = targetRate;
        }

        _activeMediaPath = sessionInfo.FilePath;
        _activeDuration = activeSnapshot.Duration;
        await RecordPlaybackStartAsync(sessionInfo.MediaSessionId);
        StartHistoryFlush();
        UpdatePlaylistPlaybackState(sessionInfo.MediaSessionId, PlaybackState);
        await RefreshTracksAsync(cancellationToken);

        var volumeResult = await _playerService.SetVolumeAsync(targetVolume, cancellationToken);
        if (IsNavigationSuperseded(navigationVersion))
        {
            return CreateSupersededResult();
        }

        var rateResult = await _playerService.SetRateAsync(targetRate, cancellationToken);
        if (IsNavigationSuperseded(navigationVersion))
        {
            return CreateSupersededResult();
        }

        var failedResult = !volumeResult.IsSuccess ? volumeResult : rateResult;
        LastError = failedResult.IsSuccess ? string.Empty : failedResult.UserMessage;

        await StartProgressRefreshCoreAsync(navigationVersion, cancellationToken);
        return IsNavigationSuperseded(navigationVersion)
            ? CreateSupersededResult()
            : ServiceResult.Success();
    }

    private async Task EnsureCurrentPlaylistItemAsync(string filePath)
    {
        var playlistService = _playlistService;
        if (playlistService is null)
        {
            return;
        }

        var setCurrent = playlistService.SetCurrent(filePath);
        if (setCurrent.IsSuccess)
        {
            return;
        }

        var added = await playlistService.AddFilesAsync(
            [filePath],
            recursive: false,
            CancellationToken.None);
        if (!added.IsSuccess)
        {
            LastError = added.UserMessage;
            return;
        }

        var retry = playlistService.SetCurrent(filePath);
        if (!retry.IsSuccess)
        {
            LastError = retry.UserMessage;
        }
    }
    private async Task<ServiceResult> QueueOpenRequestAsync(
        string filePath,
        TimeSpan startPosition,
        bool flushHistory,
        CancellationToken cancellationToken)
    {
        var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var navigationVersion = Interlocked.Increment(ref _navigationVersion);
        var previousRequest = Interlocked.Exchange(ref _requestCts, requestCts);
        previousRequest?.Cancel();

        try
        {
            await _orchestrationGate.WaitAsync(requestCts.Token);
            try
            {
                if (IsNavigationSuperseded(navigationVersion))
                {
                    return CreateSupersededResult();
                }

                return await OpenCoreAsync(
                    filePath,
                    startPosition,
                    navigationVersion,
                    requestCts.Token,
                    flushHistory);
            }
            finally
            {
                _orchestrationGate.Release();
            }
        }
        catch (OperationCanceledException exception) when (requestCts.IsCancellationRequested)
        {
            return IsNavigationSuperseded(navigationVersion)
                ? CreateSupersededResult()
                : ServiceResult.Failure(
                    ErrorCode.Cancelled,
                    "打开操作已取消。",
                    "Open request cancelled.",
                    exception);
        }
        finally
        {
            Interlocked.CompareExchange(ref _requestCts, null, requestCts);
            requestCts.Dispose();
        }
    }

    private static ServiceResult CreateSupersededResult()
        => ServiceResult.Failure(
            ErrorCode.Superseded,
            string.Empty,
            "A newer open or navigation request superseded this operation.");

    private async Task<T> RunOnUiAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (_dispatcher.CheckAccess())
        {
            return await action();
        }

        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        await _dispatcher.InvokeAsync(() =>
        {
            Interlocked.Exchange(ref started, 1);
            _ = ExecuteAndCompleteAsync(action, completion);
        }).WaitAsync(cancellationToken);

        if (Volatile.Read(ref started) == 0)
        {
            throw new OperationCanceledException("UI dispatcher is unavailable.");
        }

        return await completion.Task.WaitAsync(cancellationToken);
    }

    private Task RunOnUiAsync(Func<Task> action, CancellationToken cancellationToken)
        => RunOnUiAsync(
            async () =>
            {
                await action();
                return true;
            },
            cancellationToken);

    private static async Task ExecuteAndCompleteAsync<T>(
        Func<Task<T>> action,
        TaskCompletionSource<T> completion)
    {
        try
        {
            completion.TrySetResult(await action());
        }
        catch (OperationCanceledException exception)
        {
            completion.TrySetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private Task ShowErrorAsync(string title, string message)
    {
        if (_dialogService is null || IsDisposed)
        {
            return Task.CompletedTask;
        }

        return _dialogService.ShowErrorAsync(title, message);
    }
    private async Task StartProgressRefreshCoreAsync(
        int? navigationVersion,
        CancellationToken cancellationToken)
    {
        await _progressGate.WaitAsync(cancellationToken);
        try
        {
            if (IsSuperseded(navigationVersion))
            {
                return;
            }

            await StopProgressLoopCoreAsync();
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

    private Task SeekRelativeAsync(TimeSpan offset, CancellationToken cancellationToken)
    {
        var target = PlaybackMath.ClampPosition(Position + offset, Duration);
        return SeekToAsync(target, cancellationToken);
    }

    private void ChangeRate(int direction)
    {
        var currentIndex = FindClosestRateIndex(Rate);
        var nextIndex = Math.Clamp(currentIndex + direction, 0, PlaybackRates.Length - 1);
        Rate = PlaybackRates[nextIndex];
    }

    private static int FindClosestRateIndex(double rate)
    {
        var closestIndex = 0;
        var closestDistance = double.MaxValue;
        for (var index = 0; index < PlaybackRates.Length; index++)
        {
            var distance = Math.Abs(PlaybackRates[index] - rate);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestIndex = index;
            }
        }

        return closestIndex;
    }

    private Task SeekToAsync(TimeSpan position, CancellationToken cancellationToken)
        => RunOnUiAsync(async () =>
        {
            var result = await _playerService.SeekAsync(position, cancellationToken);
            LastError = result.IsSuccess ? string.Empty : result.UserMessage;
        }, cancellationToken);

    private async Task RunProgressLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
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
                });
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            await UpdateLastErrorAsync("播放进度更新失败。");
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
            PlaybackState = snapshot.State;
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
            var result = await _playerService.SetVolumeAsync(value);
            await UpdateLastErrorAsync(
                result.IsSuccess ? string.Empty : result.UserMessage);
            if (_settingsService is not null)
            {
                _settingsService.Current.Player.DefaultVolume = value;
                await PersistSettingsSafelyAsync();
            }
        }
        catch (Exception)
        {
            await UpdateLastErrorAsync("音量设置失败。");
        }
    }

    private async Task ApplyRateAsync(double value)
    {
        try
        {
            var result = await _playerService.SetRateAsync(value);
            await UpdateLastErrorAsync(
                result.IsSuccess ? string.Empty : result.UserMessage);
            if (_settingsService is not null)
            {
                _settingsService.Current.Player.DefaultPlaybackRate = value;
                await PersistSettingsSafelyAsync();
            }
        }
        catch (Exception)
        {
            await UpdateLastErrorAsync("速率设置失败。");
        }
    }

    private void ShowScreenshotSuccessMessage()
    {
        var version = Interlocked.Increment(ref _screenshotMessageVersion);
        ScreenshotMessage = "截图已保存。";
        _ = ClearScreenshotMessageAsync(version);
    }

    private async Task ClearScreenshotMessageAsync(int version)
    {
        try
        {
            await Task.Delay(ScreenshotSuccessMessageLifetime);
            await _dispatcher.InvokeAsync(() =>
            {
                if (!IsDisposed
                    && Volatile.Read(ref _screenshotMessageVersion) == version)
                {
                    ScreenshotMessage = string.Empty;
                }
            });
        }
        catch (Exception)
        {
        }
    }

    private void ClearScreenshotMessage()
    {
        Interlocked.Increment(ref _screenshotMessageVersion);
        ScreenshotMessage = string.Empty;
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
            });
        }
        catch (Exception)
        {
        }
    }

    private void RestartHistoryFlushForActiveMedia()
    {
        if (IsDisposed || _historyService?.IsAvailable != true || _activeSessionId <= 0)
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
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (IsDisposed
                    || _settingsService is null
                    || _historyService?.IsAvailable != true
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
                    ;
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
                await historyTask.WaitAsync(HistoryFlushTimeout);
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

        if (_historyService?.IsAvailable != true)
        {
            return;
        }

        using var flushCts = new CancellationTokenSource(HistoryFlushTimeout);
        await SaveFinalProgressAsync(flushCts.Token);

        try
        {
            await _historyService.FlushAsync(flushCts.Token)
                .WaitAsync(HistoryFlushTimeout)
                ;
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
        if (historyService?.IsAvailable != true)
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
                ;
        }
        catch (TimeoutException)
        {
        }
        catch (Exception exception)
        {
            LogHistoryFailure(exception, "Failed to save final playback progress.");
        }
    }

    private async Task PersistSettingsSafelyAsync()
    {
        if (_settingsService is null)
        {
            return;
        }

        try
        {
            var result = await _settingsService.SaveAsync();
            if (!result.IsSuccess)
            {
                Log.Warning(
                    result.Exception,
                    "Failed to persist runtime settings: {TechnicalMessage}",
                    result.TechnicalMessage);
            }
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Failed to persist runtime settings.");
        }
    }

    private static void LogHistoryFailure(Exception exception, string message)
    {
        try
        {
            Log.Warning(exception, message);
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
                await progressTask;
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
        if (IsDisposed
            || e.MediaSessionId <= 0
            || e.MediaSessionId != Interlocked.Read(ref _activeSessionId))
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
        _isUpdatingTracks = true;
        try
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

            SelectedAudioTrack = AudioTracks.FirstOrDefault(track => track.IsSelected);
            SelectedSubtitleTrack = SubtitleTracks.FirstOrDefault(track => track.IsSelected);
        }
        finally
        {
            _isUpdatingTracks = false;
        }
    }

    private async void OnStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        try
        {
            if (IsDisposed
                || e.MediaSessionId <= 0
                || e.MediaSessionId != Interlocked.Read(ref _activeSessionId))
            {
                return;
            }

            await ApplyUiAsync(() =>
            {
                if (IsDisposed
                    || e.MediaSessionId != Interlocked.Read(ref _activeSessionId))
                {
                    return;
                }

                PlaybackState = e.NewState;
                UpdatePlaylistPlaybackState(e.MediaSessionId, e.NewState);
            });

            await RecordPlaybackStartAsync(e.MediaSessionId);
        }
        catch (Exception exception)
        {
            LogHistoryFailure(exception, "Failed to record playback start.");
        }
    }

    private async Task RecordPlaybackStartAsync(long sessionId)
    {
        if (IsDisposed
            || sessionId != Interlocked.Read(ref _activeSessionId)
            || PlaybackState != PlaybackState.Playing
            || _historyService?.IsAvailable != true
            || _activeMediaPath is not { } path
            || Interlocked.Exchange(ref _startedSessionId, sessionId) == sessionId)
        {
            return;
        }

        await _historyService.StartPlaybackAsync(
            path,
            Path.GetFileName(path),
            _activeDuration,
            sessionId);
    }

    private async void OnMediaParsed(object? sender, MediaParsedEventArgs e)
    {
        try
        {
            if (IsDisposed
                || e.MediaSessionId <= 0
                || e.MediaSessionId != Interlocked.Read(ref _activeSessionId))
            {
                return;
            }

            await ApplyUiAsync(() =>
            {
                if (IsDisposed
                    || e.MediaSessionId != Interlocked.Read(ref _activeSessionId))
                {
                    return;
                }

                ApplyTracks(e.Tracks);
                UpdatePlaylistDuration(e.MediaSessionId, e.Duration);
                MediaParsed?.Invoke(this, e);
            });
        }
        catch (Exception exception)
        {
            LogHistoryFailure(exception, "Failed to process parsed media information.");
        }
    }

    private async void OnPlaybackFailed(object? sender, PlaybackFailedEventArgs e)
    {
        try
        {
            if (IsDisposed
                || e.MediaSessionId <= 0
                || e.MediaSessionId != Interlocked.Read(ref _activeSessionId)
                || Interlocked.Exchange(ref _handledFailureSessionId, e.MediaSessionId)
                    == e.MediaSessionId)
            {
                return;
            }

            await ApplyUiAsync(() =>
            {
                if (IsDisposed
                    || e.MediaSessionId != Interlocked.Read(ref _activeSessionId))
                {
                    return;
                }

                PlaybackState = VideoPlayer.Enums.PlaybackState.Error;
                LastError = e.UserMessage;
                UpdatePlaylistPlaybackState(
                    e.MediaSessionId,
                    VideoPlayer.Enums.PlaybackState.Error);
            });

            await FlushHistoryAsync();
            if (!IsDisposed
                && e.MediaSessionId == Interlocked.Read(ref _activeSessionId))
            {
                await ShowErrorAsync("播放失败", e.UserMessage);
            }
        }
        catch (Exception exception)
        {
            LogHistoryFailure(exception, "Failed to process playback failure.");
        }
    }

    private async void OnEndReached(object? sender, EndReachedEventArgs e)
    {
        try
        {
            await HandleEndReachedAsync(e);
        }
        catch (Exception exception)
        {
            LogHistoryFailure(exception, "Failed to handle end reached.");
        }
    }

    private Task ApplyUiAsync(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(action);
    }

    private void UpdatePlaylistPlaybackState(long sessionId, PlaybackState state)
    {
        var playlistService = _playlistService;
        var current = playlistService?.Current;
        if (current is null
            || sessionId != Interlocked.Read(ref _activeSessionId)
            || !string.Equals(
                current.FilePath,
                _activeMediaPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        current.IsPlaying = state == VideoPlayer.Enums.PlaybackState.Playing;
        playlistService?.NotifyCurrentItemChanged();
    }

    private void UpdatePlaylistDuration(long sessionId, TimeSpan duration)
    {
        if (sessionId != Interlocked.Read(ref _activeSessionId))
        {
            return;
        }

        var current = _playlistService?.Current;
        if (current is null || duration <= TimeSpan.Zero)
        {
            return;
        }

        current.Duration = duration;
        _activeDuration = duration;
        _playlistService?.NotifyCurrentItemChanged();
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
