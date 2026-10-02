using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using VideoPlayer.Enums;
using VideoPlayer.Models;
using VideoPlayer.Tests.Fakes;
using VideoPlayer.Views;
using Xunit;

namespace VideoPlayer.Tests.Unit.Views;

public sealed class PlayerViewSeekTests
{
    [WpfTheory]
    [InlineData(PlaybackState.Playing, 75.5)]
    [InlineData(PlaybackState.Paused, 75.5)]
    [InlineData(PlaybackState.Playing, 0)]
    [InlineData(PlaybackState.Playing, 120)]
    public async Task SeekBar_HandledTrackClick_SeeksSelectedPositionOnceAndPreservesPlaybackState(
        PlaybackState playbackState,
        double selectedSeconds)
    {
        var fixture = new PlayerViewModelFixture(1);
        await using var viewModel = fixture.ViewModel;
        viewModel.Duration = TimeSpan.FromSeconds(120);
        viewModel.PlaybackState = playbackState;
        TimeSpan? soughtPosition = null;
        var seekCount = 0;
        fixture.Player.SeekHandler = (position, _) =>
        {
            soughtPosition = position;
            seekCount++;
            return Task.FromResult(ServiceResult.Success());
        };
        using var view = new PlayerView { DataContext = viewModel };
        view.Measure(new Size(800, 500));
        view.Arrange(new Rect(0, 0, 800, 500));
        view.UpdateLayout();
        var slider = Assert.IsType<Slider>(view.FindName("SeekSlider"));

        // WPF 原生点击处理会先更新 Value 并标记事件；验证视图仍将该位置提交给播放服务。
        slider.SetCurrentValue(Slider.ValueProperty, selectedSeconds);
        slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
            Handled = true
        });
        slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent
        });
        Assert.Equal(TimeSpan.FromSeconds(selectedSeconds), soughtPosition);
        Assert.Equal(1, seekCount);
        Assert.Equal(playbackState, viewModel.PlaybackState);
        Assert.Equal(0, fixture.Player.PlayCallCount);
        Assert.Equal(0, fixture.Player.PauseCallCount);
    }

    [WpfFact]
    public async Task SeekBar_TrackClick_WhenReleasedOutsideSlider_StillCommitsOnce()
    {
        var fixture = new PlayerViewModelFixture(1);
        await using var viewModel = fixture.ViewModel;
        viewModel.Duration = TimeSpan.FromSeconds(120);
        TimeSpan? soughtPosition = null;
        var seekCount = 0;
        fixture.Player.SeekHandler = (position, _) =>
        {
            soughtPosition = position;
            seekCount++;
            return Task.FromResult(ServiceResult.Success());
        };
        using var view = new PlayerView { DataContext = viewModel };
        view.Measure(new Size(800, 500));
        view.Arrange(new Rect(0, 0, 800, 500));
        view.UpdateLayout();
        var slider = Assert.IsType<Slider>(view.FindName("SeekSlider"));
        slider.SetCurrentValue(Slider.ValueProperty, 75.5);
        slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
            Handled = true
        });
        // 轨道点击没有原生鼠标捕获，移出后释放只会投递给滑块之外的控件。
        var outside = Assert.IsType<Button>(view.FindName("OpenButton"));
        outside.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent
        });

        Assert.Equal(TimeSpan.FromSeconds(75.5), soughtPosition);
        Assert.Equal(1, seekCount);
    }

    [WpfFact]
    public async Task SeekBar_ThumbDrag_CommitsNativeTrackValueOnRelease()
    {
        var fixture = new PlayerViewModelFixture(1);
        await using var viewModel = fixture.ViewModel;
        viewModel.Duration = TimeSpan.FromSeconds(120);
        viewModel.Position = TimeSpan.FromSeconds(20);
        viewModel.PendingSeekPosition = 20;
        TimeSpan? soughtPosition = null;
        fixture.Player.SeekHandler = (position, _) =>
        {
            soughtPosition = position;
            return Task.FromResult(ServiceResult.Success());
        };
        using var view = new PlayerView { DataContext = viewModel };
        view.Measure(new Size(800, 500));
        view.Arrange(new Rect(0, 0, 800, 500));
        view.UpdateLayout();
        var slider = Assert.IsType<Slider>(view.FindName("SeekSlider"));
        var track = Assert.IsType<Track>(slider.Template.FindName("PART_Track", slider));

        viewModel.BeginSeek();
        // 可拖动距离的一半对应 60 秒增量，从 20 秒拖到 80 秒；定位依赖真实双向绑定。
        track.Thumb.RaiseEvent(new DragDeltaEventArgs(
            (track.ActualWidth - track.Thumb.ActualWidth) / 2, 0)
        {
            RoutedEvent = Thumb.DragDeltaEvent
        });
        slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent
        });

        Assert.Equal(TimeSpan.FromSeconds(80), soughtPosition);
    }
}
