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
        ViewModel = new PlayerViewModel(
            Player,
            Playlist,
            Settings,
            Dialog,
            Subtitle,
            History);
    }

    public FakePlayerService Player { get; }
    public FakePlaylistService Playlist { get; }
    public FakeSettingsService Settings { get; }
    public FakeDialogService Dialog { get; }
    public FakeSubtitleService Subtitle { get; }
    public FakeHistoryService History { get; }
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
    public ServiceResult<IReadOnlyList<PlayerTrackInfo>> TracksResult { get; set; } =
        ServiceResult<IReadOnlyList<PlayerTrackInfo>>.Success([]);
    public ServiceResult SelectAudioTrackResult { get; set; } = ServiceResult.Success();
    public ServiceResult SelectSubtitleTrackResult { get; set; } = ServiceResult.Success();
    public ServiceResult AddExternalSubtitleResult { get; set; } = ServiceResult.Success();
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
    public event EventHandler<MediaOpenedEventArgs>? MediaOpened
    {
        add { }
        remove { }
    }

    public event EventHandler<MediaParsedEventArgs>? MediaParsed
    {
        add { }
        remove { }
    }
    public event EventHandler<TracksChangedEventArgs>? TracksChanged;
    public event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed
    {
        add { }
        remove { }
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
        => Task.FromResult(ServiceResult.Success());
    public Task<ServiceResult> PauseAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ServiceResult.Success());
    public Task<ServiceResult> StopAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ServiceResult.Success());
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
        => Task.FromResult(TracksResult);

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

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
    public Task<ResumeChoice> AskResumeAsync(
        string fileName,
        TimeSpan lastPosition,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ResumeChoice.Continue);
}

public sealed class FakeHistoryService : IHistoryService
{
    public bool IsAvailable => true;

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
        CancellationToken cancellationToken = default)
        => Task.FromResult(ServiceResult.Success());

    public Task<ServiceResult> ClearCompletedAsync(
        string filePath,
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
