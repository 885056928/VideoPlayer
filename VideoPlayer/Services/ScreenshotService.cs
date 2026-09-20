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

        string path;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            path = await Task.Run(
                () => ReserveScreenshotPath(snapshot),
                cancellationToken);
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
                "Screenshot path creation failed.",
                exception);
        }

        try
        {
            var result = await _playerService.TakeSnapshotAsync(path, cancellationToken);
            if (!result.IsSuccess)
            {
                DeletePartialFile(path);
                return ServiceResult<string>.Failure(
                    result.ErrorCode,
                    result.UserMessage,
                    result.TechnicalMessage,
                    result.Exception);
            }

            if (!await WaitForSnapshotFileAsync(path, cancellationToken))
            {
                DeletePartialFile(path);
                return ServiceResult<string>.Failure(
                    ErrorCode.SnapshotFailed,
                    "截图未完成。",
                    "LibVLC reported success but no non-empty PNG was written.");
            }

            return ServiceResult<string>.Success(path);
        }
        catch (OperationCanceledException exception)
        {
            DeletePartialFile(path);
            return ServiceResult<string>.Failure(
                ErrorCode.Cancelled,
                "截图已取消。",
                "Screenshot cancelled",
                exception);
        }
        catch (Exception exception)
        {
            DeletePartialFile(path);
            return ServiceResult<string>.Failure(
                ErrorCode.SnapshotFailed,
                "截图失败。",
                "Screenshot write failed.",
                exception);
        }
    }

    private string ReserveScreenshotPath(PlaybackSnapshot snapshot)
    {
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
            Path.GetFileNameWithoutExtension(snapshot.MediaPath!));
        if (baseName.Length > 80)
        {
            baseName = baseName[..80];
        }

        var position = FormatPosition(snapshot.Position);
        var timestamp = now.ToString("HH-mm-ss");
        var path = Path.Combine(
            directory,
            $"{baseName}_{position}_{timestamp}.png");
        var suffix = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(
                directory,
                $"{baseName}_{position}_{timestamp}_{suffix:00}.png");
            suffix++;
        }

        return path;
    }

    private static string FormatPosition(TimeSpan position)
    {
        var value = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        return value.TotalHours >= 1
            ? value.ToString(@"hh\-mm\-ss\.fff")
            : value.ToString(@"mm\-ss\.fff");
    }

    private static async Task<bool> WaitForSnapshotFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length > 0)
                {
                    return true;
                }
            }
            catch (IOException)
            {
                // The writer may still be flushing the file.
            }

            await Task.Delay(50, cancellationToken);
        }

        return File.Exists(path) && new FileInfo(path).Length > 0;
    }

    private static void DeletePartialFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Screenshot cleanup is best-effort.
        }
    }
}