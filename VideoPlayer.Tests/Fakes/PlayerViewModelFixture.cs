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
        ViewModel = new PlayerViewModel(
            Player,
            Playlist,
            Settings,
            Dialog);
    }

    public FakePlayerService Player { get; }
    public FakePlaylistService Playlist { get; }
    public FakeSettingsService Settings { get; }
    public FakeDialogService Dialog { get; }
    public PlayerViewModel ViewModel { get; }
}

public sealed class FakePlayerService : IPlayerService
{
    private readonly long _sessionId;

    public FakePlayerService(long sessionId) => _sessionId = sessionId;

    public Func<
        OpenMediaRequest,
        CancellationToken,
        Task<ServiceResult<MediaSessionInfo>>>? OpenHandler { get; set; }
    public int OpenCount { get; private set; }
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
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
