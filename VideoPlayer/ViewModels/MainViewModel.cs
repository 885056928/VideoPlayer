using CommunityToolkit.Mvvm.ComponentModel;
using VideoPlayer.Enums;
using VideoPlayer.Interfaces;

namespace VideoPlayer.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
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

    #region 属性
    public PlayerViewModel Player { get; }

    public PlaylistViewModel Playlist { get; }

    public VideoInfoViewModel VideoInfo { get; }

    public ISettingsService Settings { get; }
    #endregion

    #region 命令
    #endregion

    #region 方法
    internal async Task AddPathsAndPlayFirstAsync(IReadOnlyList<string> paths)
    {
        // 无参数直接启动时保留空白界面，不触发“没有找到视频”的列表错误。
        if (paths.Count == 0)
        {
            return;
        }

        // 启动参数和拖放共用添加、筛选与播放策略，完整保留带空格的文件路径。
        var wasEmpty = Playlist.Items.Count == 0;
        var result = await Playlist.AddPathsAsync(paths, recursive: true);
        if (!result.IsSuccess || result.Value is not { Count: > 0 } added)
        {
            return;
        }

        var state = Player.PlaybackState;
        if (wasEmpty
            || state is PlaybackState.None
                or PlaybackState.Stopped
                or PlaybackState.Ended
                or PlaybackState.Error)
        {
            await Player.OpenAsync(added[0].FilePath);
        }
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