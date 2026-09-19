using CommunityToolkit.Mvvm.ComponentModel;

namespace VideoPlayer.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel(PlayerViewModel player)
    {
        Player = player;
    }

    public PlayerViewModel Player { get; }
}
