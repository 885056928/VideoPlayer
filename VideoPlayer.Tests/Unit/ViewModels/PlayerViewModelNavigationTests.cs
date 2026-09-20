using VideoPlayer.Enums;
using VideoPlayer.Models;
using VideoPlayer.Models.Events;
using VideoPlayer.Tests.Fakes;
using Xunit;

namespace VideoPlayer.Tests.Unit.ViewModels;

public sealed class PlayerViewModelNavigationTests
{
    [Fact]
    public async Task HandleEndReachedAsync_ForSameSession_OpensNextOnlyOnce()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 4);
        await fixture.ViewModel.OpenAsync(@"C:\Videos\a.mp4");
        var args = new EndReachedEventArgs
        {
            MediaSessionId = 4,
            FilePath = @"C:\Videos\a.mp4",
            OccurredAtUtc = DateTimeOffset.UtcNow
        };

        await fixture.ViewModel.HandleEndReachedAsync(args);
        await fixture.ViewModel.HandleEndReachedAsync(args);

        Assert.Equal(2, fixture.Player.OpenCount);
        Assert.Equal("b.mp4", fixture.Player.LastRequest!.FilePath);
    }

    [Fact]
    public async Task HandleEndReachedAsync_ForOldSession_DoesNothing()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 9);
        await fixture.ViewModel.OpenAsync(@"C:\Videos\a.mp4");

        await fixture.ViewModel.HandleEndReachedAsync(new EndReachedEventArgs
        {
            MediaSessionId = 8,
            FilePath = @"C:\Videos\old.mp4",
            OccurredAtUtc = DateTimeOffset.UtcNow
        });

        Assert.Equal(1, fixture.Player.OpenCount);
    }

    [Fact]
    public async Task HandleEndReachedAsync_WhenAutoPlayNextIsDisabled_DoesNothing()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 4);
        fixture.Settings.Current.Player.AutoPlayNext = false;
        await fixture.ViewModel.OpenAsync(@"C:\Videos\a.mp4");

        await fixture.ViewModel.HandleEndReachedAsync(new EndReachedEventArgs
        {
            MediaSessionId = 4,
            FilePath = @"C:\Videos\a.mp4",
            OccurredAtUtc = DateTimeOffset.UtcNow
        });

        Assert.Equal(1, fixture.Player.OpenCount);
    }

    [Fact]
    public async Task NextAsync_AtLastItem_DoesNotWrap()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 4);
        await fixture.ViewModel.OpenAsync(@"C:\Videos\a.mp4");
        fixture.Playlist.SetCurrentIndexForTest(1);

        await fixture.ViewModel.NextAsync();

        Assert.Equal(1, fixture.Player.OpenCount);
        Assert.Equal("Last", fixture.ViewModel.LastError);
    }

    [Fact]
    public async Task OpenAsync_WhenEarlierRequestCompletesLast_IgnoresStaleCompletion()
    {
        var fixture = new PlayerViewModelFixture(sessionId: 1);
        var staleStarted = NewCompletionSource();
        var releaseStale = NewCompletionSource();
        fixture.Player.OpenHandler = async (request, cancellationToken) =>
        {
            if (request.FilePath == "slow.mp4")
            {
                staleStarted.TrySetResult(true);
                await releaseStale.Task.WaitAsync(cancellationToken);
                return ServiceResult<MediaSessionInfo>.Failure(
                    ErrorCode.InvalidState,
                    "stale",
                    "stale");
            }

            return ServiceResult<MediaSessionInfo>.Success(
                new MediaSessionInfo(
                    2,
                    request.FilePath,
                    request.AutoPlay,
                    request.StartPosition,
                    DateTimeOffset.UtcNow));
        };

        var staleOpen = fixture.ViewModel.OpenAsync("slow.mp4");
        await staleStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var currentOpen = fixture.ViewModel.OpenAsync("current.mp4");
        await currentOpen;
        releaseStale.TrySetResult(true);
        await staleOpen;

        Assert.Equal(string.Empty, fixture.ViewModel.LastError);
    }

    private static TaskCompletionSource<bool> NewCompletionSource()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
