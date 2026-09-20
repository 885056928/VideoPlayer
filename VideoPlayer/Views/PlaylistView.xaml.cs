using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using VideoPlayer.ViewModels;

namespace VideoPlayer.Views;

public partial class PlaylistView : UserControl
{
    public PlaylistView()
    {
        InitializeComponent();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths &&
            DataContext is PlaylistViewModel viewModel)
        {
            await viewModel.AddPathsAsync(paths, recursive: true);
        }
    }

    private void OnPlaylistDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PlaylistViewModel viewModel)
        {
            viewModel.RequestPlay(viewModel.SelectedItem);
        }
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || DataContext is not PlaylistViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        try
        {
            await viewModel.RemoveAsync(viewModel.SelectedItem);
        }
        catch (Exception exception)
        {
            Serilog.Log.Warning(exception, "Failed to remove playlist item.");
        }
    }
}
