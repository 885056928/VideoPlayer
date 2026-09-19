using System.Windows;
using VideoPlayer.Views;
using VideoPlayer.ViewModels;

namespace VideoPlayer;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        PlayerView.Dispose();
        base.OnClosed(e);
    }
}
