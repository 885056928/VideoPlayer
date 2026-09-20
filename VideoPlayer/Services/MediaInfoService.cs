using System.IO;
using LibVLCSharp.Shared;
using VideoPlayer.Enums;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.Services;

public sealed class MediaInfoService : IMediaInfoService
{
    private const string Unknown = "未知";

    private readonly IPlayerService _playerService;

    public MediaInfoService(IPlayerService playerService)
    {
        ArgumentNullException.ThrowIfNull(playerService);
        _playerService = playerService;
    }

    public Task<ServiceResult<VideoInfo>> GetCurrentMediaInfoAsync(
        PlaybackSnapshot snapshot,
        IReadOnlyList<PlayerTrackInfo> tracks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(tracks);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(ServiceResult<VideoInfo>.Failure(
                ErrorCode.Cancelled,
                "媒体信息读取已取消。",
                "Media information query cancelled."));
        }

        if (string.IsNullOrWhiteSpace(snapshot.MediaPath))
        {
            return Task.FromResult(ServiceResult<VideoInfo>.Failure(
                ErrorCode.InvalidState,
                "没有媒体信息。",
                "No media"));
        }

        var path = snapshot.MediaPath;
        var fileName = GetFileName(path);
        var fileSize = GetFileSize(path);
        var duration = snapshot.Duration;
        int? width = null;
        int? height = null;
        var videoCodec = Unknown;
        double? frameRate = null;
        var audioCodec = Unknown;
        int? sampleRate = null;
        int? channels = null;

        try
        {
            var mediaPlayer = _playerService.MediaPlayer;
            using var media = mediaPlayer?.Media;
            if (media is not null)
            {
                if (media.Duration > 0 && duration <= TimeSpan.Zero)
                {
                    duration = TimeSpan.FromMilliseconds(media.Duration);
                }

                var mediaTracks = media.Tracks;
                var videoTrack = mediaTracks.FirstOrDefault(
                    track => track.TrackType == TrackType.Video);
                if (videoTrack.TrackType == TrackType.Video)
                {
                    var video = videoTrack.Data.Video;
                    width = ToPositiveInt(video.Width);
                    height = ToPositiveInt(video.Height);
                    frameRate = video.FrameRateNum > 0 && video.FrameRateDen > 0
                        ? (double)video.FrameRateNum / video.FrameRateDen
                        : null;
                    videoCodec = GetCodecDescription(media, videoTrack);
                }

                var audioTrack = mediaTracks.FirstOrDefault(
                    track => track.TrackType == TrackType.Audio);
                if (audioTrack.TrackType == TrackType.Audio)
                {
                    var audio = audioTrack.Data.Audio;
                    sampleRate = ToPositiveInt(audio.Rate);
                    channels = ToPositiveInt(audio.Channels);
                    audioCodec = GetCodecDescription(media, audioTrack);
                }
            }
        }
        catch
        {
            // Media details are optional; file information remains useful.
        }

        return Task.FromResult(ServiceResult<VideoInfo>.Success(
            new VideoInfo(
                fileName,
                path,
                fileSize,
                duration,
                width,
                height,
                videoCodec,
                frameRate,
                audioCodec,
                sampleRate,
                channels)));
    }

    private static string GetFileName(string path)
    {
        try
        {
            var fileName = Path.GetFileName(path);
            return string.IsNullOrWhiteSpace(fileName) ? Unknown : fileName;
        }
        catch
        {
            return Unknown;
        }
    }

    private static long GetFileSize(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static int? ToPositiveInt(uint value)
    {
        return value == 0 || value > int.MaxValue ? null : (int)value;
    }

    private static string GetCodecDescription(Media media, MediaTrack track)
    {
        try
        {
            var description = media.CodecDescription(track.TrackType, track.Codec);
            return string.IsNullOrWhiteSpace(description) ? Unknown : description;
        }
        catch
        {
            return Unknown;
        }
    }
}
