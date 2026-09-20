using System.IO;
using VideoPlayer.Enums;
using VideoPlayer.Helpers;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;

namespace VideoPlayer.Services;

public sealed class SubtitleService : ISubtitleService
{
    private static readonly string[] Extensions = [".srt", ".ass", ".ssa"];

    public Task<ServiceResult<string>> FindAutoSubtitleAsync(
        string mediaPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var media = PathHelper.NormalizePath(mediaPath);
        var basePath = Path.Combine(
            Path.GetDirectoryName(media)!,
            Path.GetFileNameWithoutExtension(media));
        foreach (var extension in Extensions)
        {
            var candidate = basePath + extension;
            if (File.Exists(candidate))
            {
                return Task.FromResult(ServiceResult<string>.Success(candidate));
            }
        }

        return Task.FromResult(ServiceResult<string>.Failure(
            ErrorCode.SubtitleNotFound, "没有找到自动字幕。", basePath));
    }

    public Task<ServiceResult> ValidateSubtitleAsync(
        string subtitlePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var valid = File.Exists(subtitlePath) &&
            Extensions.Contains(Path.GetExtension(subtitlePath), StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(valid
            ? ServiceResult.Success()
            : ServiceResult.Failure(ErrorCode.SubtitleNotFound, "字幕文件无效。", subtitlePath));
    }
}
