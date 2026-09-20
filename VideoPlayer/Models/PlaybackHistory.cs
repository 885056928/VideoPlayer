namespace VideoPlayer.Models;

public sealed class PlaybackHistory
{
    public long Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string PathKey { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long DurationTicks { get; set; }
    public long LastPositionTicks { get; set; }
    public DateTimeOffset LastPlayedAtUtc { get; set; }
    public int PlayCount { get; set; }
    public bool IsCompleted { get; set; }
}
