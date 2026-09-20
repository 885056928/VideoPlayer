using VideoPlayer.Models;

namespace VideoPlayer.Models.Events;

public sealed class TracksChangedEventArgs : EventArgs
{
    public long MediaSessionId { get; init; }
    public IReadOnlyList<PlayerTrackInfo> Tracks { get; init; } = [];
}
