using System.Windows;
using VideoPlayer.Models;

namespace VideoPlayer;

public partial class MainWindow : Window
{
    public MainWindow(AppSettings settings)
    {
        InitializeComponent();
        DataContext = settings;
    }
}
