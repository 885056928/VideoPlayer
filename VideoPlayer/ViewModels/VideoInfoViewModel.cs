using CommunityToolkit.Mvvm.ComponentModel;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.ViewModels;

public sealed partial class VideoInfoViewModel : ObservableObject
{
    #region 属性
    private const string Unknown = "未知";

    private readonly IMediaInfoService _mediaInfoService;

    [ObservableProperty]
    private string _fileName = Unknown;

    [ObservableProperty]
    private string _filePath = Unknown;

    [ObservableProperty]
    private string _resolution = Unknown;

    [ObservableProperty]
    private string _videoCodec = Unknown;

    [ObservableProperty]
    private string _audioCodec = Unknown;

    [ObservableProperty]
    private string _duration = Unknown;

    [ObservableProperty]
    private string _fileSize = Unknown;

    [ObservableProperty]
    private string _frameRate = Unknown;

    [ObservableProperty]
    private string _sampleRate = Unknown;

    [ObservableProperty]
    private string _channels = Unknown;

    [ObservableProperty]
    private string _errorMessage = string.Empty;
    #endregion

    #region 命令
    #endregion

    #region 方法
    public VideoInfoViewModel(IMediaInfoService mediaInfoService)
    {
        ArgumentNullException.ThrowIfNull(mediaInfoService);
        _mediaInfoService = mediaInfoService;
    }

    public async Task RefreshAsync(
        PlaybackSnapshot snapshot,
        IReadOnlyList<PlayerTrackInfo> tracks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(tracks);

        var result = await _mediaInfoService
            .GetCurrentMediaInfoAsync(snapshot, tracks, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            ApplyUnknown();
            ErrorMessage = result.UserMessage;
            return;
        }

        Apply(result.Value);
        ErrorMessage = string.Empty;
    }

    private void Apply(VideoInfo info)
    {
        FileName = Normalize(info.FileName);
        FilePath = Normalize(info.FilePath);
        Resolution = info.Width is > 0 and int width
            && info.Height is > 0 and int height
            ? $"{width} × {height}"
            : Unknown;
        VideoCodec = Normalize(info.VideoCodec);
        AudioCodec = Normalize(info.AudioCodec);
        Duration = info.Duration > TimeSpan.Zero
            ? FormatDuration(info.Duration)
            : Unknown;
        FileSize = info.FileSizeBytes > 0
            ? FormatFileSize(info.FileSizeBytes)
            : Unknown;
        FrameRate = info.FrameRate is > 0 and double frameRate
            ? $"{frameRate:0.###} fps"
            : Unknown;
        SampleRate = info.SampleRate is > 0 and int sampleRate
            ? $"{sampleRate} Hz"
            : Unknown;
        Channels = info.Channels is > 0 and int channels
            ? channels.ToString()
            : Unknown;
    }

    private void ApplyUnknown()
    {
        FileName = Unknown;
        FilePath = Unknown;
        Resolution = Unknown;
        VideoCodec = Unknown;
        AudioCodec = Unknown;
        Duration = Unknown;
        FileSize = Unknown;
        FrameRate = Unknown;
        SampleRate = Unknown;
        Channels = Unknown;
    }

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? Unknown : value;

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.Days > 0
            ? duration.ToString(@"d\.hh\:mm\:ss")
            : duration.ToString(@"hh\:mm\:ss");
    }

    private static string FormatFileSize(long bytes)
    {
        const double kilo = 1024;
        const double mega = kilo * 1024;
        const double giga = mega * 1024;

        return bytes switch
        {
            >= (long)giga => $"{bytes / giga:0.##} GB",
            >= (long)mega => $"{bytes / mega:0.##} MB",
            >= (long)kilo => $"{bytes / kilo:0.##} KB",
            _ => $"{bytes} B"
        };
    }
    #endregion
}
