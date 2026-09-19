namespace VideoPlayer.Models;

public sealed record OpenMediaRequest(
    string FilePath,
    TimeSpan StartPosition,
    bool AutoPlay,
    string? ExternalSubtitlePath,
    string? PreferredAudioLanguage);
