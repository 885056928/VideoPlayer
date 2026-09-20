namespace VideoPlayer.Models;

public sealed record ResumeInfo(
    string FilePath,
    TimeSpan Duration,
    TimeSpan LastPosition,
    bool IsCompleted,
    DateTimeOffset LastPlayedAtUtc,
    int PlayCount);
