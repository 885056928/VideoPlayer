using System.IO;
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Unit.Services;

public sealed class SubtitleServiceTests
{
    [Fact]
    public async Task FindAutoSubtitleAsync_PrefersSrt()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var media = Path.Combine(root, "Movie.mkv");
            File.WriteAllText(media, string.Empty);
            File.WriteAllText(Path.Combine(root, "Movie.ass"), string.Empty);
            File.WriteAllText(Path.Combine(root, "Movie.srt"), string.Empty);
            var service = new SubtitleService();

            var result = await service.FindAutoSubtitleAsync(media);

            Assert.True(result.IsSuccess);
            Assert.EndsWith("Movie.srt", result.Value);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task FindAutoSubtitleAsync_PrefersAssOverSsa()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var media = Path.Combine(root, "Movie.mkv");
            File.WriteAllText(media, string.Empty);
            File.WriteAllText(Path.Combine(root, "Movie.ssa"), string.Empty);
            File.WriteAllText(Path.Combine(root, "Movie.ass"), string.Empty);
            var service = new SubtitleService();

            var result = await service.FindAutoSubtitleAsync(media);

            Assert.True(result.IsSuccess);
            Assert.EndsWith("Movie.ass", result.Value);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task FindAutoSubtitleAsync_MatchesBaseNameAndExtensionCaseInsensitively()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var media = Path.Combine(root, "Movie.mkv");
            File.WriteAllText(media, string.Empty);
            File.WriteAllText(Path.Combine(root, "mOvIe.SRT"), string.Empty);
            var service = new SubtitleService();

            var result = await service.FindAutoSubtitleAsync(media);

            Assert.True(result.IsSuccess);
            Assert.EndsWith("Movie.srt", result.Value, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ValidateSubtitleAsync_RejectsUnsupportedExtension()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        File.WriteAllText(path, string.Empty);
        try
        {
            var service = new SubtitleService();

            var result = await service.ValidateSubtitleAsync(path);

            Assert.False(result.IsSuccess);
            Assert.Equal(Enums.ErrorCode.SubtitleNotFound, result.ErrorCode);
        }
        finally
        {
            File.Delete(path);
        }
    }
    [Fact]
    public async Task FindAutoSubtitleAsync_ReturnsFailureWhenAbsent()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4");
        var service = new SubtitleService();

        var result = await service.FindAutoSubtitleAsync(path);

        Assert.False(result.IsSuccess);
        Assert.Equal(Enums.ErrorCode.SubtitleNotFound, result.ErrorCode);
    }
}
