using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using VideoPlayer.Enums;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;
using VideoPlayer.Tests.Fakes;
using VideoPlayer.ViewModels;
using Xunit;

namespace VideoPlayer.Tests.Unit.Views;

public sealed class MainWindowLayoutTests
{
    private const uint NonClientHitTestMessage = 0x0084;
    private const int ClientHitArea = 1;
    private const int CaptionHitArea = 2;
    private const int DefaultToNearestMonitor = 2;
    [StaFact]
    public void HeaderActions_KeepFolderAndPlaylistButRemoveDuplicateOpenFile()
    {
        var player = new PlayerViewModelFixture(1);
        var viewModel = new MainViewModel(
            player.ViewModel,
            new PlaylistViewModel(player.Playlist, new ImmediateUiDispatcher()),
            new VideoInfoViewModel(new StubMediaInfoService()),
            player.Settings);
        var window = new MainWindow(viewModel);
        try
        {
            Assert.Null(window.FindName("HeaderOpenFileButton"));
            Assert.NotNull(window.FindName("HeaderOpenFolderButton"));
            Assert.NotNull(window.FindName("TogglePlaylistButton"));
        }
        finally
        {
            window.Close();
            window.Dispatcher.Invoke(
                static () => { },
                DispatcherPriority.Background);
        }
    }

