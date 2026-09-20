using System.IO;
using LibVLCSharp.Shared;
using Serilog;
using VideoPlayer.Enums;
using VideoPlayer.Helpers;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;
using VideoPlayer.Models.Events;

namespace VideoPlayer.Services;

public sealed class PlayerService : IPlayerService
{
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly PlaybackStateMachine _stateMachine = new();
    private readonly object _stateSync = new();
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private EventHandler<EventArgs>? _openingHandler;
    private EventHandler<EventArgs>? _playingHandler;
    private EventHandler<EventArgs>? _pausedHandler;
    private EventHandler<EventArgs>? _stoppedHandler;
    private EventHandler<EventArgs>? _endReachedHandler;
    private EventHandler<EventArgs>? _encounteredErrorHandler;
    private EventHandler<MediaPlayerLengthChangedEventArgs>? _lengthChangedHandler;
    private EventHandler<MediaParsedChangedEventArgs>? _mediaParsedChangedHandler;
    private Media? _media;
    private long _sessionId;
    private int _disposed;
    private int _volume;
    private bool _isMuted;
    private double _rate = 1.0;
    private long _lastTracksSessionId;
    private PlayerTrackInfo[] _lastTracks = [];

    public PlayerService(
        IUiDispatcher dispatcher,
        ILogger logger,
        string[]? vlcOptions = null)
    {
        _dispatcher = dispatcher;
        _logger = logger;
        Core.Initialize();
        _libVlc = new LibVLC(vlcOptions ?? []);
        _mediaPlayer = new MediaPlayer(_libVlc);
        _volume = _mediaPlayer.Volume;
        _isMuted = _mediaPlayer.Mute;
        _rate = _mediaPlayer.Rate;
    }

    public MediaPlayer? MediaPlayer
    {
        get
        {
            _commandGate.Wait();
            try
            {
                return IsInitialized ? _mediaPlayer : null;
            }
            finally
            {
                _commandGate.Release();
            }
        }
    }

    public bool IsInitialized => Volatile.Read(ref _disposed) == 0;

    public bool HasMedia
    {
        get
        {
            _commandGate.Wait();
            try
            {
                return IsInitialized && HasMediaCore();
            }
            finally
            {
                _commandGate.Release();
            }
        }
    }

    public PlaybackSnapshot Snapshot
    {
        get
        {
            _commandGate.Wait();
            try
            {
                return CreateSnapshot();
            }
            finally
            {
                _commandGate.Release();
            }
        }
    }

    public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;
    public event EventHandler<MediaOpenedEventArgs>? MediaOpened;
    public event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;
    public event EventHandler<MediaParsedEventArgs>? MediaParsed;
    public event EventHandler<TracksChangedEventArgs>? TracksChanged;
    public event EventHandler<EndReachedEventArgs>? EndReached;

