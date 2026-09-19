using LibVLCSharp.Shared;
using VideoPlayer.Models;
using VideoPlayer.Models.Events;

namespace VideoPlayer.Interfaces;

public interface IPlayerService : IAsyncDisposable
{
    MediaPlayer? MediaPlayer { get; }
    bool IsInitialized { get; }
    bool HasMedia { get; }
    PlaybackSnapshot Snapshot { get; }

    event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;
    event EventHandler<MediaOpenedEventArgs>? MediaOpened;
    event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;

    Task<ServiceResult<MediaSessionInfo>> OpenAsync(
        OpenMediaRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> PlayAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult> PauseAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult> StopAsync(CancellationToken cancellationToken = default);
}
