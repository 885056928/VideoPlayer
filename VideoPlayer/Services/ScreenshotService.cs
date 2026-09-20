using System.IO;
using VideoPlayer.Configuration;
using VideoPlayer.Enums;
using VideoPlayer.Helpers;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.Services;

public sealed class ScreenshotService : IScreenshotService
{
    private readonly IPlayerService _playerService;
    private readonly ISettingsService _settingsService;
    private readonly AppPaths _paths;
    private readonly Func<DateTime> _clock;

    public ScreenshotService(
        IPlayerService playerService,
        ISettingsService settingsService,
        AppPaths paths)
        : this(playerService, settingsService, paths, () => DateTime.Now)
    {
    }

    internal ScreenshotService(
        IPlayerService playerService,
        ISettingsService settingsService,
        AppPaths paths,
        Func<DateTime> clock)
    {
        ArgumentNullException.ThrowIfNull(playerService);
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(clock);

        _playerService = playerService;
        _settingsService = settingsService;
        _paths = paths;
        _clock = clock;
    }

    public async Task<ServiceResult<string>> CaptureAsync(
        PlaybackSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.HasVideo || string.IsNullOrWhiteSpace(snapshot.MediaPath))
        {
            return ServiceResult<string>.Failure(
                ErrorCode.SnapshotFailed,
                "当前没有可截图画面。",
                "No video surface");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var configuredDirectory = _settingsService.Current.Screenshot.Directory;
            var screenshotRoot = string.IsNullOrWhiteSpace(configuredDirectory)
                ? _paths.ScreenshotDirectory
                : configuredDirectory;
            var now = _clock();
            var directory = Path.Combine(
                screenshotRoot,
                now.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(directory);

            var baseName = FileNameHelper.Sanitize(
                Path.GetFileNameWithoutExtension(snapshot.MediaPath));
            var timeStamp = now.ToString("HH-mm-ss");
            var path = Path.Combine(directory, $"{baseName}_{timeStamp}.png");
            var suffix = 1;
            while (File.Exists(path))
            {
                path = Path.Combine(
                    directory,
                    $"{baseName}_{timeStamp}_{suffix:00}.png");
                suffix++;
            }

            var result = await _playerService.TakeSnapshotAsync(path, cancellationToken);
            return result.IsSuccess
                ? ServiceResult<string>.Success(path)
                : ServiceResult<string>.Failure(
                    result.ErrorCode,
                    result.UserMessage,
                    result.TechnicalMessage,
                    result.Exception);
        }
        catch (OperationCanceledException exception)
        {
            return ServiceResult<string>.Failure(
                ErrorCode.Cancelled,
                "截图已取消。",
                "Screenshot cancelled",
                exception);
        }
        catch (Exception exception)
        {
            return ServiceResult<string>.Failure(
                ErrorCode.SnapshotFailed,
                "截图失败。",
                "Screenshot path or write failed",
                exception);
        }
    }
}
