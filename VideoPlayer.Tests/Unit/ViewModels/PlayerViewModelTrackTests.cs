using VideoPlayer.Enums;
using VideoPlayer.Models;
using VideoPlayer.Tests.Fakes;
using Xunit;

namespace VideoPlayer.Tests.Unit.ViewModels;

public sealed class PlayerViewModelTrackTests
{
    [Fact]
    public async Task OpenAsync_WhenAutoSubtitleMissing_ContinuesWithoutSubtitle()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 12);
        await using var viewModel = fixture.ViewModel;

        await viewModel.OpenAsync(@"C:\media\Movie.mkv");

        Assert.NotNull(fixture.Player.LastRequest);
        Assert.Null(fixture.Player.LastRequest.ExternalSubtitlePath);
        Assert.Equal(string.Empty, viewModel.LastError);
    }
    [Fact]
    public async Task OpenAsync_WhenAutoSubtitleFound_ForwardsExternalSubtitlePath()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 7);
        fixture.Subtitle.FindAutoSubtitleResult =
            ServiceResult<string>.Success(@"C:\media\Movie.srt");
        await using var viewModel = fixture.ViewModel;

        await viewModel.OpenAsync(@"C:\media\Movie.mkv");

        Assert.Equal(@"C:\media\Movie.srt", fixture.Player.LastRequest!.ExternalSubtitlePath);
    }

    [Fact]
    public async Task TracksChanged_ProjectsAudioAndSubtitleCollections()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 8);
        await using var viewModel = fixture.ViewModel;
        await viewModel.OpenAsync(@"C:\media\Movie.mkv");
        var audio = new PlayerTrackInfo(
            PlayerTrackType.Audio,
            1,
            "English",
            "English",
            true,
            false);
        var subtitle = new PlayerTrackInfo(
            PlayerTrackType.Subtitle,
            2,
            "Chinese",
            "Chinese",
            false,
            true);

        fixture.Player.RaiseTracksChanged(audio, subtitle);

        Assert.Equal([audio], viewModel.AudioTracks);
        Assert.Equal([subtitle], viewModel.SubtitleTracks);
    }

    [Fact]
    public async Task SelectAudioTrackAsync_ForwardsSelectedTrack()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 9);
        await using var viewModel = fixture.ViewModel;
        var track = new PlayerTrackInfo(
            PlayerTrackType.Audio,
            4,
            "Japanese",
            "Japanese",
            false,
            false);

        await viewModel.SelectAudioTrackAsync(track);

        Assert.Equal(4, fixture.Player.LastSelectedAudioTrackId);
    }

    [Fact]
    public async Task SelectSubtitleTrackAsync_WithNull_DisablesSubtitles()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 10);
        await using var viewModel = fixture.ViewModel;

        await viewModel.SelectSubtitleTrackAsync(null);

        Assert.True(fixture.Player.WasSubtitleTrackSelected);
        Assert.Null(fixture.Player.LastSelectedSubtitleTrackId);
    }

    [Fact]
    public async Task SelectSubtitleTrackFailure_SetsLastError()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 11);
        fixture.Player.SelectSubtitleTrackResult = ServiceResult.Failure(
            ErrorCode.TrackNotFound,
            "字幕轨不可用。",
            "Track missing");
        await using var viewModel = fixture.ViewModel;

        await viewModel.SelectSubtitleTrackAsync(null);

        Assert.Equal("字幕轨不可用。", viewModel.LastError);
    }
}
