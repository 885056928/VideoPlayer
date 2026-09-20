using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using VideoPlayer.Services;
using VideoPlayer.ViewModels;
using VideoPlayer.Views;

namespace VideoPlayer;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly FullscreenController _fullscreenController;
    private bool _shutdownStarted;
    private bool _allowClose;
    private bool _viewDisposed;
    private bool _isPlaylistVisible = true;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _fullscreenController = new FullscreenController(this);
        DataContext = viewModel;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

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
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        PreviewKeyDown -= MainWindow_PreviewKeyDown;
        DisposeView();
        base.OnClosed(e);
    }

    private async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBoxBase)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var action = ShortcutService.Resolve(key, Keyboard.Modifiers);
        if (!ShouldHandleShortcut(action, _fullscreenController.IsFullscreen))
        {
            return;
        }

        e.Handled = true;
        await ExecuteShortcutAsync(action);
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
                TogglePlaylist();
                break;
            case ShortcutAction.VideoInfo:
                await ShowVideoInfoAsync();
                break;
            case ShortcutAction.Screenshot:
                await _viewModel.Player.ScreenshotAsync();
                break;
        }
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
            await _viewModel.Playlist.AddFolderAsync(dialog.FolderName);
        }
    }

    private void TogglePlaylist()
    {
        _isPlaylistVisible = !_isPlaylistVisible;
        PlaylistView.Visibility = _isPlaylistVisible
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        PlaylistColumn.Width = _isPlaylistVisible
            ? new GridLength(300)
            : new GridLength(0);
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
