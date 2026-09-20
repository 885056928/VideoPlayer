using VideoPlayer.Enums;
using VideoPlayer.Models;
using VideoPlayer.Tests.Fakes;
using Xunit;

namespace VideoPlayer.Tests.Unit.ViewModels;

public sealed class PlayerViewModelScreenshotTests
{
    [Fact]
    public async Task ScreenshotAsync_OnSuccess_CapturesCurrentSnapshotAndClearsError()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 21);
        fixture.ViewModel.LastError = "old error";
        await using var viewModel = fixture.ViewModel;

        await viewModel.ScreenshotAsync();

        Assert.Equal(1, fixture.Screenshot.CaptureCount);
        Assert.Equal(21, fixture.Screenshot.LastSnapshot!.MediaSessionId);
        Assert.Equal(string.Empty, viewModel.LastError);
    }

    [Fact]
    public async Task ScreenshotAsync_OnFailure_SetsLastError()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 22);
        fixture.Screenshot.CaptureResult = ServiceResult<string>.Failure(
            ErrorCode.SnapshotFailed,
            "截图失败。",
            "Snapshot failed");
        await using var viewModel = fixture.ViewModel;

        await viewModel.ScreenshotAsync();

        Assert.Equal("截图失败。", viewModel.LastError);
    }
}
