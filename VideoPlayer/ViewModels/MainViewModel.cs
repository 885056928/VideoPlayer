using CommunityToolkit.Mvvm.ComponentModel;

namespace VideoPlayer.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel(PlayerViewModel player, PlaylistViewModel playlist)
    {
        Player = player;
        Playlist = playlist;
    }

    public PlayerViewModel Player { get; }

    public PlaylistViewModel Playlist { get; }
}
