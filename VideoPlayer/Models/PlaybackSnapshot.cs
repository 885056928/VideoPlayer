using VideoPlayer.Enums;

namespace VideoPlayer.Models;

public sealed record PlaybackSnapshot(
    long MediaSessionId,
    PlaybackState State,
    string? MediaPath,
    TimeSpan Position,
    TimeSpan Duration,
    int Volume,
    bool IsMuted,
    double Rate,
    bool IsSeekable,
    bool HasVideo,
    bool HasAudio,
    DateTimeOffset CapturedAtUtc);
