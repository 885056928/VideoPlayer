using System.ComponentModel;
using System.Windows;
using VideoPlayer.Views;
using VideoPlayer.ViewModels;

namespace VideoPlayer;

public partial class MainWindow : Window
{
    private bool _shutdownStarted;
    private bool _allowClose;
    private bool _viewDisposed;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
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
        DisposeView();
        base.OnClosed(e);
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
