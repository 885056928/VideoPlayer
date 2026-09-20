using LibVLCSharp.Shared;
using VideoPlayer.Enums;
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
    event EventHandler<MediaParsedEventArgs>? MediaParsed;
    event EventHandler<TracksChangedEventArgs>? TracksChanged;
    event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;
    event EventHandler<EndReachedEventArgs>? EndReached;

    Task<ServiceResult<MediaSessionInfo>> OpenAsync(
        OpenMediaRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> PlayAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult> PauseAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult> StopAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult> SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);
    Task<ServiceResult> SetVolumeAsync(int volume, CancellationToken cancellationToken = default);
    Task<ServiceResult> SetMuteAsync(bool isMuted, CancellationToken cancellationToken = default);
    Task<ServiceResult> SetRateAsync(double rate, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<PlayerTrackInfo>>> GetTracksAsync(
        PlayerTrackType type,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> SelectAudioTrackAsync(
        int trackId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> SelectSubtitleTrackAsync(
        int? trackId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> AddExternalSubtitleAsync(
        string filePath,
        bool select,
        CancellationToken cancellationToken = default);
}
