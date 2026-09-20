using VideoPlayer.Enums;
using VideoPlayer.Models;
using VideoPlayer.Tests.Fakes;
using VideoPlayer.ViewModels;
using Xunit;

namespace VideoPlayer.Tests.Unit.ViewModels;

public sealed class PlayerViewModelSubtitleButtonTests
{
    [Fact]
    public async Task HasActiveSubtitle_TracksSelectedSubtitleTrack()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 13);
        await using var viewModel = fixture.ViewModel;
        await viewModel.OpenAsync(@"C:\media\Movie.mkv");
        Assert.False(viewModel.HasActiveSubtitle);

        var subtitle = new PlayerTrackInfo(
            PlayerTrackType.Subtitle,
            5,
            "Chinese",
            "Chinese",
            true,
            true);

        fixture.Player.RaiseTracksChanged(subtitle);

        Assert.True(viewModel.HasActiveSubtitle);

        fixture.Player.RaiseTracksChanged(subtitle with { IsSelected = false });

        Assert.False(viewModel.HasActiveSubtitle);
    }
}
