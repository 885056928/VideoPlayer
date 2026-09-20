using VideoPlayer.Enums;
using VideoPlayer.Models;
using VideoPlayer.Tests.Fakes;

using Xunit;

namespace VideoPlayer.Tests.Unit.ViewModels;

public sealed class PlayerViewModelUiStateTests
{
    [Fact]
    public async Task StateChanged_Opening_ExposesOpeningState()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 31);
        await using var viewModel = fixture.ViewModel;

        fixture.Player.RaiseStateChanged(PlaybackState.None, PlaybackState.Opening);

        Assert.Equal(PlaybackState.Opening, viewModel.PlaybackState);
        Assert.True(viewModel.IsOpening);
    }

    [Fact]
    public async Task StateChanged_Playing_ClosesOpeningState()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 32);
        await using var viewModel = fixture.ViewModel;
        fixture.Player.RaiseStateChanged(PlaybackState.None, PlaybackState.Opening);

        fixture.Player.RaiseStateChanged(PlaybackState.Opening, PlaybackState.Playing);

        Assert.Equal(PlaybackState.Playing, viewModel.PlaybackState);
        Assert.False(viewModel.IsOpening);
    }

    [Fact]
    public async Task OpenAsync_ClearsPreviousErrorBeforeOpening()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 35);
        await using var viewModel = fixture.ViewModel;
        viewModel.LastError = "旧错误";
        fixture.Player.OpenHandler = (_, _) =>
        {
            Assert.Equal(string.Empty, viewModel.LastError);
            return Task.FromResult(ServiceResult<MediaSessionInfo>.Success(
                new MediaSessionInfo(
                    MediaSessionId: 35,
                    "/tmp/movie.mp4",
                    AutoPlay: true,
                    RequestedStartPosition: TimeSpan.Zero,
                    CreatedAtUtc: DateTimeOffset.UtcNow)));
        };

        await viewModel.OpenAsync("/tmp/movie.mp4");

        Assert.Equal(string.Empty, viewModel.LastError);
    }

    [Fact]
    public async Task LastError_NotEmpty_ExposesErrorState()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 33);
        await using var viewModel = fixture.ViewModel;

        viewModel.LastError = "播放失败。";

        Assert.True(viewModel.HasError);
    }

    [Fact]
    public async Task ScreenshotSuccess_ShowsMessageForAtLeastTwoSeconds()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 34);
        await using var viewModel = fixture.ViewModel;

        await viewModel.ScreenshotAsync();

        Assert.True(viewModel.HasScreenshotMessage);
        Assert.Equal("截图已保存。", viewModel.ScreenshotMessage);
        await Task.Delay(TimeSpan.FromMilliseconds(1900));
        Assert.True(viewModel.HasScreenshotMessage);
    }
}
