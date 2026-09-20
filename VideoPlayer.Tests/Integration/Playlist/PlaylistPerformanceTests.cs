using System.Diagnostics;
using System.IO;

using VideoPlayer.Services;

using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace VideoPlayer.Tests.Integration.Playlist;

public sealed class PlaylistPerformanceTests
{
    private const int FileCount = 10_000;
    private static readonly TimeSpan MaximumAddDuration = TimeSpan.FromSeconds(10);
    private readonly ITestOutputHelper _output;

    public PlaylistPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task AddFolderAsync_WithTenThousandFiles_CompletesWithinBudgetAndDeduplicates()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"VideoPlayer-Playlist-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            for (var index = 0; index < FileCount; index++)
            {
                var path = Path.Combine(root, $"video-{index:D5}.mp4");
                using var stream = File.Create(path);
            }

            var createdCount = Directory
                .EnumerateFiles(root, "*.mp4", SearchOption.TopDirectoryOnly)
                .Take(FileCount + 1)
                .Count();
            if (createdCount < FileCount)
            {
                throw SkipException.ForSkip($"Filesystem created only {createdCount} of {FileCount} files.");
            }

            var service = new PlaylistService(new FileService());
            var stopwatch = Stopwatch.StartNew();

            var result = await service.AddFolderAsync(root, recursive: false);

            stopwatch.Stop();
            _output.WriteLine(
                $"AddFolderAsync elapsed: {stopwatch.Elapsed.TotalSeconds:F3} seconds.");
            Assert.True(result.IsSuccess, result.TechnicalMessage);
            Assert.True(
                stopwatch.Elapsed < MaximumAddDuration,
                $"Adding {FileCount} files took {stopwatch.Elapsed.TotalSeconds:F3} seconds.");
            Assert.Equal(FileCount, service.Items.Count);

            await service.AddFolderAsync(root, recursive: false);

            Assert.Equal(FileCount, service.Items.Count);
            Assert.Equal(
                FileCount,
                service.Items
                    .Select(item => item.FilePath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count());
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
