using System.IO;
using VideoPlayer.Enums;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;
using VideoPlayer.Models.Events;
using VideoPlayer.ViewModels;

namespace VideoPlayer.Tests.Fakes;

public sealed class PlayerViewModelFixture
{
    public PlayerViewModelFixture(long sessionId)
    {
        Player = new FakePlayerService(sessionId);
        Playlist = new FakePlaylistService();
        Settings = new FakeSettingsService();
        Dialog = new FakeDialogService();
        Subtitle = new FakeSubtitleService();
        History = new FakeHistoryService();
        Screenshot = new FakeScreenshotService();
        ViewModel = new PlayerViewModel(
            Player,
            Playlist,
            Settings,
            Dialog,
            Subtitle,
            History,
            Screenshot);
    }

    public FakePlayerService Player { get; }
    public FakePlaylistService Playlist { get; }
    public FakeSettingsService Settings { get; }
    public FakeDialogService Dialog { get; }
    public FakeSubtitleService Subtitle { get; }
    public FakeHistoryService History { get; }
    public FakeScreenshotService Screenshot { get; }
    public PlayerViewModel ViewModel { get; }
}

public sealed class FakePlayerService : IPlayerService
{
    private readonly long _sessionId;

    public FakePlayerService(long sessionId) => _sessionId = sessionId;

