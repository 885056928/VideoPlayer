using System.IO;

using VideoPlayer.Helpers;
using Xunit;

namespace VideoPlayer.Tests.Unit.Helpers;

public sealed class PathHelperTests
{
    [Fact]
    public void NormalizePath_ReturnsAbsolutePath()
    {
        var relative = Path.Combine("media", "movie.mp4");

        var actual = PathHelper.NormalizePath(relative);

        Assert.Equal(Path.GetFullPath(relative), actual);
    }

    [Fact]
    public void CreatePathKey_IgnoresAsciiCase()
    {
        var upper = Path.Combine(Path.GetTempPath(), "Movie.MP4");
        var lower = Path.Combine(Path.GetTempPath(), "movie.mp4");

        Assert.Equal(PathHelper.CreatePathKey(upper), PathHelper.CreatePathKey(lower));
    }

    [Fact]
    public void NormalizePath_RejectsWhitespace()
    {
        Assert.Throws<ArgumentException>(() => PathHelper.NormalizePath("   "));
    }
}