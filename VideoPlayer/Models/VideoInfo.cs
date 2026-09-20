namespace VideoPlayer.Models;

public sealed record VideoInfo(
    string FileName,
    string FilePath,
    long FileSizeBytes,
    TimeSpan Duration,
    int? Width,
    int? Height,
    string VideoCodec,
    double? FrameRate,
    string AudioCodec,
    int? SampleRate,
    int? Channels);