    public Func<
        OpenMediaRequest,
        CancellationToken,
        Task<ServiceResult<MediaSessionInfo>>>? OpenHandler
    { get; set; }
    public int OpenCount { get; private set; }
    public int PlayCallCount { get; private set; }
    public int PauseCallCount { get; private set; }
    public Func<string, TimeSpan, string, CancellationToken, Task<ServiceResult<string>>>? PreviewHandler { get; set; }
    public ServiceResult<IReadOnlyList<PlayerTrackInfo>> TracksResult { get; set; } =
        ServiceResult<IReadOnlyList<PlayerTrackInfo>>.Success([]);
    public ServiceResult SelectAudioTrackResult { get; set; } = ServiceResult.Success();
    public ServiceResult SelectSubtitleTrackResult { get; set; } = ServiceResult.Success();
    public ServiceResult AddExternalSubtitleResult { get; set; } = ServiceResult.Success();
    public ServiceResult TakeSnapshotResult { get; set; } = ServiceResult.Success();
    public Func<string, CancellationToken, Task<ServiceResult>>? TakeSnapshotHandler { get; set; }
    public int TakeSnapshotCallCount { get; private set; }
    public string? LastSnapshotPath { get; private set; }
    public int LastSelectedAudioTrackId { get; private set; }
    public int? LastSelectedSubtitleTrackId { get; private set; }
    public bool WasSubtitleTrackSelected { get; private set; }
    public string? LastExternalSubtitlePath { get; private set; }
    public bool LastExternalSubtitleSelect { get; private set; }
    public OpenMediaRequest? LastRequest { get; private set; }
    public LibVLCSharp.Shared.MediaPlayer? MediaPlayer => null;
    public bool IsInitialized => true;
    public bool HasMedia => true;
    public PlaybackSnapshot Snapshot => new(
        _sessionId,
        PlaybackState.Playing,
        "a.mp4",
        TimeSpan.Zero,
        TimeSpan.FromMinutes(1),
        80,
        false,
        1.0,
        true,
        true,
        true,
        DateTimeOffset.UtcNow);
    public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    public void RaiseStateChanged(PlaybackState oldState, PlaybackState newState)
    {
        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs
        {
            MediaSessionId = _sessionId,
            OldState = oldState,
            NewState = newState,
            OccurredAtUtc = DateTimeOffset.UtcNow
        });
    }
    public event EventHandler<MediaOpenedEventArgs>? MediaOpened
    {
        add { }
        remove { }
    }

    private EventHandler<MediaParsedEventArgs>? _mediaParsed;

    public event EventHandler<MediaParsedEventArgs>? MediaParsed
    {
        add => _mediaParsed += value;
        remove => _mediaParsed -= value;
    }
    public event EventHandler<TracksChangedEventArgs>? TracksChanged;
    private EventHandler<PlaybackFailedEventArgs>? _playbackFailed;

    public event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed
    {
        add => _playbackFailed += value;
        remove => _playbackFailed -= value;
    }

    public event EventHandler<EndReachedEventArgs>? EndReached
    {
        add { }
        remove { }
    }

    public async Task<ServiceResult<MediaSessionInfo>> OpenAsync(
        OpenMediaRequest request,
        CancellationToken cancellationToken = default)
    {
        OpenCount++;
        LastRequest = request;
        if (OpenHandler is not null)
        {
            return await OpenHandler(request, cancellationToken);
        }

        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs
        {
            MediaSessionId = _sessionId,
            OldState = PlaybackState.None,
            NewState = PlaybackState.Playing,
            OccurredAtUtc = DateTimeOffset.UtcNow
        });
        return ServiceResult<MediaSessionInfo>.Success(
            new MediaSessionInfo(
                _sessionId,
                request.FilePath,
                true,
                TimeSpan.Zero,
                DateTimeOffset.UtcNow));
    }

    public Task<ServiceResult> PlayAsync(CancellationToken cancellationToken = default)
    {
        PlayCallCount++;
        return Task.FromResult(ServiceResult.Success());
    }

    public Task<ServiceResult> PauseAsync(CancellationToken cancellationToken = default)
    {
        PauseCallCount++;
        return Task.FromResult(ServiceResult.Success());
    }
    public Task<ServiceResult> StopAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ServiceResult.Success());
    public Task<ServiceResult<string>> CapturePreviewFrameAsync(
        string mediaPath,
        TimeSpan position,
        string outputPath,
        CancellationToken cancellationToken = default)
        => PreviewHandler is not null
            ? PreviewHandler(mediaPath, position, outputPath, cancellationToken)
            : Task.FromResult(ServiceResult<string>.Failure(
                ErrorCode.InvalidState,
                string.Empty,
                "Preview unavailable."));
    public Task<ServiceResult> SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
        => Task.FromResult(ServiceResult.Success());
    public Task<ServiceResult> SetVolumeAsync(int volume, CancellationToken cancellationToken = default)
        => Task.FromResult(ServiceResult.Success());
    public Task<ServiceResult> SetMuteAsync(bool isMuted, CancellationToken cancellationToken = default)
        => Task.FromResult(ServiceResult.Success());
    public Task<ServiceResult> SetRateAsync(double rate, CancellationToken cancellationToken = default)
        => Task.FromResult(ServiceResult.Success());
    public Task<ServiceResult<IReadOnlyList<PlayerTrackInfo>>> GetTracksAsync(
        PlayerTrackType type,
        CancellationToken cancellationToken = default)
    {
        if (!TracksResult.IsSuccess || TracksResult.Value is null)
        {
            return Task.FromResult(TracksResult);
        }

        return Task.FromResult(ServiceResult<IReadOnlyList<PlayerTrackInfo>>.Success(
            TracksResult.Value.Where(track => track.Type == type).ToArray()));
    }

    public Task<ServiceResult> SelectAudioTrackAsync(
        int trackId,
        CancellationToken cancellationToken = default)
    {
        LastSelectedAudioTrackId = trackId;
        return Task.FromResult(SelectAudioTrackResult);
    }

    public Task<ServiceResult> SelectSubtitleTrackAsync(
        int? trackId,
        CancellationToken cancellationToken = default)
    {
        LastSelectedSubtitleTrackId = trackId;
        WasSubtitleTrackSelected = true;
        return Task.FromResult(SelectSubtitleTrackResult);
    }

    public Task<ServiceResult> AddExternalSubtitleAsync(
        string filePath,
        bool select,
        CancellationToken cancellationToken = default)
    {
        LastExternalSubtitlePath = filePath;
        LastExternalSubtitleSelect = select;
        return Task.FromResult(AddExternalSubtitleResult);
    }

    public void RaiseMediaParsed(TimeSpan duration, params PlayerTrackInfo[] tracks)
    {
        _mediaParsed?.Invoke(
            this,
            new MediaParsedEventArgs
            {
                MediaSessionId = _sessionId,
                Duration = duration,
                HasVideo = true,
                HasAudio = true,
                Tracks = tracks
            });
    }

    public void RaisePlaybackFailed(ErrorCode errorCode, string message)
    {
        _playbackFailed?.Invoke(
            this,
            new PlaybackFailedEventArgs
            {
                MediaSessionId = _sessionId,
                ErrorCode = errorCode,
                UserMessage = message,
                TechnicalMessage = message,
                Exception = null
            });
    }

    public void RaiseTracksChanged(params PlayerTrackInfo[] tracks)
    {
        TracksChanged?.Invoke(
            this,
            new TracksChangedEventArgs
            {
                MediaSessionId = _sessionId,
                Tracks = tracks
            });
    }

    public Task<ServiceResult> TakeSnapshotAsync(
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        TakeSnapshotCallCount++;
        LastSnapshotPath = outputPath;
        if (TakeSnapshotHandler is not null)
        {
            return TakeSnapshotHandler(outputPath, cancellationToken);
        }

        if (TakeSnapshotResult.IsSuccess)
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(outputPath, [0]);
        }

        return Task.FromResult(TakeSnapshotResult);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class FakeScreenshotService : IScreenshotService
{
    public ServiceResult<string> CaptureResult { get; set; } =
        ServiceResult<string>.Success("screenshot.png");
    public int CaptureCount { get; private set; }
    public PlaybackSnapshot? LastSnapshot { get; private set; }

    public Task<ServiceResult<string>> CaptureAsync(
        PlaybackSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        CaptureCount++;
        LastSnapshot = snapshot;
        return Task.FromResult(CaptureResult);
    }
}

