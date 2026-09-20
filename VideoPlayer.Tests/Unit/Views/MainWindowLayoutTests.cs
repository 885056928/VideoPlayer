using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using VideoPlayer.Enums;
using VideoPlayer.Interfaces;
using VideoPlayer.Models;
using VideoPlayer.Tests.Fakes;
using VideoPlayer.ViewModels;
using Xunit;

namespace VideoPlayer.Tests.Unit.Views;

public sealed class MainWindowLayoutTests
{
    [StaFact]
    public void HeaderActions_KeepFolderAndPlaylistButRemoveDuplicateOpenFile()
    {
        var player = new PlayerViewModelFixture(1);
        var viewModel = new MainViewModel(
            player.ViewModel,
            new PlaylistViewModel(player.Playlist, new ImmediateUiDispatcher()),
            new VideoInfoViewModel(new StubMediaInfoService()),
            player.Settings);
        var window = new MainWindow(viewModel);
        try
        {
            Assert.Null(window.FindName("HeaderOpenFileButton"));
            Assert.NotNull(window.FindName("HeaderOpenFolderButton"));
            Assert.NotNull(window.FindName("TogglePlaylistButton"));
        }
        finally
        {
            window.Close();
            window.Dispatcher.Invoke(
                static () => { },
                DispatcherPriority.Background);
        }
    }

    [StaFact]
    public void HeaderTitle_BindsToCurrentMediaTitle()
    {
        var player = new PlayerViewModelFixture(2);
        var viewModel = new MainViewModel(
            player.ViewModel,
            new PlaylistViewModel(player.Playlist, new ImmediateUiDispatcher()),
            new VideoInfoViewModel(new StubMediaInfoService()),
            player.Settings);
        var window = new MainWindow(viewModel);
        try
        {
            var title = Assert.IsType<TextBlock>(window.FindName("HeaderMediaTitleText"));

            Assert.Equal(
                "Player.MediaTitle",
                BindingOperations.GetBinding(title, TextBlock.TextProperty)?.Path.Path);
        }
        finally
        {
            window.Close();
            window.Dispatcher.Invoke(
                static () => { },
                DispatcherPriority.Background);
        }
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

    private sealed class StubMediaInfoService : IMediaInfoService
    {
        public Task<ServiceResult<VideoInfo>> GetCurrentMediaInfoAsync(
            PlaybackSnapshot snapshot,
            IReadOnlyList<PlayerTrackInfo> tracks,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult<VideoInfo>.Failure(
                ErrorCode.Unknown,
                "Unavailable",
                "Unavailable"));
    }
}
