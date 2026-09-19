using System.IO;
using VideoPlayer.Configuration;
using Xunit;

namespace VideoPlayer.Tests.Unit.Configuration;

public sealed class AppPathsTests
{
    [Fact]
    public void Constructor_BuildsExpectedPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "VideoPlayerTests");

        var paths = new AppPaths(root);

        Assert.Equal(Path.Combine(root, "VideoPlayer.db"), paths.DatabasePath);
        Assert.Equal(Path.Combine(root, "appsettings.json"), paths.ConfigPath);
        Assert.Equal(Path.Combine(root, "Logs"), paths.LogDirectory);
    }

    [Fact]
    public void EnsureCoreDirectories_CreatesRootAndLogs()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root);
        try
        {
            paths.EnsureCoreDirectories();

            Assert.True(Directory.Exists(root));
            Assert.True(Directory.Exists(paths.LogDirectory));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
