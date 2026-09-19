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
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private Media? _media;
    private long _sessionId;
    private int _disposed;

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
        _mediaPlayer.Opening += OnOpening;
        _mediaPlayer.Playing += OnPlaying;
        _mediaPlayer.Paused += OnPaused;
        _mediaPlayer.Stopped += OnStopped;
        _mediaPlayer.EndReached += OnEndReached;
        _mediaPlayer.EncounteredError += OnEncounteredError;
    }

    public MediaPlayer? MediaPlayer => _mediaPlayer;

    public bool IsInitialized => Volatile.Read(ref _disposed) == 0;

    public bool HasMedia
    {
        get
        {
            if (!IsInitialized)
            {
                return false;
            }

            using var media = _mediaPlayer.Media;
            return media is not null;
        }
    }

    public PlaybackSnapshot Snapshot => new(
        Interlocked.Read(ref _sessionId),
        _stateMachine.Current,
        GetMediaPath(),
        GetPosition(),
        GetDuration(),
        _mediaPlayer.Volume,
        _mediaPlayer.Mute,
        _mediaPlayer.Rate,
        _mediaPlayer.IsSeekable,
        _mediaPlayer.VideoTrackCount > 0,
        _mediaPlayer.AudioTrackCount > 0,
        DateTimeOffset.UtcNow);

    public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;
    public event EventHandler<MediaOpenedEventArgs>? MediaOpened;
    public event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;

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

            _media?.Dispose();
            _media = new Media(_libVlc, new Uri(path));
            _mediaPlayer.Media = _media;
            var sessionId = Interlocked.Increment(ref _sessionId);
            Transition(PlaybackState.Opening, sessionId);

            _dispatcher.Post(() => MediaOpened?.Invoke(
                this,
                new MediaOpenedEventArgs
                {
                    MediaSessionId = sessionId,
                    FilePath = path,
                    OccurredAtUtc = DateTimeOffset.UtcNow
                }));

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
        catch (Exception exception)
        {
            var sessionId = Interlocked.Read(ref _sessionId);
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

            if (!HasMedia)
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

            _mediaPlayer.Stop();
            Transition(PlaybackState.Stopped, Interlocked.Read(ref _sessionId));
            return ServiceResult.Success();
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

        await _commandGate.WaitAsync();
        try
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _mediaPlayer.Opening -= OnOpening;
            _mediaPlayer.Playing -= OnPlaying;
            _mediaPlayer.Paused -= OnPaused;
            _mediaPlayer.Stopped -= OnStopped;
            _mediaPlayer.EndReached -= OnEndReached;
            _mediaPlayer.EncounteredError -= OnEncounteredError;
            _mediaPlayer.Stop();
            _mediaPlayer.Dispose();
            _media?.Dispose();
            _media = null;
            _libVlc.Dispose();
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private void OnOpening(object? sender, EventArgs e)
        => Transition(PlaybackState.Opening, Interlocked.Read(ref _sessionId));

    private void OnPlaying(object? sender, EventArgs e)
        => Transition(PlaybackState.Playing, Interlocked.Read(ref _sessionId));

    private void OnPaused(object? sender, EventArgs e)
        => Transition(PlaybackState.Paused, Interlocked.Read(ref _sessionId));

    private void OnStopped(object? sender, EventArgs e)
        => Transition(PlaybackState.Stopped, Interlocked.Read(ref _sessionId));

    private void OnEndReached(object? sender, EventArgs e)
        => Transition(PlaybackState.Ended, Interlocked.Read(ref _sessionId));

    private void OnEncounteredError(object? sender, EventArgs e)
    {
        var sessionId = Interlocked.Read(ref _sessionId);
        Transition(PlaybackState.Error, sessionId);
        RaiseFailure(sessionId, ErrorCode.UnsupportedMedia, "视频无法播放。", null);
    }

    private void Transition(PlaybackState next, long sessionId)
    {
        var oldState = _stateMachine.Current;
        if (!_stateMachine.TryTransition(next, out var reason))
        {
            _logger.Debug("Ignored state transition: {Reason}", reason);
            return;
        }

        _dispatcher.Post(() => StateChanged?.Invoke(
            this,
            new PlaybackStateChangedEventArgs
            {
                MediaSessionId = sessionId,
                OldState = oldState,
                NewState = next,
                OccurredAtUtc = DateTimeOffset.UtcNow
            }));
    }

    private void RaiseFailure(
        long sessionId,
        ErrorCode code,
        string message,
        Exception? exception)
    {
        _dispatcher.Post(() => PlaybackFailed?.Invoke(
            this,
            new PlaybackFailedEventArgs
            {
                MediaSessionId = sessionId,
                ErrorCode = code,
                UserMessage = message,
                TechnicalMessage = exception?.Message ?? message,
                Exception = exception
            }));
    }

    private string? GetMediaPath()
    {
        using var media = _mediaPlayer.Media;
        return media?.Mrl;
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
