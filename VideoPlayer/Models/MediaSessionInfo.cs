namespace VideoPlayer.Models;

public sealed record MediaSessionInfo(
    long MediaSessionId,
    string FilePath,
    bool AutoPlay,
    TimeSpan RequestedStartPosition,
    DateTimeOffset CreatedAtUtc);
