using System.IO;

using Serilog;

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
                var paths = new List<string>();
                if (recursive)
                {
                    EnumerateRecursive(folderPath, paths, cancellationToken);
                }
                else
                {
                    foreach (var path in _enumerateFiles(folderPath, SearchOption.TopDirectoryOnly))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        AddSupportedPath(path, paths);
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
            catch (DirectoryNotFoundException exception)
            {
                return ServiceResult<IReadOnlyList<string>>.Failure(
                    ErrorCode.FileNotFound,
                    "文件夹不存在。",
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

    private void EnumerateRecursive(
        string root,
        List<string> paths,
        CancellationToken cancellationToken)
    {
        var pending = new Queue<string>();
        pending.Enqueue(root);
        var isRoot = true;
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Dequeue();
            try
            {
                foreach (var file in _enumerateFiles(directory, SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AddSupportedPath(file, paths);
                }

                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    pending.Enqueue(child);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (!isRoot
                && exception is UnauthorizedAccessException
                    or IOException
                    or DirectoryNotFoundException)
            {
                Log.Warning(
                    exception,
                    "Skipping inaccessible or locked folder {Folder}.",
                    directory);
            }

            isRoot = false;
        }
    }

    private void AddSupportedPath(string path, List<string> paths)
    {
        if (IsSupportedVideo(path))
        {
            paths.Add(PathHelper.NormalizePath(path));
        }
    }
}
