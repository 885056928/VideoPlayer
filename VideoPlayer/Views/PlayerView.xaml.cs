using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using VideoPlayer.ViewModels;

namespace VideoPlayer.Views;

public partial class PlayerView : UserControl, IDisposable
{
    private static readonly TimeSpan ControlBarIdleTimeout = TimeSpan.FromSeconds(2);
    private readonly DispatcherTimer _controlBarTimer;
    private bool _isFullscreen;

    public PlayerView()
    {
        InitializeComponent();
        DataContextChanged += (_, args) => SeekPreviewPopup.DataContext = args.NewValue;
        SeekPreviewPopup.DataContext = DataContext;
        _controlBarTimer = new DispatcherTimer
        {
            Interval = ControlBarIdleTimeout
        };
        _controlBarTimer.Tick += ControlBarTimer_Tick;
        PreviewMouseMove += PlayerView_PreviewMouseMove;
        PreviewMouseDown += PlayerView_PreviewMouseDown;
        Loaded += PlayerView_Loaded;
        Unloaded += PlayerView_Unloaded;
    }

    private void PlayerView_Loaded(object sender, RoutedEventArgs e)
    {
        ShowControlBar();
    }

    private void PlayerView_Unloaded(object sender, RoutedEventArgs e)
    {
        _controlBarTimer.Stop();
    }

    private void PlayerView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        ShowControlBar();
    }

    private void PlayerView_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        ShowControlBar();
    }

    private void ControlBarTimer_Tick(object? sender, EventArgs e)
    {
        _controlBarTimer.Stop();
        if (ControlBarVisibilityPolicy.ShouldCollapseOnIdle(_isFullscreen))
        {
            ControlBar.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowControlBar()
    {
        ControlBar.Visibility = Visibility.Visible;
        _controlBarTimer.Stop();
        if (ControlBarVisibilityPolicy.ShouldStartAutoHideTimer(IsLoaded, _isFullscreen))
        {
            _controlBarTimer.Start();
        }
    }

    public void SetFullscreenMode(bool isFullscreen)
    {
        _isFullscreen = isFullscreen;
        _controlBarTimer.Stop();

        if (isFullscreen)
        {
            ShowControlBar();
            return;
        }

        ControlBar.Visibility = Visibility.Visible;
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "视频文件|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.ts;*.m2ts;*.webm|所有文件|*.*"
        };

        if (dialog.ShowDialog() == true && DataContext is PlayerViewModel viewModel)
        {
            await viewModel.OpenAsync(dialog.FileName);
        }
    }

    private async void AddSubtitle_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not PlayerViewModel viewModel)
            {
                return;
            }

            var dialog = new OpenFileDialog
            {
                Filter = "字幕文件|*.srt;*.ass;*.ssa|所有文件|*.*"
            };
            if (dialog.ShowDialog() == true)
            {
                await viewModel.AddSubtitleAsync(dialog.FileName);
            }
        }
        catch (Exception exception)
        {
            Serilog.Log.Warning(exception, "Manual subtitle selection failed.");
        }
    }

    private void SeekSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PlayerViewModel viewModel)
        {
            viewModel.BeginSeek();
        }
    }

    private async void SeekSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        await CommitSeekAsync();
    }

    private async void SeekSlider_LostMouseCapture(object sender, MouseEventArgs e)
    {
        await CommitSeekAsync();
    }

    private void SeekSlider_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsSeekKey(e.Key) && DataContext is PlayerViewModel viewModel)
        {
            viewModel.BeginSeek();
        }
    }

    private async void SeekSlider_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (IsSeekKey(e.Key))
        {
            await CommitSeekAsync();
        }
    }

    private void SeekSlider_MouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is not PlayerViewModel viewModel
            || viewModel.Duration <= TimeSpan.Zero
            || SeekSlider.ActualWidth <= 0)
        {
            return;
        }

        var ratio = Math.Clamp(
            e.GetPosition(SeekSlider).X / SeekSlider.ActualWidth,
            0,
            1);
        var position = TimeSpan.FromTicks((long)(viewModel.Duration.Ticks * ratio));
        SeekPreviewPopup.IsOpen = true;
        _ = viewModel.RequestFramePreviewAsync(position);
    }

    private void SeekSlider_MouseLeave(object sender, MouseEventArgs e)
    {
        SeekPreviewPopup.IsOpen = false;
        if (DataContext is PlayerViewModel viewModel)
        {
            viewModel.CancelFramePreview();
        }
    }
    private async Task CommitSeekAsync()
    {
        if (DataContext is PlayerViewModel viewModel)
        {
            await viewModel.CommitSeekAsync();
        }
    }

    private static bool IsSeekKey(Key key)
        => key is Key.Left
            or Key.Right
            or Key.Up
            or Key.Down
            or Key.PageUp
            or Key.PageDown
            or Key.Home
            or Key.End;

    public void Dispose()
    {
        _controlBarTimer.Stop();
        _controlBarTimer.Tick -= ControlBarTimer_Tick;
        PreviewMouseMove -= PlayerView_PreviewMouseMove;
        PreviewMouseDown -= PlayerView_PreviewMouseDown;
        Loaded -= PlayerView_Loaded;
        Unloaded -= PlayerView_Unloaded;
        VideoView.Dispose();
    }
}
