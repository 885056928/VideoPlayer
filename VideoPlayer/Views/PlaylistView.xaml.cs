using System.Windows;
using System.Windows.Controls;

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
            await viewModel.AddPathsAsync(paths);
        }
    }
}
