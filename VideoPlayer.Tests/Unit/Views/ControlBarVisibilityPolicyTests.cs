using VideoPlayer.Views;
using Xunit;

namespace VideoPlayer.Tests.Unit.Views;

public sealed class ControlBarVisibilityPolicyTests
{
    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void ShouldStartAutoHideTimer_OnlyInLoadedFullscreen(
        bool isLoaded,
        bool isFullscreen,
        bool expected)
    {
        Assert.Equal(expected, ControlBarVisibilityPolicy.ShouldStartAutoHideTimer(isLoaded, isFullscreen));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void ShouldCollapseOnIdle_OnlyInFullscreen(bool isFullscreen, bool expected)
    {
        Assert.Equal(expected, ControlBarVisibilityPolicy.ShouldCollapseOnIdle(isFullscreen));
    }
}