public sealed class FakeSubtitleService : ISubtitleService
{
    public ServiceResult<string> FindAutoSubtitleResult { get; set; } =
        ServiceResult<string>.Failure(
            ErrorCode.SubtitleNotFound,
            "没有找到自动字幕。",
            "Not found");
    public ServiceResult ValidateSubtitleResult { get; set; } = ServiceResult.Success();
    public int FindCallCount { get; private set; }
    public string? LastMediaPath { get; private set; }
    public string? LastSubtitlePath { get; private set; }

    public Task<ServiceResult<string>> FindAutoSubtitleAsync(
        string mediaPath,
        CancellationToken cancellationToken = default)
    {
        FindCallCount++;
        LastMediaPath = mediaPath;
        return Task.FromResult(FindAutoSubtitleResult);
    }

    public Task<ServiceResult> ValidateSubtitleAsync(
        string subtitlePath,
        CancellationToken cancellationToken = default)
    {
        LastSubtitlePath = subtitlePath;
        return Task.FromResult(ValidateSubtitleResult);
    }
}
public sealed class FakePlaylistService : IPlaylistService
{
    private readonly PlaylistItem[] _items =
    [
        new() { FilePath = "a.mp4" },
        new() { FilePath = "b.mp4" }
    ];
    private int _index;

    public IReadOnlyList<PlaylistItem> Items => _items;
    public int CurrentIndex => _index;
    public PlaylistItem? Current => _items[_index];
    public event EventHandler? PlaylistChanged
    {
        add { }
        remove { }
    }

    public event EventHandler? CurrentChanged;