    [StaFact]
    public void HeaderTitle_BindsToCurrentMediaTitle()
    {
        var player = new PlayerViewModelFixture(2);
        var viewModel = new MainViewModel(
            player.ViewModel,
            new PlaylistViewModel(player.Playlist, new ImmediateUiDispatcher()),
            new VideoInfoViewModel(new StubMediaInfoService()),
            player.Settings);
        var window = new MainWindow(viewModel);
        try
        {
            var title = Assert.IsType<TextBlock>(window.FindName("HeaderMediaTitleText"));

            Assert.Equal(
                "Player.MediaTitle",
                BindingOperations.GetBinding(title, TextBlock.TextProperty)?.Path.Path);
        }
        finally
        {
            window.Close();
            window.Dispatcher.Invoke(
                static () => { },
                DispatcherPriority.Background);
        }
    }
    [WpfFact]
    public async Task Header_UsesCustomChromeAndKeepsWindowActionsAfterPlaylist()
    {
        var player = new PlayerViewModelFixture(3);
        await using var playerViewModel = player.ViewModel;
        var window = CreateWindow(player);
        try
        {
            var chrome = Assert.IsType<WindowChrome>(WindowChrome.GetWindowChrome(window));
            Assert.Equal(new Thickness(0), chrome.GlassFrameThickness);
            Assert.False(chrome.UseAeroCaptionButtons);
            Assert.True(chrome.ResizeBorderThickness.Left > 0);
            Assert.Equal(WindowStyle.SingleBorderWindow, window.WindowStyle);

            var header = Assert.IsType<Border>(window.FindName("AppHeader"));
            var layout = Assert.IsType<Grid>(window.FindName("RootLayout"));
            Assert.True(layout.RowDefinitions[0].Height.IsAuto);
            Assert.Equal(header.Height, chrome.CaptionHeight + chrome.ResizeBorderThickness.Top);
            var playlistButton = Assert.IsType<Button>(window.FindName("TogglePlaylistButton"));
            var actions = Assert.IsType<StackPanel>(playlistButton.Parent);
            Assert.Equal(
                new[] { "HeaderOpenFolderButton", "HeaderFullscreenButton", "TogglePlaylistButton", "MinimizeWindowButton", "MaximizeWindowButton", "CloseWindowButton" },
                actions.Children.OfType<Button>().Select(button => button.Name));

            // 自绘标题区内的按钮必须接收客户端点击，不能被系统当作拖动标题栏。
            foreach (var button in actions.Children.OfType<Button>())
            {
                Assert.True(WindowChrome.GetIsHitTestVisibleInChrome(button));
                Assert.False(string.IsNullOrWhiteSpace(button.ToolTip?.ToString()));
                Assert.False(string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(button)));
            }
        }
        finally
        {
            CloseWindow(window);
        }
    }

    [WpfFact]
    public async Task MaximizeButton_FollowsWindowStateForGlyphAndAccessibleLabel()
    {
        var player = new PlayerViewModelFixture(4);
        await using var playerViewModel = player.ViewModel;
        var window = CreateWindow(player);
        try
        {
            var maximize = Assert.IsType<Button>(window.FindName("MaximizeWindowButton"));
            Assert.Equal("\uE922", maximize.Content);
            Assert.Equal("最大化", maximize.ToolTip);

            // 双击标题栏或系统快捷键改变窗口状态时，按钮同样切换为还原。
            window.WindowState = WindowState.Maximized;
            window.Dispatcher.Invoke(static () => { }, DispatcherPriority.DataBind);
            Assert.Equal("\uE923", maximize.Content);
            Assert.Equal("还原", maximize.ToolTip);
            Assert.Equal("还原", System.Windows.Automation.AutomationProperties.GetName(maximize));

            window.WindowState = WindowState.Normal;
            window.Dispatcher.Invoke(static () => { }, DispatcherPriority.DataBind);
            Assert.Equal("\uE922", maximize.Content);
            Assert.Equal("最大化", System.Windows.Automation.AutomationProperties.GetName(maximize));
        }
        finally
        {
            CloseWindow(window);
        }
    }

    [WpfFact]
    public async Task WindowButtons_ExecuteNativeStateChangesAndExistingCloseFlow()
    {
        var player = new PlayerViewModelFixture(5);
        await using var playerViewModel = player.ViewModel;
        var window = CreateWindow(player);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        try
        {
            // 创建测试专用 HWND，让系统命令经过真实消息队列；窗口透明且不出现在任务栏。
            window.Opacity = 0;
            window.ShowInTaskbar = false;
            window.ShowActivated = false;
            _ = new WindowInteropHelper(window).EnsureHandle();
            var maximize = Assert.IsType<Button>(window.FindName("MaximizeWindowButton"));
            var minimize = Assert.IsType<Button>(window.FindName("MinimizeWindowButton"));
            var close = Assert.IsType<Button>(window.FindName("CloseWindowButton"));
            await ClickAndWaitForStateAsync(window, maximize, WindowState.Maximized);
            await ClickAndWaitForStateAsync(window, maximize, WindowState.Normal);
            await ClickAndWaitForStateAsync(window, minimize, WindowState.Minimized);

            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(window.IsEnabled);
        }
        finally
        {
            if (!closed.Task.IsCompleted)
            {
                CloseWindow(window);
            }
        }
    }

    private static async Task ClickAndWaitForStateAsync(MainWindow window, Button button, WindowState expected)
    {
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStateChanged(object? sender, EventArgs e)
        {
            if (window.WindowState == expected)
            {
                changed.TrySetResult();
            }
        }

        window.StateChanged += OnStateChanged;
        try
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(expected, window.WindowState);
        }
        finally
        {
            window.StateChanged -= OnStateChanged;
        }
    }

    [WpfTheory]
    [InlineData(WindowState.Normal, Key.F11)]
    [InlineData(WindowState.Normal, Key.Escape)]
    [InlineData(WindowState.Maximized, Key.F11)]
    [InlineData(WindowState.Maximized, Key.Escape)]
    public async Task Fullscreen_FromCustomTitleBar_RemovesHeaderAndRestoresNativeBounds(WindowState originalState, Key exitKey)
    {
        var player = new PlayerViewModelFixture(6);
        await using var playerViewModel = player.ViewModel;
        var window = CreateWindow(player);
        try
        {
            window.Opacity = 0;
            window.ShowInTaskbar = false;
            window.ShowActivated = false;
            window.Show();
            var handle = new WindowInteropHelper(window).Handle;
            var chrome = WindowChrome.GetWindowChrome(window);
            var header = Assert.IsType<Border>(window.FindName("AppHeader"));
            var layout = Assert.IsType<Grid>(window.FindName("RootLayout"));
            var monitor = GetMonitorInfoForWindow(handle);
            if (originalState == WindowState.Maximized)
            {
                var maximize = Assert.IsType<Button>(window.FindName("MaximizeWindowButton"));
                await ClickAndWaitForStateAsync(window, maximize, WindowState.Maximized);
                window.UpdateLayout();
                Assert.Equal(ToRect(monitor.WorkArea), GetVisibleWindowBounds(handle));
                AssertContentFitsWorkArea(layout, monitor.WorkArea);
            }

            window.UpdateLayout();
            AssertHeaderHitAreas(window, layout, header, handle);
            var restoredBounds = window.RestoreBounds;
            var source = Assert.IsType<HwndSource>(PresentationSource.FromVisual(window));
            Assert.True(PressKey(window, source, Key.F11).Handled);
            window.UpdateLayout();
            Assert.Null(WindowChrome.GetWindowChrome(window));
            Assert.Equal(Visibility.Collapsed, header.Visibility);
            Assert.Equal(0, layout.RowDefinitions[0].ActualHeight);
            Assert.Equal(ToRect(monitor.MonitorArea), GetVisibleWindowBounds(handle));

            Assert.True(PressKey(window, source, exitKey).Handled);
            window.UpdateLayout();
            Assert.Same(chrome, WindowChrome.GetWindowChrome(window));
            Assert.Equal(Visibility.Visible, header.Visibility);
            Assert.Equal(42, layout.RowDefinitions[0].ActualHeight);
            Assert.Equal(originalState, window.WindowState);
            Assert.Equal(restoredBounds, window.RestoreBounds);
            if (originalState == WindowState.Maximized)
            {
                Assert.Equal(ToRect(monitor.WorkArea), GetVisibleWindowBounds(handle));
                AssertContentFitsWorkArea(layout, monitor.WorkArea);
            }
            AssertHeaderHitAreas(window, layout, header, handle);
        }
        finally
        {
            CloseWindow(window);
        }
    }

    [WpfTheory]
    [InlineData(true, Key.F11)]
    [InlineData(true, Key.Escape)]
    [InlineData(false, Key.F11)]
    [InlineData(false, Key.Escape)]
    public async Task FullscreenButton_RestoresPlaylistPreferenceOnKeyboardExit(bool showPlaylist, Key exitKey)
    {
        var player = new PlayerViewModelFixture(7);
        await using var playerViewModel = player.ViewModel;
        player.Settings.Current.UI.ShowPlaylist = showPlaylist;
        var window = CreateWindow(player);
        try
        {
            ShowInvisibleWindow(window);
            var button = Assert.IsType<Button>(window.FindName("HeaderFullscreenButton"));
            var chrome = WindowChrome.GetWindowChrome(window);
            var playlist = Assert.IsAssignableFrom<FrameworkElement>(window.FindName("PlaylistView"));
            var column = Assert.IsType<ColumnDefinition>(window.FindName("PlaylistColumn"));
            var source = Assert.IsType<HwndSource>(PresentationSource.FromVisual(window));

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(WindowChrome.GetWindowChrome(window));
            Assert.Equal(Visibility.Collapsed, playlist.Visibility);
            Assert.Equal(0, column.Width.Value);

            Assert.True(PressKey(window, source, exitKey).Handled);
            Assert.Same(chrome, WindowChrome.GetWindowChrome(window));
            Assert.Equal(showPlaylist ? Visibility.Visible : Visibility.Collapsed, playlist.Visibility);
            Assert.Equal(showPlaylist ? 320 : 0, column.Width.Value);
            Assert.Equal(showPlaylist, player.Settings.Current.UI.ShowPlaylist);
            var view = Assert.IsType<VideoPlayer.Views.PlayerView>(window.FindName("PlayerView"));
            Assert.Equal(Visibility.Visible, Assert.IsType<Border>(view.FindName("ControlBar")).Visibility);
            // 窗口模式的 Esc 不应被全屏处理占用。
            Assert.False(PressKey(window, source, Key.Escape).Handled);
        }
        finally
        {
            CloseWindow(window);
        }
    }

    [WpfTheory]
    [InlineData(Key.F11)]
    [InlineData(Key.Escape)]
    public async Task FullscreenKeys_WorkInNativeVideoOverlayAndDetachWhenUnloaded(Key exitKey)
    {
        var player = new PlayerViewModelFixture(8);
        await using var playerViewModel = player.ViewModel;
        var window = CreateWindow(player);
        try
        {
            var overlayWindow = PrepareInvisibleOverlay(window);
            ShowInvisibleWindow(window);
            var source = Assert.IsType<HwndSource>(PresentationSource.FromVisual(overlayWindow));
            Assert.NotSame(window, overlayWindow);
            Assert.Same(window, overlayWindow.Owner);
            var chrome = WindowChrome.GetWindowChrome(window);

            // LibVLC 的内容位于另一棵窗口事件树，不能用主窗口模拟视频区按键。
            Assert.True(PressKey(overlayWindow, source, Key.F11).Handled);
            Assert.Null(WindowChrome.GetWindowChrome(window));
            Assert.True(PressKey(overlayWindow, source, exitKey).Handled);
            Assert.Same(chrome, WindowChrome.GetWindowChrome(window));

            var view = Assert.IsType<VideoPlayer.Views.PlayerView>(window.FindName("PlayerView"));
            var overlay = Assert.IsType<Grid>(view.FindName("OverlayRoot"));
            var parent = Assert.IsType<Grid>(overlay.Parent);
            var unloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            overlay.Unloaded += (_, _) => unloaded.TrySetResult();
            parent.Children.Remove(overlay);
            await unloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(PressKey(overlayWindow, source, Key.F11).Handled);
            Assert.Same(chrome, WindowChrome.GetWindowChrome(window));

            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            overlay.Loaded += (_, _) => loaded.TrySetResult();
            parent.Children.Add(overlay);
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(PressKey(overlayWindow, source, Key.F11).Handled);
            Assert.Null(WindowChrome.GetWindowChrome(window));
            Assert.True(PressKey(overlayWindow, source, Key.Escape).Handled);
            Assert.Same(chrome, WindowChrome.GetWindowChrome(window));
        }
        finally
        {
            CloseWindow(window);
        }
    }

    [WpfFact]
    public async Task TextInput_DoesNotBlockFullscreenKeysOrReceivePlaybackShortcuts()
    {
        var player = new PlayerViewModelFixture(9);
        await using var playerViewModel = player.ViewModel;
        var window = CreateWindow(player);
        try
        {
            var layout = Assert.IsType<Grid>(window.FindName("RootLayout"));
            var input = new TextBox();
            layout.Children.Add(input);
            ShowInvisibleWindow(window);
            Keyboard.Focus(input);
            Assert.Same(input, Keyboard.FocusedElement);
            var source = Assert.IsType<HwndSource>(PresentationSource.FromVisual(window));
            var chrome = WindowChrome.GetWindowChrome(window);

            Assert.False(PressKey(input, source, Key.Space).Handled);
            Assert.True(PressKey(input, source, Key.F11).Handled);
            Assert.Null(WindowChrome.GetWindowChrome(window));
            Assert.True(PressKey(input, source, Key.Escape).Handled);
            Assert.Same(chrome, WindowChrome.GetWindowChrome(window));
        }
        finally
        {
            CloseWindow(window);
        }
    }

    [WpfTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VideoOverlay_MouseActivityWakesFullscreenControls(bool mouseDown)
    {
        var player = new PlayerViewModelFixture(10);
        await using var playerViewModel = player.ViewModel;
        var window = CreateWindow(player);
        try
        {
            var overlayWindow = PrepareInvisibleOverlay(window);
            overlayWindow.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/VideoPlayer;component/Resources/Styles/Colors.xaml", UriKind.Relative)
            });
            ShowInvisibleWindow(window);
            var source = Assert.IsType<HwndSource>(PresentationSource.FromVisual(window));
            Assert.True(PressKey(window, source, Key.F11).Handled);
            var view = Assert.IsType<VideoPlayer.Views.PlayerView>(window.FindName("PlayerView"));
            var controls = Assert.IsType<Border>(view.FindName("ControlBar"));
            var overlay = Assert.IsType<Grid>(view.FindName("OverlayRoot"));
            controls.Visibility = Visibility.Collapsed;

            MouseEventArgs activity = mouseDown
                ? new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseDownEvent }
                : new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.PreviewMouseMoveEvent };
            overlay.RaiseEvent(activity);
            Assert.Equal(Visibility.Visible, controls.Visibility);
            // 分层浮窗的全透明像素会穿透鼠标，必须保留非零透明度。
            Assert.True(Assert.IsType<SolidColorBrush>(overlay.Background).Color.A > 0);
        }
        finally
        {
            CloseWindow(window);
        }
    }

    private static KeyEventArgs PressKey(UIElement target, PresentationSource source, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
        {
            RoutedEvent = UIElement.PreviewKeyDownEvent
        };
        target.RaiseEvent(args);
        return args;
    }

    [WpfFact]
    public async Task VideoOverlay_PreservesPlaybackShortcutAndFileDrop()
    {
        var player = new PlayerViewModelFixture(11);
        await using var playerViewModel = player.ViewModel;
        var window = CreateWindow(player);
        try
        {
            var overlayWindow = PrepareInvisibleOverlay(window);
            ShowInvisibleWindow(window);
            var source = Assert.IsType<HwndSource>(PresentationSource.FromVisual(overlayWindow));
            Assert.True(PressKey(overlayWindow, source, Key.Space).Handled);
            Assert.Equal(1, player.Player.PauseCallCount);
            Assert.True(overlayWindow.AllowDrop);

            var view = Assert.IsType<VideoPlayer.Views.PlayerView>(window.FindName("PlayerView"));
            var overlay = Assert.IsType<Grid>(view.FindName("OverlayRoot"));
            var data = new DataObject(DataFormats.FileDrop, new[] { "a.mp4" });
            var drag = CreateDragEvent(overlay, data, UIElement.PreviewDragOverEvent);
            overlay.RaiseEvent(drag);
            Assert.True(drag.Handled);
            Assert.Equal(DragDropEffects.Copy, drag.Effects);

            var drop = CreateDragEvent(overlay, data, UIElement.PreviewDropEvent);
            overlay.RaiseEvent(drop);
            Assert.True(drop.Handled);
            Assert.Equal(1, player.Player.OpenCount);
        }
        finally
        {
            CloseWindow(window);
        }
    }

    private static DragEventArgs CreateDragEvent(DependencyObject target, IDataObject data, RoutedEvent routedEvent)
    {
        // WPF 的拖放事件参数构造器不公开，测试在真实窗口路由中注入参数以避免外部拖放副作用。
        var args = Assert.IsType<DragEventArgs>(Activator.CreateInstance(
            typeof(DragEventArgs),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [data, DragDropKeyStates.None, DragDropEffects.Copy, target, new Point()],
            culture: null));
        args.RoutedEvent = routedEvent;
        return args;
    }

    private static Window PrepareInvisibleOverlay(MainWindow window)
    {
        var view = Assert.IsType<VideoPlayer.Views.PlayerView>(window.FindName("PlayerView"));
        Assert.IsAssignableFrom<FrameworkElement>(view.FindName("VideoView")).ApplyTemplate();
        var overlay = Assert.IsType<Grid>(view.FindName("OverlayRoot"));
        var overlayWindow = Assert.IsAssignableFrom<Window>(Window.GetWindow(overlay));
        overlayWindow.Opacity = 0;
        overlayWindow.ShowActivated = false;
        return overlayWindow;
    }

    private static void ShowInvisibleWindow(MainWindow window)
    {
        window.Opacity = 0;
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.Show();
    }

    private static void AssertContentFitsWorkArea(Grid layout, NativeRect workArea)
    {
        // 可见区域正确还不够，内容上下边缘也必须避开最大化时被裁掉的系统边框。
        Assert.Equal(new Point(workArea.Left, workArea.Top), layout.PointToScreen(new Point()));
        Assert.Equal(new Point(workArea.Right, workArea.Bottom),
            layout.PointToScreen(new Point(layout.ActualWidth, layout.ActualHeight)));
    }

    private static void AssertHeaderHitAreas(MainWindow window, Grid layout, Border header, IntPtr handle)
    {
        // 验证完整顶栏的下缘仍是标题区，而所有标题按钮均由 WPF 接收点击。
        var titlePoint = layout.PointToScreen(new Point(200, header.Height - 2));
        Assert.Equal(new IntPtr(CaptionHitArea), HitTestWindow(handle, titlePoint));
        foreach (var name in new[] { "HeaderOpenFolderButton", "HeaderFullscreenButton", "TogglePlaylistButton", "MinimizeWindowButton", "MaximizeWindowButton", "CloseWindowButton" })
        {
            var button = Assert.IsType<Button>(window.FindName(name));
            var center = button.PointToScreen(new Point(button.ActualWidth / 2, button.ActualHeight / 2));
            Assert.Equal(new IntPtr(ClientHitArea), HitTestWindow(handle, center));
        }
    }

    private static IntPtr HitTestWindow(IntPtr handle, Point screenPoint)
    {
        var packedPoint = unchecked((int)((ushort)screenPoint.X | ((uint)(ushort)screenPoint.Y << 16)));
        return SendMessage(handle, NonClientHitTestMessage, IntPtr.Zero, new IntPtr(packedPoint));
    }

    private static MonitorInfo GetMonitorInfoForWindow(IntPtr handle)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        Assert.True(GetMonitorInfo(MonitorFromWindow(handle, DefaultToNearestMonitor), ref info));
        return info;
    }

    private static Rect GetVisibleWindowBounds(IntPtr handle)
    {
        Assert.True(GetWindowRect(handle, out var bounds));
        var region = CreateRectRgn(0, 0, 0, 0);
        Assert.NotEqual(IntPtr.Zero, region);
        try
        {
            // WindowChrome 用窗口区域裁掉最大化时的隐藏边框，检查实际可见范围而非外框尺寸。
            if (GetWindowRgn(handle, region) != 0)
            {
                Assert.NotEqual(0, GetRgnBox(region, out var clipped));
                return new Rect(bounds.Left + clipped.Left, bounds.Top + clipped.Top,
                    clipped.Right - clipped.Left, clipped.Bottom - clipped.Top);
            }

            return ToRect(bounds);
        }
        finally
        {
            Assert.True(DeleteObject(region));
        }
    }

    private static Rect ToRect(NativeRect rect)
        => new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public int Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern int GetWindowRgn(IntPtr handle, IntPtr region);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern int GetRgnBox(IntPtr region, out NativeRect rect);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    private static MainWindow CreateWindow(PlayerViewModelFixture player)
        => new(new MainViewModel(
            player.ViewModel,
            new PlaylistViewModel(player.Playlist, new ImmediateUiDispatcher()),
            new VideoInfoViewModel(new StubMediaInfoService()),
            player.Settings));

    private static void CloseWindow(MainWindow window)
    {
        window.Close();
        window.Dispatcher.Invoke(static () => { }, DispatcherPriority.Background);
    }

    private sealed class ImmediateUiDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;

        public void Post(Action action) => action();

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private sealed class StubMediaInfoService : IMediaInfoService
    {
        public Task<ServiceResult<VideoInfo>> GetCurrentMediaInfoAsync(
            PlaybackSnapshot snapshot,
            IReadOnlyList<PlayerTrackInfo> tracks,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult<VideoInfo>.Failure(
                ErrorCode.Unknown,
                "Unavailable",
                "Unavailable"));
    }
}
