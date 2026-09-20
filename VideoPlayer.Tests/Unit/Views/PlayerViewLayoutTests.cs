using System.Windows;
using System.Windows.Controls;
using VideoPlayer.Views;
using Xunit;

namespace VideoPlayer.Tests.Unit.Views;

public sealed class PlayerViewLayoutTests
{
    [StaFact]
    public void ControlBar_IsInWindowedRowOutsideVideoOverlay()
    {
        var view = new PlayerView();
        var layout = Assert.IsType<Grid>(view.FindName("PlayerLayout"));
        var videoView = Assert.IsAssignableFrom<FrameworkElement>(view.FindName("VideoView"));
        var overlay = Assert.IsType<Grid>(view.FindName("OverlayRoot"));
        var controlBar = Assert.IsType<Border>(view.FindName("ControlBar"));

        Assert.Same(layout, controlBar.Parent);
        Assert.NotSame(overlay, controlBar.Parent);
        Assert.Equal(0, Grid.GetRow(videoView));
        Assert.Equal(1, Grid.GetRow(controlBar));
    }
}