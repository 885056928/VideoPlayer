using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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

    [StaFact]
    public void Layout_HasPrimaryOpenStateAndThreeControlGroups()
    {
        var view = new PlayerView();

        Assert.NotNull(view.FindName("PrimaryOpenButton"));
        Assert.IsType<Grid>(view.FindName("SeekGroup"));
        var playbackGroup = Assert.IsType<StackPanel>(view.FindName("PlaybackGroup"));
        var trackTools = Assert.IsType<StackPanel>(view.FindName("TrackToolsGroup"));
        var volumeSlider = Assert.IsType<Slider>(view.FindName("VolumeSlider"));
        var seekSlider = Assert.IsType<Slider>(view.FindName("SeekSlider"));

        Assert.NotNull(seekSlider.Style);
        Assert.IsType<Popup>(view.FindName("SeekPreviewPopup"));
        Assert.Same(playbackGroup, volumeSlider.Parent);
        Assert.DoesNotContain(
            trackTools.Children.OfType<Slider>(),
            slider => ReferenceEquals(slider, volumeSlider));
    }

    [StaFact]
    public void PlaybackGroup_UsesFiveSecondSeekButtonsAndStyledVolumeSlider()
    {
        var view = new PlayerView();
        var playbackGroup = Assert.IsType<StackPanel>(view.FindName("PlaybackGroup"));
        var rewindButton = Assert.IsType<Button>(view.FindName("RewindButton"));
        var fastForwardButton = Assert.IsType<Button>(view.FindName("FastForwardButton"));
        var volumeSlider = Assert.IsType<Slider>(view.FindName("VolumeSlider"));

        Assert.Null(view.FindName("PreviousButton"));
        Assert.Null(view.FindName("NextButton"));
        Assert.Equal("\uEB9E", rewindButton.Content);
        Assert.Equal("\uEB9D", fastForwardButton.Content);
        Assert.Equal("快退 5 秒", rewindButton.ToolTip);
        Assert.Equal("快进 5 秒", fastForwardButton.ToolTip);
        Assert.Equal(
            "SeekBackward5Command",
            BindingOperations.GetBinding(rewindButton, Button.CommandProperty)?.Path.Path);
        Assert.Equal(
            "SeekForward5Command",
            BindingOperations.GetBinding(fastForwardButton, Button.CommandProperty)?.Path.Path);
        Assert.Same(view.Resources["MediaVolumeSliderStyle"], volumeSlider.Style);
        Assert.True(volumeSlider.IsMoveToPointEnabled);
        Assert.Equal(
            new[]
            {
                "StopButton",
                "RewindButton",
                "PlayPauseButton",
                "FastForwardButton",
                "MuteButton",
                "VolumeSlider"
            },
            playbackGroup.Children
                .OfType<FrameworkElement>()
                .Select(element => element.Name));
    }

    [StaFact]
    public void IconButtons_HaveGlyphTooltipAndAutomationName()
    {
        var view = new PlayerView();
        var names = new[]
        {
            "OpenButton",
            "PlayPauseButton",
            "StopButton",
            "RewindButton",
            "FastForwardButton",
            "CloseSubtitleButton",
            "AddSubtitleButton",
            "ScreenshotButton",
            "MuteButton"
        };

        foreach (var name in names)
        {
            var button = Assert.IsType<Button>(view.FindName(name));
            var glyph = Assert.IsType<string>(button.Content);
            Assert.False(string.IsNullOrWhiteSpace(glyph));
            Assert.Contains("Segoe MDL2 Assets", button.FontFamily.Source);
            Assert.False(string.IsNullOrWhiteSpace(button.ToolTip?.ToString()));
            Assert.False(string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(button)));
        }
    }
}
