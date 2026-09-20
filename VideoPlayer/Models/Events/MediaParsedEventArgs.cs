using VideoPlayer.Models;

namespace VideoPlayer.Models.Events;

public sealed class MediaParsedEventArgs : EventArgs
{
    public long MediaSessionId { get; init; }
    public TimeSpan Duration { get; init; }
    public bool HasVideo { get; init; }
    public bool HasAudio { get; init; }
    public IReadOnlyList<PlayerTrackInfo> Tracks { get; init; } = [];
}
