using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Win32;
using Serilog;
using VideoPlayer.Enums;
using VideoPlayer.Models;
using VideoPlayer.Services;
using VideoPlayer.ViewModels;
using VideoPlayer.Views;

namespace VideoPlayer;

public partial class MainWindow : Window
{
    private const int ResizeFrameWidthMetric = 32;
    private const int ResizeFrameHeightMetric = 33;
    private const int PaddedBorderMetric = 92;
    private readonly MainViewModel _viewModel;
    private readonly FullscreenController _fullscreenController;
    private readonly FrameworkElement _videoOverlayRoot;
    private Window? _videoOverlayWindow;
    private bool _shutdownStarted;
    private bool _allowClose;
    private bool _viewDisposed;
    private bool _isPlaylistVisible = true;
    private bool _playlistVisibleBeforeFullscreen = true;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _fullscreenController = new FullscreenController(this, OnFullscreenChanged);
        _videoOverlayRoot = (FrameworkElement)PlayerView.FindName("OverlayRoot");
        _videoOverlayRoot.Loaded += VideoOverlay_Loaded;
        _videoOverlayRoot.Unloaded += VideoOverlay_Unloaded;
        DataContext = viewModel;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        AllowDrop = true;
        PreviewDragOver += MainWindow_PreviewDragOver;
        PreviewDrop += MainWindow_PreviewDrop;
        _viewModel.Playlist.PlayRequested += Playlist_PlayRequested;
        ApplyPlaylistVisibility(_viewModel.Settings.Current.UI.ShowPlaylist);
        ApplyTheme(_viewModel.Settings.Current.UI.Theme);
        UpdateWindowChromeLayout();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        UpdateWindowChromeLayout();
        base.OnStateChanged(e);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UpdateWindowChromeLayout();
    }

    private void UpdateWindowChromeLayout()
    {
        var chrome = WindowChrome.GetWindowChrome(this);
        if (chrome is null)
        {
            RootLayout.Margin = new Thickness(0);
            return;
        }

        // 普通最大化外框含隐藏系统边框，内容向内补偿；全屏移除 Chrome 后不保留边距。
        var inset = WindowState == WindowState.Maximized
            ? GetMaximizedWindowInset()
            : new Thickness(0);
        RootLayout.Margin = inset;
        // 标题命中区覆盖完整可见顶栏，避免最大化补偿后底部变成无法拖动的区域。
        chrome.CaptionHeight = AppHeader.Height + inset.Top - chrome.ResizeBorderThickness.Top;
    }

    private Thickness GetMaximizedWindowInset()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var horizontalDpi = (uint)Math.Round(dpi.PixelsPerInchX);
        var verticalDpi = (uint)Math.Round(dpi.PixelsPerInchY);
        // WPF 的 WindowResizeBorderThickness 不包含系统附加边框，需按当前显示器 DPI 一并计入。
        var horizontal = (GetSystemMetricsForDpi(ResizeFrameWidthMetric, horizontalDpi)
            + GetSystemMetricsForDpi(PaddedBorderMetric, horizontalDpi)) / dpi.DpiScaleX;
        var vertical = (GetSystemMetricsForDpi(ResizeFrameHeightMetric, verticalDpi)
            + GetSystemMetricsForDpi(PaddedBorderMetric, verticalDpi)) / dpi.DpiScaleY;
        return new Thickness(horizontal, vertical, horizontal, vertical);
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_allowClose)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        IsEnabled = false;
        DisposeView();
        try
        {
            if (Application.Current is App app)
            {
                await app.DisposeServicesAsync();
            }
        }
        finally
        {
            _allowClose = true;
            _ = Dispatcher.BeginInvoke((Action)Close);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        PreviewKeyDown -= MainWindow_PreviewKeyDown;
        _videoOverlayRoot.Loaded -= VideoOverlay_Loaded;
        _videoOverlayRoot.Unloaded -= VideoOverlay_Unloaded;
        DetachVideoOverlayEvents();
        PreviewDragOver -= MainWindow_PreviewDragOver;
        PreviewDrop -= MainWindow_PreviewDrop;
        _viewModel.Playlist.PlayRequested -= Playlist_PlayRequested;
        DisposeView();
        base.OnClosed(e);
    }

    private void VideoOverlay_Loaded(object sender, RoutedEventArgs e)
    {
        DetachVideoOverlayEvents();
        var overlayWindow = Window.GetWindow(_videoOverlayRoot);
        if (overlayWindow is null || ReferenceEquals(overlayWindow, this))
        {
            return;
        }

        // LibVLC 浮层属于独立窗口，复用主窗输入处理，保留视频区快捷键与文件拖放。
        _videoOverlayWindow = overlayWindow;
        _videoOverlayWindow.PreviewKeyDown += MainWindow_PreviewKeyDown;
        _videoOverlayWindow.AllowDrop = AllowDrop;
        _videoOverlayWindow.PreviewDragOver += MainWindow_PreviewDragOver;
        _videoOverlayWindow.PreviewDrop += MainWindow_PreviewDrop;
    }

    private void VideoOverlay_Unloaded(object sender, RoutedEventArgs e)
        => DetachVideoOverlayEvents();

    private void DetachVideoOverlayEvents()
    {
        if (_videoOverlayWindow is not null)
        {
            _videoOverlayWindow.PreviewKeyDown -= MainWindow_PreviewKeyDown;
            _videoOverlayWindow.PreviewDragOver -= MainWindow_PreviewDragOver;
            _videoOverlayWindow.PreviewDrop -= MainWindow_PreviewDrop;
            _videoOverlayWindow = null;
        }
    }

    private async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var action = ShortcutService.Resolve(key, Keyboard.Modifiers);
        // 输入框继续独占文本编辑按键，但全屏退出不能被输入焦点阻挡。
        if (Keyboard.FocusedElement is TextBoxBase
            && action is not (ShortcutAction.ToggleFullscreen or ShortcutAction.ExitFullscreen))
        {
            return;
        }

        if (!ShouldHandleShortcut(action, _fullscreenController.IsFullscreen))
        {
            return;
        }

        e.Handled = true;
        // 长按 F11 的自动重复不能连续进出全屏。
        if (action == ShortcutAction.ToggleFullscreen && e.IsRepeat)
        {
            return;
        }

        try
        {
            await ExecuteShortcutAsync(action);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Failed to execute keyboard shortcut {ShortcutAction}.", action);
        }
    }

    internal static bool ShouldHandleShortcut(
        ShortcutAction action,
        bool isFullscreen)
        => action != ShortcutAction.None
            && (action != ShortcutAction.ExitFullscreen || isFullscreen);

    private async Task ExecuteShortcutAsync(ShortcutAction action)
    {
        switch (action)
        {
            case ShortcutAction.TogglePlayPause:
                await _viewModel.Player.TogglePlayPauseAsync();
                break;
            case ShortcutAction.Previous:
                await _viewModel.Player.PreviousAsync();
                break;
            case ShortcutAction.Next:
                await _viewModel.Player.NextAsync();
                break;
            case ShortcutAction.SeekBackward5:
                await _viewModel.Player.SeekBackward5Async();
                break;
            case ShortcutAction.SeekForward5:
                await _viewModel.Player.SeekForward5Async();
                break;
            case ShortcutAction.SeekBackward30:
                await _viewModel.Player.SeekBackward30Async();
                break;
            case ShortcutAction.SeekForward30:
                await _viewModel.Player.SeekForward30Async();
                break;
            case ShortcutAction.VolumeUp:
                _viewModel.Player.VolumeUp();
                break;
            case ShortcutAction.VolumeDown:
                _viewModel.Player.VolumeDown();
                break;
            case ShortcutAction.ToggleMute:
                await _viewModel.Player.ToggleMuteAsync();
                break;
            case ShortcutAction.RateDown:
                _viewModel.Player.RateDown();
                break;
            case ShortcutAction.RateUp:
                _viewModel.Player.RateUp();
                break;
            case ShortcutAction.RateReset:
                _viewModel.Player.RateReset();
                break;
            case ShortcutAction.ToggleFullscreen:
                _fullscreenController.Toggle();
                break;
            case ShortcutAction.ExitFullscreen:
                _fullscreenController.Exit();
                break;
            case ShortcutAction.OpenFile:
                await OpenFileAsync();
                break;
            case ShortcutAction.OpenFolder:
                await OpenFolderAsync();
                break;
            case ShortcutAction.TogglePlaylist:
                await TogglePlaylistAsync();
                break;
            case ShortcutAction.VideoInfo:
                await ShowVideoInfoAsync();
                break;
            case ShortcutAction.Screenshot:
                await _viewModel.Player.ScreenshotAsync();
                break;
        }
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
        => SystemCommands.MinimizeWindow(this);

    private void ToggleFullscreen_Click(object sender, RoutedEventArgs e)
        => _fullscreenController.Toggle();

    private void ToggleMaximizeWindow_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
        // 沿用窗口关闭事件中的异步资源释放流程。
        => SystemCommands.CloseWindow(this);

    private async void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        await OpenFileAsync();
    }

    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        await OpenFolderAsync();
    }

    private async void TogglePlaylist_Click(object sender, RoutedEventArgs e)
    {
        await TogglePlaylistAsync();
    }
    private async Task OpenFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "视频文件|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.ts;*.m2ts;*.webm|所有文件|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            await _viewModel.Player.OpenAsync(dialog.FileName);
        }
    }

    private async Task OpenFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择视频文件夹"
        };

        if (dialog.ShowDialog(this) == true)
        {
            await AddPathsAndPlayFirstAsync([dialog.FolderName]);
        }
    }

    private async Task TogglePlaylistAsync()
    {
        var visible = !_isPlaylistVisible;
        ApplyPlaylistVisibility(visible);
        _viewModel.Settings.Current.UI.ShowPlaylist = visible;
        await _viewModel.Settings.SaveAsync();
    }

    private void ApplyPlaylistVisibility(bool visible)
    {
        _isPlaylistVisible = visible;
        PlaylistView.Visibility = visible
            ? Visibility.Visible
            : Visibility.Collapsed;
        PlaylistColumn.Width = visible
            ? new GridLength(320)
            : new GridLength(0);
        UpdatePlaylistToggleVisual(visible);
    }

    private void OnFullscreenChanged(bool isFullscreen)
    {
        PlayerView.SetFullscreenMode(isFullscreen);
        AppHeader.Visibility = isFullscreen ? Visibility.Collapsed : Visibility.Visible;

        if (isFullscreen)
        {
            _playlistVisibleBeforeFullscreen = _isPlaylistVisible;
            SetPlaylistDisplay(false);
            return;
        }

        SetPlaylistDisplay(_playlistVisibleBeforeFullscreen);
    }

    private void SetPlaylistDisplay(bool visible)
    {
        PlaylistView.Visibility = visible
            ? Visibility.Visible
            : Visibility.Collapsed;
        PlaylistColumn.Width = visible
            ? new GridLength(320)
            : new GridLength(0);
        UpdatePlaylistToggleVisual(visible);
    }

    private void UpdatePlaylistToggleVisual(bool visible)
    {
        TogglePlaylistButton.Content = visible ? "\uE76C" : "\uE76B";
        TogglePlaylistButton.ToolTip = visible ? "隐藏播放列表" : "显示播放列表";
    }

    private static void ApplyTheme(string theme)
    {
        var resources = Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        var isDark = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase);
        resources["BackgroundBrush"] = new System.Windows.Media.SolidColorBrush(
            isDark ? System.Windows.Media.Color.FromRgb(0x11, 0x11, 0x11)
                : System.Windows.Media.Colors.Black);
        resources["SurfaceBrush"] = new System.Windows.Media.SolidColorBrush(
            isDark ? System.Windows.Media.Color.FromRgb(0x1B, 0x1B, 0x1B)
                : System.Windows.Media.Color.FromRgb(0x12, 0x12, 0x12));
        resources["PrimaryTextBrush"] = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Colors.White);
    }

    private void MainWindow_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void MainWindow_PreviewDrop(object sender, DragEventArgs e)
    {
        try
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths
                || paths.Length == 0)
            {
                return;
            }

            e.Handled = true;
            await AddPathsAndPlayFirstAsync(paths);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Failed to process dropped media.");
        }
    }

    private async Task AddPathsAndPlayFirstAsync(IEnumerable<string> paths)
    {
        var wasEmpty = _viewModel.Playlist.Items.Count == 0;
        var result = await _viewModel.Playlist.AddPathsAsync(
            paths,
            recursive: true);
        if (!result.IsSuccess || result.Value is not { Count: > 0 } added)
        {
            return;
        }

        var state = _viewModel.Player.PlaybackState;
        if (wasEmpty
            || state is PlaybackState.None
                or PlaybackState.Stopped
                or PlaybackState.Ended
                or PlaybackState.Error)
        {
            await _viewModel.Player.OpenAsync(added[0].FilePath);
        }
    }

    private async void Playlist_PlayRequested(object? sender, PlaylistItem? item)
    {
        try
        {
            if (item is not null)
            {
                await _viewModel.Player.OpenAsync(item.FilePath);
            }
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Failed to open selected playlist item.");
        }
    }

    private async Task ShowVideoInfoAsync()
    {
        await _viewModel.VideoInfo.RefreshAsync(
            _viewModel.Player.Snapshot,
            _viewModel.Player.Tracks);

        var window = new Window
        {
            Owner = this,
            Title = "视频信息",
            Width = 640,
            Height = 480,
            MinWidth = 480,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new VideoInfoView
            {
                DataContext = _viewModel.VideoInfo
            }
        };
        window.ShowDialog();
    }

    private void DisposeView()
    {
        if (_viewDisposed)
        {
            return;
        }

        _viewDisposed = true;
        PlayerView.Dispose();
    }
}
