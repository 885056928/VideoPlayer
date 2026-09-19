using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using VideoPlayer.ViewModels;

namespace VideoPlayer.Views;

public partial class PlayerView : UserControl, IDisposable
{
    public PlayerView()
    {
        InitializeComponent();
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
        VideoView.Dispose();
    }
}
