using VideoPlayer.Enums;

namespace VideoPlayer.Models.Events;

public sealed class PlaybackStateChangedEventArgs : EventArgs
{
    public long MediaSessionId { get; init; }
    public PlaybackState OldState { get; init; }
    public PlaybackState NewState { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; }
}
