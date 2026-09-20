namespace VideoPlayer.Models.Events;

public sealed class EndReachedEventArgs : EventArgs
{
    public long MediaSessionId { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; init; }
}