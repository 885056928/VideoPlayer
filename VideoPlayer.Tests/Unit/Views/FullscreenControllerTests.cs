using System.Collections.Generic;
using System.Windows;
using System.Windows.Shell;
using VideoPlayer.Views;
using Xunit;

namespace VideoPlayer.Tests.Unit.Views;

public sealed class FullscreenControllerTests
{
    [Fact]
    public void BuildState_CapturesWindowValues()
    {
        var state = FullscreenState.From(
            System.Windows.WindowStyle.SingleBorderWindow,
            System.Windows.ResizeMode.CanResize,
            System.Windows.WindowState.Normal,
            new System.Windows.Rect(10, 20, 800, 600));

        Assert.Equal(System.Windows.WindowStyle.SingleBorderWindow, state.Style);
        Assert.Equal(800, state.Bounds.Width);
    }

    [WpfFact]
    public void Toggle_NotifiesFullscreenChanges()
    {
        var window = new System.Windows.Window();
        var states = new List<bool>();
        var controller = new FullscreenController(window, states.Add);

        controller.Toggle();
        controller.Toggle();

        Assert.Equal([true, false], states);
    }

    [WpfTheory]
    [InlineData(WindowState.Normal)]
    [InlineData(WindowState.Maximized)]
    public void Toggle_CustomChromeIsRemovedDuringFullscreenAndRestoredOnExit(WindowState originalState)
    {
        var window = new Window
        {
            WindowState = originalState,
            Left = 10,
            Top = 20,
            Width = 800,
            Height = 600
        };
        var chrome = new WindowChrome
        {
            CaptionHeight = 36,
            GlassFrameThickness = new Thickness(0),
            ResizeBorderThickness = new Thickness(6)
        };
        WindowChrome.SetWindowChrome(window, chrome);
        var controller = new FullscreenController(window);
        try
        {
            controller.Enter();
            // 全屏不能保留标题命中区或按任务栏工作区裁剪的自绘窗口框架。
            Assert.Null(WindowChrome.GetWindowChrome(window));
            Assert.Equal(WindowStyle.None, window.WindowStyle);
            Assert.Equal(ResizeMode.NoResize, window.ResizeMode);

            controller.Exit();
            Assert.Same(chrome, WindowChrome.GetWindowChrome(window));
            Assert.Equal(originalState, window.WindowState);
            Assert.Equal(800, window.Width);
            Assert.Equal(600, window.Height);
        }
        finally
        {
            window.Close();
        }
    }

    [WpfFact]
    public void Toggle_EntersAndRestoresWindowState()
    {
        var window = new System.Windows.Window
        {
            WindowStyle = System.Windows.WindowStyle.SingleBorderWindow,
            ResizeMode = System.Windows.ResizeMode.CanResize,
            WindowState = System.Windows.WindowState.Normal,
            Left = 10,
            Top = 20,
            Width = 800,
            Height = 600
        };
        var controller = new FullscreenController(window);

        controller.Toggle();

        Assert.True(controller.IsFullscreen);
        Assert.Equal(System.Windows.WindowStyle.None, window.WindowStyle);
        Assert.Equal(System.Windows.ResizeMode.NoResize, window.ResizeMode);
        Assert.Equal(System.Windows.WindowState.Maximized, window.WindowState);

        controller.Toggle();

        Assert.False(controller.IsFullscreen);
        Assert.Equal(System.Windows.WindowStyle.SingleBorderWindow, window.WindowStyle);
        Assert.Equal(System.Windows.ResizeMode.CanResize, window.ResizeMode);
        Assert.Equal(System.Windows.WindowState.Normal, window.WindowState);
        Assert.Equal(10, window.Left);
        Assert.Equal(20, window.Top);
        Assert.Equal(800, window.Width);
        Assert.Equal(600, window.Height);
    }
}
