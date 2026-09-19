using VideoPlayer.Enums;

namespace VideoPlayer.Models.Events;

public sealed class PlaybackFailedEventArgs : EventArgs
{
    public long MediaSessionId { get; init; }
    public ErrorCode ErrorCode { get; init; }
    public string UserMessage { get; init; } = string.Empty;
    public string TechnicalMessage { get; init; } = string.Empty;
    public Exception? Exception { get; init; }
}
