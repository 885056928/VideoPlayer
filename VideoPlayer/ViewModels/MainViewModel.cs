using CommunityToolkit.Mvvm.ComponentModel;
using VideoPlayer.Interfaces;

namespace VideoPlayer.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    #region 属性
    public PlayerViewModel Player { get; }

    public PlaylistViewModel Playlist { get; }

    public VideoInfoViewModel VideoInfo { get; }

    public ISettingsService Settings { get; }
    #endregion

    #region 命令
    #endregion

    #region 方法
    public MainViewModel(
        PlayerViewModel player,
        PlaylistViewModel playlist,
        VideoInfoViewModel videoInfo,
        ISettingsService settings)
    {
        Player = player;
        Playlist = playlist;
        VideoInfo = videoInfo;
        Settings = settings;
        Player.MediaParsed += OnMediaParsed;
    }

    private async void OnMediaParsed(object? sender, Models.Events.MediaParsedEventArgs e)
    {
        try
        {
            await VideoInfo.RefreshAsync(Player.Snapshot, Player.Tracks);
        }
        catch (Exception exception)
        {
            Serilog.Log.Warning(exception, "Failed to refresh media information.");
        }
    }
    #endregion
}