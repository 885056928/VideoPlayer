using System.IO;

using VideoPlayer.Enums;
using VideoPlayer.Helpers;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.Services;

public sealed class FileService : IFileService
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".ts", ".m2ts", ".webm"
        };

    public bool IsSupportedVideo(string filePath)
    {
        return SupportedExtensions.Contains(Path.GetExtension(filePath));
    }

    public string NormalizePath(string filePath)
    {
        return PathHelper.NormalizePath(filePath);
    }

    public Task<ServiceResult<IReadOnlyList<string>>> EnumerateVideosAsync(
        string folderPath,
        bool recursive,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    return ServiceResult<IReadOnlyList<string>>.Failure(
                        ErrorCode.FileNotFound,
                        "文件夹不存在。",
                        folderPath);
                }

                var option = recursive
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;

                IReadOnlyList<string> paths = Directory
                    .EnumerateFiles(folderPath, "*", option)
                    .Where(IsSupportedVideo)
                    .Select(PathHelper.NormalizePath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, Comparer<string>.Create(PathHelper.NaturalCompare))
                    .ToArray();

                return ServiceResult<IReadOnlyList<string>>.Success(paths);
            }
            catch (UnauthorizedAccessException exception)
            {
                return ServiceResult<IReadOnlyList<string>>.Failure(
                    ErrorCode.NoPermission,
                    "没有读取该文件夹的权限。",
                    folderPath,
                    exception);
            }
            catch (Exception exception)
            {
                return ServiceResult<IReadOnlyList<string>>.Failure(
                    ErrorCode.Unknown,
                    "扫描文件夹失败。",
                    folderPath,
                    exception);
            }
        }, cancellationToken);
    }
}