    public void NotifyCurrentItemChanged()
    {
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<ServiceResult<IReadOnlyList<PlaylistItem>>> AddFilesAsync(
        IEnumerable<string> paths,
        bool recursive,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ServiceResult<IReadOnlyList<PlaylistItem>>.Success(_items));
    public Task<ServiceResult> AddFolderAsync(
        string folderPath,
        bool recursive,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ServiceResult.Success());
    public Task<ServiceResult> RemoveAsync(
        string filePath,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ServiceResult.Success());
    public Task<ServiceResult> ClearAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ServiceResult.Success());
    public ServiceResult<PlaylistItem> GetNext(bool wrap = false)
        => _index + 1 < _items.Length
            ? ServiceResult<PlaylistItem>.Success(_items[++_index])
            : ServiceResult<PlaylistItem>.Failure(
                ErrorCode.InvalidState,
                "Last",
                "Last");
    public ServiceResult<PlaylistItem> GetPrevious(bool wrap = false)
        => _index > 0
            ? ServiceResult<PlaylistItem>.Success(_items[--_index])
            : ServiceResult<PlaylistItem>.Failure(
                ErrorCode.InvalidState,
                "First",
                "First");
    public ServiceResult SetCurrent(string filePath)
    {
        var index = Array.FindIndex(
            _items,
            item => string.Equals(item.FilePath, filePath, StringComparison.Ordinal));
        if (index < 0)
        {
            return ServiceResult.Failure(
                ErrorCode.FileNotFound,
                "Missing",
                "Missing");
        }

        _index = index;
        return ServiceResult.Success();
    }

    public void SetCurrentIndexForTest(int index)
    {
        _index = index;
    }

    public void RaiseEvents() => CurrentChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class FakeSettingsService : ISettingsService
{
    public AppSettings Current { get; } = AppSettings.CreateDefault();
    public Task<ServiceResult> LoadAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ServiceResult.Success());
    public Task<ServiceResult> SaveAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ServiceResult.Success());
    public Task<ServiceResult> SaveImmediateAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ServiceResult.Success());
}

public sealed class FakeDialogService : IDialogService
{
    public List<string> Errors { get; } = [];
    public List<string> InformationMessages { get; } = [];

    public Task<ResumeChoice> AskResumeAsync(
        string fileName,
        TimeSpan lastPosition,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ResumeChoice.Continue);

    public Task ShowErrorAsync(
        string title,
        string message,
        CancellationToken cancellationToken = default)
    {
        Errors.Add(title + ": " + message);
        return Task.CompletedTask;
    }

    public Task ShowInformationAsync(
        string title,
        string message,
        CancellationToken cancellationToken = default)
    {
        InformationMessages.Add(title + ": " + message);
        return Task.CompletedTask;
    }
}

public sealed class FakeHistoryService : IHistoryService
{
    public bool IsAvailable { get; private set; } = true;

    public void SetAvailable(bool isAvailable)
    {
        IsAvailable = isAvailable;
    }

    public ServiceResult<ResumeInfo?> ResumeInfoResult { get; set; } =
        ServiceResult<ResumeInfo?>.Success(null);

    public int StartPlaybackCallCount { get; private set; }

    public int ClearCompletedCallCount { get; private set; }

    public int SaveProgressCallCount { get; private set; }

    public int FlushCallCount { get; private set; }

    public Task<ServiceResult<ResumeInfo?>> GetResumeInfoAsync(
        string filePath,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ResumeInfoResult);

    public Task<ServiceResult> StartPlaybackAsync(
        string filePath,
        string fileName,
        TimeSpan duration,
        long mediaSessionId = 0,
        CancellationToken cancellationToken = default)
    {
        StartPlaybackCallCount++;
        return Task.FromResult(ServiceResult.Success());
    }

    public Task<ServiceResult> SaveProgressAsync(
        PlaybackSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        SaveProgressCallCount++;
        return Task.FromResult(ServiceResult.Success());
    }

    public Task<ServiceResult> MarkCompletedAsync(
        string filePath,
        TimeSpan duration,
        long mediaSessionId = 0,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ServiceResult.Success());

    public Task<ServiceResult> ClearCompletedAsync(
        string filePath,
        long mediaSessionId = 0,
        CancellationToken cancellationToken = default)
    {
        ClearCompletedCallCount++;
        return Task.FromResult(ServiceResult.Success());
    }

    public Task<ServiceResult> FlushAsync(CancellationToken cancellationToken = default)
    {
        FlushCallCount++;
        return Task.FromResult(ServiceResult.Success());
    }
}
