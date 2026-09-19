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

    private readonly Func<string, SearchOption, IEnumerable<string>> _enumerateFiles;

    public FileService()
        : this((folderPath, option) => Directory.EnumerateFiles(folderPath, "*", option))
    {
    }

    internal FileService(Func<string, SearchOption, IEnumerable<string>> enumerateFiles)
    {
        ArgumentNullException.ThrowIfNull(enumerateFiles);
        _enumerateFiles = enumerateFiles;
    }

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
                cancellationToken.ThrowIfCancellationRequested();

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

                var paths = new List<string>();
                foreach (var path in _enumerateFiles(folderPath, option))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (IsSupportedVideo(path))
                    {
                        paths.Add(PathHelper.NormalizePath(path));
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();

                IReadOnlyList<string> sortedPaths = paths
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, Comparer<string>.Create(PathHelper.NaturalCompare))
                    .ToArray();

                return ServiceResult<IReadOnlyList<string>>.Success(sortedPaths);
            }
            catch (OperationCanceledException exception)
            {
                return ServiceResult<IReadOnlyList<string>>.Failure(
                    ErrorCode.Cancelled,
                    "操作已取消。",
                    folderPath,
                    exception);
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
        }, CancellationToken.None);
    }
}