    public async Task<ServiceResult<MediaSessionInfo>> OpenAsync(
        OpenMediaRequest request,
        CancellationToken cancellationToken = default)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsInitialized)
            {
                return ServiceResult<MediaSessionInfo>.Failure(
                    ErrorCode.PlayerNotInitialized,
                    "播放器未初始化。",
                    "PlayerService is disposed.");
            }

            string path;
            try
            {
                path = PathHelper.NormalizePath(request.FilePath);
            }
            catch (Exception exception)
            {
                return ServiceResult<MediaSessionInfo>.Failure(
                    ErrorCode.InvalidPath,
                    "视频路径无效。",
                    request.FilePath,
                    exception);
            }

            if (!File.Exists(path))
            {
                return ServiceResult<MediaSessionInfo>.Failure(
                    ErrorCode.FileNotFound,
                    "视频文件不存在。",
                    path);
            }

            Media? newMedia = null;
            var sessionId = 0L;
            try
            {
                newMedia = new Media(_libVlc, new Uri(path));
                sessionId = NextSessionId();
                UnsubscribeMediaPlayerEvents();
                _media?.Dispose();
                _media = newMedia;
                newMedia = null;
                BindMediaPlayerEvents(sessionId);
                _mediaPlayer.Media = _media;
                TryAttachExternalSubtitle(request.ExternalSubtitlePath);
                ApplyPlaybackSettingsCore();
                Transition(PlaybackState.Opening, sessionId);
                PostMediaOpened(sessionId, path);

                if (request.AutoPlay)
                {
                    _mediaPlayer.Play();
                }

                return ServiceResult<MediaSessionInfo>.Success(
                    new MediaSessionInfo(
                        sessionId,
                        path,
                        request.AutoPlay,
                        request.StartPosition,
                        DateTimeOffset.UtcNow));
            }
            finally
            {
                newMedia?.Dispose();
            }
        }
        catch (Exception exception)
        {
            var sessionId = GetCurrentSessionId();
            _logger.Error(exception, "Failed to open media {Path}", request.FilePath);
            RaiseFailure(sessionId, ErrorCode.Unknown, "无法打开视频。", exception);
            return ServiceResult<MediaSessionInfo>.Failure(
                ErrorCode.Unknown,
                "无法打开视频。",
                request.FilePath,
                exception);
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task<ServiceResult> PlayAsync(
        CancellationToken cancellationToken = default)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsInitialized)
            {
                return ServiceResult.Failure(
                    ErrorCode.PlayerNotInitialized,
                    "播放器未初始化。",
                    "PlayerService is disposed.");
            }

            if (!HasMediaCore())
            {
                return ServiceResult.Failure(
                    ErrorCode.InvalidState,
                    "没有可播放的视频。",
                    "No media");
            }

            return _mediaPlayer.Play()
                ? ServiceResult.Success()
                : ServiceResult.Failure(
                    ErrorCode.InvalidState,
                    "无法开始播放。",
                    "MediaPlayer.Play returned false.");
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task<ServiceResult> PauseAsync(
        CancellationToken cancellationToken = default)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsInitialized)
            {
                return ServiceResult.Failure(
                    ErrorCode.PlayerNotInitialized,
                    "播放器未初始化。",
                    "PlayerService is disposed.");
            }

            if (_stateMachine.Current != PlaybackState.Playing)
            {
                return ServiceResult.Failure(
                    ErrorCode.InvalidState,
                    "当前没有正在播放的视频。",
                    "Pause rejected.");
            }

            _mediaPlayer.Pause();
            return ServiceResult.Success();
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task<ServiceResult> StopAsync(
        CancellationToken cancellationToken = default)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsInitialized)
            {
                return ServiceResult.Success();
            }

            var sessionId = GetCurrentSessionId();
            _mediaPlayer.Stop();
            Transition(PlaybackState.Stopped, sessionId);
            return ServiceResult.Success();
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task<ServiceResult> SeekAsync(
        TimeSpan position,
        CancellationToken cancellationToken = default)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsInitialized)
            {
                return ServiceResult.Failure(
                    ErrorCode.PlayerNotInitialized,
                    "播放器未初始化。",
                    "PlayerService is disposed.");
            }

            if (!HasMediaCore())
            {
                return ServiceResult.Failure(ErrorCode.InvalidState, "没有可播放的视频。", "No media");
            }

            if (!_mediaPlayer.IsSeekable || _mediaPlayer.Length <= 0)
            {
                return ServiceResult.Failure(ErrorCode.SeekNotAvailable, "当前视频不支持跳转。", "Not seekable");
            }

            var target = PlaybackMath.ClampPosition(
                position,
                TimeSpan.FromMilliseconds(_mediaPlayer.Length));
            _mediaPlayer.Time = (long)target.TotalMilliseconds;
            return ServiceResult.Success();
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task<ServiceResult> SetVolumeAsync(
        int volume,
        CancellationToken cancellationToken = default)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsInitialized)
            {
                return ServiceResult.Failure(
                    ErrorCode.PlayerNotInitialized,
                    "播放器未初始化。",
                    "PlayerService is disposed.");
            }

            _volume = PlaybackMath.ClampVolume(volume);
            _mediaPlayer.Volume = _volume;
            return ServiceResult.Success();
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task<ServiceResult> SetMuteAsync(
        bool isMuted,
        CancellationToken cancellationToken = default)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsInitialized)
            {
                return ServiceResult.Failure(
                    ErrorCode.PlayerNotInitialized,
                    "播放器未初始化。",
                    "PlayerService is disposed.");
            }

            _isMuted = isMuted;
            _mediaPlayer.Mute = _isMuted;
            return ServiceResult.Success();
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task<ServiceResult> SetRateAsync(
        double rate,
        CancellationToken cancellationToken = default)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsInitialized)
            {
                return ServiceResult.Failure(
                    ErrorCode.PlayerNotInitialized,
                    "播放器未初始化。",
                    "PlayerService is disposed.");
            }

            _rate = PlaybackMath.ClampRate(rate);
            _mediaPlayer.SetRate((float)_rate);
            return ServiceResult.Success();
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task<ServiceResult<IReadOnlyList<PlayerTrackInfo>>> GetTracksAsync(
        PlayerTrackType type,
        CancellationToken cancellationToken = default)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsInitialized)
            {
                return ServiceResult<IReadOnlyList<PlayerTrackInfo>>.Failure(
                    ErrorCode.PlayerNotInitialized,
                    "播放器未初始化。",
                    "PlayerService is disposed.");
            }

            return ServiceResult<IReadOnlyList<PlayerTrackInfo>>.Success(
                GetTracksCore(type));
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task<ServiceResult> SelectAudioTrackAsync(
        int trackId,
        CancellationToken cancellationToken = default)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsInitialized)
            {
                return ServiceResult.Failure(
                    ErrorCode.PlayerNotInitialized,
                    "播放器未初始化。",
                    "PlayerService is disposed.");
            }

            return _mediaPlayer.SetAudioTrack(trackId)
                ? ServiceResult.Success()
                : ServiceResult.Failure(
                    ErrorCode.TrackNotFound,
                    "音轨不可用。",
                    $"Track {trackId}");
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task<ServiceResult> SelectSubtitleTrackAsync(
        int? trackId,
        CancellationToken cancellationToken = default)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsInitialized)
            {
                return ServiceResult.Failure(
                    ErrorCode.PlayerNotInitialized,
                    "播放器未初始化。",
                    "PlayerService is disposed.");
            }

            return _mediaPlayer.SetSpu(trackId ?? -1)
                ? ServiceResult.Success()
                : ServiceResult.Failure(
                    ErrorCode.TrackNotFound,
                    "字幕轨不可用。",
                    $"Track {trackId}");
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task<ServiceResult> AddExternalSubtitleAsync(
        string filePath,
        bool select,
        CancellationToken cancellationToken = default)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsInitialized)
            {
                return ServiceResult.Failure(
                    ErrorCode.PlayerNotInitialized,
                    "播放器未初始化。",
                    "PlayerService is disposed.");
            }

            string uri;
            try
            {
                var path = PathHelper.NormalizePath(filePath);
                if (!File.Exists(path))
                {
                    return ServiceResult.Failure(
                        ErrorCode.SubtitleNotFound,
                        "字幕加载失败。",
                        filePath);
                }

                uri = new Uri(path).AbsoluteUri;
            }
            catch (Exception exception)
            {
                return ServiceResult.Failure(
                    ErrorCode.SubtitleNotFound,
                    "字幕加载失败。",
                    filePath,
                    exception);
            }

            return _mediaPlayer.AddSlave(MediaSlaveType.Subtitle, uri, select)
                ? ServiceResult.Success()
                : ServiceResult.Failure(
                    ErrorCode.SubtitleNotFound,
                    "字幕加载失败。",
                    filePath);
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!IsInitialized)
        {
            return;
        }

        await _commandGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            UnsubscribeMediaPlayerEvents();
            StateChanged = null;
            MediaParsed = null;
            TracksChanged = null;
            MediaOpened = null;
            PlaybackFailed = null;
            DisposePlayerSafely();
        }
        finally
        {
            _commandGate.Release();
        }
    }

    internal void HandleMediaEvent(long sessionId, PlaybackState state)
    {
        if (!IsCurrentSession(sessionId))
        {
            return;
        }

        try
        {
            Transition(state, sessionId);
        }
        catch (Exception exception)
        {
            HandleCallbackFailure(sessionId, exception);
        }
    }

    internal void HandleEndReached(long sessionId)
    {
        if (!IsCurrentSession(sessionId))
        {
            return;
        }

        try
        {
            Transition(PlaybackState.Ended, sessionId);
            if (!IsCurrentSession(sessionId))
            {
                return;
            }

            var filePath = GetMediaPath() ?? string.Empty;
            _dispatcher.Post(() =>
            {
                if (!IsCurrentSession(sessionId))
                {
                    return;
                }

                EndReached?.Invoke(
                    this,
                    new EndReachedEventArgs
                    {
                        MediaSessionId = sessionId,
                        FilePath = filePath,
                        OccurredAtUtc = DateTimeOffset.UtcNow
                    });
            });
        }
        catch (Exception exception)
        {
            HandleCallbackFailure(sessionId, exception);
        }
    }

    internal void HandleMediaError(long sessionId)
    {
        if (!IsCurrentSession(sessionId))
        {
            return;
        }

        try
        {
            Transition(PlaybackState.Error, sessionId);
            RaiseFailure(sessionId, ErrorCode.UnsupportedMedia, "视频无法播放。", null);
        }
        catch (Exception exception)
        {
            HandleCallbackFailure(sessionId, exception);
        }
    }

    internal void HandleLengthChanged(long sessionId, long length)
    {
        if (!IsCurrentSession(sessionId))
        {
            return;
        }

        PostMediaParsed(
            sessionId,
            TimeSpan.FromMilliseconds(Math.Max(0, length)));
    }

    internal void HandleMediaParsedChanged(long sessionId, MediaParsedStatus status)
    {
        if (status != MediaParsedStatus.Done || !IsCurrentSession(sessionId))
        {
            return;
        }

        PostMediaParsed(sessionId, null);
    }

    private void PostMediaParsed(long sessionId, TimeSpan? duration)
    {
        try
        {
            _dispatcher.Post(() =>
            {
                if (!IsCurrentSession(sessionId))
                {
                    return;
                }

                try
                {
                    var tracks = GetAllTracksCore();
                    var args = new MediaParsedEventArgs
                    {
                        MediaSessionId = sessionId,
                        Duration = duration ?? GetDuration(),
                        HasVideo = _mediaPlayer.VideoTrackCount > 0,
                        HasAudio = _mediaPlayer.AudioTrackCount > 0,
                        Tracks = tracks
                    };
                    try
                    {
                        MediaParsed?.Invoke(this, args);
                    }
                    catch (Exception exception)
                    {
                        TryLogCallbackFailure(
                            exception,
                            "Failed to publish media parsed details for session {MediaSessionId}.",
                            sessionId);
                    }

                    RaiseTracksChanged(sessionId, tracks);
                }
                catch (Exception exception)
                {
                    TryLogCallbackFailure(
                        exception,
                        "Failed to collect parsed media details for session {MediaSessionId}.",
                        sessionId);
                }
            });
        }
        catch (Exception exception)
        {
            TryLogCallbackFailure(
                exception,
                "Failed to dispatch media parsed details for session {MediaSessionId}.",
                sessionId);
        }
    }

    private void RaiseTracksChanged(
        long sessionId,
        IReadOnlyList<PlayerTrackInfo> tracks)
    {
        if (!IsCurrentSession(sessionId))
        {
            return;
        }

        var snapshot = tracks.ToArray();
        lock (_stateSync)
        {
            if (_lastTracksSessionId == sessionId
                && _lastTracks.SequenceEqual(snapshot))
            {
                return;
            }

            _lastTracksSessionId = sessionId;
            _lastTracks = snapshot;
        }

        try
        {
            TracksChanged?.Invoke(
                this,
                new TracksChangedEventArgs
                {
                    MediaSessionId = sessionId,
                    Tracks = snapshot
                });
        }
        catch (Exception exception)
        {
            TryLogCallbackFailure(
                exception,
                "Failed to publish track changes for session {MediaSessionId}.",
                sessionId);
        }
    }

    private PlayerTrackInfo[] GetTracksCore(PlayerTrackType type)
    {
        if (type == PlayerTrackType.Audio)
        {
            return _mediaPlayer.AudioTrackDescription
                .Select(track => new PlayerTrackInfo(
                    PlayerTrackType.Audio,
                    track.Id,
                    track.Name ?? $"音轨 {track.Id}",
                    track.Name ?? "und",
                    track.Id == _mediaPlayer.AudioTrack,
                    false))
                .ToArray();
        }

        return _mediaPlayer.SpuDescription
            .Select(track => new PlayerTrackInfo(
                PlayerTrackType.Subtitle,
                track.Id,
                track.Name ?? $"字幕 {track.Id}",
                track.Name ?? "und",
                track.Id == _mediaPlayer.Spu,
                false))
            .ToArray();
    }

    private PlayerTrackInfo[] GetAllTracksCore()
    {
        return
        [
            .. GetTracksCore(PlayerTrackType.Audio),
            .. GetTracksCore(PlayerTrackType.Subtitle)
        ];
    }

    private void TryAttachExternalSubtitle(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            var path = PathHelper.NormalizePath(filePath);
            if (!File.Exists(path))
            {
                _logger.Warning(
                    "External subtitle does not exist: {SubtitlePath}",
                    path);
                return;
            }

            if (!_mediaPlayer.AddSlave(
                    MediaSlaveType.Subtitle,
                    new Uri(path).AbsoluteUri,
                    select: true))
            {
                _logger.Warning(
                    "LibVLC rejected external subtitle: {SubtitlePath}",
                    path);
            }
        }
        catch (Exception exception)
        {
            TryLogCallbackFailure(
                exception,
                "Failed to attach external subtitle for session {MediaSessionId}.",
                GetCurrentSessionId());
        }
    }

    private void BindMediaPlayerEvents(long sessionId)
    {
        _openingHandler = (_, _) => HandleMediaEvent(sessionId, PlaybackState.Opening);
        _playingHandler = (_, _) => HandleMediaEvent(sessionId, PlaybackState.Playing);
        _pausedHandler = (_, _) => HandleMediaEvent(sessionId, PlaybackState.Paused);
        _stoppedHandler = (_, _) => HandleMediaEvent(sessionId, PlaybackState.Stopped);
        _endReachedHandler = (_, _) => HandleEndReached(sessionId);
        _encounteredErrorHandler = (_, _) => HandleMediaError(sessionId);
        _lengthChangedHandler = (_, e) => HandleLengthChanged(sessionId, e.Length);
        _mediaParsedChangedHandler = (_, e) => HandleMediaParsedChanged(sessionId, e.ParsedStatus);

        _mediaPlayer.Opening += _openingHandler;
        _mediaPlayer.Playing += _playingHandler;
        _mediaPlayer.Paused += _pausedHandler;
        _mediaPlayer.Stopped += _stoppedHandler;
        _mediaPlayer.EndReached += _endReachedHandler;
        _mediaPlayer.EncounteredError += _encounteredErrorHandler;
        _mediaPlayer.LengthChanged += _lengthChangedHandler;
        _media!.ParsedChanged += _mediaParsedChangedHandler;
    }

    private void UnsubscribeMediaPlayerEvents()
    {
        if (_openingHandler is not null)
        {
            _mediaPlayer.Opening -= _openingHandler;
        }

        if (_playingHandler is not null)
        {
            _mediaPlayer.Playing -= _playingHandler;
        }

        if (_pausedHandler is not null)
        {
            _mediaPlayer.Paused -= _pausedHandler;
        }

        if (_stoppedHandler is not null)
        {
            _mediaPlayer.Stopped -= _stoppedHandler;
        }

        if (_endReachedHandler is not null)
        {
            _mediaPlayer.EndReached -= _endReachedHandler;
        }

        if (_encounteredErrorHandler is not null)
        {
            _mediaPlayer.EncounteredError -= _encounteredErrorHandler;
        }

        if (_lengthChangedHandler is not null)
        {
            _mediaPlayer.LengthChanged -= _lengthChangedHandler;
        }

        if (_mediaParsedChangedHandler is not null && _media is not null)
        {
            _media.ParsedChanged -= _mediaParsedChangedHandler;
        }

        _openingHandler = null;
        _playingHandler = null;
        _pausedHandler = null;
        _stoppedHandler = null;
        _endReachedHandler = null;
        _encounteredErrorHandler = null;
        _lengthChangedHandler = null;
        _mediaParsedChangedHandler = null;
    }

    private void DisposePlayerSafely()
    {
        TryDisposeNative(() => _mediaPlayer.Stop(), "stop MediaPlayer");
        TryDisposeNative(() => _mediaPlayer.Dispose(), "dispose MediaPlayer");
        TryDisposeNative(() => _media?.Dispose(), "dispose Media");
        _media = null;
        TryDisposeNative(() => _libVlc.Dispose(), "dispose LibVLC");
    }

    private void TryDisposeNative(Action action, string operation)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            try
            {
                _logger.Error(exception, "Failed to {Operation}.", operation);
            }
            catch
            {
                // Native disposal must continue even if logging fails.
            }
        }
    }

    private void Transition(PlaybackState next, long sessionId)
    {
        if (!IsCurrentSession(sessionId))
        {
            return;
        }

        PlaybackState oldState;
        string? reason;
        lock (_stateSync)
        {
            if (Volatile.Read(ref _disposed) != 0 || _sessionId != sessionId)
            {
                return;
            }

            if (!_stateMachine.TryTransition(next, out oldState, out reason))
            {
                _logger.Debug("Ignored state transition: {Reason}", reason);
                return;
            }
        }

        PostStateChanged(sessionId, oldState, next);
    }

    private void PostStateChanged(
        long sessionId,
        PlaybackState oldState,
        PlaybackState newState)
    {
        _dispatcher.Post(() =>
        {
            if (!IsCurrentSession(sessionId))
            {
                return;
            }

            StateChanged?.Invoke(
                this,
                new PlaybackStateChangedEventArgs
                {
                    MediaSessionId = sessionId,
                    OldState = oldState,
                    NewState = newState,
                    OccurredAtUtc = DateTimeOffset.UtcNow
                });
        });
    }

    private void PostMediaOpened(long sessionId, string path)
    {
        _dispatcher.Post(() =>
        {
            if (!IsCurrentSession(sessionId))
            {
                return;
            }

            MediaOpened?.Invoke(
                this,
                new MediaOpenedEventArgs
                {
                    MediaSessionId = sessionId,
                    FilePath = path,
                    OccurredAtUtc = DateTimeOffset.UtcNow
                });
        });
    }

    private void RaiseFailure(
        long sessionId,
        ErrorCode code,
        string message,
        Exception? exception)
    {
        if (!IsCurrentSession(sessionId))
        {
            return;
        }

        try
        {
            _dispatcher.Post(() =>
            {
                if (!IsCurrentSession(sessionId))
                {
                    return;
                }

                PlaybackFailed?.Invoke(
                    this,
                    new PlaybackFailedEventArgs
                    {
                        MediaSessionId = sessionId,
                        ErrorCode = code,
                        UserMessage = message,
                        TechnicalMessage = exception?.Message ?? message,
                        Exception = exception
                    });
            });
        }
        catch (Exception dispatchException)
        {
            TryLogCallbackFailure(
                dispatchException,
                "Failed to publish playback failure for media session {MediaSessionId}.",
                sessionId);
        }
    }

    private void HandleCallbackFailure(long sessionId, Exception exception)
    {
        if (!IsCurrentSession(sessionId))
        {
            return;
        }

        TryLogCallbackFailure(
            exception,
            "VLC callback failed for media session {MediaSessionId}.",
            sessionId);

        try
        {
            RaiseFailure(
                sessionId,
                ErrorCode.Unknown,
                "播放器事件处理失败。",
                exception);
        }
        catch (Exception failureException)
        {
            TryLogCallbackFailure(
                failureException,
                "Failed to publish VLC callback failure for media session {MediaSessionId}.",
                sessionId);
        }
    }

    private void TryLogCallbackFailure(
        Exception exception,
        string messageTemplate,
        long sessionId)
    {
        try
        {
            _logger.Error(exception, messageTemplate, sessionId);
        }
        catch
        {
            // VLC callbacks must never observe managed logging failures.
        }
    }

    private PlaybackSnapshot CreateSnapshot()
    {
        if (!IsInitialized)
        {
            return CreateDefaultSnapshot();
        }

        long sessionId;
        PlaybackState state;
        lock (_stateSync)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return CreateDefaultSnapshot();
            }

            sessionId = _sessionId;
            state = _stateMachine.Current;
        }

        return new PlaybackSnapshot(
            sessionId,
            state,
            GetMediaPath(),
            GetPosition(),
            GetDuration(),
            _volume,
            _isMuted,
            _rate,
            _mediaPlayer.IsSeekable,
            _mediaPlayer.VideoTrackCount > 0,
            _mediaPlayer.AudioTrackCount > 0,
            DateTimeOffset.UtcNow);
    }

    private static PlaybackSnapshot CreateDefaultSnapshot()
    {
        return new PlaybackSnapshot(
            0,
            PlaybackState.None,
            null,
            TimeSpan.Zero,
            TimeSpan.Zero,
            0,
            false,
            1.0,
            false,
            false,
            false,
            DateTimeOffset.UtcNow);
    }

    private void ApplyPlaybackSettingsCore()
    {
        _mediaPlayer.Volume = _volume;
        _mediaPlayer.Mute = _isMuted;
        _mediaPlayer.SetRate((float)_rate);
    }

    private bool HasMediaCore()
    {
        using var media = _mediaPlayer.Media;
        return media is not null;
    }

    private string? GetMediaPath()
    {
        using var media = _mediaPlayer.Media;
        return media?.Mrl;
    }

    private long NextSessionId()
    {
        lock (_stateSync)
        {
            return ++_sessionId;
        }
    }

    private long GetCurrentSessionId()
    {
        lock (_stateSync)
        {
            return _sessionId;
        }
    }

    private bool IsCurrentSession(long sessionId)
    {
        if (!IsInitialized)
        {
            return false;
        }

        lock (_stateSync)
        {
            return Volatile.Read(ref _disposed) == 0 && _sessionId == sessionId;
        }
    }

    private TimeSpan GetPosition()
    {
        var value = _mediaPlayer.Time;
        return value < 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(value);
    }

    private TimeSpan GetDuration()
    {
        var value = _mediaPlayer.Length;
        return value <= 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(value);
    }
}
