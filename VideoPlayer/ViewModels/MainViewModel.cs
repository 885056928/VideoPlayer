using CommunityToolkit.Mvvm.ComponentModel;

namespace VideoPlayer.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel(
        PlayerViewModel player,
        PlaylistViewModel playlist,
        VideoInfoViewModel videoInfo)
    {
        Player = player;
        Playlist = playlist;
        VideoInfo = videoInfo;
    }

    public PlayerViewModel Player { get; }

    public PlaylistViewModel Playlist { get; }

    public VideoInfoViewModel VideoInfo { get; }
}
