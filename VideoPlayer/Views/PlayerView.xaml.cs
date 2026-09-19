using System.Windows;
using System.Windows.Controls;
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

    public void Dispose()
    {
        VideoView.Dispose();
    }
}
