using System.IO;

using VideoPlayer.Enums;
using VideoPlayer.Services;
using Xunit;

namespace VideoPlayer.Tests.Unit.Services;

public sealed class FileServiceTests
{
    [Theory]
    [InlineData("movie.mp4", true)]
    [InlineData("movie.MKV", true)]
    [InlineData("movie.txt", false)]
    public void IsSupportedVideo_RecognizesOnlyConfiguredExtensions(string path, bool expected)
    {
        var service = new FileService();

        Assert.Equal(expected, service.IsSupportedVideo(path));
    }

    [Fact]
    public async Task EnumerateVideosAsync_NaturallySortsAndDeduplicates()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var movie10 = Path.Combine(root, "Movie10.mp4");
            var movie2 = Path.Combine(root, "Movie2.mp4");
            var notes = Path.Combine(root, "notes.txt");
            File.WriteAllText(movie10, string.Empty);
            File.WriteAllText(movie2, string.Empty);
            File.WriteAllText(notes, string.Empty);
            var service = new FileService((_, _) => new[]
            {
                movie10,
                movie2,
                Path.Combine(root, "mOVIE10.MP4"),
                notes
            });

            var result = await service.EnumerateVideosAsync(root, false, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(2, result.Value!.Count);
            Assert.EndsWith("Movie2.mp4", result.Value[0]);
            Assert.EndsWith("Movie10.mp4", result.Value[1]);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task EnumerateVideosAsync_ReturnsCancelledWhenTokenCanceledDuringEnumeration()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var first = Path.Combine(root, "Movie1.mp4");
            var second = Path.Combine(root, "Movie2.mp4");
            using var cancellation = new CancellationTokenSource();
            var service = new FileService((_, _) => EnumerateAndCancel(
                first,
                second,
                cancellation));

            var result = await service.EnumerateVideosAsync(
                root,
                false,
                cancellation.Token);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCode.Cancelled, result.ErrorCode);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task EnumerateVideosAsync_ReturnsFileNotFoundForMissingFolder()
    {
        var service = new FileService();

        var result = await service.EnumerateVideosAsync(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            false,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.FileNotFound, result.ErrorCode);
    }

    private static IEnumerable<string> EnumerateAndCancel(
        string first,
        string second,
        CancellationTokenSource cancellation)
    {
        yield return first;
        cancellation.Cancel();
        yield return second;
    }
}
