using System.IO;
using VideoPlayer.Enums;
using VideoPlayer.Interfaces;
using VideoPlayer.Services;
using VideoPlayer.Tests.Fakes;
using VideoPlayer.ViewModels;
using Xunit;

namespace VideoPlayer.Tests.Unit.ViewModels;

public sealed class MainViewModelOpenPathsTests : IAsyncLifetime
{
    public MainViewModelOpenPathsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "VideoPlayer 启动测试 " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _player = new FakePlayerService(1);
        var playlist = new PlaylistService(new FileService());
        var settings = new FakeSettingsService();
        var player = new PlayerViewModel(_player, playlist, settings, new FakeDialogService());
        _viewModel = new MainViewModel(
            player,
            new PlaylistViewModel(playlist, new ImmediateUiDispatcher()),
            new VideoInfoViewModel(new MediaInfoService(_player)),
            settings);
    }

    private readonly string _root;
    private readonly FakePlayerService _player;
    private readonly MainViewModel _viewModel;

    [Fact]
    public async Task OpenPaths_ChineseAndSpacePath_AddsVideoAndRequestsAutoPlay()
    {
        var file = CreateFile("桌面 视频.mp4");

        await _viewModel.AddPathsAndPlayFirstAsync([file]);

        Assert.Equal(file, Assert.Single(_viewModel.Playlist.Items).FilePath);
        Assert.Equal(file, _player.LastRequest?.FilePath);
        Assert.True(_player.LastRequest?.AutoPlay);
        Assert.Equal(1, _player.OpenCount);
    }

    [Fact]
    public async Task OpenPaths_MultipleArguments_SkipsInvalidFilesAndPlaysFirstValidVideo()
    {
        var first = CreateFile("视频 2.MP4");
        var second = CreateFile("视频 1.mkv");
        var text = CreateFile("说明.txt");

        await _viewModel.AddPathsAndPlayFirstAsync([
            Path.Combine(_root, "missing.mp4"), text, first, second, first]);

        Assert.Equal(new[] { first, second }, _viewModel.Playlist.Items.Select(item => item.FilePath));
        Assert.Equal(first, _player.LastRequest?.FilePath);
        Assert.True(_player.LastRequest?.AutoPlay);
        Assert.Equal(1, _player.OpenCount);
    }

    [Fact]
    public async Task OpenPaths_Folder_StillIncludesNestedVideosAndPlaysFirst()
    {
        var nested = Directory.CreateDirectory(Path.Combine(_root, "子目录")).FullName;
        var file = Path.Combine(nested, "嵌套视频.mp4");
        File.WriteAllBytes(file, []);

        await _viewModel.AddPathsAndPlayFirstAsync([_root]);

        Assert.Equal(file, Assert.Single(_viewModel.Playlist.Items).FilePath);
        Assert.Equal(file, _player.LastRequest?.FilePath);
        Assert.True(_player.LastRequest?.AutoPlay);
    }

    [Fact]
    public async Task OpenPaths_NoArguments_DoesNotStartPlaybackOrReportError()
    {
        await _viewModel.AddPathsAndPlayFirstAsync([]);

        Assert.Empty(_viewModel.Playlist.Items);
        Assert.Equal(0, _player.OpenCount);
        Assert.Empty(_viewModel.Playlist.LastError);
    }

    [Fact]
    public async Task OpenPaths_InvalidArguments_DoesNotStartPlaybackAndPreservesPlaylistError()
    {
        await _viewModel.AddPathsAndPlayFirstAsync([Path.Combine(_root, "missing.mp4")]);

        Assert.Empty(_viewModel.Playlist.Items);
        Assert.Equal(0, _player.OpenCount);
        Assert.NotEmpty(_viewModel.Playlist.LastError);
    }

    [Theory]
    [InlineData(PlaybackState.Playing, false)]
    [InlineData(PlaybackState.Paused, false)]
    [InlineData(PlaybackState.None, true)]
    [InlineData(PlaybackState.Stopped, true)]
    [InlineData(PlaybackState.Ended, true)]
    [InlineData(PlaybackState.Error, true)]
    public async Task OpenPaths_WithExistingPlaylist_PreservesDropPlaybackPolicy(
        PlaybackState state,
        bool shouldOpen)
    {
        await _viewModel.Playlist.AddPathsAsync([CreateFile("原视频.mp4")]);
        _viewModel.Player.PlaybackState = state;
        var added = CreateFile("新视频.mp4");

        await _viewModel.AddPathsAndPlayFirstAsync([added]);

        Assert.Equal(2, _viewModel.Playlist.Items.Count);
        Assert.Equal(shouldOpen ? 1 : 0, _player.OpenCount);
        if (shouldOpen)
        {
            Assert.Equal(added, _player.LastRequest?.FilePath);
        }
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        try
        {
            await _viewModel.Player.DisposeAsync();
        }
        finally
        {
            Directory.Delete(_root, true);
        }
    }

    private string CreateFile(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, []);
        return path;
    }

    private sealed class ImmediateUiDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;
        public void Post(Action action) => action();
        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }
}
